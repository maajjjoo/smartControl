using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Tests;

/// <summary>Casos de docs/communication-protocol.md §4 y docs/security-allowlist.md (portados de la PoC 1).</summary>
internal static class ValidatorTests
{
    private const long NowMs = 1792633800125;
    private const string Device = "wearos-watch-01";
    // Valor ficticio solo para pruebas; no es una credencial real.
    private const string Token = "test-token-not-a-secret";

    private static int _counter;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("validador: acepta las 4 acciones en su modo", Sync(AcceptsFourActionsInTheirMode));
        yield return ("validador: rechaza acción de otro modo", Sync(RejectsModeActionMismatch));
        yield return ("validador: rechaza acciones fuera de la lista blanca", Sync(RejectsActionsOutsideAllowlist));
        yield return ("validador: rechaza modo desconocido", Sync(RejectsUnknownMode));
        yield return ("validador: respeta el modo del receptor", Sync(RespectsReceiverMode));
        yield return ("validador: estructura de cabecera y payload", Sync(RejectsBadStructure));
        yield return ("validador: tipos y formatos de campos", Sync(RejectsBadFieldTypes));
        yield return ("validador: JSON malformado, vacío, grande o duplicado", Sync(RejectsMalformedInput));
        yield return ("validador: versión, tipo y vinculación", Sync(RejectsVersionTypeAndPairing));
        yield return ("validador: frescura y repetición", Sync(EnforcesFreshnessAndReplay));
        yield return ("validador: caché de repetición llena", Sync(RejectsWhenReplayCacheFull));
        yield return ("validador: umbral de confianza", Sync(EnforcesConfidenceThreshold));
        yield return ("ejecutor simulado: solo valores de la enumeración", Sync(ExecutorRejectsUndefinedAction));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static CommandValidator NewValidator(Func<long>? clock = null, CommandValidatorOptions? options = null) =>
        new(new Dictionary<string, string> { [Device] = Token }, options, clock ?? (() => NowMs));

    /// <summary>Mensaje válido con messageId único, al que se aplican cambios.</summary>
    private static JsonObject Message(Action<JsonObject>? change = null)
    {
        _counter++;
        var message = new JsonObject
        {
            ["protocolVersion"] = "1.0-draft",
            ["messageId"] = $"00000000-0000-4000-8000-{_counter:x12}",
            ["timestampUtc"] = NowMs,
            ["deviceId"] = Device,
            ["sessionToken"] = Token,
            ["messageType"] = "COMMAND_REQUEST",
            ["payload"] = new JsonObject
            {
                ["activeMode"] = "PRESENTATION",
                ["gestureId"] = "SWIPE_RIGHT",
                ["confidence"] = 0.94,
                ["requestedAction"] = "SLIDE_NEXT",
            },
        };
        change?.Invoke(message);
        return message;
    }

    private static JsonObject Payload(JsonObject message) => message["payload"]!.AsObject();

    private static ValidationResult Expect(CommandValidator validator, string raw, CommandStatus status, string? reason = null)
    {
        var result = validator.Validate(raw);
        Check.Equal(status, result.Status, raw.Length > 120 ? raw[..120] : raw);
        if (reason is not null)
            Check.Equal(reason, result.ReasonCode, raw.Length > 120 ? raw[..120] : raw);
        if (status != CommandStatus.Accepted)
        {
            Check.True(result.Action is null, "un rechazo nunca debe llevar acción");
            Check.True(!result.IsAccepted, "un rechazo nunca debe estar aceptado");
        }
        return result;
    }

    private static ValidationResult Expect(CommandValidator validator, JsonObject message, CommandStatus status, string? reason = null) =>
        Expect(validator, message.ToJsonString(), status, reason);

    private static void AcceptsFourActionsInTheirMode()
    {
        var cases = new (string Mode, string Action, AllowedAction Expected, ControlMode ExpectedMode)[]
        {
            ("PRESENTATION", "SLIDE_NEXT", AllowedAction.SlideNext, ControlMode.Presentation),
            ("PRESENTATION", "SLIDE_PREVIOUS", AllowedAction.SlidePrevious, ControlMode.Presentation),
            ("MULTIMEDIA", "MEDIA_PLAY", AllowedAction.MediaPlay, ControlMode.Multimedia),
            ("MULTIMEDIA", "MEDIA_PAUSE", AllowedAction.MediaPause, ControlMode.Multimedia),
            ("MULTIMEDIA", "MEDIA_NEXT_TRACK", AllowedAction.MediaNextTrack, ControlMode.Multimedia),
            ("MULTIMEDIA", "MEDIA_PREVIOUS_TRACK", AllowedAction.MediaPreviousTrack, ControlMode.Multimedia),
        };
        var validator = NewValidator();
        foreach (var (mode, action, expected, expectedMode) in cases)
        {
            var message = Message(m => { Payload(m)["activeMode"] = mode; Payload(m)["requestedAction"] = action; });
            var result = Expect(validator, message, CommandStatus.Accepted, ReasonCodes.Ok);
            Check.Equal<AllowedAction?>(expected, result.Action);
            Check.Equal<ControlMode?>(expectedMode, result.Mode);
            Check.Equal(message["messageId"]!.GetValue<string>(), result.MessageId);
        }
    }

