using System.Drawing;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Icono de la app (un reloj que envía una señal a la pantalla), incluido en el ensamblado.
/// Se genera con tools/make-icon.ps1; la misma figura es el icono del reloj.
/// </summary>
internal static class AppIcon
{
    public static Icon? Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SmartControl.ico");
        return stream is null ? null : new Icon(stream);
    }
}
