using System;
using System.IO;
using System.Text.Json;

namespace SmartControl.Core.Protocol;

/// <summary>Construye las respuestas COMMAND_RESPONSE que el receptor devuelve.</summary>
public static class CommandResponseWriter
{
    /// <summary>
    /// El comando se validó y se entregó a un ejecutor simulado: no se realizó ninguna acción
    /// real. Solo aparece en pruebas. No forma parte del protocolo.
    /// </summary>
    public const string ValidatedNotExecuted = "VALIDATED_NOT_EXECUTED";

    /// <summary>La acción se realizó en Windows (protocolo §3.2).</summary>
    public const string Executed = "EXECUTED";

    public static string ToWireStatus(CommandStatus status, bool executed = false) => status switch
    {
        CommandStatus.Accepted => executed ? Executed : ValidatedNotExecuted,
        CommandStatus.RejectedLowConfidence => "REJECTED_LOW_CONFIDENCE",
        CommandStatus.RejectedNotAllowed => "REJECTED_NOT_ALLOWED",
        CommandStatus.RejectedInvalidMessage => "REJECTED_INVALID_MESSAGE",
        CommandStatus.ExecutionError => "EXECUTION_ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>
    /// Devuelve la respuesta en UTF-8, sin el salto de línea final. <paramref name="executed"/>
    /// indica que el ejecutor actúa de verdad (no simulado).
    /// </summary>
    public static byte[] Write(ValidationResult result, string? effect, long nowMs, bool executed = false,
        SmartControl.Core.Recognition.RecognitionInfo? recognition = null)
    {
        var acceptedAction = result.Status == CommandStatus.Accepted && result.Action is { } a
            ? CommandAllowlist.ToWireName(a)
            : null;
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("protocolVersion", CommandValidator.SupportedVersion);
            json.WriteString("messageId", Guid.NewGuid().ToString("D"));
            WriteStringOrNull(json, "correlationId", result.MessageId);
            json.WriteNumber("timestampUtc", nowMs);
            json.WriteString("messageType", "COMMAND_RESPONSE");
            json.WritePropertyName("payload");
            json.WriteStartObject();
            json.WriteString("status", ToWireStatus(result.Status, executed));
            WriteStringOrNull(json, "executedAction", executed ? acceptedAction : null);
            WriteStringOrNull(json, "validatedAction",
                result.Action is { } action ? CommandAllowlist.ToWireName(action) : null);
            json.WriteString("reasonCode", result.ReasonCode);
            json.WriteString("message", effect ?? "Comando rechazado; no se ejecutó ninguna acción");
            if (recognition is not null)
            {
                // Solo en respuestas a SENSOR_WINDOW: quién decidió el gesto y con qué confianza.
                json.WriteString("recognizedBy", recognition.RecognizedBy);
                WriteStringOrNull(json, "gesture", recognition.Gesture);
                json.WriteNumber("confidence", Math.Round(recognition.Confidence, 3));
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteStringOrNull(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null) json.WriteNull(name);
        else json.WriteString(name, value);
    }
}

/// <summary>
/// Construye mensajes COMMAND_REQUEST de prueba. Acepta texto libre en modo y acción
/// a propósito, para poder generar también casos que el validador debe rechazar.
/// Los comandos así generados son explícitos: no proceden de ningún gesto reconocido.
/// </summary>
public sealed record CommandRequest
{
    public string ProtocolVersion { get; init; } = CommandValidator.SupportedVersion;
    public string MessageId { get; init; } = Guid.NewGuid().ToString("D");
    public long TimestampUtc { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public required string DeviceId { get; init; }
    public required string SessionToken { get; init; }
    public string MessageType { get; init; } = CommandValidator.CommandRequestType;
    public required string ActiveMode { get; init; }
    public string GestureId { get; init; } = "TEST_COMMAND";
    public double Confidence { get; init; } = 1.0;
    public required string RequestedAction { get; init; }

    public byte[] ToUtf8Json()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("protocolVersion", ProtocolVersion);
            json.WriteString("messageId", MessageId);
            json.WriteNumber("timestampUtc", TimestampUtc);
            json.WriteString("deviceId", DeviceId);
            json.WriteString("sessionToken", SessionToken);
            json.WriteString("messageType", MessageType);
            json.WritePropertyName("payload");
            json.WriteStartObject();
            json.WriteString("activeMode", ActiveMode);
            json.WriteString("gestureId", GestureId);
            json.WriteNumber("confidence", Confidence);
            json.WriteString("requestedAction", RequestedAction);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
