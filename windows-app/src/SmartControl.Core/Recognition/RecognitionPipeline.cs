using System;
using System.Collections.Generic;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Recognition;

public enum RecognitionStatus
{
    /// <summary>El validador existente aceptó la acción candidata. No se ha ejecutado nada.</summary>
    Accepted,
    InvalidWindow,
    /// <summary>No hay modelo válido: el reconocimiento está desactivado y todo se rechaza.</summary>
    ModelUnavailable,
    InferenceFailed,
    NoGesture,
    LowConfidence,
    UnsupportedContext,
    RejectedByValidator,
}

/// <summary>Códigos propios del reconocimiento. Los rechazos de la ventana y del validador conservan sus códigos.</summary>
public static class RecognitionReasons
{
    public const string ModelUnavailable = "MODEL_UNAVAILABLE";
    public const string InferenceFailed = "INFERENCE_FAILED";
    public const string NoGesture = "NO_GESTURE";
    public const string LowConfidence = "LOW_CONFIDENCE";
    public const string UnsupportedContext = "UNSUPPORTED_CONTEXT";
}

public sealed record RecognitionOutcome(
    RecognitionStatus Status,
    string ReasonCode,
    GestureDecision? Decision = null,
    AllowedAction? Candidate = null,
    ValidationResult? Validation = null)
{
    /// <summary>Acción que superó el validador existente; null en cualquier otro caso.</summary>
    public AllowedAction? ValidatedAction =>
        Status == RecognitionStatus.Accepted && Validation is { IsAccepted: true } ? Validation.Action : null;
}

/// <summary>
/// Reconocimiento local en Windows (ADR-01 C, ADR-03, ADR-06):
/// ventana → características gyro-v1 → modelo → decisión → selección por modo →
/// <see cref="CommandValidator"/> existente, sin modificarlo ni omitirlo.
/// El modo es contexto de confianza que aporta quien llama. Este componente NO ejecuta
/// acciones: devuelve, como mucho, un <see cref="AllowedAction"/> ya validado.
/// </summary>
public sealed class RecognitionPipeline
{
    private readonly DenseClassifier? _classifier;
    private readonly CommandValidator _validator;
    private readonly string _deviceId;
    private readonly string _sessionToken;
    private readonly Func<long> _clockMs;

    /// <param name="classifier">null si no hay modelo válido: entonces todo se rechaza.</param>
    /// <param name="deviceId">Dispositivo de la sesión vinculada de la que procede la ventana.</param>
    /// <param name="sessionToken">Token de esa sesión; el validador comprueba la vinculación.</param>
    public RecognitionPipeline(
        DenseClassifier? classifier,
        CommandValidator validator,
        string deviceId,
        string sessionToken,
        Func<long>? clockMs = null)
    {
        _classifier = classifier;
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        _sessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        _clockMs = clockMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public bool HasModel => _classifier is not null;

    /// <param name="messageId">
    /// Identificador del mensaje del reloj del que procede la ventana. Si se indica, la candidata
    /// lo conserva (con <paramref name="timestampUtc"/>), así el validador detecta repeticiones y
    /// mensajes viejos de esa ventana. Si no, cada candidata es un mensaje nuevo.
    /// </param>
    public RecognitionOutcome Recognize(
        IReadOnlyList<SensorSample>? gyroscope,
        IReadOnlyList<SensorSample>? accelerometer,
        ControlMode mode,
        string? messageId = null,
        long? timestampUtc = null)
    {
        if (_classifier is null)
            return new RecognitionOutcome(RecognitionStatus.ModelUnavailable, RecognitionReasons.ModelUnavailable);
        if (!Enum.IsDefined(mode))
            return new RecognitionOutcome(RecognitionStatus.UnsupportedContext, RecognitionReasons.UnsupportedContext);

        if (!SensorWindow.TryCreate(gyroscope, accelerometer, out var window, out var windowReason))
            return new RecognitionOutcome(RecognitionStatus.InvalidWindow, windowReason);

        var features = MovementFeatures.Extract(window!);
        if (!_classifier.TryPredict(features, out var prediction))
            return new RecognitionOutcome(RecognitionStatus.InferenceFailed, RecognitionReasons.InferenceFailed);

        var decision = GestureDecision.Decide(prediction, _classifier.Model.DecisionThreshold);
        switch (decision.Status)
        {
            case GestureDecisionStatus.NoGesture:
                return new RecognitionOutcome(RecognitionStatus.NoGesture, RecognitionReasons.NoGesture, decision);
            case GestureDecisionStatus.LowConfidence:
                return new RecognitionOutcome(RecognitionStatus.LowConfidence, RecognitionReasons.LowConfidence, decision);
        }

        if (!ContextualActionSelector.TrySelect(mode, decision.Gesture!.Value, out var candidate))
            return new RecognitionOutcome(RecognitionStatus.UnsupportedContext, RecognitionReasons.UnsupportedContext, decision);

        // La candidata pasa por el validador existente como cualquier COMMAND_REQUEST.
        var request = new CommandRequest
        {
            MessageId = messageId ?? Guid.NewGuid().ToString("D"),
            DeviceId = _deviceId,
            SessionToken = _sessionToken,
            TimestampUtc = timestampUtc ?? _clockMs(),
            ActiveMode = CommandAllowlist.ToWireName(mode),
            GestureId = GestureId(decision.Gesture.Value),
            Confidence = decision.Confidence,
            RequestedAction = CommandAllowlist.ToWireName(candidate),
        };
        var validation = _validator.Validate(request.ToUtf8Json());
        var status = validation.IsAccepted ? RecognitionStatus.Accepted : RecognitionStatus.RejectedByValidator;
        return new RecognitionOutcome(status, validation.ReasonCode, decision, candidate, validation);
    }

    /// <summary>Identificadores de gesto del protocolo (docs/communication-protocol.md §3.1).</summary>
    private static string GestureId(GestureClass gesture) => gesture switch
    {
        GestureClass.TwistOut => "WRIST_TWIST_OUT",
        GestureClass.TwistIn => "WRIST_TWIST_IN",
        _ => throw new ArgumentOutOfRangeException(nameof(gesture)),
    };
}