    private static void RejectsModeActionMismatch()
    {
        var validator = NewValidator();
        foreach (var (mode, action) in new[]
                 {
                     ("MULTIMEDIA", "SLIDE_NEXT"), ("MULTIMEDIA", "SLIDE_PREVIOUS"),
                     ("PRESENTATION", "MEDIA_PLAY"), ("PRESENTATION", "MEDIA_PAUSE"),
                 })
        {
            Expect(validator, Message(m => { Payload(m)["activeMode"] = mode; Payload(m)["requestedAction"] = action; }),
                CommandStatus.RejectedNotAllowed, ReasonCodes.ModeActionMismatch);
        }
    }

    private static void RejectsActionsOutsideAllowlist()
    {
        var validator = NewValidator();
        var actions = new[]
        {
            "", "slide_next", "Slide_Next", "SLIDE_NEXT ", " SLIDE_NEXT", "SLIDE_NEXT\n", "SLIDE_NEXT\u0000",
            "SLIDE_NEXT;rm -rf /", "MEDIA_PLAY_PAUSE", "VOLUME_UP",
            "cmd.exe /c calc", "powershell -enc AAAA", "C:\\Windows\\System32\\calc.exe", "$(whoami)", "`calc`",
            "ALT+F4", "Ctrl+Alt+Del", "Win+R",
        };
        foreach (var action in actions)
            Expect(validator, Message(m => Payload(m)["requestedAction"] = action),
                CommandStatus.RejectedNotAllowed, ReasonCodes.ActionNotInAllowlist);
    }

    private static void RejectsUnknownMode()
    {
        var validator = NewValidator();
        foreach (var mode in new[] { "", "presentation", "Presentation", "SYSTEM", "PRESENTATION " })
            Expect(validator, Message(m => Payload(m)["activeMode"] = mode),
                CommandStatus.RejectedInvalidMessage, ReasonCodes.UnknownMode);
    }

    private static void RespectsReceiverMode()
    {
        var validator = NewValidator();
        validator.ReceiverMode = ControlMode.Multimedia;
        Expect(validator, Message(), CommandStatus.RejectedNotAllowed, ReasonCodes.ReceiverModeMismatch);
        Expect(validator, Message(m => { Payload(m)["activeMode"] = "MULTIMEDIA"; Payload(m)["requestedAction"] = "MEDIA_PLAY"; }),
            CommandStatus.Accepted);
        validator.ReceiverMode = null;
        Expect(validator, Message(), CommandStatus.Accepted);
    }

    private static void RejectsBadStructure()
    {
        var validator = NewValidator();
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;
        foreach (var field in new[] { "protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload" })
            Expect(validator, Message(m => m.Remove(field)), Invalid, ReasonCodes.MissingHeaderField);
        Expect(validator, Message(m => m["command"] = "calc"), Invalid, ReasonCodes.UnknownHeaderField);
        foreach (var field in new[] { "activeMode", "gestureId", "confidence", "requestedAction" })
            Expect(validator, Message(m => Payload(m).Remove(field)), Invalid, ReasonCodes.MissingPayloadField);
        Expect(validator, Message(m => Payload(m)["keys"] = "ALT+F4"), Invalid, ReasonCodes.UnknownPayloadField);
        Expect(validator, Message(m => m["payload"] = "SLIDE_NEXT"), Invalid, ReasonCodes.InvalidPayload);
        Expect(validator, Message(m => m["payload"] = new JsonArray()), Invalid, ReasonCodes.InvalidPayload);
        Expect(validator, "[]", Invalid, ReasonCodes.NotAnObject);
        Expect(validator, "\"SLIDE_NEXT\"", Invalid, ReasonCodes.NotAnObject);
        Expect(validator, "42", Invalid, ReasonCodes.NotAnObject);
    }

