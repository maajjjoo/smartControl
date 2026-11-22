using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Recognition;

/// <summary>
/// Quién decidió el gesto y con qué confianza. Se muestra en la ventana y se envía al reloj.
/// [Engine] es la IA concreta (versión del modelo local o «Gemini (…)»), solo para la ventana y el registro.
/// </summary>
public sealed record RecognitionInfo(string RecognizedBy, string? Gesture, double Confidence, string? Engine = null)
{
    public const string ByModel = "AI";
    public const string ByRules = "RULES";

    public bool IsModel => RecognizedBy == ByModel;
}

/// <summary>Ventana de movimiento enviada por el reloj (SENSOR_WINDOW), ya leída con su formato exacto.</summary>
public sealed record SensorWindowMessage(
    string MessageId,
    long TimestampUtc,
    string DeviceId,
    string SessionToken,
    ControlMode Mode,
    string DetectorEvent,
    double DetectorConfidence,
    IReadOnlyList<SensorSample> Gyroscope);

/// <summary>Resultado de reconocer una ventana: lo que dice el validador y quién decidió.</summary>
public sealed record GestureRecognition(ValidationResult Result, RecognitionInfo? Info);

/// <summary>
/// Reconocimiento de gestos en Windows con el enfoque híbrido (ADR-01 C, ADR-03, ADR-07): el reloj
/// segmenta el movimiento con sus reglas y envía la ventana; aquí decide la IA, por este orden:
/// 1) un modelo local entrenado, si está instalado (rápido y sin internet);
/// 2) Gemini, si hay clave (ADR-07);
/// 3) si no hay IA o la IA remota no responde a tiempo, lo que dijeron las reglas del reloj.
/// En todos los casos la acción pasa por el <see cref="CommandValidator"/> existente, que comprueba
/// vinculación, frescura, repetición, confianza y lista blanca.
/// </summary>
public sealed class GestureRecognizer
{
    public const string MessageType = "SENSOR_WINDOW";

    /// <summary>Tamaño máximo de un SENSOR_WINDOW: hasta 256 muestras caben de sobra.</summary>
    public const int MaxMessageBytes = 16 * 1024;

