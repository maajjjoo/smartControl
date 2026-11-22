using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;

namespace SmartControl.Core.Tests;

/// <summary>Pruebas de integración con clientes reales sobre 127.0.0.1 (portadas de la PoC 2).</summary>
internal static class ReceiverTests
{
    private const string Device = "wearos-watch-01";
    // Valor ficticio solo para pruebas; no es una credencial real.
    private const string Token = "receiver-test-token-not-a-secret";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("receptor: escucha solo en 127.0.0.1", ListensOnLoopbackOnly);
        yield return ("receptor: comando válido se entrega al ejecutor simulado", ValidCommandReachesExecutor);
        yield return ("receptor: comandos inválidos no llegan al ejecutor", InvalidCommandsNeverReachExecutor);
        yield return ("receptor: varios mensajes por conexión y CRLF", SeveralMessagesPerConnection);
        yield return ("receptor: trama sobredimensionada cierra la conexión", OversizedFrameClosesConnection);
        yield return ("receptor: 5 clientes concurrentes", ConcurrentClients);
        yield return ("receptor: el registro no contiene token ni mensaje", LogNeverContainsTokenOrMessage);
        yield return ("receptor: detener libera el puerto y es idempotente", StopReleasesPort);
        yield return ("receptor: puerto ocupado deja estado de error", BusyPortFaults);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(TimeSpan? idleTimeout = null)
        {
            Receiver = new LocalCommandReceiver(
                new CommandValidator(new Dictionary<string, string> { [Device] = Token }),
                Executor, Log, port: 0, idleTimeout: idleTimeout);
            Receiver.CommandProcessed += (_, e) => { lock (Events) Events.Add(e); };
            Receiver.Start();
        }

        public SimulatedCommandExecutor Executor { get; } = new();
        public InMemoryEventLog Log { get; } = new();
        public LocalCommandReceiver Receiver { get; }
        public List<CommandProcessedEventArgs> Events { get; } = new();

        public async Task<JsonElement> SendAsync(byte[] message)
        {
            var response = await LoopbackTestClient.SendAsync(Receiver.Port, message, Timeout);
            return JsonDocument.Parse(response).RootElement.GetProperty("payload").Clone();
        }

