namespace SmartControl.Core.Protocol;

public enum CommandStatus
{
    /// <summary>Validado y entregado al ejecutor, que lo realizó o lo simuló.</summary>
    Accepted,
    RejectedLowConfidence,
    RejectedNotAllowed,
    RejectedInvalidMessage,
    ExecutionError,
}

/// <summary>Códigos de motivo. Son constantes propias: nunca contienen datos recibidos.</summary>
public static class ReasonCodes
{
    public const string Ok = "OK";
    public const string EmptyMessage = "EMPTY_MESSAGE";
    public const string MessageTooLarge = "MESSAGE_TOO_LARGE";
    public const string MessageTooLargeOrUnterminated = "MESSAGE_TOO_LARGE_OR_UNTERMINATED";
    public const string MalformedJson = "MALFORMED_JSON";
    public const string NotAnObject = "NOT_AN_OBJECT";
    public const string MissingHeaderField = "MISSING_HEADER_FIELD";
    public const string UnknownHeaderField = "UNKNOWN_HEADER_FIELD";
    public const string InvalidMessageId = "INVALID_MESSAGE_ID";
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";
    public const string InvalidDeviceId = "INVALID_DEVICE_ID";
    public const string InvalidSessionToken = "INVALID_SESSION_TOKEN";
    public const string UnsupportedMessageType = "UNSUPPORTED_MESSAGE_TYPE";
    public const string InvalidPayload = "INVALID_PAYLOAD";
    public const string MissingPayloadField = "MISSING_PAYLOAD_FIELD";
    public const string UnknownPayloadField = "UNKNOWN_PAYLOAD_FIELD";
    public const string InvalidFieldType = "INVALID_FIELD_TYPE";
    public const string InvalidGestureId = "INVALID_GESTURE_ID";
    public const string InvalidConfidence = "INVALID_CONFIDENCE";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string UnauthorizedDevice = "UNAUTHORIZED_DEVICE";
    public const string StaleMessage = "STALE_MESSAGE";
    public const string FutureTimestamp = "FUTURE_TIMESTAMP";
    public const string ReplayedMessage = "REPLAYED_MESSAGE";
    public const string ReplayCacheFull = "REPLAY_CACHE_FULL";
    public const string BelowThreshold = "BELOW_THRESHOLD";
    public const string UnknownMode = "UNKNOWN_MODE";
    public const string ActionNotInAllowlist = "ACTION_NOT_IN_ALLOWLIST";
    public const string ModeActionMismatch = "MODE_ACTION_MISMATCH";
    public const string ReceiverModeMismatch = "RECEIVER_MODE_MISMATCH";
    public const string ExecutorFailed = "EXECUTOR_FAILED";
    /// <summary>No hay ningún reproductor multimedia abierto al que pedirle la acción.</summary>
    public const string NoMediaSession = "NO_MEDIA_SESSION";
    /// <summary>El reproductor existe pero rechazó la orden de reproducir o pausar.</summary>
    public const string MediaCommandRefused = "MEDIA_COMMAND_REFUSED";
    /// <summary>Windows no dejó enviar la tecla (por ejemplo, la ventana activa es de administrador).</summary>
    public const string InputBlocked = "INPUT_BLOCKED";
    public const string InternalValidationError = "INTERNAL_VALIDATION_ERROR";
}

/// <summary>
/// Resultado de validar un mensaje. <see cref="MessageId"/> solo se rellena cuando
/// ya superó el formato UUID, así que es seguro mostrarlo y registrarlo.
/// </summary>
public sealed record ValidationResult(
    CommandStatus Status,
    string ReasonCode,
    AllowedAction? Action = null,
    ControlMode? Mode = null,
    string? MessageId = null)
{
    public bool IsAccepted => Status == CommandStatus.Accepted && Action is not null;

    public static ValidationResult Reject(CommandStatus status, string reasonCode, string? messageId = null) =>
        new(status, reasonCode, MessageId: messageId);
}
