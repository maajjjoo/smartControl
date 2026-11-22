using System;
using System.Drawing;
using System.Windows.Forms;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;

namespace SmartControl.Desktop.Ui;

/// <summary>Una fila de la actividad, ya traducida a lenguaje para el usuario.</summary>
internal sealed record ActivityEntry(DateTimeOffset Time, bool Accepted, string Glyph, string Title, string Detail, string ReasonCode)
{
    /// <summary>
    /// Solo usa valores validados o generados por la aplicación (enumeraciones y códigos de motivo
    /// constantes), nunca texto recibido.
    /// </summary>
    public static ActivityEntry From(CommandProcessedEventArgs e)
    {
        var result = e.Result;
        if (result.Status == CommandStatus.Accepted && result.Action is { } action)
        {
            var mode = Texts.ModeName(CommandAllowlist.RequiredMode(action));
            var by = e.Recognition is { } r ? $"{Texts.RecognizedBy(r)} · " : "";
            return new ActivityEntry(e.ReceivedAt, true, Texts.ActionGlyph(action), Texts.ActionName(action),
                $"{mode} · {by}{Texts.ActionEffect(action)}", result.ReasonCode);
        }
        var (title, detail) = Texts.Rejection(result);
        return new ActivityEntry(e.ReceivedAt, false, Icons.Blocked, title, detail, result.ReasonCode);
    }

    /// <summary>Hora local con la que se muestra la fila.</summary>
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// Lo que leen los lectores de pantalla: la lista usa este texto como nombre de cada fila.
    /// Dice el resultado (que en pantalla es un color), qué pasó, por qué y la hora, sin el
    /// carácter del icono. El separador «·» pasa a ser una coma para que se lea como una pausa.
    /// </summary>
    public override string ToString() =>
        $"{(Accepted ? "Hecho" : "Bloqueado")}: {Announcer.Sentence(Title)} {Announcer.Sentence(Detail.Replace(" · ", ", "))} A las {TimeText}.";
}

/// <summary>Textos para el usuario. Los códigos técnicos solo aparecen como ayuda emergente y en el registro.</summary>
internal static class Texts
{
    public static string ActionName(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext => "Siguiente diapositiva",
        AllowedAction.SlidePrevious => "Diapositiva anterior",
        AllowedAction.MediaPlay => "Reproducir",
        AllowedAction.MediaPause => "Pausar",
        AllowedAction.MediaNextTrack => "Siguiente canción",
        AllowedAction.MediaPreviousTrack => "Canción anterior",
        _ => action.ToString(),
    };

    public static string ShortActionName(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext => "Siguiente",
        AllowedAction.SlidePrevious => "Anterior",
        _ => ActionName(action),
    };

    public static string ActionGlyph(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext => Icons.Next,
        AllowedAction.SlidePrevious => Icons.Previous,
        AllowedAction.MediaPlay => Icons.Play,
        AllowedAction.MediaPause => Icons.Pause,
        AllowedAction.MediaNextTrack => Icons.TrackNext,
        AllowedAction.MediaPreviousTrack => Icons.TrackPrevious,
        _ => Icons.Check,
    };

    /// <summary>Quién reconoció el gesto: la IA con su confianza, o las reglas del reloj.</summary>
    public static string RecognizedBy(RecognitionInfo info) =>
        info.IsModel ? $"IA {Math.Round(info.Confidence * 100):0} %" : "reglas del reloj";

    /// <summary>Qué se hizo en Windows para la acción, en palabras del usuario.</summary>
    public static string ActionEffect(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext => "flecha derecha en la ventana activa",
        AllowedAction.SlidePrevious => "flecha izquierda en la ventana activa",
        _ => "enviado al reproductor activo",
    };

    public static string ModeName(ControlMode mode) => mode switch
    {
        ControlMode.Presentation => "Presentación",
        ControlMode.Multimedia => "Multimedia",
        _ => mode.ToString(),
    };

