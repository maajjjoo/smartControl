using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SmartControl.Core.Pairing;

/// <summary>
/// Un reloj vinculado. <see cref="Token"/> es la clave que el reloj presenta en cada mensaje.
/// <see cref="Persistent"/> es false para dispositivos de esta sesión (el panel de prueba), que no
/// se guardan en disco.
/// </summary>
public sealed record PairedDevice(string DeviceId, string Name, string Token, bool Persistent);

/// <summary>
/// Relojes vinculados con este equipo. Seguro entre hilos: el receptor lo consulta en cada mensaje
/// y la interfaz lo modifica al vincular u olvidar un reloj.
/// </summary>
public sealed class PairedDeviceRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (PairedDevice Device, byte[] TokenBytes)> _devices = new(StringComparer.Ordinal);

    /// <summary>Se dispara al vincular u olvidar un reloj guardado. Hilo de quien hizo el cambio.</summary>
    public event EventHandler? Changed;

    /// <summary>Relojes guardados (sin los dispositivos de la sesión).</summary>
    public IReadOnlyList<PairedDevice> Saved
    {
        get { lock (_lock) return _devices.Values.Select(d => d.Device).Where(d => d.Persistent).ToArray(); }
    }

    /// <summary>Clave esperada de <paramref name="deviceId"/>, para compararla en tiempo constante.</summary>
    public bool TryGetToken(string deviceId, out byte[] token)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var entry))
            {
                token = entry.TokenBytes;
                return true;
            }
        }
        token = Array.Empty<byte>();
        return false;
    }

    public bool TryGet(string deviceId, out PairedDevice? device)
    {
        lock (_lock)
        {
            var found = _devices.TryGetValue(deviceId, out var entry);
            device = found ? entry.Device : null;
            return found;
        }
    }

    /// <summary>
    /// Vincula un reloj con una clave nueva y aleatoria (256 bits). Si ya estaba vinculado, la clave
    /// anterior deja de servir.
    /// </summary>
    public PairedDevice Pair(string deviceId, string name)
    {
        var device = new PairedDevice(deviceId, name, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Persistent: true);
        Put(device);
        Changed?.Invoke(this, EventArgs.Empty);
        return device;
    }

    /// <summary>Dispositivo solo de esta sesión, como el panel de prueba de la ventana.</summary>
    public void AddSessionDevice(string deviceId, string token, string name) =>
        Put(new PairedDevice(deviceId, name, token, Persistent: false));

    /// <summary>Carga los relojes guardados al arrancar, sin avisar de cambios.</summary>
    public void Load(IEnumerable<PairedDevice> devices)
    {
        foreach (var device in devices)
            Put(device with { Persistent = true });
    }

    /// <summary>Olvida un reloj: su clave deja de servir en el acto.</summary>
    public bool Forget(string deviceId)
    {
        bool removed;
        lock (_lock)
            removed = _devices.TryGetValue(deviceId, out var entry) && entry.Device.Persistent && _devices.Remove(deviceId);
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    private void Put(PairedDevice device)
    {
        lock (_lock)
            _devices[device.DeviceId] = (device, Encoding.UTF8.GetBytes(device.Token));
    }
}