    private static void RejectsBadFieldTypes()
    {
        var validator = NewValidator();
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;

        foreach (var id in new JsonNode?[] { "not-a-uuid", "550E8400-E29B-41D4-A716-446655440000", "550e8400-e29b-41d4-a716-446655440000\n", 42, null })
            Expect(validator, Message(m => m["messageId"] = id), Invalid, ReasonCodes.InvalidMessageId);

        foreach (var ts in new JsonNode?[] { "1792633800125", 1792633800125.5, -1, true, null })
            Expect(validator, Message(m => m["timestampUtc"] = ts), Invalid, ReasonCodes.InvalidTimestamp);
        Expect(validator, Message().ToJsonString().Replace(NowMs.ToString(), "1.792633800125e12"), Invalid, ReasonCodes.InvalidTimestamp);

        foreach (var device in new JsonNode?[] { "", "watch 01", "watch/../01", "wearos-watch-01\n", new string('a', 65), 7 })
            Expect(validator, Message(m => m["deviceId"] = device), Invalid, ReasonCodes.InvalidDeviceId);

        foreach (var token in new JsonNode?[] { "", new string('t', CommandValidator.MaxTokenLength + 1), 123, null })
            Expect(validator, Message(m => m["sessionToken"] = token), Invalid, ReasonCodes.InvalidSessionToken);

        Expect(validator, Message(m => Payload(m)["activeMode"] = 1), Invalid, ReasonCodes.InvalidFieldType);
        Expect(validator, Message(m => Payload(m)["requestedAction"] = new JsonArray("SLIDE_NEXT")), Invalid, ReasonCodes.InvalidFieldType);
        Expect(validator, Message(m => Payload(m)["gestureId"] = null), Invalid, ReasonCodes.InvalidFieldType);
        foreach (var gesture in new[] { "", "swipe_right", "SWIPE RIGHT", "1SWIPE", "SWIPE_RIGHT\n" })
            Expect(validator, Message(m => Payload(m)["gestureId"] = gesture), Invalid, ReasonCodes.InvalidGestureId);

        foreach (var confidence in new JsonNode?[] { "0.9", true, null, -0.01, 1.01, 1e300 })
            Expect(validator, Message(m => Payload(m)["confidence"] = confidence), Invalid, ReasonCodes.InvalidConfidence);
        Expect(validator, Message().ToJsonString().Replace("0.94", "1e400"), Invalid, ReasonCodes.InvalidConfidence);
    }

    private static void RejectsMalformedInput()
    {
        var validator = NewValidator();
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;

        Expect(validator, "", Invalid, ReasonCodes.EmptyMessage);
        foreach (var raw in new[]
                 {
                     "{not json", "{\"a\":1,}", "{\"a\":1} // c", "/* c */ {}", "{'a':1}", "{\"a\":NaN}", "{\"a\":Infinity}",
                     "{} {}", "{\"a\":" + new string('[', 50) + new string(']', 50) + "}",
                 })
            Expect(validator, raw, Invalid, ReasonCodes.MalformedJson);

        // UTF-8 inválido.
        var bytes = Encoding.UTF8.GetBytes(Message().ToJsonString().Replace("SWIPE_RIGHT", "SWIPE_XRIGHT"));
        var index = Encoding.UTF8.GetString(bytes).IndexOf("XRIGHT", StringComparison.Ordinal);
        bytes[index] = 0xFF;
        var invalidUtf8 = validator.Validate(bytes);
        Check.Equal(CommandStatus.RejectedInvalidMessage, invalidUtf8.Status);
        Check.Equal(ReasonCodes.MalformedJson, invalidUtf8.ReasonCode);

        // Claves repetidas: arriba y dentro del payload (la última no puede "ganar" en silencio).
        var json = Message().ToJsonString();
        Expect(validator, json.Replace("\"deviceId\":", "\"deviceId\":\"other\",\"deviceId\":"), Invalid, ReasonCodes.MalformedJson);
        Expect(validator, json.Replace("\"requestedAction\":\"SLIDE_NEXT\"", "\"requestedAction\":\"SLIDE_NEXT\",\"requestedAction\":\"cmd.exe\""),
            Invalid, ReasonCodes.MalformedJson);

        // Tamaño: justo en el límite pasa la comprobación de tamaño; uno más, no.
        var fits = Message().ToJsonString();
        fits = new string(' ', CommandValidator.MaxMessageBytes - Encoding.UTF8.GetByteCount(fits)) + fits;
        Check.Equal(CommandValidator.MaxMessageBytes, Encoding.UTF8.GetByteCount(fits));
        Expect(validator, fits, CommandStatus.Accepted);
        var tooBig = Message().ToJsonString();
        tooBig = new string(' ', CommandValidator.MaxMessageBytes + 1 - Encoding.UTF8.GetByteCount(tooBig)) + tooBig;
        Expect(validator, tooBig, Invalid, ReasonCodes.MessageTooLarge);
    }

