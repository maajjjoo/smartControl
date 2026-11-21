using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Transport;

public enum ReceiverState
{
    Stopped,
    Running,
    Faulted,
}

public sealed class CommandProcessedEventArgs : EventArgs
{
    public CommandProcessedEventArgs(ValidationResult result, string? effect, DateTimeOffset receivedAt)
    {
        Result = result;
        Effect = effect;
        ReceivedAt = receivedAt;
    }

    public ValidationResult Result { get; }
    public string? Effect { get; }
    public DateTimeOffset ReceivedAt { get; }
}

/// <summary>Dónde escucha el receptor y cómo se presenta a los relojes.</summary>
public sealed record ReceiverOptions
{
    /// <summary>
    /// 127.0.0.1 por defecto (solo este equipo). La aplicación usa <see cref="IPAddress.Any"/>
    /// para que el reloj llegue por la red Wi-Fi; aun así, solo un reloj vinculado puede enviar
    /// comandos.
    /// </summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;

    /// <summary>Nombre del equipo que ve el reloj.</summary>
    public string PcName { get; init; } = Environment.MachineName;

    /// <summary>Identificador estable de este equipo, para que el reloj reconozca a su PC.</summary>
    public string PcId { get; init; } = "smartcontrol-pc";

    /// <summary>Puerto UDP en el que se responde a la búsqueda del reloj; nulo para no responder.</summary>
    public int? DiscoveryPort { get; init; }
}

/// <summary>
/// Receptor de comandos. Recibe mensajes JSON delimitados por salto de línea. Cada
/// COMMAND_REQUEST se valida y, si es aceptable, se entrega al ejecutor como
/// <see cref="AllowedAction"/>. Si tiene un <see cref="PairingCoordinator"/>, también atiende la
/// vinculación (PAIR_REQUEST), el saludo de un reloj vinculado (HELLO) y el latido (PING).
/// No sabe nada de la interfaz gráfica.
/// </summary>
public sealed class LocalCommandReceiver : IAsyncDisposable
{
    public const int DefaultPort = 47800;
    public const int MaxClients = 8;
    // Mensaje + "\r\n".
    private const int MaxFrameBytes = CommandValidator.MaxMessageBytes + 2;

    private readonly CommandValidator _validator;
    private readonly ICommandExecutor _executor;
    private readonly IEventLog _log;
    private readonly int _requestedPort;
    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private readonly ConcurrentDictionary<int, string> _watches = new();
    private readonly PairingCoordinator? _pairing;
    private readonly ReceiverOptions _options;
    private DiscoveryResponder? _discovery;
    private readonly object _stateLock = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _nextClientId;
    private ReceiverState _state = ReceiverState.Stopped;

