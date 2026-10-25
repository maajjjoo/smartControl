using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
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

/// <summary>
/// Receptor local de comandos de prueba (versión C# de poc/local-receiver).
/// Escucha EXCLUSIVAMENTE en 127.0.0.1 y recibe mensajes JSON delimitados por salto
/// de línea. Cada mensaje se valida y, si es aceptable, se entrega al ejecutor como
/// <see cref="AllowedAction"/>. No sabe nada de la interfaz gráfica.
/// Una prueba en loopback no demuestra la comunicación entre dispositivos.
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
        TimeSpan? idleTimeout = null)
    {
        if (port < 0 || port > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port));
        _validator = validator;
        _executor = executor;
        _log = log;
        _requestedPort = port;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
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

    /// <summary>Motivo del último fallo al iniciar, si lo hubo.</summary>
    public string? LastError { get; private set; }

    /// <summary>Inicia la escucha. Lanza <see cref="SocketException"/> si el puerto no está disponible.</summary>
    public void Start()
    {
        lock (_stateLock)
        {
            if (_state == ReceiverState.Running)
                return;

            var listener = new TcpListener(IPAddress.Loopback, _requestedPort);
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
            _cts = new CancellationTokenSource();
            _state = ReceiverState.Running;
            LastError = null;
            _acceptLoop = AcceptLoopAsync(listener, _cts.Token);
        }
        _log.Write(EventLevel.Info, "receiver", $"started endpoint=127.0.0.1:{Port}");
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
                await HandleClientAsync(client, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Desconexión del cliente o cierre del receptor.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            RaiseStatusChanged();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
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
                    await SendAsync(stream, tooLarge, null, ct).ConfigureAwait(false);
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
                    var (result, effect) = Process(frame.AsMemory(0, bodyLength));
                    frameLength = 0;
                    await SendAsync(stream, result, effect, ct).ConfigureAwait(false);
                }
            }
        }
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
            $"messageId={result.MessageId ?? "-"} status={CommandResponseWriter.ToWireStatus(result.Status)} reason={result.ReasonCode} action={action}");

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

    private static async Task SendAsync(NetworkStream stream, ValidationResult result, string? effect, CancellationToken ct)
    {
        var json = CommandResponseWriter.Write(result, effect, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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
