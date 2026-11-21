using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SmartControl.Core.Pairing;

public enum PairingOutcome
{
    Accepted,
    Rejected,
    TimedOut,
    /// <summary>Ya había otra solicitud esperando respuesta.</summary>
    Busy,
    /// <summary>El reloj se desconectó o el receptor se detuvo antes de responder.</summary>
    Cancelled,
}

/// <summary>
/// Una solicitud de vinculación esperando a que la persona responda en Windows. El reloj muestra el
/// mismo <see cref="Code"/>: solo hay que aceptar si coincide con el de su pantalla.
/// </summary>
public sealed class PairingRequest
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PairingRequest(string deviceId, string deviceName, string code)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        Code = code;
    }

    public string DeviceId { get; }
    public string DeviceName { get; }
    public string Code { get; }

    /// <summary>Termina al responder, al caducar o si el reloj se va. La ventana la usa para cerrar el aviso.</summary>
    public Task Completion => _decision.Task;

    public void Accept() => _decision.TrySetResult(true);

    public void Reject() => _decision.TrySetResult(false);

    internal Task<bool> Decision => _decision.Task;

    internal void Cancel() => _decision.TrySetCanceled();
}

/// <summary>
/// Atiende las solicitudes de vinculación de una en una. Solo una persona delante de Windows puede
/// aceptar un reloj: así nadie más en la misma red Wi-Fi puede controlar este equipo.
/// </summary>
public sealed class PairingCoordinator
{
    private readonly PairedDeviceRegistry _registry;
    private readonly TimeSpan _timeout;
    private int _busy;

    public PairingCoordinator(PairedDeviceRegistry registry, TimeSpan? timeout = null)
    {
        _registry = registry;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Hay una solicitud nueva. Hilo del receptor: la interfaz debe pasarla a su hilo.</summary>
    public event EventHandler<PairingRequest>? PairingRequested;

    public PairedDeviceRegistry Registry => _registry;

    /// <summary>
    /// Pide la vinculación y espera la respuesta. <paramref name="onPending"/> envía el código al
    /// reloj antes de esperar.
    /// </summary>
    public async Task<(PairingOutcome Outcome, PairedDevice? Device)> RequestAsync(
        string deviceId, string deviceName, Func<string, Task> onPending, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return (PairingOutcome.Busy, null);
        var request = new PairingRequest(deviceId, deviceName, RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4"));
        try
        {
            await onPending(request.Code).ConfigureAwait(false);
            PairingRequested?.Invoke(this, request);

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(_timeout);
            using (wait.Token.Register(request.Cancel))
            {
                bool accepted;
                try
                {
                    accepted = await request.Decision.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return (ct.IsCancellationRequested ? PairingOutcome.Cancelled : PairingOutcome.TimedOut, null);
                }
                return accepted ? (PairingOutcome.Accepted, _registry.Pair(deviceId, deviceName)) : (PairingOutcome.Rejected, null);
            }
        }
        finally
        {
            request.Cancel();
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
