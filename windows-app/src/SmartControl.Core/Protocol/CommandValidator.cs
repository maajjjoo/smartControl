using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using SmartControl.Core.Pairing;

namespace SmartControl.Core.Protocol;

/// <summary>Parámetros de ensayo del validador. No son decisiones definitivas.</summary>
public sealed record CommandValidatorOptions
{
    public double ConfidenceThreshold { get; init; } = 0.80;
    public long MaxAgeMs { get; init; } = 5000;
    public long MaxFutureSkewMs { get; init; } = 2000;
    public int ReplayCacheSize { get; init; } = 1024;
}

/// <summary>
/// Valida mensajes COMMAND_REQUEST según docs/communication-protocol.md §4 y
/// docs/security-allowlist.md. Es la versión C# de poc/command-validator.
/// No ejecuta nada: solo devuelve un <see cref="AllowedAction"/> cuando el mensaje es aceptable.
/// Es seguro usarlo desde varios hilos.
/// </summary>
public sealed class CommandValidator
{
    public const string SupportedVersion = "1.0-draft";
    public const string CommandRequestType = "COMMAND_REQUEST";
    public const int MaxMessageBytes = 4096;
    public const int MaxTokenLength = 256;
    private const int MaxJsonDepth = 8;

    private static readonly string[] HeaderFields =
        { "protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload" };
    private static readonly string[] PayloadFields =
        { "activeMode", "gestureId", "confidence", "requestedAction" };