    public LocalCommandReceiver(
        CommandValidator validator,
        ICommandExecutor executor,
        IEventLog log,
        int port = DefaultPort,
        TimeSpan? idleTimeout = null,
        PairingCoordinator? pairing = null,
        ReceiverOptions? options = null)
    {
        if (port < 0 || port > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port));
        _validator = validator;
        _executor = executor;
        _log = log;
        _requestedPort = port;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
        _pairing = pairing;
        _options = options ?? new ReceiverOptions();
    }

    /// <summary>Se dispara en un hilo del receptor, no en el de la interfaz.</summary>
    public event EventHandler<CommandProcessedEventArgs>? CommandProcessed;

    /// <summary>Cambios de estado o de número de clientes. Hilo del receptor.</summary>
    public event EventHandler? StatusChanged;

    public ReceiverState State
    {
        get { lock (_stateLock) return _state; }
    }

    /// <summary>Puerto real de escucha (útil cuando se pidió el puerto 0).</summary>
    public int Port { get; private set; }

    public int ConnectedClients => _clients.Count;

    /// <summary>Nombres de los relojes vinculados con una conexión abierta ahora mismo.</summary>
    public string[] ConnectedWatches => _watches.Values.Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToArray();

    /// <summary>Puerto UDP de la búsqueda, o 0 si no se responde a la búsqueda.</summary>
    public int DiscoveryPort => _discovery?.Port ?? 0;

    /// <summary>Motivo del último fallo al iniciar, si lo hubo.</summary>
    public string? LastError { get; private set; }

    /// <summary>Inicia la escucha. Lanza <see cref="SocketException"/> si el puerto no está disponible.</summary>
    public void Start()
    {
        lock (_stateLock)
        {
            if (_state == ReceiverState.Running)
                return;

            var listener = new TcpListener(_options.ListenAddress, _requestedPort);
            listener.ExclusiveAddressUse = true;
            try
            {
                listener.Start(backlog: MaxClients);
            }
            catch (SocketException ex)
            {
                _state = ReceiverState.Faulted;
                LastError = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"El puerto {_requestedPort} ya está en uso"
                    : $"No se pudo abrir el puerto {_requestedPort} ({ex.SocketErrorCode})";
                _log.Write(EventLevel.Error, "receiver", $"start_failed port={_requestedPort} error={ex.SocketErrorCode}");
                RaiseStatusChanged();
                throw;
            }

            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (_options.DiscoveryPort is { } discoveryPort)
            {
                // Sin búsqueda el reloj no encuentra el equipo, pero los comandos siguen funcionando.
                var discovery = new DiscoveryResponder(discoveryPort, _options.ListenAddress, () => Port, _options.PcName, _options.PcId, _log);
                try
                {
                    discovery.Start();
                    _discovery = discovery;
                }
                catch (SocketException ex)
                {
                    _log.Write(EventLevel.Warning, "discovery", $"start_failed port={discoveryPort} error={ex.SocketErrorCode}");
                }
            }
            _cts = new CancellationTokenSource();
            _state = ReceiverState.Running;
            LastError = null;
            _acceptLoop = AcceptLoopAsync(listener, _cts.Token);
        }
        _log.Write(EventLevel.Info, "receiver", $"started endpoint={_options.ListenAddress}:{Port}");
        RaiseStatusChanged();
    }

    public async Task StopAsync()
    {
        Task? acceptLoop;
        lock (_stateLock)
        {
            if (_state != ReceiverState.Running)
                return;
            _state = ReceiverState.Stopped;
            _cts!.Cancel();
            _listener!.Stop();
            acceptLoop = _acceptLoop;
        }

        if (_discovery is { } discovery)
        {
            await discovery.StopAsync().ConfigureAwait(false);
            _discovery = null;
        }

        if (acceptLoop is not null)
            await acceptLoop.ConfigureAwait(false);
        await Task.WhenAll(_clients.Values.ToArray()).ConfigureAwait(false);

        lock (_stateLock)
        {
            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _acceptLoop = null;
        }
        _log.Write(EventLevel.Info, "receiver", "stopped");
        RaiseStatusChanged();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (ct.IsCancellationRequested) { return; }
            catch (SocketException ex)
            {
                _log.Write(EventLevel.Warning, "receiver", $"accept_failed error={ex.SocketErrorCode}");
                continue;
            }

            if (_clients.Count >= MaxClients)
            {
                _log.Write(EventLevel.Warning, "receiver", "client_rejected reason=CLIENT_LIMIT");
                client.Dispose();
                continue;
            }

            var id = Interlocked.Increment(ref _nextClientId);
            // El registro se hace antes de arrancar la tarea para que StopAsync siempre la espere.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _clients[id] = RunClientAsync(id, client, start.Task, ct);
            start.SetResult();
        }
    }

    private async Task RunClientAsync(int id, TcpClient client, Task start, CancellationToken ct)
    {
        await start.ConfigureAwait(false);
        RaiseStatusChanged();
        try
        {
            using (client)
                await HandleClientAsync(id, client, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Desconexión del cliente o cierre del receptor.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _watches.TryRemove(id, out _);
            RaiseStatusChanged();
        }
    }

    private async Task HandleClientAsync(int id, TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var frame = new byte[MaxFrameBytes];
        var frameLength = 0;
        var chunk = new byte[1024];

        while (true)
        {
            int read;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(_idleTimeout);
                read = await stream.ReadAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            if (read == 0)
                return;

            var offset = 0;
            while (offset < read)
            {
                var newline = Array.IndexOf(chunk, (byte)'\n', offset, read - offset);
                var take = (newline < 0 ? read : newline + 1) - offset;
                if (frameLength + take > MaxFrameBytes)
                {
                    // Trama demasiado grande o sin delimitador: no se puede resincronizar.
                    var tooLarge = ValidationResult.Reject(CommandStatus.RejectedInvalidMessage, ReasonCodes.MessageTooLargeOrUnterminated);
                    Report(tooLarge, null);
                    await SendAsync(stream, tooLarge, null, executed: false, ct).ConfigureAwait(false);
                    return;
                }
                Buffer.BlockCopy(chunk, offset, frame, frameLength, take);
                frameLength += take;
                offset += take;

                if (newline >= 0)
                {
                    var bodyLength = frameLength - 1;
                    if (bodyLength > 0 && frame[bodyLength - 1] == (byte)'\r')
                        bodyLength--;
                    var body = frame.AsMemory(0, bodyLength);
                    if (_pairing is not null && SessionProtocol.IsSessionType(SessionProtocol.PeekMessageType(body)))
                    {
                        var message = SessionProtocol.Parse(body);
                        frameLength = 0;
                        if (!await HandleSessionAsync(id, message, stream, ct).ConfigureAwait(false))
                            return;
                        continue;
                    }
                    var (result, effect) = Process(body);
                    frameLength = 0;
                    await SendAsync(stream, result, effect, _executor.PerformsRealActions, ct).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Atiende HELLO, PAIR_REQUEST y PING. Devuelve false si hay que cerrar la conexión: mensaje
    /// inválido, reloj desconocido o vinculación rechazada.
    /// </summary>
    private async Task<bool> HandleSessionAsync(int id, SessionMessage? message, NetworkStream stream, CancellationToken ct)
    {
        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        switch (message)
        {
            case null:
                _log.Write(EventLevel.Warning, "session", "rejected reason=INVALID_MESSAGE");
                await WriteLineAsync(stream, SessionProtocol.HelloRejected(null, ReasonCodes.MalformedJson), ct).ConfigureAwait(false);
                return false;

            case { Type: SessionMessageType.Ping }:
                if (!_watches.ContainsKey(id))
                {
                    await WriteLineAsync(stream, SessionProtocol.HelloRejected(message.MessageId, SessionProtocol.NotAuthenticated), ct).ConfigureAwait(false);
                    return false;
                }
                await WriteLineAsync(stream, SessionProtocol.Pong(message.MessageId, Now()), ct).ConfigureAwait(false);
                return true;

            case { Type: SessionMessageType.Hello }:
                // Solo relojes guardados: el panel de prueba no se presenta como reloj.
                if (!_pairing!.Registry.TryGet(message.DeviceId!, out var known) || !known!.Persistent
                    || !_pairing.Registry.TryGetToken(message.DeviceId!, out var expected)
                    || !CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(message.SessionToken!)))
                {
                    _log.Write(EventLevel.Warning, "session", "hello_rejected reason=UNAUTHORIZED_DEVICE");
                    await WriteLineAsync(stream, SessionProtocol.HelloRejected(message.MessageId, ReasonCodes.UnauthorizedDevice), ct).ConfigureAwait(false);
                    return false;
                }
                _watches[id] = known.Name;
                _log.Write(EventLevel.Info, "session", $"hello device={message.DeviceId}");
                await WriteLineAsync(stream, SessionProtocol.HelloAck(message.MessageId, _options.PcName, Now()), ct).ConfigureAwait(false);
                RaiseStatusChanged();
                return true;

            default: // PairRequest
                _log.Write(EventLevel.Info, "session", $"pair_requested device={message.DeviceId}");
                var (outcome, device) = await _pairing!.RequestAsync(
                    message.DeviceId!, message.DeviceName!,
                    code => WriteLineAsync(stream, SessionProtocol.PairPending(message.MessageId, code, _options.PcName), ct),
                    ct).ConfigureAwait(false);
                _log.Write(EventLevel.Info, "session", $"pair_finished device={message.DeviceId} outcome={outcome}");
                if (outcome == PairingOutcome.Accepted)
                {
                    _watches[id] = device!.Name;
                    await WriteLineAsync(stream, SessionProtocol.PairAccepted(message.MessageId, device.Token, _options.PcName, Now()), ct).ConfigureAwait(false);
                    RaiseStatusChanged();
                    return true;
                }
                var reason = outcome switch
                {
                    PairingOutcome.Rejected => SessionProtocol.PairingRejectedByUser,
                    PairingOutcome.Busy => SessionProtocol.PairingBusy,
                    _ => SessionProtocol.PairingTimedOut,
                };
                if (outcome != PairingOutcome.Cancelled)
                    await WriteLineAsync(stream, SessionProtocol.PairRejected(message.MessageId, reason), ct).ConfigureAwait(false);
                return false;
        }
    }

    private static async Task WriteLineAsync(NetworkStream stream, byte[] json, CancellationToken ct)
    {
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        await stream.WriteAsync(line.AsMemory(), ct).ConfigureAwait(false);
    }

    private (ValidationResult Result, string? Effect) Process(ReadOnlyMemory<byte> body)
    {
        var result = _validator.Validate(body);
        string? effect = null;
        if (result.IsAccepted)
        {
            try
            {
                effect = _executor.Execute(result.Action!.Value);
            }
            catch (ActionUnavailableException ex)
            {
                result = result with { Status = CommandStatus.ExecutionError, ReasonCode = ex.ReasonCode };
            }
            catch (Exception)
            {
                result = result with { Status = CommandStatus.ExecutionError, ReasonCode = ReasonCodes.ExecutorFailed };
            }
        }
        Report(result, effect);
        return (result, effect);
    }

    private void Report(ValidationResult result, string? effect)
    {
        // Solo se registran datos ya validados o generados aquí; nunca el token ni el mensaje.
        var action = result.Action is { } a ? CommandAllowlist.ToWireName(a) : "-";
        _log.Write(
            result.Status == CommandStatus.Accepted ? EventLevel.Info : EventLevel.Warning,
            "command",
            $"messageId={result.MessageId ?? "-"} status={CommandResponseWriter.ToWireStatus(result.Status, _executor.PerformsRealActions)} reason={result.ReasonCode} action={action}");

        var handler = CommandProcessed;
        if (handler is null)
            return;
        try
        {
            handler(this, new CommandProcessedEventArgs(result, effect, DateTimeOffset.Now));
        }
        catch (Exception)
        {
            // Un fallo de un suscriptor (por ejemplo, la interfaz) no debe detener el receptor.
        }
    }

    private static async Task SendAsync(NetworkStream stream, ValidationResult result, string? effect, bool executed, CancellationToken ct)
    {
        var json = CommandResponseWriter.Write(result, effect, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), executed);
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        await stream.WriteAsync(line.AsMemory(), ct).ConfigureAwait(false);
    }

    private void RaiseStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
        }
    }
}
