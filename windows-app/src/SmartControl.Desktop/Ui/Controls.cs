using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SmartControl.Desktop.Ui;

/// <summary>Tarjeta blanca con esquinas redondeadas y borde fino sobre el fondo de la ventana.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        this.WithColors(back: () => Palette.Surface);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Shapes.RoundedRect(rect, LogicalToDeviceUnits(8));
        using var fill = new SolidBrush(Palette.Surface);
        using var border = new Pen(Palette.Outline);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal enum ButtonKind
{
    /// <summary>Acción principal: fondo de acento.</summary>
    Primary,
    /// <summary>Acción normal: fondo blanco con borde.</summary>
    Secondary,
    /// <summary>Acción discreta, sin fondo hasta pasar el ratón.</summary>
    Subtle,
}

/// <summary>
/// Botón con esquinas redondeadas, icono opcional y todos sus estados (normal, ratón encima,
/// pulsado, seleccionado, deshabilitado y foco de teclado). Hereda de <see cref="Button"/>
/// para conservar el teclado, los eventos y la accesibilidad del botón estándar.
/// </summary>
internal sealed class SmartButton : Button
{
    private bool _hover;
    private bool _pressed;
    private bool _selected;

    public SmartButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        Font = Typography.Body;
    }

    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;

    /// <summary>Carácter de <see cref="Icons"/>; nulo para un botón sin icono.</summary>
    public string? Glyph { get; set; }

    /// <summary>Alinea icono y texto a la izquierda en lugar de centrarlos.</summary>
    public bool AlignLeft { get; set; }

    /// <summary>Estado activo dentro de un selector de opciones.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        }
    }

    /// <summary>Se comporta como una opción de un grupo (por ejemplo, el selector de modo).</summary>
    public bool IsOption { get; set; }

    protected override AccessibleObject CreateAccessibilityInstance() => new SmartButtonAccessibleObject(this);

    /// <summary>Expone las opciones como botones de opción marcados o no para los lectores de pantalla.</summary>
    private sealed class SmartButtonAccessibleObject : ButtonBaseAccessibleObject
    {
        private readonly SmartButton _owner;

        public SmartButtonAccessibleObject(SmartButton owner) : base(owner) => _owner = owner;

        public override AccessibleRole Role => _owner.IsOption ? AccessibleRole.RadioButton : base.Role;

        public override AccessibleStates State =>
            _owner.IsOption && _owner.Selected ? base.State | AccessibleStates.Checked : base.State;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (fill, border, fore) = Colors();
        var radius = LogicalToDeviceUnits(6);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Shapes.RoundedRect(rect, radius))
        {
            if (fill != Color.Empty)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (border != Color.Empty)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }
        }

        if (Focused && ShowFocusCues)
        {
            var inset = 1.5f;
            var ring = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            using var path = Shapes.RoundedRect(ring, radius - 1);
            var ringColor = Palette.HighContrast ? fore : Kind == ButtonKind.Primary ? Palette.OnAccent : Palette.Accent;
            using var pen = new Pen(ringColor, 2f);
            g.DrawPath(pen, path);
        }

        DrawContent(g, fore);
    }

    internal (Color Fill, Color Border, Color Fore) Colors()
    {
        if (Palette.HighContrast)
            return HighContrastColors();
        if (!Enabled)
            return (Kind == ButtonKind.Subtle ? Color.Empty : Palette.IdleSoft, Color.Empty, Palette.Idle);
        if (_selected)
            return (Palette.AccentSoft, Palette.Accent, Palette.AccentOnSoft);
        return Kind switch
        {
            ButtonKind.Primary => (_pressed ? Palette.AccentPressed : _hover ? Palette.AccentDark : Palette.Accent, Color.Empty, Palette.OnAccent),
            ButtonKind.Subtle => (_pressed ? Palette.SurfacePressed : _hover ? Palette.IdleSoft : Color.Empty, Color.Empty, Palette.AccentOnSoft),
            _ => (_pressed ? Palette.SurfacePressed : _hover ? Palette.SurfaceHover : Palette.Surface,
                  _hover ? Palette.OutlineStrong : Palette.Outline, Palette.Text),
        };
    }

    /// <summary>
    /// Contraste alto: los colores de botón del sistema, siempre con borde para que se vea dónde
    /// empieza el botón. Ratón encima, pulsado y seleccionado usan el resaltado del sistema.
    /// </summary>
    private (Color Fill, Color Border, Color Fore) HighContrastColors()
    {
        if (!Enabled)
            return (SystemColors.Window, SystemColors.GrayText, SystemColors.GrayText);
        if (_selected || _hover || _pressed)
            return (SystemColors.Highlight, SystemColors.Highlight, SystemColors.HighlightText);
        return (SystemColors.ButtonFace, SystemColors.ControlText, SystemColors.ControlText);
    }

    private void DrawContent(Graphics g, Color fore)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        var padding = LogicalToDeviceUnits(14);
        var gap = LogicalToDeviceUnits(8);
        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);

        using var iconFont = Glyph is null ? null : Icons.Create(Font.Size + 1f);
        var iconSize = iconFont is null ? Size.Empty : TextRenderer.MeasureText(g, Glyph, iconFont, Size.Empty, flags);
        var contentWidth = textSize.Width + (iconFont is null ? 0 : iconSize.Width + gap);
        var x = AlignLeft ? padding : Math.Max(padding, (Width - contentWidth) / 2);

        if (iconFont is not null)
        {
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(x, 0, iconSize.Width, Height), fore, flags);
            x += iconSize.Width + gap;
        }
        var textRect = new Rectangle(x, 0, Math.Max(0, Width - x - padding), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, fore, flags | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Punto de color que indica el estado del receptor.</summary>
internal sealed class StatusDot : Control
{
    private Color _color = Palette.Idle;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = false;
    }

    public Color DotColor
    {
        get => _color;
        set { _color = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height) - 1;
        using var brush = new SolidBrush(_color);
        e.Graphics.FillEllipse(brush, (Width - size) / 2f, (Height - size) / 2f, size, size);
    }
}

/// <summary>Pastilla de texto para avisos breves, como el modo de prueba.</summary>
internal sealed class Pill : Label
{
    private readonly Func<Color> _fill;

    public Pill(string text, Func<Color> fill, Func<Color> fore)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Text = text;
        Font = Typography.Small;
        this.WithColors(fore: fore);
        _fill = fill;
        AutoSize = true;
        Padding = new Padding(10, 4, 10, 4);
    }

    public Color Fill => _fill();

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        using var path = Shapes.RoundedRect(rect, Height / 2f);
        using var brush = new SolidBrush(Fill);
        e.Graphics.FillPath(brush, path);
        if (Palette.HighContrast)
        {
            // Sin el color de fondo, el borde es lo que la distingue del texto normal.
            using var border = new Pen(ForeColor);
            e.Graphics.DrawPath(border, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
