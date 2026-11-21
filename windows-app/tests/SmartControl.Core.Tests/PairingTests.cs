using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;

namespace SmartControl.Core.Tests;

/// <summary>
/// Vinculación, saludo, latido y búsqueda, con conexiones reales en 127.0.0.1 que hacen de reloj.
/// Una prueba en loopback comprueba el protocolo, no la red Wi-Fi.
/// </summary>
internal static class PairingTests
{
    private const string Watch = "watch-test-01";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("vinculación: aceptar entrega una clave que sirve para los comandos", AcceptedPairingAllowsCommands);
        yield return ("vinculación: rechazar cierra la conexión sin clave", RejectedPairingClosesConnection);
        yield return ("vinculación: sin respuesta caduca", PairingTimesOut);
        yield return ("vinculación: solo una solicitud a la vez", OnlyOnePairingAtATime);
        yield return ("saludo: clave correcta, incorrecta y del panel de prueba", HelloChecksToken);
        yield return ("latido: solo después del saludo", PingNeedsHello);
        yield return ("olvidar: la clave deja de servir en el acto", ForgetRevokesToken);
        yield return ("sesión: mensajes con campos de más o mal formados se rechazan", MalformedSessionMessagesAreRejected);
        yield return ("sesión: el nombre del reloj se sanea", DeviceNameIsSanitized);
        yield return ("búsqueda: responde solo a la pregunta exacta", DiscoveryAnswersExactQuery);
        yield return ("sin vinculación activada, HELLO es un tipo no admitido", SessionMessagesNeedCoordinator);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(TimeSpan? pairingTimeout = null)
        {
            Registry.AddSessionDevice("panel", new string('P', 64), "Panel de prueba");
            Coordinator = new PairingCoordinator(Registry, pairingTimeout);
            Coordinator.PairingRequested += (_, r) => { lock (Requests) Requests.Add(r); };
            Receiver = new LocalCommandReceiver(new CommandValidator(Registry), new SimulatedCommandExecutor(), Log, port: 0,
                pairing: Coordinator, options: new ReceiverOptions { PcName = "PC-PRUEBA", DiscoveryPort = 0 });
            Receiver.Start();
        }

        public PairedDeviceRegistry Registry { get; } = new();
        public PairingCoordinator Coordinator { get; }
        public InMemoryEventLog Log { get; } = new();
        public LocalCommandReceiver Receiver { get; }
        public List<PairingRequest> Requests { get; } = new();

        public async Task<PairingRequest> NextRequest()
        {
            for (var i = 0; i < 200; i++)
            {
                lock (Requests) if (Requests.Count > 0) { var r = Requests[^1]; Requests.Clear(); return r; }
                await Task.Delay(10);
            }
            throw new TestFailure("no llegó la solicitud de vinculación a la ventana");
        }

