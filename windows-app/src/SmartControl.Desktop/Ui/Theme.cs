using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Colores de la ventana. Son los mismos que usa la app del reloj (watch-app, ui/Theme.kt)
/// para que las dos partes se reconozcan como el mismo producto.
/// Con el contraste alto de Windows activado, cada color pasa a su color de sistema equivalente
/// (ventana, texto, resaltado y texto deshabilitado), que son los que eligió el usuario.
/// </summary>
internal static class Palette
{
    /// <summary>
    /// Contraste alto activado. Se lee al arrancar y de nuevo con <see cref="Refresh"/> cuando
    /// Windows avisa de un cambio; se guarda para no consultarlo en cada dibujo.
    /// </summary>
    public static bool HighContrast { get; internal set; } = SystemInformation.HighContrast;

    /// <summary>Vuelve a leer la configuración de contraste de Windows.</summary>
    public static void Refresh() => HighContrast = SystemInformation.HighContrast;

    public static Color Background => HighContrast ? SystemColors.Window : Color.FromArgb(0xF2, 0xF5, 0xFB);
    public static Color Surface => HighContrast ? SystemColors.Window : Color.White;
    public static Color SurfaceHover => HighContrast ? SystemColors.Window : Color.FromArgb(0xF5, 0xF7, 0xFB);
    public static Color SurfacePressed => HighContrast ? SystemColors.Window : Color.FromArgb(0xEA, 0xEE, 0xF5);
    public static Color Text => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x15, 0x1C, 0x2B);
    public static Color TextMuted => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x5E, 0x68, 0x7A);
    public static Color Outline => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xDC, 0xE2, 0xEC);
    public static Color OutlineStrong => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xC3, 0xCC, 0xDA);

    public static Color Accent => HighContrast ? SystemColors.Highlight : Color.FromArgb(0x25, 0x63, 0xEB);
    public static Color AccentDark => HighContrast ? SystemColors.Highlight : Color.FromArgb(0x1D, 0x4E, 0xD8);
    public static Color AccentPressed => HighContrast ? SystemColors.Highlight : Color.FromArgb(0x1E, 0x40, 0xAF);
    public static Color AccentSoft => HighContrast ? SystemColors.Highlight : Color.FromArgb(0xE3, 0xEC, 0xFF);
    public static Color AccentOnSoft => HighContrast ? SystemColors.HighlightText : Color.FromArgb(0x1E, 0x3A, 0x8A);

    public static Color Success => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x16, 0xA3, 0x5A);
    public static Color SuccessSoft => HighContrast ? SystemColors.Window : Color.FromArgb(0xE1, 0xF6, 0xEA);
    public static Color SuccessOnSoft => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x0B, 0x5E, 0x33);

    public static Color Danger => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xDC, 0x3B, 0x30);
    public static Color DangerSoft => HighContrast ? SystemColors.Window : Color.FromArgb(0xFD, 0xE8, 0xE6);
    public static Color DangerOnSoft => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x9A, 0x20, 0x19);

    public static Color WarningSoft => HighContrast ? SystemColors.Window : Color.FromArgb(0xFF, 0xF1, 0xDC);
    public static Color WarningOnSoft => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x8A, 0x4B, 0x00);

    public static Color Idle => HighContrast ? SystemColors.GrayText : Color.FromArgb(0x8A, 0x94, 0xA6);
    public static Color IdleSoft => HighContrast ? SystemColors.Window : Color.FromArgb(0xF0, 0xF2, 0xF6);
}

/// <summary>
/// Recuerda de qué color de <see cref="Palette"/> sale el texto y el fondo de cada control, para
/// volver a aplicarlo cuando cambia el contraste. Un color asignado directamente se quedaría fijo.
/// </summary>
internal static class ThemeColors
{
    private static readonly ConditionalWeakTable<Control, Action> Bindings = new();

    /// <summary>Asigna los colores ahora y los vuelve a calcular en cada <see cref="Reapply"/>.</summary>
    public static T WithColors<T>(this T control, Func<Color>? fore = null, Func<Color>? back = null) where T : Control
    {
        void Apply()
        {
            if (fore is not null) control.ForeColor = fore();
            if (back is not null) control.BackColor = back();
        }
        Bindings.AddOrUpdate(control, Apply);
        Apply();
        return control;
    }

    /// <summary>Recalcula los colores de <paramref name="root"/> y sus hijos y los vuelve a dibujar.</summary>
    public static void Reapply(Control root)
    {
        if (Bindings.TryGetValue(root, out var apply))
            apply();
        foreach (Control child in root.Controls)
            Reapply(child);
        root.Invalidate();
    }
}

/// <summary>Tipografía: una sola familia, con pocos tamaños bien separados.</summary>
internal static class Typography
{
    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Label = new("Segoe UI Semibold", 10f);
    public static readonly Font Section = new("Segoe UI Semibold", 12f);
    public static readonly Font StatusTitle = new("Segoe UI Semibold", 14f);
    public static readonly Font Title = new("Segoe UI Semibold", 20f);
}

/// <summary>Iconos de la fuente de símbolos de Windows (Segoe Fluent Icons en Windows 11).</summary>
internal static class Icons
{
    public static readonly string FontName =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public const string Previous = "";
    public const string Next = "";
    public const string Play = "";
    public const string Pause = "";
    public const string TrackNext = "";
    public const string TrackPrevious = "";
    public const string Check = "";
    public const string Blocked = "";
    public const string Warning = "";
    public const string Info = "";

    public static Font Create(float size) => new(FontName, size);
}

internal static class Shapes
{
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
