using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SmartControl.Core.Pairing;

namespace SmartControl.Desktop.Pairing;

/// <summary>
/// Guarda los relojes vinculados y el identificador de este equipo en
/// %LOCALAPPDATA%\SmartControl\pairing.dat, cifrado con DPAPI para la cuenta de Windows actual:
/// otra cuenta del mismo equipo no puede leer las claves. Si el archivo no existe o está dañado,
/// se empieza sin relojes vinculados (hay que volver a vincular), nunca con datos a medias.
/// </summary>
internal sealed class PairingStore
{
    private static readonly byte[] Entropy = "SmartControl/pairing/v1"u8.ToArray();
    private readonly string _path;

    public PairingStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartControl", "pairing.dat");
    }

    public string FilePath => _path;

    /// <summary>Identificador estable del equipo y relojes guardados.</summary>
    public (string PcId, IReadOnlyList<PairedDevice> Devices) Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
                var data = JsonSerializer.Deserialize<StoredData>(json);
                if (data is { PcId: { Length: > 0 } pcId, Devices: { } devices })
                    return (pcId, devices.Select(d => new PairedDevice(d.DeviceId, d.Name, d.Token, Persistent: true)).ToArray());
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            // Archivo de otra cuenta o dañado: se descarta.
        }
        return ("pc-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), Array.Empty<PairedDevice>());
    }

    /// <summary>Escribe primero en un archivo temporal para no dejar el guardado a medias.</summary>
    public void Save(string pcId, IEnumerable<PairedDevice> devices)
    {
        var data = new StoredData
        {
            PcId = pcId,
            Devices = devices.Where(d => d.Persistent).Select(d => new StoredDevice { DeviceId = d.DeviceId, Name = d.Name, Token = d.Token }).ToList(),
        };
        var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _path, overwrite: true);
    }

    private sealed class StoredData
    {
        public string PcId { get; set; } = "";
        public List<StoredDevice> Devices { get; set; } = new();
    }

    private sealed class StoredDevice
    {
        public string DeviceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Token { get; set; } = "";
    }
}
