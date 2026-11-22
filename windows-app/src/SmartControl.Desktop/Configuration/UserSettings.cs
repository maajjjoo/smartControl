using System;
using System.IO;
using Microsoft.Win32;

namespace SmartControl.Desktop.Configuration;

/// <summary>Tema de la ventana: el de Windows o uno fijo elegido en Ajustes.</summary>
internal enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Preferencias del usuario que se eligen en el menú Ajustes. Se guardan como líneas CLAVE=valor en
/// %LOCALAPPDATA%\SmartControl\settings.ini. Sin ruta (pruebas) solo viven en memoria. Un archivo
/// ausente, ilegible o con valores desconocidos deja los valores por defecto (seguir a Windows).
/// </summary>
internal sealed class UserSettings
{
    private const string ThemeKey = "theme";

    private readonly string? _path;

    public UserSettings(string? path = null)
    {
        _path = path;
        if (path is null)
            return;
        var values = EnvFile.Read(path);
        if (values.TryGetValue(ThemeKey, out var theme) && Enum.TryParse<ThemeChoice>(theme, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
            Theme = parsed;
    }

    /// <summary>Archivo de ajustes de esta cuenta de Windows.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartControl", "settings.ini");

    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Guarda los ajustes. Un fallo al escribir no impide seguir usando la app.</summary>
    public bool Save()
    {
        if (_path is null)
            return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, $"{ThemeKey}={Theme}\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Modo oscuro efectivo: el elegido, o el de las aplicaciones de Windows
    /// (Configuración › Personalización › Colores) si se sigue al sistema.
    /// </summary>
    public bool IsDark => Theme switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => SystemPrefersDark(),
    };

    /// <summary>Windows pide el modo oscuro para las aplicaciones (AppsUseLightTheme = 0).</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
