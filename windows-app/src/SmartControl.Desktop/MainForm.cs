using System;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using Microsoft.Win32;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Ui;

namespace SmartControl.Desktop;

/// <summary>
/// Ventana principal. Solo presenta el estado del receptor y los comandos procesados;
/// la validación y la recepción viven en SmartControl.Core.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

    private readonly LocalCommandReceiver _receiver;
    private readonly string _logPath;
    private readonly string _testDeviceId;
    private readonly string _testToken;

    private readonly StatusDot _stateDot = new();
    private readonly Label _stateTitle = new();
    private readonly Label _stateDetail = new();
    private readonly SmartButton _startStopButton = new();
    private readonly ActivityList _activity = new();
    private readonly SmartButton _presentationMode = new();
    private readonly SmartButton _multimediaMode = new();
    private readonly SmartButton _firstAction = new();
    private readonly SmartButton _secondAction = new();
    private readonly Label _lastResponseLabel = new();
    private readonly Label _technicalLabel = new();
    private readonly StatusAnnouncement _statusAnnouncement = new();
    private readonly PairingCoordinator? _pairing;
    private readonly GestureRecognizer? _recognizer;
    private readonly Label _aiStatus = new();
    private readonly Card _pairingCard = new();
    private readonly Label _pairingDetail = new();
    private readonly Label _pairingCode = new();
    private readonly SmartButton _pairButton = new();
    private readonly SmartButton _rejectPairButton = new();
    private readonly SmartButton _watchesButton = new();
    private PairingRequest? _pendingPairing;

    private ControlMode _testMode = ControlMode.Presentation;
    private byte[]? _lastSentMessage;
    private bool _closingAfterStop;

    public MainForm(LocalCommandReceiver receiver, FileEventLog log, string testDeviceId, string testToken, PairingCoordinator? pairing = null,
        GestureRecognizer? recognizer = null)
    {
        _recognizer = recognizer;
        _receiver = receiver;
        _pairing = pairing;
        _logPath = log.CurrentFilePath;
        _testDeviceId = testDeviceId;
        _testToken = testToken;

        Text = "SmartControl";
        Font = Typography.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(640));
        Size = new Size(LogicalToDeviceUnits(1180), LogicalToDeviceUnits(780));
        StartPosition = FormStartPosition.CenterScreen;
        this.WithColors(fore: () => Palette.Text, back: () => Palette.Background);

        BuildLayout();
        SelectTestMode(ControlMode.Presentation);

        _receiver.StatusChanged += (_, _) => RunOnUi(UpdateStatus);
        _receiver.CommandProcessed += (_, e) => RunOnUi(() => AddActivity(e));
        if (_pairing is not null)
        {
            _pairing.PairingRequested += (_, request) => RunOnUi(() => ShowPairing(request));
            _pairing.Registry.Changed += (_, _) => RunOnUi(UpdateStatus);
        }
        Load += (_, _) => StartReceiver();
        FormClosing += OnFormClosing;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateStatus();
    }

    /// <summary>Windows cambió los colores del sistema (por ejemplo, otro tema de contraste).</summary>
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        Palette.Refresh();
        ApplyTheme();
    }

    /// <summary>Activar o desactivar el contraste alto llega como un cambio de accesibilidad.</summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Accessibility)
            RunOnUi(() =>
            {
                Palette.Refresh();
                ApplyTheme();
            });
    }

    /// <summary>Vuelve a aplicar los colores de <see cref="Palette"/> a toda la ventana y la repinta.</summary>
    internal void ApplyTheme()
    {
        ThemeColors.Reapply(this);
        UpdateStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 12),
        }.WithColors(back: () => Palette.Background);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(360)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        var pairing = BuildPairingCard();
        root.Controls.Add(pairing, 0, 1);
        root.SetColumnSpan(pairing, 2);
        var status = BuildStatusCard();
        root.Controls.Add(status, 0, 2);
        root.SetColumnSpan(status, 2);
        root.Controls.Add(BuildActivityCard(), 0, 3);
        root.Controls.Add(BuildTestCard(), 1, 3);

        _technicalLabel.Dock = DockStyle.Fill;
        _technicalLabel.AutoEllipsis = true;
        _technicalLabel.Font = Typography.Small;
        _technicalLabel.WithColors(fore: () => Palette.TextMuted);
        _technicalLabel.Margin = new Padding(2, 10, 2, 0);
        _technicalLabel.Height = LogicalToDeviceUnits(22);
        root.Controls.Add(_technicalLabel, 0, 4);
        root.SetColumnSpan(_technicalLabel, 2);

        Controls.Add(root);
    }

    /// <summary>Texto de la IA bajo el título: si hay un modelo entrenado o si deciden las reglas.</summary>
    internal string AiStatusText => _aiStatus.Text;

    private static string AiStatus(GestureRecognizer? recognizer) => recognizer switch
    {
        null => "",
        { HasAi: true } => $"IA activa · {recognizer.AiName}",
        _ => "IA sin configurar · por ahora deciden las reglas del reloj",
    };

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 16),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        header.Controls.Add(new Label
        {
            Text = "SmartControl",
            AutoSize = true,
            Font = Typography.Title,
            Margin = new Padding(0),
        }.WithColors(fore: () => Palette.Text), 0, 0);
        _aiStatus.Name = "aiStatus";
        _aiStatus.AutoSize = true;
        _aiStatus.Anchor = AnchorStyles.Left;
        _aiStatus.Font = Typography.Small;
        _aiStatus.Margin = new Padding(14, 6, 0, 0);
        _aiStatus.Text = AiStatus(_recognizer);
        _aiStatus.Visible = _recognizer is not null;
        _aiStatus.WithColors(fore: () => _recognizer is { HasAi: true } ? Palette.SuccessOnSoft : Palette.TextMuted);
        header.Controls.Add(_aiStatus, 1, 0);
        var subtitle = new Label
        {
            Text = "Controla tus presentaciones y tu música con gestos del reloj.",
            AutoSize = true,
            Margin = new Padding(2, 2, 0, 0),
        }.WithColors(fore: () => Palette.TextMuted);
        header.Controls.Add(subtitle, 0, 1);
        header.SetColumnSpan(subtitle, 2);
        return header;
    }

    /// <summary>
    /// Aviso que aparece cuando un reloj pide vincularse. Solo se ve mientras hay una solicitud.
    /// </summary>
    private Control BuildPairingCard()
    {
        _pairingCard.Dock = DockStyle.Fill;
        _pairingCard.AutoSize = true;
        _pairingCard.Padding = new Padding(20, 16, 16, 16);
        _pairingCard.Margin = new Padding(0, 0, 0, 16);
        _pairingCard.Visible = false;
        _pairingCard.AccessibleName = "Solicitud de vinculación";
        _pairingCard.AccessibleRole = AccessibleRole.Alert;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
        }.WithColors(back: () => Palette.Surface);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Un reloj quiere conectarse",
            AutoSize = true,
            Font = Typography.StatusTitle,
            Margin = new Padding(0),
        }.WithColors(fore: () => Palette.Text);
        _pairingDetail.AutoSize = true;
        _pairingDetail.Margin = new Padding(1, 4, 16, 0);
        _pairingDetail.WithColors(fore: () => Palette.TextMuted);

        _pairingCode.AutoSize = true;
        _pairingCode.Font = Typography.Title;
        _pairingCode.Anchor = AnchorStyles.Right;
        _pairingCode.Margin = new Padding(0, 0, 20, 0);
        _pairingCode.WithColors(fore: () => Palette.Text);

        _pairButton.Name = "pairAccept";
        _pairButton.Text = "Vincular";
        _pairButton.Kind = ButtonKind.Primary;
        _pairButton.Size = new Size(LogicalToDeviceUnits(116), LogicalToDeviceUnits(38));
        _pairButton.Margin = new Padding(0, 0, 8, 0);
        _pairButton.Anchor = AnchorStyles.Right;
        _pairButton.Click += (_, _) => AnswerPairing(accept: true);

        _rejectPairButton.Name = "pairReject";
        _rejectPairButton.Text = "Rechazar";
        _rejectPairButton.Size = new Size(LogicalToDeviceUnits(116), LogicalToDeviceUnits(38));
        _rejectPairButton.Anchor = AnchorStyles.Right;
        _rejectPairButton.Click += (_, _) => AnswerPairing(accept: false);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right }
            .WithColors(back: () => Palette.Surface);
        buttons.Controls.Add(_pairButton);
        buttons.Controls.Add(_rejectPairButton);

        grid.Controls.Add(title, 0, 0);
        grid.Controls.Add(_pairingDetail, 0, 1);
        grid.Controls.Add(_pairingCode, 1, 0);
        grid.SetRowSpan(_pairingCode, 2);
        grid.Controls.Add(buttons, 2, 0);
        grid.SetRowSpan(buttons, 2);
        _pairingCard.Controls.Add(grid);
        return _pairingCard;
    }

    private void ShowPairing(PairingRequest request)
    {
        if (request.Completion.IsCompleted)
            return;
        _pendingPairing = request;
        _pairingDetail.Text = $"«{request.DeviceName}» quiere controlar este equipo. Vincúlalo solo si es tu reloj y muestra el mismo código.";
        _pairingCode.Text = request.Code;
        // Las cifras separadas para que el lector de pantalla las lea una a una.
        _pairingCode.AccessibleName = $"Código {string.Join(" ", request.Code.ToCharArray())}";
        _pairingCard.Visible = true;
        request.Completion.ContinueWith(_ => RunOnUi(() => HidePairing(request)), TaskScheduler.Default);
        BringToAttention();
        Announcer.Announce(this, $"Un reloj quiere conectarse: {request.DeviceName}. Código {string.Join(" ", request.Code.ToCharArray())}.",
            AutomationNotificationKind.Other);
    }

    /// <summary>Solicitud que muestra el aviso ahora mismo, o nula.</summary>
    internal PairingRequest? PendingPairing => _pendingPairing;

    /// <summary>Código que muestra el aviso.</summary>
    internal string PairingCodeText => _pairingCode.Text;

    internal void AnswerPairing(bool accept)
    {
        var request = _pendingPairing;
        if (request is null)
            return;
        if (accept) request.Accept();
        else request.Reject();
        HidePairing(request);
    }

    private void HidePairing(PairingRequest request)
    {
        if (!ReferenceEquals(_pendingPairing, request))
            return;
        _pendingPairing = null;
        _pairingCard.Visible = false;
    }

    /// <summary>
    /// Restaura la ventana si estaba minimizada y hace parpadear su botón en la barra de tareas:
    /// Windows no deja que una aplicación se ponga al frente sola, pero sí pedir atención.
    /// </summary>
    private void BringToAttention()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = Handle,
            Flags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG: hasta que la ventana pase al frente.
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Menú con los relojes vinculados, para olvidar uno.</summary>
    private void ShowWatchesMenu()
    {
        if (_pairing is null)
            return;
        var menu = new ContextMenuStrip();
        var saved = _pairing.Registry.Saved;
        if (saved.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("Ningún reloj vinculado") { Enabled = false });
        foreach (var device in saved)
        {
            var item = new ToolStripMenuItem($"Olvidar «{device.Name}»");
            item.Click += (_, _) => ForgetWatch(device);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_watchesButton, new Point(0, _watchesButton.Height));
    }

    private void ForgetWatch(PairedDevice device)
    {
        var answer = MessageBox.Show(this,
            $"«{device.Name}» dejará de controlar este equipo. Para volver a usarlo habrá que vincularlo otra vez.",
            "Olvidar reloj", MessageBoxButtons.OKCancel, MessageBoxIcon.None, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
            _pairing!.Registry.Forget(device.DeviceId);
    }

    private Control BuildStatusCard()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(20, 16, 16, 16),
            Margin = new Padding(0, 0, 0, 16),
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
        }.WithColors(back: () => Palette.Surface);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var dot = LogicalToDeviceUnits(14);
        _stateDot.Size = new Size(dot, dot);
        _stateDot.Anchor = AnchorStyles.None;
        _stateDot.Margin = new Padding(0, 0, 14, 0);
        _stateDot.AccessibleName = "Indicador de estado";

        _stateTitle.AutoSize = true;
        _stateTitle.Font = Typography.StatusTitle;
        _stateTitle.WithColors(fore: () => Palette.Text);
        _stateTitle.Margin = new Padding(0);

        _stateDetail.AutoSize = true;
        _stateDetail.WithColors(fore: () => Palette.TextMuted);
        _stateDetail.Margin = new Padding(1, 2, 0, 0);

        _startStopButton.Name = "receiverToggle";
        _startStopButton.Size = new Size(LogicalToDeviceUnits(132), LogicalToDeviceUnits(38));
        _startStopButton.Margin = new Padding(0);
        _startStopButton.Click += async (_, _) => await ToggleReceiverAsync();

        _watchesButton.Name = "pairedWatches";
        _watchesButton.Kind = ButtonKind.Subtle;
        _watchesButton.Size = new Size(LogicalToDeviceUnits(170), LogicalToDeviceUnits(38));
        _watchesButton.Margin = new Padding(0, 0, 8, 0);
        _watchesButton.Visible = _pairing is not null;
        _watchesButton.Click += (_, _) => ShowWatchesMenu();

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right }
            .WithColors(back: () => Palette.Surface);
        actions.Controls.Add(_watchesButton);
        actions.Controls.Add(_startStopButton);

        grid.Controls.Add(_stateDot, 0, 0);
        grid.SetRowSpan(_stateDot, 2);
        grid.Controls.Add(_stateTitle, 1, 0);
        grid.Controls.Add(_stateDetail, 1, 1);
        grid.Controls.Add(actions, 2, 0);
        grid.SetRowSpan(actions, 2);
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildActivityCard()
    {
        // El margen inferior deja libre la esquina redondeada de la tarjeta.
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1, 1, 1, 8), Margin = new Padding(0, 0, 8, 0) };

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(19, 14, 12, 8),
        }.WithColors(back: () => Palette.Surface);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) }.WithColors(back: () => Palette.Surface);
        title.Controls.Add(new Label { Text = "Actividad", AutoSize = true, Font = Typography.Section, Margin = new Padding(0) }.WithColors(fore: () => Palette.Text));
        title.Controls.Add(new Label { Text = "Lo más reciente arriba. Una X marca lo que se bloqueó y no se realizó.", AutoSize = true, Margin = new Padding(0, 2, 0, 0) }.WithColors(fore: () => Palette.TextMuted));
        header.Controls.Add(title, 0, 0);

        var clear = new SmartButton
        {
            Text = "Limpiar",
            Kind = ButtonKind.Subtle,
            Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(34)),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            AccessibleDescription = "Vacía la lista de actividad. El registro en disco no cambia.",
        };
        clear.Click += (_, _) =>
        {
            _activity.Clear();
            clear.Enabled = false;
        };
        clear.Enabled = false;
        _activity.EntryAdded += (_, _) => clear.Enabled = true;
        header.Controls.Add(clear, 1, 0);

        _activity.Dock = DockStyle.Fill;
        _activity.EmptyTitle = "Todavía no hay actividad";
        _activity.EmptyDetail = "Aquí verás cada comando que llegue: si se realizó o se bloqueó, y por qué. Prueba uno desde «Probar sin el reloj».";

        // Fill primero para que el encabezado (Top) se acomode antes.
        card.Controls.Add(_activity);
        card.Controls.Add(header);
        return card;
    }

    private Control BuildTestCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 12, 12), Margin = new Padding(8, 0, 0, 0) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0),
        }.WithColors(back: () => Palette.Surface);

        flow.Controls.Add(new Label { Text = "Probar sin el reloj", AutoSize = true, Font = Typography.Section, Margin = new Padding(0) }.WithColors(fore: () => Palette.Text));
        flow.Controls.Add(MutedText("Envía un comando desde este equipo, no desde el reloj. Las flechas llegan a la ventana que está al frente: desde aquí, esta misma.", new Padding(0, 2, 0, 14)));

        flow.Controls.Add(new Label { Text = "Modo", AutoSize = true, Font = Typography.Label, Margin = new Padding(0, 0, 0, 6) }.WithColors(fore: () => Palette.Text));
        _presentationMode.Text = Texts.ModeName(ControlMode.Presentation);
        _multimediaMode.Text = Texts.ModeName(ControlMode.Multimedia);
        _presentationMode.Click += (_, _) => SelectTestMode(ControlMode.Presentation);
        _multimediaMode.Click += (_, _) => SelectTestMode(ControlMode.Multimedia);
        flow.Controls.Add(TwoColumns(_presentationMode, _multimediaMode, 38, new Padding(0, 0, 0, 10)));

        // Los nombres sirven de AutomationId: «Pausar» también es el texto del botón del receptor.
        _presentationMode.Name = "testModePresentation";
        _multimediaMode.Name = "testModeMultimedia";
        _presentationMode.IsOption = _multimediaMode.IsOption = true;
        _firstAction.Name = "testFirstAction";
        _secondAction.Name = "testSecondAction";
        _firstAction.Kind = ButtonKind.Primary;
        _secondAction.Kind = ButtonKind.Primary;
        _firstAction.Click += async (_, _) => await SendTestCommandAsync(CommandAllowlist.ActionsFor(_testMode)[ActionOrder(0)]);
        _secondAction.Click += async (_, _) => await SendTestCommandAsync(CommandAllowlist.ActionsFor(_testMode)[ActionOrder(1)]);
        flow.Controls.Add(TwoColumns(_firstAction, _secondAction, 46, new Padding(0)));

        flow.Controls.Add(new Panel { Height = 1, Margin = new Padding(0, 16, 0, 14) }.WithColors(back: () => Palette.Outline));

        flow.Controls.Add(new Label { Text = "Comprobar la seguridad", AutoSize = true, Font = Typography.Label, Margin = new Padding(0) }.WithColors(fore: () => Palette.Text));
        flow.Controls.Add(MutedText("Deben bloquearse y aparecer en la actividad marcadas con una X.", new Padding(0, 2, 0, 8)));

        flow.Controls.Add(CheckButton("Orden no permitida", () => SendRawRequestAsync("cmd.exe /c calc")));
        flow.Controls.Add(CheckButton("Acción de otro modo", () =>
        {
            var other = _testMode == ControlMode.Presentation ? ControlMode.Multimedia : ControlMode.Presentation;
            return SendTestCommandAsync(CommandAllowlist.ActionsFor(other)[0]);
        }));
        flow.Controls.Add(CheckButton("Gesto poco claro", () => SendTestCommandAsync(CommandAllowlist.ActionsFor(_testMode)[0], 0.40)));
        flow.Controls.Add(CheckButton("Mensaje dañado", () => SendRawAsync(System.Text.Encoding.UTF8.GetBytes("{no es json"))));
        flow.Controls.Add(CheckButton("Repetir el último mensaje", () =>
        {
            if (_lastSentMessage is not null)
                return SendRawAsync(_lastSentMessage);
            ShowLastResponse("Primero envía un comando para poder repetirlo.", () => Palette.TextMuted);
            return Task.CompletedTask;
        }));

        _lastResponseLabel.AutoSize = true;
        _lastResponseLabel.Font = Typography.Label;
        _lastResponseLabel.Margin = new Padding(0, 10, 0, 0);
        _lastResponseLabel.AccessibleRole = AccessibleRole.StaticText;
        flow.Controls.Add(_lastResponseLabel);

        // Los controles ocupan todo el ancho disponible, sin barra de desplazamiento horizontal.
        flow.ClientSizeChanged += (_, _) =>
        {
            var available = flow.ClientSize.Width - flow.Padding.Horizontal;
            foreach (Control child in flow.Controls)
            {
                if (child is Label label) label.MaximumSize = new Size(available, 0);
                else child.Width = available;
            }
        };

        card.Controls.Add(flow);
        return card;
    }

    private static Label MutedText(string text, Padding margin) => new Label
    {
        Text = text,
        AutoSize = true,
        Margin = margin,
    }.WithColors(fore: () => Palette.TextMuted);

    private TableLayoutPanel TwoColumns(Control left, Control right, int height, Padding margin)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Height = LogicalToDeviceUnits(height),
            Margin = margin,
        }.WithColors(back: () => Palette.Surface);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Dock = DockStyle.Fill;
        right.Dock = DockStyle.Fill;
        left.Margin = new Padding(0, 0, 4, 0);
        right.Margin = new Padding(4, 0, 0, 0);
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);
        return grid;
    }

    private SmartButton CheckButton(string text, Func<Task> onClick)
    {
        var button = new SmartButton
        {
            Text = text,
            AlignLeft = true,
            Height = LogicalToDeviceUnits(34),
            Margin = new Padding(0, 0, 0, 5),
        };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    /// <summary>En Presentación, «Anterior» va a la izquierda de «Siguiente», como en el reloj.</summary>
    private int ActionOrder(int column) => _testMode == ControlMode.Presentation ? 1 - column : column;

    private void SelectTestMode(ControlMode mode)
    {
        _testMode = mode;
        _presentationMode.Selected = mode == ControlMode.Presentation;
        _multimediaMode.Selected = mode == ControlMode.Multimedia;

        var actions = CommandAllowlist.ActionsFor(mode);
        var first = actions[ActionOrder(0)];
        var second = actions[ActionOrder(1)];
        _firstAction.Text = Texts.ShortActionName(first);
        _firstAction.Glyph = Texts.ActionGlyph(first);
        _secondAction.Text = Texts.ShortActionName(second);
        _secondAction.Glyph = Texts.ActionGlyph(second);
        _firstAction.Invalidate();
        _secondAction.Invalidate();
    }

    private Task SendTestCommandAsync(AllowedAction action, double confidence = 1.0) =>
        SendRawRequestAsync(CommandAllowlist.ToWireName(action), confidence);

    private Task SendRawRequestAsync(string requestedAction, double confidence = 1.0)
    {
        var request = new CommandRequest
        {
            DeviceId = _testDeviceId,
            SessionToken = _testToken,
            ActiveMode = CommandAllowlist.ToWireName(_testMode),
            RequestedAction = requestedAction,
            Confidence = confidence,
        };
        var message = request.ToUtf8Json();
        _lastSentMessage = message;
        return SendRawAsync(message);
    }

    private async Task SendRawAsync(byte[] message)
    {
        if (_receiver.State != ReceiverState.Running)
        {
            ShowLastResponse("SmartControl está en pausa. Reanúdalo para enviar pruebas.", () => Palette.DangerOnSoft);
            return;
        }
        try
        {
            await LoopbackTestClient.SendAsync(_receiver.Port, message, SendTimeout);
        }
        catch (Exception ex) when (ex is SocketException or System.IO.IOException or OperationCanceledException)
        {
            ShowLastResponse("No se pudo entregar el mensaje. Comprueba que SmartControl esté activo y vuelve a intentarlo.", () => Palette.DangerOnSoft);
        }
    }

    private void ShowLastResponse(string text, Func<Color> color)
    {
        _lastResponseLabel.Text = text;
        _lastResponseLabel.WithColors(fore: color);
        Announcer.Announce(this, text, AutomationNotificationKind.ActionCompleted);
    }

    private void AddActivity(CommandProcessedEventArgs e)
    {
        var entry = ActivityEntry.From(e);
        _activity.Add(entry);
        ShowLastResponse(
            entry.Accepted ? $"Hecho: {entry.Title}." : $"Bloqueado: {entry.Title.ToLowerInvariant()}.",
            entry.Accepted ? () => Palette.SuccessOnSoft : () => Palette.DangerOnSoft);
    }

    private void UpdateStatus()
    {
        switch (_receiver.State)
        {
            case ReceiverState.Running:
                var watches = _receiver.ConnectedWatches;
                if (watches.Length > 0)
                {
                    _stateDot.DotColor = Palette.Success;
                    _stateTitle.Text = "Reloj conectado";
                    _stateDetail.Text = $"{string.Join(", ", watches)}. Los gestos controlan este equipo.";
                }
                else
                {
                    _stateDot.DotColor = Palette.Accent;
                    _stateTitle.Text = "Esperando al reloj";
                    _stateDetail.Text = "Abre SmartControl en el reloj. Tiene que estar en la misma red Wi-Fi que este equipo.";
                }
                _startStopButton.Text = "Pausar";
                _startStopButton.Kind = ButtonKind.Secondary;
                var discovery = _receiver.DiscoveryPort > 0 ? $" · búsqueda en UDP {_receiver.DiscoveryPort}" : "";
                _technicalLabel.Text = $"Detalles técnicos · puerto {_receiver.Port}{discovery} · conexiones abiertas: {_receiver.ConnectedClients} · registro: {_logPath}";
                break;
            case ReceiverState.Faulted:
                _stateDot.DotColor = Palette.Danger;
                _stateTitle.Text = "No se pudo iniciar";
                _stateDetail.Text = _receiver.LastError ?? "Algo impidió abrir la recepción de comandos.";
                _startStopButton.Text = "Reintentar";
                _startStopButton.Kind = ButtonKind.Primary;
                _technicalLabel.Text = $"Detalles técnicos · receptor con error · registro: {_logPath}";
                break;
            default:
                _stateDot.DotColor = Palette.Idle;
                _stateTitle.Text = "En pausa";
                _stateDetail.Text = "No se recibe ningún comando hasta que lo reanudes.";
                _startStopButton.Text = "Reanudar";
                _startStopButton.Kind = ButtonKind.Primary;
                _technicalLabel.Text = $"Detalles técnicos · receptor detenido · registro: {_logPath}";
                break;
        }
        _startStopButton.Invalidate();
        if (_pairing is not null)
        {
            var count = _pairing.Registry.Saved.Count;
            _watchesButton.Text = count == 1 ? "1 reloj vinculado" : $"{count} relojes vinculados";
            _watchesButton.Invalidate();
        }
        AnnounceStatus();
    }

    /// <summary>Lee el estado nuevo a quien use un lector de pantalla, sin repetirlo si no cambió.</summary>
    private void AnnounceStatus()
    {
        if (!IsHandleCreated)
            return;
        if (_statusAnnouncement.Next(_stateTitle.Text, _stateDetail.Text) is { } text)
            Announcer.Announce(this, text, AutomationNotificationKind.Other);
    }

    private async Task ToggleReceiverAsync()
    {
        _startStopButton.Enabled = false;
        try
        {
            if (_receiver.State == ReceiverState.Running)
                await _receiver.StopAsync();
            else
                StartReceiver();
        }
        finally
        {
            _startStopButton.Enabled = true;
            UpdateStatus();
        }
    }

    private void StartReceiver()
    {
        try
        {
            _receiver.Start();
        }
        catch (SocketException)
        {
            // El receptor ya registró el error y pasó a Faulted.
        }
        UpdateStatus();
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closingAfterStop || _receiver.State != ReceiverState.Running)
            return;
        // Se cierra el receptor de forma ordenada antes de cerrar la ventana.
        e.Cancel = true;
        Enabled = false;
        await _receiver.StopAsync();
        _closingAfterStop = true;
        Close();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // La ventana se está cerrando.
        }
    }
}