    private static readonly string[] HeaderFields =
        { "protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload" };
    private static readonly string[] PayloadFields = { "activeMode", "detectorEvent", "detectorConfidence", "gyroscope" };
    private static readonly Regex UuidPattern =
        new(@"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DeviceIdPattern = new(@"\A[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant);

    private readonly PairedDeviceRegistry _devices;
    private readonly CommandValidator _validator;
    private readonly DenseClassifier? _classifier;
    private readonly IRemoteGestureClassifier? _remote;

    /// <summary>Umbral mínimo de confianza para actuar (el mismo que el del protocolo).</summary>
    public const double RemoteThreshold = 0.80;

    public GestureRecognizer(PairedDeviceRegistry devices, CommandValidator validator, DenseClassifier? classifier,
        IRemoteGestureClassifier? remote = null)
    {
        _devices = devices;
        _validator = validator;
        _classifier = classifier;
        _remote = remote;
    }

    /// <summary>true si hay un modelo de IA local cargado.</summary>
    public bool HasModel => _classifier is not null;

    /// <summary>true si decide una IA (local o Gemini); false si solo hay reglas.</summary>
    public bool HasAi => _classifier is not null || _remote is not null;

    /// <summary>Versión del modelo local cargado, para mostrarla en la ventana.</summary>
    public string? ModelVersion => _classifier?.Model.ModelVersion;

    /// <summary>La IA que decide: «modelo …» o «Gemini (…)»; null si solo hay reglas.</summary>
    public string? AiName => _classifier is not null ? $"modelo {ModelVersion}" : _remote?.Name;

    /// <summary>Reconoce una ventana sin esperar a la red (sin Gemini). Para pruebas y herramientas.</summary>
    public GestureRecognition Recognize(ReadOnlyMemory<byte> raw) =>
        RecognizeAsync(raw, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Reconoce una ventana recibida. Nunca ejecuta nada: devuelve, como mucho, una acción validada.
    /// El mensaje se lee antes de cualquier espera, así el búfer de quien llama se puede reutilizar.
    /// </summary>
    public Task<GestureRecognition> RecognizeAsync(ReadOnlyMemory<byte> raw, CancellationToken ct)
    {
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;
        var (message, reason) = Parse(raw);
        if (message is null)
            return Task.FromResult(new GestureRecognition(ValidationResult.Reject(Invalid, reason), null));

        // Antes de gastar tiempo en la ventana (o en la red), el dispositivo tiene que estar vinculado.
        if (!_devices.TryGetToken(message.DeviceId, out var expected)
            || !CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(message.SessionToken)))
            return Task.FromResult(new GestureRecognition(ValidationResult.Reject(Invalid, ReasonCodes.UnauthorizedDevice, message.MessageId), null));

        if (!SensorWindow.TryCreate(message.Gyroscope, null, out _, out var windowReason))
            return Task.FromResult(new GestureRecognition(ValidationResult.Reject(Invalid, windowReason, message.MessageId), null));

        if (_classifier is not null)
            return Task.FromResult(ByModel(message));
        if (_remote is not null)
            return ByRemoteAsync(message, ct);
        return Task.FromResult(ByRules(message));
    }

    /// <summary>Decide Gemini. Si no responde a tiempo o responde fuera de formato, deciden las reglas.</summary>
    private async Task<GestureRecognition> ByRemoteAsync(SensorWindowMessage message, CancellationToken ct)
    {
        var answer = await _remote!.ClassifyAsync(message.Gyroscope, ct).ConfigureAwait(false);
        if (answer is null)
            return ByRules(message);

        var info = new RecognitionInfo(RecognitionInfo.ByModel, GestureName(answer.Gesture), answer.Confidence, _remote.Name);
        if (answer.Gesture == GestureClass.NoGesture)
            return new(ValidationResult.Reject(CommandStatus.RejectedLowConfidence, RecognitionReasons.NoGesture, message.MessageId), info);
        if (answer.Confidence < RemoteThreshold)
            return new(ValidationResult.Reject(CommandStatus.RejectedLowConfidence, RecognitionReasons.LowConfidence, message.MessageId), info);
        return new(Validate(message, answer.Gesture, answer.Confidence), info);
    }

    private GestureRecognition ByModel(SensorWindowMessage message)
    {
        var pipeline = new RecognitionPipeline(_classifier, _validator, message.DeviceId, message.SessionToken);
        var outcome = pipeline.Recognize(message.Gyroscope, null, message.Mode, message.MessageId, message.TimestampUtc);
        var info = new RecognitionInfo(RecognitionInfo.ByModel, GestureName(outcome.Decision?.Gesture), outcome.Decision?.Confidence ?? 0, AiName);
        var result = outcome.Validation ?? ValidationResult.Reject(
            outcome.Status is RecognitionStatus.NoGesture or RecognitionStatus.LowConfidence
                ? CommandStatus.RejectedLowConfidence
                : CommandStatus.RejectedInvalidMessage,
            outcome.ReasonCode,
            message.MessageId);
        return new(result, info);
    }

    /// <summary>Sin modelo: decide lo que reconocieron las reglas del reloj.</summary>
    private GestureRecognition ByRules(SensorWindowMessage message)
    {
        GestureClass? gesture = message.DetectorEvent switch
        {
            "TWIST_OUT" => GestureClass.TwistOut,
            "TWIST_IN" => GestureClass.TwistIn,
            _ => null,
        };
        var info = new RecognitionInfo(RecognitionInfo.ByRules, GestureName(gesture), message.DetectorConfidence);
        if (gesture is null)
            return new(ValidationResult.Reject(CommandStatus.RejectedLowConfidence, ReasonCodes.BelowThreshold, message.MessageId), info);
        return new(Validate(message, gesture.Value, message.DetectorConfidence), info);
    }

    /// <summary>Acción del gesto en el modo activo, validada con el mismo mensaje del reloj.</summary>
    private ValidationResult Validate(SensorWindowMessage message, GestureClass gesture, double confidence)
    {
        if (!ContextualActionSelector.TrySelect(message.Mode, gesture, out var action))
            return ValidationResult.Reject(CommandStatus.RejectedNotAllowed, ReasonCodes.ActionNotInAllowlist, message.MessageId);
        var request = new CommandRequest
        {
            MessageId = message.MessageId,
            TimestampUtc = message.TimestampUtc,
            DeviceId = message.DeviceId,
            SessionToken = message.SessionToken,
            ActiveMode = CommandAllowlist.ToWireName(message.Mode),
            GestureId = gesture == GestureClass.TwistOut ? "WRIST_TWIST_OUT" : "WRIST_TWIST_IN",
            Confidence = confidence,
            RequestedAction = CommandAllowlist.ToWireName(action),
        };
        return _validator.Validate(request.ToUtf8Json());
    }

    private static string? GestureName(GestureClass? gesture) => gesture switch
    {
        GestureClass.TwistOut => "TWIST_OUT",
        GestureClass.TwistIn => "TWIST_IN",
        GestureClass.NoGesture => "NO_GESTURE",
        _ => null,
    };

    /// <summary>Lee un SENSOR_WINDOW con campos exactos. Devuelve null y el motivo si no lo cumple.</summary>
    public static (SensorWindowMessage? Message, string Reason) Parse(ReadOnlyMemory<byte> raw)
    {
        if (raw.Length == 0)
            return (null, ReasonCodes.EmptyMessage);
        if (raw.Length > MaxMessageBytes)
            return (null, ReasonCodes.MessageTooLarge);
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, ReasonCodes.NotAnObject);
            if (!HasExactly(root, HeaderFields))
                return (null, ReasonCodes.UnknownHeaderField);
            if (Text(root, "protocolVersion") != CommandValidator.SupportedVersion)
                return (null, ReasonCodes.UnsupportedVersion);
            if (Text(root, "messageType") != MessageType)
                return (null, ReasonCodes.UnsupportedMessageType);
            var messageId = Text(root, "messageId");
            if (messageId is null || !UuidPattern.IsMatch(messageId))
                return (null, ReasonCodes.InvalidMessageId);
            if (!root.GetProperty("timestampUtc").TryGetInt64(out var timestamp))
                return (null, ReasonCodes.InvalidTimestamp);
            var deviceId = Text(root, "deviceId");
            if (deviceId is null || !DeviceIdPattern.IsMatch(deviceId))
                return (null, ReasonCodes.InvalidDeviceId);
            var token = Text(root, "sessionToken");
            if (token is null || token.Length == 0 || token.Length > CommandValidator.MaxTokenLength)
                return (null, ReasonCodes.InvalidSessionToken);

            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object || !HasExactly(payload, PayloadFields))
                return (null, ReasonCodes.InvalidPayload);
            if (Text(payload, "activeMode") is not { } modeText || !CommandAllowlist.TryParseMode(modeText, out var mode))
                return (null, ReasonCodes.UnknownMode);
            var detector = Text(payload, "detectorEvent");
            if (detector is not ("TWIST_OUT" or "TWIST_IN" or "UNCLEAR"))
                return (null, ReasonCodes.InvalidPayload);
            if (!payload.GetProperty("detectorConfidence").TryGetDouble(out var confidence) || !(confidence >= 0 && confidence <= 1))
                return (null, ReasonCodes.InvalidConfidence);

            var gyro = payload.GetProperty("gyroscope");
            if (gyro.ValueKind != JsonValueKind.Array || gyro.GetArrayLength() > SensorWindow.MaxSamples)
                return (null, ReasonCodes.InvalidPayload);
            var samples = new List<SensorSample>(gyro.GetArrayLength());
            foreach (var row in gyro.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 4)
                    return (null, ReasonCodes.InvalidPayload);
                if (!row[0].TryGetInt32(out var t) || !row[1].TryGetDouble(out var x) || !row[2].TryGetDouble(out var y) || !row[3].TryGetDouble(out var z))
                    return (null, ReasonCodes.InvalidFieldType);
                samples.Add(new SensorSample(t, x, y, z));
            }
            return (new SensorWindowMessage(messageId, timestamp, deviceId, token, mode, detector, confidence, samples), ReasonCodes.Ok);
        }
        catch (JsonException)
        {
            return (null, ReasonCodes.MalformedJson);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasExactly(JsonElement element, string[] names)
    {
        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(names, property.Name) < 0)
                return false;
            count++;
        }
        // Un campo repetido sumaría de más o taparía uno que falta: se exigen todos.
        return count == names.Length && names.All(n => element.TryGetProperty(n, out _));
    }
}
