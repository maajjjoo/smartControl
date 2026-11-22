using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Colores de la ventana. Son los mismos que usa la app del reloj (watch-app, ui/Theme.kt)
/// para que las dos partes se reconozcan como el mismo producto: diseño minimalista con un solo
/// color principal (vinotinto, con rosado de fondo) y grises neutros. Un acierto usa el vinotinto;
/// un error, un rojo vivo solo en el icono y sobre gris, para no confundirlo con el principal.
/// En modo oscuro el fondo es casi negro y el vinotinto pasa a rosado para que se lea bien.
/// Con el contraste alto de Windows activado, cada color pasa a su color de sistema equivalente
/// (ventana, texto, resaltado y texto deshabilitado), que son los que eligió el usuario; el
/// contraste alto manda sobre el modo oscuro.
/// </summary>
internal static class Palette
{
    /// <summary>
    /// Contraste alto activado. Se lee al arrancar y de nuevo con <see cref="Refresh"/> cuando
    /// Windows avisa de un cambio; se guarda para no consultarlo en cada dibujo.
    /// </summary>
    public static bool HighContrast { get; internal set; } = SystemInformation.HighContrast;

    /// <summary>Modo oscuro. Lo decide <see cref="Refresh"/> con el tema elegido en Ajustes.</summary>
    public static bool Dark { get; internal set; }

    /// <summary>
    /// Vuelve a leer la configuración de contraste de Windows y aplica el tema: oscuro si se eligió
    /// así o si se sigue a Windows y Windows usa el modo oscuro para las aplicaciones.
    /// </summary>
    public static void Refresh(bool? dark = null)
    {
        HighContrast = SystemInformation.HighContrast;
        if (dark is not null)
            Dark = dark.Value;
    }

    private static Color Pick(int light, int dark) => Color.FromArgb(unchecked((int)0xFF000000) | (Dark ? dark : light));

    public static Color Background => HighContrast ? SystemColors.Window : Pick(0xF7F7F8, 0x0E0E10);
    public static Color Surface => HighContrast ? SystemColors.Window : Pick(0xFFFFFF, 0x1A1A1D);
    public static Color SurfaceHover => HighContrast ? SystemColors.Window : Pick(0xF4F4F5, 0x242428);
    public static Color SurfacePressed => HighContrast ? SystemColors.Window : Pick(0xE9E9EC, 0x2E2E33);
    public static Color Text => HighContrast ? SystemColors.WindowText : Pick(0x18181B, 0xF4F4F5);
    public static Color TextMuted => HighContrast ? SystemColors.WindowText : Pick(0x63636B, 0xA1A1AA);
    public static Color Outline => HighContrast ? SystemColors.WindowText : Pick(0xE4E4E7, 0x2E2E33);
    public static Color OutlineStrong => HighContrast ? SystemColors.WindowText : Pick(0xD4D4D8, 0x46464E);

    public static Color Accent => HighContrast ? SystemColors.Highlight : Pick(0x8E1C3A, 0xEC7A99);
    public static Color AccentDark => HighContrast ? SystemColors.Highlight : Pick(0x761530, 0xF292AD);
    public static Color AccentPressed => HighContrast ? SystemColors.Highlight : Pick(0x5E1027, 0xF7ABC0);
    public static Color AccentSoft => HighContrast ? SystemColors.Highlight : Pick(0xFBEAF0, 0x3A1622);
    public static Color AccentOnSoft => HighContrast ? SystemColors.HighlightText : Pick(0x6E1530, 0xF6A9BE);

    /// <summary>Texto e iconos sobre el color principal (botón principal).</summary>
    public static Color OnAccent => HighContrast ? SystemColors.HighlightText : Pick(0xFFFFFF, 0x1A0710);

    public static Color Success => HighContrast ? SystemColors.WindowText : Pick(0x8E1C3A, 0xEC7A99);
    public static Color SuccessSoft => HighContrast ? SystemColors.Window : Pick(0xFBEAF0, 0x3A1622);
    public static Color SuccessOnSoft => HighContrast ? SystemColors.WindowText : Pick(0x6E1530, 0xF6A9BE);

    public static Color Danger => HighContrast ? SystemColors.WindowText : Pick(0xC62828, 0xFF6B6B);
    public static Color DangerSoft => HighContrast ? SystemColors.Window : Pick(0xF4F4F5, 0x2A2A2E);
    public static Color DangerOnSoft => HighContrast ? SystemColors.WindowText : Pick(0xB42318, 0xFF8A80);

    public static Color WarningSoft => HighContrast ? SystemColors.Window : Pick(0xF4F4F5, 0x2A2A2E);
    public static Color WarningOnSoft => HighContrast ? SystemColors.WindowText : Pick(0x3F3F46, 0xD4D4D8);

    public static Color Idle => HighContrast ? SystemColors.GrayText : Pick(0x8E8E96, 0x76767F);
    public static Color IdleSoft => HighContrast ? SystemColors.Window : Pick(0xF1F1F3, 0x26262A);
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
    public const string Settings = "";

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