    private static void RejectsVersionTypeAndPairing()
    {
        var validator = NewValidator();
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;
        foreach (var version in new JsonNode?[] { "1.0", "2.0-draft", "1.0-DRAFT", 1, null })
            Expect(validator, Message(m => m["protocolVersion"] = version), Invalid, ReasonCodes.UnsupportedVersion);
        foreach (var type in new JsonNode?[] { "COMMAND_RESPONSE", "SENSOR_WINDOW", "command_request", "EXEC", 0 })
            Expect(validator, Message(m => m["messageType"] = type), Invalid, ReasonCodes.UnsupportedMessageType);
        Expect(validator, Message(m => m["sessionToken"] = Token + "x"), Invalid, ReasonCodes.UnauthorizedDevice);
        Expect(validator, Message(m => m["sessionToken"] = Token.ToUpperInvariant()), Invalid, ReasonCodes.UnauthorizedDevice);
        Expect(validator, Message(m => m["deviceId"] = "other-watch"), Invalid, ReasonCodes.UnauthorizedDevice);
    }

    private static void EnforcesFreshnessAndReplay()
    {
        var now = NowMs;
        var validator = NewValidator(() => now);
        const CommandStatus Invalid = CommandStatus.RejectedInvalidMessage;

        Expect(validator, Message(m => m["timestampUtc"] = NowMs - 5000), CommandStatus.Accepted);
        Expect(validator, Message(m => m["timestampUtc"] = NowMs - 5001), Invalid, ReasonCodes.StaleMessage);
        Expect(validator, Message(m => m["timestampUtc"] = NowMs + 2000), CommandStatus.Accepted);
        Expect(validator, Message(m => m["timestampUtc"] = NowMs + 2001), Invalid, ReasonCodes.FutureTimestamp);

        var original = Message().ToJsonString();
        Expect(validator, original, CommandStatus.Accepted);
        Expect(validator, original, Invalid, ReasonCodes.ReplayedMessage);

        // El mismo messageId con otra acción también es una repetición.
        var replayedWithOtherAction = original.Replace("SLIDE_NEXT", "SLIDE_PREVIOUS");
        Expect(validator, replayedWithOtherAction, Invalid, ReasonCodes.ReplayedMessage);

        // Pasada la ventana, el mensaje antiguo se rechaza por antigüedad, no se acepta.
        now += 60_000;
        Expect(validator, original, Invalid, ReasonCodes.StaleMessage);
    }

    private static void RejectsWhenReplayCacheFull()
    {
        var now = NowMs;
        var validator = NewValidator(() => now, new CommandValidatorOptions { ReplayCacheSize = 3 });
        for (var i = 0; i < 3; i++)
            Expect(validator, Message(), CommandStatus.Accepted);
        Expect(validator, Message(), CommandStatus.RejectedInvalidMessage, ReasonCodes.ReplayCacheFull);

        // Cuando los identificadores caducan, se liberan.
        now += 7001;
        Expect(validator, Message(m => m["timestampUtc"] = now), CommandStatus.Accepted);
    }

    private static void EnforcesConfidenceThreshold()
    {
        var validator = NewValidator();
        Expect(validator, Message(m => Payload(m)["confidence"] = 0.80), CommandStatus.Accepted);
        Expect(validator, Message(m => Payload(m)["confidence"] = 0.7999), CommandStatus.RejectedLowConfidence, ReasonCodes.BelowThreshold);
        Expect(validator, Message(m => Payload(m)["confidence"] = 0), CommandStatus.RejectedLowConfidence, ReasonCodes.BelowThreshold);
        Expect(validator, Message(m => Payload(m)["confidence"] = 1), CommandStatus.Accepted);
        // La confianza baja se evalúa antes de la lista blanca: nunca llega a aceptarse.
        Expect(validator, Message(m => { Payload(m)["confidence"] = 0.1; Payload(m)["requestedAction"] = "cmd.exe"; }),
            CommandStatus.RejectedLowConfidence);

        Check.Throws<ArgumentOutOfRangeException>(() => NewValidator(options: new CommandValidatorOptions { ConfidenceThreshold = 1.5 }));
        Check.Throws<ArgumentOutOfRangeException>(() => NewValidator(options: new CommandValidatorOptions { ConfidenceThreshold = double.NaN }));
    }

    private static void ExecutorRejectsUndefinedAction()
    {
        var executor = new SimulatedCommandExecutor();
        Check.Throws<ArgumentOutOfRangeException>(() => executor.Execute((AllowedAction)99));
        Check.Equal(0, executor.History.Count);
        executor.Execute(AllowedAction.MediaPause);
        Check.Equal(1, executor.History.Count);
        Check.Equal(AllowedAction.MediaPause, executor.History[0]);
    }
}
