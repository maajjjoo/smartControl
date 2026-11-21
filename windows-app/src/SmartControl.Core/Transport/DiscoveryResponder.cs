using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Transport;

/// <summary>
/// Responde a la búsqueda del reloj en la red local, para que nadie tenga que escribir una
/// dirección IP. El reloj envía por UDP (difusión) el texto exacto <see cref="Query"/> y este
/// equipo contesta solo a quien preguntó con su nombre, su identificador y el puerto TCP.
/// No revela nada más y no acepta ninguna orden por este canal.
/// </summary>
public sealed class DiscoveryResponder : IAsyncDisposable
{
    public const int DefaultPort = 47801;
    public const string Query = "SMARTCONTROL_DISCOVER/1";
    private const int MaxRepliesPerSecond = 20;

    private readonly int _requestedPort;
    private readonly IPAddress _listenAddress;
    private readonly Func<int> _tcpPort;
    private readonly string _pcName;
    private readonly string _pcId;
    private readonly IEventLog _log;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DiscoveryResponder(int port, IPAddress listenAddress, Func<int> tcpPort, string pcName, string pcId, IEventLog log)
    {
        _requestedPort = port;
        _listenAddress = listenAddress;
        _tcpPort = tcpPort;
        _pcName = pcName;
        _pcId = pcId;
        _log = log;
    }

    /// <summary>Puerto UDP real (útil cuando se pidió el 0).</summary>
    public int Port { get; private set; }

    public void Start()
    {
        if (_udp is not null)
            return;
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        udp.Client.Bind(new IPEndPoint(_listenAddress, _requestedPort));
        _udp = udp;
        Port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        _cts = new CancellationTokenSource();
        _loop = LoopAsync(udp, _cts.Token);
        _log.Write(EventLevel.Info, "discovery", $"started port={Port}");
    }

    public async Task StopAsync()
    {
        if (_udp is null)
            return;
        _cts!.Cancel();
        _udp.Dispose();
        try { await _loop!.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
        _udp = null;
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Respuesta que recibe el reloj.</summary>
    public static byte[] Reply(string pcName, string pcId, int tcpPort)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("service", "smartcontrol");
            json.WriteString("protocolVersion", CommandValidator.SupportedVersion);
            json.WriteString("pcName", pcName);
            json.WriteString("pcId", pcId);
            json.WriteNumber("port", tcpPort);
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private async Task LoopAsync(UdpClient udp, CancellationToken ct)
    {
        var query = Encoding.ASCII.GetBytes(Query);
        var windowStart = Environment.TickCount64;
        var replies = 0;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (ct.IsCancellationRequested) { return; }
            catch (SocketException)
            {
                // Por ejemplo, un ICMP "puerto inalcanzable" de un envío anterior: se sigue escuchando.
                continue;
            }

            if (!received.Buffer.AsSpan().SequenceEqual(query))
                continue;
            var now = Environment.TickCount64;
            if (now - windowStart >= 1000)
            {
                windowStart = now;
                replies = 0;
            }
            if (++replies > MaxRepliesPerSecond)
                continue;
            try
            {
                var reply = Reply(_pcName, _pcId, _tcpPort());
                await udp.SendAsync(reply, reply.Length, received.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _log.Write(EventLevel.Warning, "discovery", $"reply_failed error={ex.SocketErrorCode}");
            }
        }
    }
}
