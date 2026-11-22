using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Partes que dibuja Windows y no la app: la barra de título y las barras de desplazamiento.
/// Se ponen oscuras con el mismo tema que usa el Explorador de archivos. En versiones de Windows
/// que no lo admiten las llamadas no hacen nada y esas partes se quedan claras.
/// </summary>
internal static class NativeTheme
{
    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>Barra de título clara u oscura según <see cref="Palette.Dark"/>.</summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        var dark = Palette.Dark && !Palette.HighContrast ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Barras de desplazamiento claras u oscuras según <see cref="Palette.Dark"/>.</summary>
    public static void ApplyScrollbars(Control control)
    {
        if (!control.IsHandleCreated)
            return;
        try
        {
            SetWindowTheme(control.Handle, Palette.Dark && !Palette.HighContrast ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? appName, string? idList);
}
