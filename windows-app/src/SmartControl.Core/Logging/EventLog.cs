using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SmartControl.Core.Logging;

public enum EventLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Registro de eventos relevantes. Quien escribe solo debe pasar texto compuesto por
/// la propia aplicación (códigos, identificadores ya validados, números): nunca el
/// mensaje recibido ni el token de sesión.
/// </summary>
public interface IEventLog
{
    void Write(EventLevel level, string category, string message);
}

internal static class EventLogFormat
{
    public static string Line(DateTimeOffset at, EventLevel level, string category, string message) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{at.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ} {level.ToString().ToUpperInvariant()} {category} {Sanitize(message)}");

    // Defensa adicional: una entrada nunca puede partirse en varias líneas.
    private static string Sanitize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(char.IsControl(c) ? ' ' : c);
        return builder.ToString();
    }
}

/// <summary>
/// Registro en archivos diarios dentro de un directorio propio. Al crearse elimina
/// sus propios archivos de más de <c>retentionDays</c> días.
/// </summary>
public sealed class FileEventLog : IEventLog
{
    private const string FilePrefix = "smartcontrol-";
    private const string FileExtension = ".log";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _clock;

    public FileEventLog(string directory, int retentionDays = 7, Func<DateTimeOffset>? clock = null)
    {
        Directory.CreateDirectory(directory);
        DirectoryPath = directory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        DeleteOldFiles(retentionDays);
    }

    public string DirectoryPath { get; }

    public string CurrentFilePath => PathFor(_clock());

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartControl", "logs");

    public void Write(EventLevel level, string category, string message)
    {
        var now = _clock();
        var line = EventLogFormat.Line(now, level, category, message) + Environment.NewLine;
        try
        {
            lock (_lock)
                File.AppendAllText(PathFor(now), line, Utf8NoBom);
        }
        catch (IOException)
        {
            // Un fallo del registro no debe detener el receptor.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(DateTimeOffset at) =>
        Path.Combine(DirectoryPath, FilePrefix + at.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension);

    private void DeleteOldFiles(int retentionDays)
    {
        var cutoff = _clock().UtcDateTime.Date.AddDays(-retentionDays);
        // Solo se tocan archivos con el nombre exacto que crea esta clase.
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, FilePrefix + "*" + FileExtension))
        {
            var stamp = Path.GetFileNameWithoutExtension(file).Substring(FilePrefix.Length);
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

/// <summary>Registro en memoria, para pruebas.</summary>
public sealed class InMemoryEventLog : IEventLog
{
    private readonly object _lock = new();
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines
    {
        get { lock (_lock) return _lines.ToArray(); }
    }

    public void Write(EventLevel level, string category, string message)
    {
        var line = EventLogFormat.Line(DateTimeOffset.UtcNow, level, category, message);
        lock (_lock) _lines.Add(line);
    }
}
