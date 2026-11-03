using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Colores de la ventana. Son los mismos que usa la app del reloj (watch-app, ui/Theme.kt)
/// para que las dos partes se reconozcan como el mismo producto.
/// </summary>
internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(0xF2, 0xF5, 0xFB);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceHover = Color.FromArgb(0xF5, 0xF7, 0xFB);
    public static readonly Color SurfacePressed = Color.FromArgb(0xEA, 0xEE, 0xF5);
    public static readonly Color Text = Color.FromArgb(0x15, 0x1C, 0x2B);
    public static readonly Color TextMuted = Color.FromArgb(0x5E, 0x68, 0x7A);
    public static readonly Color Outline = Color.FromArgb(0xDC, 0xE2, 0xEC);
    public static readonly Color OutlineStrong = Color.FromArgb(0xC3, 0xCC, 0xDA);

    public static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);
    public static readonly Color AccentDark = Color.FromArgb(0x1D, 0x4E, 0xD8);
    public static readonly Color AccentPressed = Color.FromArgb(0x1E, 0x40, 0xAF);
    public static readonly Color AccentSoft = Color.FromArgb(0xE3, 0xEC, 0xFF);
    public static readonly Color AccentOnSoft = Color.FromArgb(0x1E, 0x3A, 0x8A);

    public static readonly Color Success = Color.FromArgb(0x16, 0xA3, 0x5A);
    public static readonly Color SuccessSoft = Color.FromArgb(0xE1, 0xF6, 0xEA);
    public static readonly Color SuccessOnSoft = Color.FromArgb(0x0B, 0x5E, 0x33);

    public static readonly Color Danger = Color.FromArgb(0xDC, 0x3B, 0x30);
    public static readonly Color DangerSoft = Color.FromArgb(0xFD, 0xE8, 0xE6);
    public static readonly Color DangerOnSoft = Color.FromArgb(0x9A, 0x20, 0x19);

    public static readonly Color WarningSoft = Color.FromArgb(0xFF, 0xF1, 0xDC);
    public static readonly Color WarningOnSoft = Color.FromArgb(0x8A, 0x4B, 0x00);

    public static readonly Color Idle = Color.FromArgb(0x8A, 0x94, 0xA6);
    public static readonly Color IdleSoft = Color.FromArgb(0xF0, 0xF2, 0xF6);
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