    /// <summary>Qué pasó y por qué no se hizo nada, en palabras del usuario.</summary>
    public static (string Title, string Detail) Rejection(ValidationResult result)
    {
        const string Nothing = "No se hizo nada.";
        switch (result.ReasonCode)
        {
            case ReasonCodes.BelowThreshold:
                return ("Gesto poco claro", "El movimiento no se reconoció con seguridad. " + Nothing);
            case ReasonCodes.ActionNotInAllowlist:
                return ("Orden no permitida", "SmartControl solo acepta las acciones de sus modos. " + Nothing);
            case ReasonCodes.ModeActionMismatch:
            case ReasonCodes.ReceiverModeMismatch:
                return ("Acción de otro modo", "Esa acción no pertenece al modo elegido. " + Nothing);
            case ReasonCodes.ReplayedMessage:
                return ("Mensaje repetido", "Ya se había recibido; se ignora por seguridad. " + Nothing);
            case ReasonCodes.StaleMessage:
            case ReasonCodes.FutureTimestamp:
                return ("Mensaje fuera de tiempo", "Llegó demasiado tarde o con una hora incorrecta. " + Nothing);
            case ReasonCodes.UnauthorizedDevice:
            case ReasonCodes.InvalidSessionToken:
            case ReasonCodes.InvalidDeviceId:
                return ("Dispositivo no vinculado", "El mensaje no viene de un dispositivo autorizado. " + Nothing);
            case ReasonCodes.UnsupportedVersion:
                return ("Versión no compatible", "El mensaje usa una versión que este equipo no entiende. " + Nothing);
            case ReasonCodes.ReplayCacheFull:
                return ("Demasiados mensajes seguidos", "Espera un momento y vuelve a intentarlo. " + Nothing);
            case ReasonCodes.MessageTooLarge:
            case ReasonCodes.MessageTooLargeOrUnterminated:
                return ("Mensaje demasiado grande", "Superaba el tamaño permitido. " + Nothing);
            case ReasonCodes.ExecutorFailed:
                return ("No se pudo realizar la acción", "La orden era válida, pero falló al ejecutarse.");
            case ReasonCodes.NoMediaSession:
                return ("No hay música abierta", "Abre Spotify, YouTube u otro reproductor y vuelve a intentarlo.");
            case ReasonCodes.MediaCommandRefused:
                return ("El reproductor no respondió", "El reproductor abierto no aceptó la orden.");
            case ReasonCodes.InputBlocked:
                return ("Windows bloqueó la tecla", "No se pudo pulsar la flecha. Comprueba que la pantalla no esté bloqueada.");
            case RecognitionReasons.NoGesture:
                return ("Movimiento normal", "La IA decidió que no era un gesto. " + Nothing);
            case RecognitionReasons.LowConfidence:
                return ("Gesto poco claro", "La IA no estaba segura de qué gesto era. " + Nothing);
        }
        return result.Status == CommandStatus.RejectedInvalidMessage
            ? ("Mensaje dañado", "Llegó incompleto o con un formato que no se entiende. " + Nothing)
            : ("Rechazado", Nothing);
    }
}

/// <summary>
/// Lista de la actividad: una fila por comando con icono, qué pasó, por qué y la hora.
/// Cuando está vacía explica qué aparecerá en ella.
/// </summary>
internal sealed class ActivityList : ListBox
{
    private const int MaxRows = 200;
    private readonly ToolTip _toolTip = new();
    private int _toolTipIndex = -1;

