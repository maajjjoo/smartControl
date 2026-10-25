using System;
using System.IO;
using System.Text.Json;

namespace SmartControl.Core.Protocol;

/// <summary>Construye las respuestas COMMAND_RESPONSE que el receptor devuelve.</summary>
public static class CommandResponseWriter
{
    /// <summary>
    /// Estado propio de esta etapa: el comando se validó y se entregó al ejecutor
    /// simulado, pero no se realizó ninguna acción real. No forma parte del protocolo.
    /// </summary>
    public const string ValidatedNotExecuted = "VALIDATED_NOT_EXECUTED";

    public static string ToWireStatus(CommandStatus status) => status switch
    {
        CommandStatus.Accepted => ValidatedNotExecuted,
        CommandStatus.RejectedLowConfidence => "REJECTED_LOW_CONFIDENCE",
        CommandStatus.RejectedNotAllowed => "REJECTED_NOT_ALLOWED",
        CommandStatus.RejectedInvalidMessage => "REJECTED_INVALID_MESSAGE",
        CommandStatus.ExecutionError => "EXECUTION_ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>Devuelve la respuesta en UTF-8, sin el salto de línea final.</summary>
    public static byte[] Write(ValidationResult result, string? effect, long nowMs)
    {
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
            json.WriteString("status", ToWireStatus(result.Status));
            json.WriteNull("executedAction");
            WriteStringOrNull(json, "validatedAction",
                result.Action is { } action ? CommandAllowlist.ToWireName(action) : null);
            json.WriteString("reasonCode", result.ReasonCode);
            json.WriteString("message", effect ?? "Comando rechazado; no se ejecutó ninguna acción");
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
