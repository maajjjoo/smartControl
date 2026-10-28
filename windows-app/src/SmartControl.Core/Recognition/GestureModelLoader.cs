using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SmartControl.Core.Recognition;

/// <summary>
/// Carga un modelo desde disco (ADR-06). Junto a "modelo.json" debe existir "modelo.json.sha256"
/// con el resumen SHA-256 del archivo (64 caracteres hexadecimales, opcionalmente seguidos del
/// nombre, como escribe sha256sum). Si algo falla, no hay modelo: el reconocimiento queda
/// desactivado y todos los gestos se rechazan.
/// </summary>
public static class GestureModelLoader
{
    public const long MaxModelBytes = 1024 * 1024;
    public const string ChecksumExtension = ".sha256";

    public static bool TryLoad(string path, bool allowSyntheticFixture, out GestureModel? model, out string reason)
    {
        model = null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                reason = ModelReasons.FileNotFound;
                return false;
            }
            if (file.Length > MaxModelBytes)
            {
                reason = ModelReasons.FileTooLarge;
                return false;
            }

            var checksumPath = path + ChecksumExtension;
            if (!File.Exists(checksumPath) || ReadExpectedHash(checksumPath) is not { } expected)
            {
                reason = ModelReasons.ChecksumMissing;
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            // El archivo pudo crecer entre la comprobación de tamaño y la lectura.
            if (bytes.Length > MaxModelBytes)
            {
                reason = ModelReasons.FileTooLarge;
                return false;
            }
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected))
            {
                reason = ModelReasons.ChecksumMismatch;
                return false;
            }

            return GestureModel.TryParse(bytes, allowSyntheticFixture, out model, out reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            model = null;
            reason = ModelReasons.ReadError;
            return false;
        }
    }

    /// <summary>Escribe el archivo de resumen de un modelo (para la herramienta de entrenamiento y las pruebas).</summary>
    public static void WriteChecksum(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText(path + ChecksumExtension, $"{hash}  {Path.GetFileName(path)}\n", new UTF8Encoding(false));
    }

    private static byte[]? ReadExpectedHash(string checksumPath)
    {
        var info = new FileInfo(checksumPath);
        if (info.Length > 1024)
            return null;
        var text = File.ReadAllText(checksumPath, Encoding.UTF8).Trim();
        var hex = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[0] : "";
        if (hex.Length != 64)
            return null;
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