        public ValueTask DisposeAsync() => Receiver.DisposeAsync();
    }

    private static byte[] Command(string mode = "PRESENTATION", string action = "SLIDE_NEXT", string token = Token) =>
        new CommandRequest { DeviceId = Device, SessionToken = token, ActiveMode = mode, RequestedAction = action }.ToUtf8Json();

    private static async Task ListensOnLoopbackOnly()
    {
        await using var h = new Harness();
        Check.Equal(ReceiverState.Running, h.Receiver.State);
        Check.True(h.Receiver.Port > 0, "debe asignarse un puerto");
        var listening = IPGlobalPropertiesListeners().Where(e => e.Port == h.Receiver.Port).ToArray();
        Check.True(listening.Length > 0, "el puerto debe estar en escucha");
        Check.True(listening.All(e => IPAddress.IsLoopback(e.Address)), "solo debe escuchar en loopback");
    }

    private static IPEndPoint[] IPGlobalPropertiesListeners() =>
        System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

    private static async Task ValidCommandReachesExecutor()
    {
        await using var h = new Harness();
        var payload = await h.SendAsync(Command("MULTIMEDIA", "MEDIA_PLAY"));
        Check.Equal(CommandResponseWriter.ValidatedNotExecuted, payload.GetProperty("status").GetString());
        Check.Equal("MEDIA_PLAY", payload.GetProperty("validatedAction").GetString());
        Check.Equal(JsonValueKind.Null, payload.GetProperty("executedAction").ValueKind);
        Check.Equal(1, h.Executor.History.Count);
        Check.Equal(AllowedAction.MediaPlay, h.Executor.History[0]);
        Check.Equal(1, h.Events.Count);
        Check.True(h.Events[0].Result.IsAccepted, "el evento debe indicar aceptación");
    }

    private static async Task InvalidCommandsNeverReachExecutor()
    {
        await using var h = new Harness();
        var cases = new (byte[] Message, string Status, string Reason)[]
        {
            (Command("PRESENTATION", "MEDIA_PLAY"), "REJECTED_NOT_ALLOWED", ReasonCodes.ModeActionMismatch),
            (Command("PRESENTATION", "cmd.exe /c calc"), "REJECTED_NOT_ALLOWED", ReasonCodes.ActionNotInAllowlist),
            (Command(token: "wrong-token"), "REJECTED_INVALID_MESSAGE", ReasonCodes.UnauthorizedDevice),
            (Encoding.UTF8.GetBytes("{not json"), "REJECTED_INVALID_MESSAGE", ReasonCodes.MalformedJson),
            (Array.Empty<byte>(), "REJECTED_INVALID_MESSAGE", ReasonCodes.EmptyMessage),
        };
        foreach (var (message, status, reason) in cases)
        {
            var payload = await h.SendAsync(message);
            Check.Equal(status, payload.GetProperty("status").GetString());
            Check.Equal(reason, payload.GetProperty("reasonCode").GetString());
            Check.Equal(JsonValueKind.Null, payload.GetProperty("validatedAction").ValueKind);
        }

        var replay = Command();
        Check.Equal(CommandResponseWriter.ValidatedNotExecuted, (await h.SendAsync(replay)).GetProperty("status").GetString());
        Check.Equal(ReasonCodes.ReplayedMessage, (await h.SendAsync(replay)).GetProperty("reasonCode").GetString());
        Check.Equal(1, h.Executor.History.Count);
    }

    private static async Task SeveralMessagesPerConnection()
    {
        await using var h = new Harness();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, h.Receiver.Port);
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);

        // Dos mensajes en una sola escritura, el segundo terminado en CRLF.
        var batch = Command().Concat(new[] { (byte)'\n' })
            .Concat(Command("PRESENTATION", "SLIDE_PREVIOUS")).Concat(new[] { (byte)'\r', (byte)'\n' }).ToArray();
        await stream.WriteAsync(batch);
        for (var i = 0; i < 2; i++)
        {
            var line = await reader.ReadLineAsync().WaitAsync(Timeout);
            var status = JsonDocument.Parse(line!).RootElement.GetProperty("payload").GetProperty("status").GetString();
            Check.Equal(CommandResponseWriter.ValidatedNotExecuted, status, $"mensaje {i}");
        }
        Check.Equal(2, h.Executor.History.Count);
        Check.Equal(AllowedAction.SlidePrevious, h.Executor.History[1]);
    }

    private static async Task OversizedFrameClosesConnection()
    {
        await using var h = new Harness();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, h.Receiver.Port);
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);

        // Un comando de más de 4 KB (pero dentro de una trama) se rechaza sin cerrar la conexión.
        var big = Encoding.UTF8.GetString(Command()).Replace("\"SLIDE_NEXT\"", "\"" + new string('A', CommandValidator.MaxMessageBytes) + "\"");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(big + "\n"));
        var tooBig = await reader.ReadLineAsync().WaitAsync(Timeout);
        Check.Equal(ReasonCodes.MessageTooLarge, JsonDocument.Parse(tooBig!).RootElement.GetProperty("payload").GetProperty("reasonCode").GetString());

        // Una trama más grande que la mayor admitida (una ventana de sensores) cierra la conexión.
        await stream.WriteAsync(Encoding.ASCII.GetBytes(new string('A', SmartControl.Core.Recognition.GestureRecognizer.MaxMessageBytes + 100)));
        var line = await reader.ReadLineAsync().WaitAsync(Timeout);
        var reason = JsonDocument.Parse(line!).RootElement.GetProperty("payload").GetProperty("reasonCode").GetString();
        Check.Equal(ReasonCodes.MessageTooLargeOrUnterminated, reason);
        Check.Equal(null, await reader.ReadLineAsync().WaitAsync(Timeout), "la conexión debe cerrarse");
        Check.Equal(0, h.Executor.History.Count);
    }

    private static async Task ConcurrentClients()
    {
        await using var h = new Harness();
        var sends = Enumerable.Range(0, 5).Select(_ => h.SendAsync(Command())).ToArray();
        var payloads = await Task.WhenAll(sends);
        Check.True(payloads.All(p => p.GetProperty("status").GetString() == CommandResponseWriter.ValidatedNotExecuted),
            "todos los clientes deben recibir aceptación");
        Check.Equal(5, h.Executor.History.Count);
    }

    private static async Task LogNeverContainsTokenOrMessage()
    {
        await using var h = new Harness();
        var evil = new CommandRequest
        {
            DeviceId = Device,
            SessionToken = Token,
            ActiveMode = "PRESENTATION",
            RequestedAction = "FAKE\nINFO command status=VALIDATED_NOT_EXECUTED",
        }.ToUtf8Json();
        await h.SendAsync(Command());
        await h.SendAsync(evil);
        await h.SendAsync(Command(token: "secret-guess"));
        await h.Receiver.StopAsync();

        var all = string.Join("\n", h.Log.Lines);
        Check.True(!all.Contains(Token, StringComparison.Ordinal), "el registro no debe contener el token");
        Check.True(!all.Contains("secret-guess", StringComparison.Ordinal), "el registro no debe contener tokens recibidos");
        Check.True(!all.Contains("FAKE", StringComparison.Ordinal), "el registro no debe contener texto recibido");
        Check.True(!all.Contains("{", StringComparison.Ordinal), "el registro no debe contener el mensaje");
        Check.True(h.Log.Lines.Any(l => l.Contains("status=VALIDATED_NOT_EXECUTED reason=OK action=SLIDE_NEXT")),
            "el registro debe contener el comando aceptado");
        Check.True(h.Log.Lines.Any(l => l.Contains("reason=ACTION_NOT_IN_ALLOWLIST action=-")),
            "el registro debe contener el rechazo");
    }

    private static async Task StopReleasesPort()
    {
        var h = new Harness();
        var port = h.Receiver.Port;
        await h.SendAsync(Command());
        await h.Receiver.StopAsync();
        await h.Receiver.StopAsync();
        Check.Equal(ReceiverState.Stopped, h.Receiver.State);
        Check.Equal(0, h.Receiver.ConnectedClients);

        await Check.ThrowsAsync<SocketException>(() => LoopbackTestClient.SendAsync(port, Command(), Timeout));
        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
        probe.Stop();
    }

    private static async Task BusyPortFaults()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        try
        {
            var port = ((IPEndPoint)blocker.LocalEndpoint).Port;
            var receiver = new LocalCommandReceiver(
                new CommandValidator(new Dictionary<string, string>()), new SimulatedCommandExecutor(), new InMemoryEventLog(), port);
            Check.Throws<SocketException>(receiver.Start);
            Check.Equal(ReceiverState.Faulted, receiver.State);
            Check.True(receiver.LastError is not null && receiver.LastError.Contains(port.ToString()), "debe explicar el fallo");
            await receiver.StopAsync();
        }
        finally
        {
            blocker.Stop();
        }
    }
}