    // \z en lugar de $: en .NET, $ también acepta un salto de línea final.
    private static readonly Regex UuidPattern =
        new(@"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DeviceIdPattern =
        new(@"\A[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex GestureIdPattern =
        new(@"\A[A-Z][A-Z0-9_]{0,63}\z", RegexOptions.CultureInvariant);

    private readonly PairedDeviceRegistry _devices;
    private readonly CommandValidatorOptions _options;
    private readonly Func<long> _clockMs;

    private readonly object _lock = new();
    private readonly Queue<(string Id, long SeenAt)> _seenOrder = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private ControlMode? _receiverMode;

    /// <summary>Validador con dispositivos fijos (pruebas y herramientas).</summary>
    public CommandValidator(
        IReadOnlyDictionary<string, string> pairedDevices,
        CommandValidatorOptions? options = null,
        Func<long>? clockMs = null)
        : this(SessionRegistry(pairedDevices), options, clockMs)
    {
    }

    /// <summary>
    /// Validador que consulta <paramref name="devices"/> en cada mensaje, así que un reloj recién
    /// vinculado u olvidado cuenta en el acto.
    /// </summary>
    public CommandValidator(
        PairedDeviceRegistry devices,
        CommandValidatorOptions? options = null,
        Func<long>? clockMs = null)
    {
        _options = options ?? new CommandValidatorOptions();
        if (!(_options.ConfidenceThreshold >= 0.0 && _options.ConfidenceThreshold <= 1.0))
            throw new ArgumentOutOfRangeException(nameof(options), "ConfidenceThreshold fuera de [0, 1]");
        if (_options.ReplayCacheSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ReplayCacheSize debe ser positivo");

        _devices = devices;
        _clockMs = clockMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static PairedDeviceRegistry SessionRegistry(IReadOnlyDictionary<string, string> pairedDevices)
    {
        var registry = new PairedDeviceRegistry();
        foreach (var (deviceId, token) in pairedDevices)
            registry.AddSessionDevice(deviceId, token, deviceId);
        return registry;
    }

    /// <summary>Si tiene valor, solo se aceptan acciones de ese modo.</summary>
    public ControlMode? ReceiverMode
    {
        get { lock (_lock) return _receiverMode; }
        set { lock (_lock) _receiverMode = value; }
    }

    public ValidationResult Validate(string message) => Validate(Encoding.UTF8.GetBytes(message));

    /// <summary>Valida un mensaje sin delimitador. Nunca lanza excepciones.</summary>
    public ValidationResult Validate(ReadOnlyMemory<byte> raw)
    {
        try
        {
            return ValidateCore(raw);
        }
        catch (Exception)
        {
            // Cualquier fallo inesperado termina en rechazo, nunca en aceptación.
            return ValidationResult.Reject(CommandStatus.RejectedInvalidMessage, ReasonCodes.InternalValidationError);
        }
    }

    private ValidationResult ValidateCore(ReadOnlyMemory<byte> raw)
    {
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;

        // 1. Tamaño y sintaxis.
        if (raw.Length == 0)
            return ValidationResult.Reject(Invalid, ReasonCodes.EmptyMessage);
        if (raw.Length > MaxMessageBytes)
            return ValidationResult.Reject(Invalid, ReasonCodes.MessageTooLarge);

        // JsonDocument solo comprueba el UTF-8 de las cadenas al leerlas; se valida antes.
        if (!Utf8.IsValid(raw.Span))
            return ValidationResult.Reject(Invalid, ReasonCodes.MalformedJson);

        JsonDocument document;
        try
        {
            // Las opciones por defecto ya rechazan comentarios, comas finales y NaN.
            document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        }
        catch (JsonException)
        {
            return ValidationResult.Reject(Invalid, ReasonCodes.MalformedJson);
        }

        using (document)
        {
            var root = document.RootElement;
            // System.Text.Json (.NET 8) acepta claves repetidas en silencio; aquí se rechazan.
            if (HasDuplicateProperties(root))
                return ValidationResult.Reject(Invalid, ReasonCodes.MalformedJson);

            // 1b. Estructura y tipos de la cabecera.
            if (root.ValueKind != JsonValueKind.Object)
                return ValidationResult.Reject(Invalid, ReasonCodes.NotAnObject);
            var headerProblem = CheckExactFields(root, HeaderFields, ReasonCodes.MissingHeaderField, ReasonCodes.UnknownHeaderField);
            if (headerProblem is not null)
                return ValidationResult.Reject(Invalid, headerProblem);

            var messageIdElement = root.GetProperty("messageId");
            if (messageIdElement.ValueKind != JsonValueKind.String || !UuidPattern.IsMatch(messageIdElement.GetString()!))
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidMessageId);
            var messageId = messageIdElement.GetString()!;

            var timestampElement = root.GetProperty("timestampUtc");
            if (timestampElement.ValueKind != JsonValueKind.Number || !timestampElement.TryGetInt64(out var timestamp) || timestamp < 0)
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidTimestamp, messageId);

            var deviceIdElement = root.GetProperty("deviceId");
            if (deviceIdElement.ValueKind != JsonValueKind.String || !DeviceIdPattern.IsMatch(deviceIdElement.GetString()!))
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidDeviceId, messageId);
            var deviceId = deviceIdElement.GetString()!;

            var tokenElement = root.GetProperty("sessionToken");
            var token = tokenElement.ValueKind == JsonValueKind.String ? tokenElement.GetString()! : null;
            if (token is null || token.Length == 0 || token.Length > MaxTokenLength)
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidSessionToken, messageId);

            var typeElement = root.GetProperty("messageType");
            if (typeElement.ValueKind != JsonValueKind.String || typeElement.GetString() != CommandRequestType)
                return ValidationResult.Reject(Invalid, ReasonCodes.UnsupportedMessageType, messageId);

            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object)
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidPayload, messageId);
            var payloadProblem = CheckExactFields(payload, PayloadFields, ReasonCodes.MissingPayloadField, ReasonCodes.UnknownPayloadField);
            if (payloadProblem is not null)
                return ValidationResult.Reject(Invalid, payloadProblem, messageId);