        public ValueTask DisposeAsync() => Receiver.DisposeAsync();
    }

    /// <summary>Conexión que hace de reloj: envía líneas JSON y lee las respuestas.</summary>
    private sealed class FakeWatch : IAsyncDisposable
    {
        private readonly TcpClient _client = new();
        private StreamReader? _reader;
        private Stream? _stream;

        public static async Task<FakeWatch> Connect(int port)
        {
            var watch = new FakeWatch();
            await watch._client.ConnectAsync(IPAddress.Loopback, port);
            watch._stream = watch._client.GetStream();
            watch._reader = new StreamReader(watch._stream, Encoding.UTF8);
            return watch;
        }

        public async Task Send(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await _stream!.WriteAsync(bytes);
        }

        /// <summary>Lee una respuesta, o nulo si Windows cerró la conexión.</summary>
        public async Task<JsonElement?> Read()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var line = await _reader!.ReadLineAsync(timeout.Token);
            return line is null ? null : JsonDocument.Parse(line).RootElement.Clone();
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string Id() => Guid.NewGuid().ToString("D");

    private static string Header() =>
        "{\"protocolVersion\":\"1.0-draft\",\"messageId\":\"" + Id() + "\",\"timestampUtc\":" + Now;

    private static string PairRequest(string deviceId = Watch, string name = "Reloj de prueba") =>
        Header() + ",\"deviceId\":\"" + deviceId + "\",\"messageType\":\"PAIR_REQUEST\",\"payload\":{\"deviceName\":" + JsonSerializer.Serialize(name) + "}}";

    private static string Hello(string token, string deviceId = Watch) =>
        Header() + ",\"deviceId\":\"" + deviceId + "\",\"sessionToken\":\"" + token + "\",\"messageType\":\"HELLO\",\"payload\":{\"deviceName\":\"Reloj de prueba\"}}";

    private static string Ping() =>
        Header() + ",\"messageType\":\"PING\",\"payload\":{}}";

    private static string Command(string token, string deviceId = Watch) =>
        Encoding.UTF8.GetString(new CommandRequest { DeviceId = deviceId, SessionToken = token, ActiveMode = "PRESENTATION", RequestedAction = "SLIDE_NEXT" }.ToUtf8Json());

    private static string Type(JsonElement? message) => message?.GetProperty("messageType").GetString() ?? "(conexión cerrada)";
    private static string Payload(JsonElement? message, string name) => message!.Value.GetProperty("payload").GetProperty(name).ToString();

    private static async Task<string> PairAndGetToken(Harness h, FakeWatch watch)
    {
        await watch.Send(PairRequest());
        var pending = await watch.Read();
        Check.Equal("PAIR_PENDING", Type(pending));
        var request = await h.NextRequest();
        Check.Equal(Payload(pending, "code"), request.Code, "el reloj y la ventana muestran el mismo código");
        request.Accept();
        var accepted = await watch.Read();
        Check.Equal("PAIR_ACCEPTED", Type(accepted));
        return Payload(accepted, "sessionToken");
    }

    private static async Task AcceptedPairingAllowsCommands()
    {
        await using var h = new Harness();
        await using var watch = await FakeWatch.Connect(h.Receiver.Port);
        await watch.Send(PairRequest());
        var pending = await watch.Read();
        Check.Equal("PAIR_PENDING", Type(pending));
        Check.Equal("PC-PRUEBA", Payload(pending, "pcName"));
        Check.True(System.Text.RegularExpressions.Regex.IsMatch(Payload(pending, "code"), @"\A\d{4}\z"), "código de 4 cifras");

        var request = await h.NextRequest();
        Check.Equal("Reloj de prueba", request.DeviceName);
        request.Accept();
        var accepted = await watch.Read();
        Check.Equal("PAIR_ACCEPTED", Type(accepted));
        var token = Payload(accepted, "sessionToken");
        Check.Equal(64, token.Length, "clave de 256 bits en hexadecimal");
        Check.True(long.Parse(Payload(accepted, "serverTimeUtc")) > 0, "lleva la hora de Windows");
        Check.Equal(1, h.Registry.Saved.Count, "el reloj queda guardado");
        Check.Equal("Reloj de prueba", h.Receiver.ConnectedWatches.Single());

        await watch.Send(Command(token));
        var response = await watch.Read();
        Check.Equal("COMMAND_RESPONSE", Type(response));
        Check.Equal(CommandResponseWriter.ValidatedNotExecuted, Payload(response, "status"));
    }

    private static async Task RejectedPairingClosesConnection()
    {
        await using var h = new Harness();
        await using var watch = await FakeWatch.Connect(h.Receiver.Port);
        await watch.Send(PairRequest());
        await watch.Read();
        (await h.NextRequest()).Reject();
        var rejected = await watch.Read();
        Check.Equal("PAIR_REJECTED", Type(rejected));
        Check.Equal(SessionProtocol.PairingRejectedByUser, Payload(rejected, "reasonCode"));
        Check.Equal("(conexión cerrada)", Type(await watch.Read()));
        Check.Equal(0, h.Registry.Saved.Count);
    }

    private static async Task PairingTimesOut()
    {
        await using var h = new Harness(pairingTimeout: TimeSpan.FromMilliseconds(200));
        await using var watch = await FakeWatch.Connect(h.Receiver.Port);
        await watch.Send(PairRequest());
        await watch.Read();
        var request = await h.NextRequest();
        var rejected = await watch.Read();
        Check.Equal(SessionProtocol.PairingTimedOut, Payload(rejected, "reasonCode"));
        Check.True(request.Completion.IsCompleted, "el aviso de la ventana se cierra");
        request.Accept();
        Check.Equal(0, h.Registry.Saved.Count, "aceptar tarde no vincula");
    }

    private static async Task OnlyOnePairingAtATime()
    {
        await using var h = new Harness();
        await using var first = await FakeWatch.Connect(h.Receiver.Port);
        await first.Send(PairRequest());
        await first.Read();
        await h.NextRequest();
        await using var second = await FakeWatch.Connect(h.Receiver.Port);
        await second.Send(PairRequest("watch-test-02"));
        var busy = await second.Read();
        Check.Equal(SessionProtocol.PairingBusy, Payload(busy, "reasonCode"));
    }

    private static async Task HelloChecksToken()
    {
        await using var h = new Harness();
        string token;
        await using (var pairing = await FakeWatch.Connect(h.Receiver.Port))
            token = await PairAndGetToken(h, pairing);

        await using (var good = await FakeWatch.Connect(h.Receiver.Port))
        {
            await good.Send(Hello(token));
            var ack = await good.Read();
            Check.Equal("HELLO_ACK", Type(ack));
            Check.Equal("PC-PRUEBA", Payload(ack, "pcName"));
        }

        await using (var bad = await FakeWatch.Connect(h.Receiver.Port))
        {
            await bad.Send(Hello(new string('0', 64)));
            var rejected = await bad.Read();
            Check.Equal("HELLO_REJECTED", Type(rejected));
            Check.Equal(ReasonCodes.UnauthorizedDevice, Payload(rejected, "reasonCode"));
            Check.Equal("(conexión cerrada)", Type(await bad.Read()));
        }

        await using (var panel = await FakeWatch.Connect(h.Receiver.Port))
        {
            await panel.Send(Hello(new string('P', 64), "panel"));
            Check.Equal("HELLO_REJECTED", Type(await panel.Read()));
        }
    }

    private static async Task PingNeedsHello()
    {
        await using var h = new Harness();
        await using (var anonymous = await FakeWatch.Connect(h.Receiver.Port))
        {
            await anonymous.Send(Ping());
            var rejected = await anonymous.Read();
            Check.Equal(SessionProtocol.NotAuthenticated, Payload(rejected, "reasonCode"));
        }
        await using var watch = await FakeWatch.Connect(h.Receiver.Port);
        await PairAndGetToken(h, watch);
        await watch.Send(Ping());
        var pong = await watch.Read();
        Check.Equal("PONG", Type(pong));
        Check.True(long.Parse(Payload(pong, "serverTimeUtc")) > 0, "lleva la hora de Windows");
    }

    private static async Task ForgetRevokesToken()
    {
        await using var h = new Harness();
        await using var watch = await FakeWatch.Connect(h.Receiver.Port);
        var token = await PairAndGetToken(h, watch);
        Check.True(h.Registry.Forget(Watch), "se olvida");
        await watch.Send(Command(token));
        var response = await watch.Read();
        Check.Equal(ReasonCodes.UnauthorizedDevice, Payload(response, "reasonCode"));
        Check.True(!h.Registry.Forget("panel"), "el panel de prueba no se puede olvidar");
    }

    private static async Task MalformedSessionMessagesAreRejected()
    {
        await using var h = new Harness();
        var cases = new[]
        {
            PairRequest().Replace("\"payload\":", "\"extra\":1,\"payload\":"),
            PairRequest().Replace("\"deviceName\"", "\"name\""),
            PairRequest().Replace("1.0-draft", "2.0"),
            PairRequest(deviceId: "reloj con espacios"),
            Hello("").Replace("\"sessionToken\":\"\"", "\"sessionToken\":\"\""),
            Ping().Replace("\"payload\":{}", "\"payload\":{\"x\":1}"),
        };
        foreach (var message in cases)
        {
            await using var watch = await FakeWatch.Connect(h.Receiver.Port);
            await watch.Send(message);
            Check.Equal("HELLO_REJECTED", Type(await watch.Read()), message);
            Check.Equal("(conexión cerrada)", Type(await watch.Read()), message);
        }
        lock (h.Requests) Check.Equal(0, h.Requests.Count, "ninguna llegó a la ventana");
    }

    private static Task DeviceNameIsSanitized()
    {
        Check.Equal("Galaxy Watch6 (40mm)", SessionProtocol.SanitizeName("Galaxy Watch6 (40mm)"));
        Check.Equal("Reloj", SessionProtocol.SanitizeName("‮\n<>"));
        Check.Equal("ab", SessionProtocol.SanitizeName("a\u0000b"));
        Check.Equal(SessionProtocol.MaxDeviceNameLength, SessionProtocol.SanitizeName(new string('x', 200)).Length);
        return Task.CompletedTask;
    }

    private static async Task DiscoveryAnswersExactQuery()
    {
        await using var h = new Harness();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = new IPEndPoint(IPAddress.Loopback, h.Receiver.DiscoveryPort);

        var wrong = Encoding.ASCII.GetBytes("HOLA");
        await udp.SendAsync(wrong, wrong.Length, target);
        var query = Encoding.ASCII.GetBytes(DiscoveryResponder.Query);
        await udp.SendAsync(query, query.Length, target);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reply = await udp.ReceiveAsync(timeout.Token);
        var json = JsonDocument.Parse(reply.Buffer).RootElement;
        Check.Equal("smartcontrol", json.GetProperty("service").GetString());
        Check.Equal("PC-PRUEBA", json.GetProperty("pcName").GetString());
        Check.Equal(h.Receiver.Port, json.GetProperty("port").GetInt32());

        using var none = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await udp.ReceiveAsync(none.Token);
            throw new TestFailure("respondió a una pregunta que no era la exacta");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task SessionMessagesNeedCoordinator()
    {
        await using var receiver = new LocalCommandReceiver(new CommandValidator(new Dictionary<string, string>()),
            new SimulatedCommandExecutor(), new InMemoryEventLog(), port: 0);
        receiver.Start();
        await using var watch = await FakeWatch.Connect(receiver.Port);
        await watch.Send(Hello(new string('0', 64)));
        var response = await watch.Read();
        Check.Equal("COMMAND_RESPONSE", Type(response));
        Check.Equal(ReasonCodes.UnsupportedMessageType, Payload(response, "reasonCode"));
    }
}