    public ActivityList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        this.WithColors(back: () => Palette.Surface);
        Font = Typography.Body;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Actividad";
    }

    public string EmptyTitle { get; set; } = "";
    public string EmptyDetail { get; set; } = "";

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = LogicalToDeviceUnits(64);
    }

    public event EventHandler? EntryAdded;

    public void Add(ActivityEntry entry)
    {
        BeginUpdate();
        Items.Insert(0, entry);
        while (Items.Count > MaxRows)
            Items.RemoveAt(Items.Count - 1);
        EndUpdate();
        Invalidate();
        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Items.Clear();
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not ActivityEntry entry)
            return;

        var g = e.Graphics;
        var bounds = e.Bounds;
        var selected = (e.State & DrawItemState.Selected) != 0;
        // Con contraste alto, la fila seleccionada usa el resaltado del sistema y su texto.
        var highlighted = selected && Palette.HighContrast;
        var titleColor = highlighted ? SystemColors.HighlightText : Palette.Text;
        var mutedColor = highlighted ? SystemColors.HighlightText : Palette.TextMuted;
        using (var bg = new SolidBrush(selected ? Palette.AccentSoft : Palette.Surface))
            g.FillRectangle(bg, bounds);
        if (e.Index < Items.Count - 1)
        {
            using var line = new Pen(Palette.Outline);
            var inset = LogicalToDeviceUnits(16);
            g.DrawLine(line, bounds.Left + inset, bounds.Bottom - 1, bounds.Right - inset, bounds.Bottom - 1);
        }

        var pad = LogicalToDeviceUnits(16);
        var badge = LogicalToDeviceUnits(36);
        var badgeRect = new Rectangle(bounds.Left + pad, bounds.Top + (bounds.Height - badge) / 2, badge, badge);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var badgeBrush = new SolidBrush(entry.Accepted ? Palette.SuccessSoft : Palette.DangerSoft))
            g.FillEllipse(badgeBrush, badgeRect);
        if (Palette.HighContrast)
        {
            // El fondo del círculo no se distingue con contraste alto: se dibuja su contorno.
            using var badgeBorder = new Pen(Palette.Text);
            g.DrawEllipse(badgeBorder, badgeRect);
        }
        using (var iconFont = Icons.Create(11f))
        {
            TextRenderer.DrawText(g, entry.Glyph, iconFont, badgeRect, entry.Accepted ? Palette.SuccessOnSoft : Palette.DangerOnSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        const TextFormatFlags line1 = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Bottom;
        const TextFormatFlags line2 = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Top;
        var time = entry.TimeText;
        var timeWidth = TextRenderer.MeasureText(g, time, Typography.Small).Width;
        var textLeft = badgeRect.Right + LogicalToDeviceUnits(14);
        var textWidth = Math.Max(0, bounds.Right - pad - timeWidth - LogicalToDeviceUnits(12) - textLeft);
        var mid = bounds.Top + bounds.Height / 2;

        TextRenderer.DrawText(g, entry.Title, Typography.Label, new Rectangle(textLeft, bounds.Top, textWidth, mid - bounds.Top + 1), titleColor, line1);
        TextRenderer.DrawText(g, entry.Detail, Typography.Small, new Rectangle(textLeft, mid + LogicalToDeviceUnits(2), textWidth, bounds.Bottom - mid), mutedColor, line2);
        TextRenderer.DrawText(g, time, Typography.Small, new Rectangle(bounds.Right - pad - timeWidth, bounds.Top, timeWidth, bounds.Height), mutedColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

        if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0)
            ControlPaint.DrawFocusRectangle(g, bounds);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // WM_PAINT: con la lista vacía se dibuja la explicación encima del fondo.
        if (m.Msg == 0x000F && Items.Count == 0)
            DrawEmptyState();
    }

    private void DrawEmptyState()
    {
        using var g = CreateGraphics();
        g.Clear(Palette.Surface);
        var width = Math.Min(ClientSize.Width - LogicalToDeviceUnits(48), LogicalToDeviceUnits(420));
        var x = (ClientSize.Width - width) / 2;
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter;
        var titleHeight = TextRenderer.MeasureText(g, EmptyTitle, Typography.Section, new Size(width, 0), flags).Height;
        var detailHeight = TextRenderer.MeasureText(g, EmptyDetail, Typography.Body, new Size(width, 0), flags).Height;
        var gap = LogicalToDeviceUnits(6);
        var y = Math.Max(LogicalToDeviceUnits(24), (ClientSize.Height - titleHeight - gap - detailHeight) / 2 - LogicalToDeviceUnits(16));
        TextRenderer.DrawText(g, EmptyTitle, Typography.Section, new Rectangle(x, y, width, titleHeight), Palette.Text, flags);
        TextRenderer.DrawText(g, EmptyDetail, Typography.Body, new Rectangle(x, y + titleHeight + gap, width, detailHeight), Palette.TextMuted, flags);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexFromPoint(e.Location);
        if (index == _toolTipIndex) return;
        _toolTipIndex = index;
        if (index >= 0 && index < Items.Count && Items[index] is ActivityEntry entry)
            _toolTip.SetToolTip(this, $"Código: {entry.ReasonCode}");
        else
            _toolTip.SetToolTip(this, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