            var modeElement = payload.GetProperty("activeMode");
            var gestureElement = payload.GetProperty("gestureId");
            var actionElement = payload.GetProperty("requestedAction");
            if (modeElement.ValueKind != JsonValueKind.String
                || gestureElement.ValueKind != JsonValueKind.String
                || actionElement.ValueKind != JsonValueKind.String)
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidFieldType, messageId);
            if (!GestureIdPattern.IsMatch(gestureElement.GetString()!))
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidGestureId, messageId);

            var confidenceElement = payload.GetProperty("confidence");
            if (confidenceElement.ValueKind != JsonValueKind.Number
                || !confidenceElement.TryGetDouble(out var confidence)
                || !double.IsFinite(confidence)
                || confidence < 0.0 || confidence > 1.0)
                return ValidationResult.Reject(Invalid, ReasonCodes.InvalidConfidence, messageId);

            // 2. Versión.
            var versionElement = root.GetProperty("protocolVersion");
            if (versionElement.ValueKind != JsonValueKind.String || versionElement.GetString() != SupportedVersion)
                return ValidationResult.Reject(Invalid, ReasonCodes.UnsupportedVersion, messageId);

            // 3. Vinculación del dispositivo (comparación en tiempo constante).
            if (!_devices.TryGetToken(deviceId, out var expectedToken)
                || !CryptographicOperations.FixedTimeEquals(expectedToken, Encoding.UTF8.GetBytes(token)))
                return ValidationResult.Reject(Invalid, ReasonCodes.UnauthorizedDevice, messageId);

            ControlMode? receiverMode;
            lock (_lock)
            {
                // 4. Frescura y repetición.
                var now = _clockMs();
                var age = now - timestamp;
                if (age > _options.MaxAgeMs)
                    return ValidationResult.Reject(Invalid, ReasonCodes.StaleMessage, messageId);
                if (-age > _options.MaxFutureSkewMs)
                    return ValidationResult.Reject(Invalid, ReasonCodes.FutureTimestamp, messageId);
                if (_seen.Contains(messageId))
                    return ValidationResult.Reject(Invalid, ReasonCodes.ReplayedMessage, messageId);
                if (!Remember(messageId, now))
                    return ValidationResult.Reject(Invalid, ReasonCodes.ReplayCacheFull, messageId);
                receiverMode = _receiverMode;
            }

            // 5. Umbral de confianza.
            if (confidence < _options.ConfidenceThreshold)
                return ValidationResult.Reject(CommandStatus.RejectedLowConfidence, ReasonCodes.BelowThreshold, messageId);

            // 6. Modo y lista blanca (coincidencia exacta, sensible a mayúsculas).
            if (!CommandAllowlist.TryParseMode(modeElement.GetString()!, out var mode))
                return ValidationResult.Reject(Invalid, ReasonCodes.UnknownMode, messageId);
            if (!CommandAllowlist.TryParseAction(actionElement.GetString()!, out var action))
                return ValidationResult.Reject(CommandStatus.RejectedNotAllowed, ReasonCodes.ActionNotInAllowlist, messageId);
            if (CommandAllowlist.RequiredMode(action) != mode)
                return ValidationResult.Reject(CommandStatus.RejectedNotAllowed, ReasonCodes.ModeActionMismatch, messageId);
            if (receiverMode is not null && receiverMode != mode)
                return ValidationResult.Reject(CommandStatus.RejectedNotAllowed, ReasonCodes.ReceiverModeMismatch, messageId);

            return new ValidationResult(CommandStatus.Accepted, ReasonCodes.Ok, action, mode, messageId);
        }
    }

    // Debe llamarse con _lock tomado.
    private bool Remember(string messageId, long now)
    {
        // Un identificador más antiguo que la ventana de frescura ya no puede repetirse
        // con éxito (se rechazaría por STALE), así que se olvida. Si la caché está llena
        // de identificadores vigentes, se rechaza el mensaje en vez de olvidar uno.
        var window = _options.MaxAgeMs + _options.MaxFutureSkewMs;
        while (_seenOrder.Count > 0 && now - _seenOrder.Peek().SeenAt > window)
            _seen.Remove(_seenOrder.Dequeue().Id);
        if (_seen.Count >= _options.ReplayCacheSize)
            return false;
        _seen.Add(messageId);
        _seenOrder.Enqueue((messageId, now));
        return true;
    }

    private static string? CheckExactFields(JsonElement obj, string[] expected, string missingCode, string unknownCode)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
            present.Add(property.Name);
        foreach (var field in expected)
            if (!present.Contains(field))
                return missingCode;
        if (present.Count != expected.Length)
            return unknownCode;
        return null;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name) || HasDuplicateProperties(property.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (HasDuplicateProperties(item))
                        return true;
                return false;
            default:
                return false;
        }
    }
}
