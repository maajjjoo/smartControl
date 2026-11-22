using System;
using System.Collections.Generic;
using System.IO;

namespace SmartControl.Desktop.Configuration;

/// <summary>
/// Configuración de la IA (GEMINI_API_KEY, GEMINI_MODEL) desde variables de entorno o archivos
/// .env. Se busca, por este orden: la variable de entorno del sistema;
/// %LOCALAPPDATA%\SmartControl\.env (instalación para el usuario); y un .env junto a la app o en
/// una carpeta superior (en desarrollo, la raíz del repositorio). Git ignora los .env: la clave no
/// se sube nunca. Los valores nunca se escriben en el registro.
/// </summary>
internal static class EnvFile
{
    public const string GeminiApiKey = "GEMINI_API_KEY";
    public const string GeminiModel = "GEMINI_MODEL";

    /// <summary>Valor de <paramref name="key"/>, o null si no está en ninguna parte.</summary>
    public static string? Get(string key, IEnumerable<string>? files = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();
        foreach (var file in files ?? DefaultFiles())
        {
            if (Read(file).TryGetValue(key, out var value) && value.Length > 0)
                return value;
        }
        return null;
    }

    /// <summary>Archivos .env candidatos, del más específico al más general.</summary>
    public static IEnumerable<string> DefaultFiles()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartControl", ".env");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            yield return Path.Combine(dir.FullName, ".env");
    }

    /// <summary>
    /// Lee líneas CLAVE=valor. Admite comentarios (#), «export », comillas simples o dobles y
    /// espacios alrededor. Un archivo que no existe o no se puede leer se trata como vacío.
    /// </summary>
    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] lines;
        try
        {
            if (!File.Exists(path))
                return values;
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return values;
        }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line[7..].TrimStart();
            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            values[key] = value;
        }
        return values;
    }
}
