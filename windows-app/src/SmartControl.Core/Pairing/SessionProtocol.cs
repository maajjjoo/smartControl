using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Pairing;

public enum SessionMessageType
{
    /// <summary>Reloj ya vinculado que se presenta con su clave al conectarse.</summary>
    Hello,
    /// <summary>Reloj nuevo que pide vincularse; aún no tiene clave.</summary>
    PairRequest,
    /// <summary>Latido para saber que la conexión sigue viva.</summary>
    Ping,
}

/// <summary>Mensaje de sesión ya validado. <see cref="DeviceName"/> ya está saneado.</summary>
public sealed record SessionMessage(SessionMessageType Type, string MessageId, string? DeviceId, string? SessionToken, string? DeviceName);

/// <summary>
/// Mensajes de conexión entre el reloj y Windows (docs/communication-protocol.md §3.4): HELLO,
/// PAIR_REQUEST y PING, y sus respuestas. Usan el mismo canal, la misma versión y la misma forma
/// de cabecera que COMMAND_REQUEST, con campos exactos: los desconocidos se rechazan.
/// </summary>
public static class SessionProtocol
{
    public const string Hello = "HELLO";
    public const string PairRequest = "PAIR_REQUEST";
    public const string Ping = "PING";

    public const string NotAuthenticated = "NOT_AUTHENTICATED";
    public const string PairingRejectedByUser = "USER_REJECTED";
    public const string PairingTimedOut = "PAIRING_TIMEOUT";
    public const string PairingBusy = "PAIRING_BUSY";
    public const string PairingDisabled = "PAIRING_DISABLED";

    /// <summary>Longitud máxima del nombre del reloj que se muestra en la ventana.</summary>
    public const int MaxDeviceNameLength = 40;

    private static readonly string[] HelloFields = { "protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload" };
    private static readonly string[] PairFields = { "protocolVersion", "messageId", "timestampUtc", "deviceId", "messageType", "payload" };
    private static readonly string[] PingFields = { "protocolVersion", "messageId", "timestampUtc", "messageType", "payload" };

    private static readonly Regex UuidPattern =
        new(@"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DeviceIdPattern =
        new(@"\A[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>true si el tipo pertenece a estos mensajes y no a COMMAND_REQUEST.</summary>
    public static bool IsSessionType(string? messageType) =>
        messageType is Hello or PairRequest or Ping;

    /// <summary>Lee solo el tipo de mensaje; nulo si no es JSON o no lo tiene.</summary>
    public static string? PeekMessageType(ReadOnlyMemory<byte> raw)
    {
        // Hasta el tamaño de una ventana de sensores; cada tipo aplica después su propio límite.
        if (raw.Length == 0 || raw.Length > SmartControl.Core.Recognition.GestureRecognizer.MaxMessageBytes)
            return null;
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("messageType", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Valida un mensaje de sesión. Devuelve nulo si no cumple el formato exacto.</summary>
    public static SessionMessage? Parse(ReadOnlyMemory<byte> raw)
    {
        if (raw.Length == 0 || raw.Length > CommandValidator.MaxMessageBytes)
            return null;
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var type = String(root, "messageType");
            var (expected, kind) = type switch
            {
                Hello => (HelloFields, SessionMessageType.Hello),
                PairRequest => (PairFields, SessionMessageType.PairRequest),
                Ping => (PingFields, SessionMessageType.Ping),
                _ => (null, default),
            };
            if (expected is null || !HasExactly(root, expected))
                return null;
            if (String(root, "protocolVersion") != CommandValidator.SupportedVersion)
                return null;
            var messageId = String(root, "messageId");
            if (messageId is null || !UuidPattern.IsMatch(messageId))
                return null;
            if (!root.GetProperty("timestampUtc").TryGetInt64(out _))
                return null;
            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object)
                return null;

            if (kind == SessionMessageType.Ping)
                return HasExactly(payload, Array.Empty<string>()) ? new SessionMessage(kind, messageId, null, null, null) : null;

            var deviceId = String(root, "deviceId");
            if (deviceId is null || !DeviceIdPattern.IsMatch(deviceId))
                return null;
            if (!HasExactly(payload, new[] { "deviceName" }) || String(payload, "deviceName") is not { } rawName)
                return null;
            string? token = null;
            if (kind == SessionMessageType.Hello)
            {
                token = String(root, "sessionToken");
                if (token is null || token.Length == 0 || token.Length > CommandValidator.MaxTokenLength)
                    return null;
            }
            return new SessionMessage(kind, messageId, deviceId, token, SanitizeName(rawName));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// El nombre lo elige el reloj y se muestra en la ventana: solo letras, números, espacios y
    /// unos pocos signos, recortado. Nunca vacío.
    /// </summary>
    public static string SanitizeName(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')').ToArray()).Trim();
        if (clean.Length > MaxDeviceNameLength)
            clean = clean[..MaxDeviceNameLength].TrimEnd();
        return clean.Length == 0 ? "Reloj" : clean;
    }

    public static byte[] PairPending(string correlationId, string code, string pcName) =>
        Write("PAIR_PENDING", correlationId, null, json =>
        {
            json.WriteString("code", code);
            json.WriteString("pcName", pcName);
        });

    public static byte[] PairAccepted(string correlationId, string sessionToken, string pcName, long nowMs) =>
        Write("PAIR_ACCEPTED", correlationId, nowMs, json =>
        {
            json.WriteString("sessionToken", sessionToken);
            json.WriteString("pcName", pcName);
            json.WriteNumber("serverTimeUtc", nowMs);
        });

    public static byte[] PairRejected(string? correlationId, string reasonCode) =>
        Write("PAIR_REJECTED", correlationId, null, json => json.WriteString("reasonCode", reasonCode));

    public static byte[] HelloAck(string correlationId, string pcName, long nowMs) =>
        Write("HELLO_ACK", correlationId, nowMs, json =>
        {
            json.WriteString("pcName", pcName);
            json.WriteNumber("serverTimeUtc", nowMs);
        });

    public static byte[] HelloRejected(string? correlationId, string reasonCode) =>
        Write("HELLO_REJECTED", correlationId, null, json => json.WriteString("reasonCode", reasonCode));

    public static byte[] Pong(string correlationId, long nowMs) =>
        Write("PONG", correlationId, nowMs, json => json.WriteNumber("serverTimeUtc", nowMs));

    private static byte[] Write(string type, string? correlationId, long? nowMs, Action<Utf8JsonWriter> payload)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("protocolVersion", CommandValidator.SupportedVersion);
            json.WriteString("messageId", Guid.NewGuid().ToString("D"));
            if (correlationId is null) json.WriteNull("correlationId");
            else json.WriteString("correlationId", correlationId);
            json.WriteNumber("timestampUtc", nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            json.WriteString("messageType", type);
            json.WritePropertyName("payload");
            json.WriteStartObject();
            payload(json);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static string? String(JsonElement element, string name) =>
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
        // Un nombre repetido sumaría de más: también se rechaza.
        return count == names.Length && names.All(n => element.TryGetProperty(n, out _));
    }
}
