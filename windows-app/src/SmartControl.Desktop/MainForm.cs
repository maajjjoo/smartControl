using System;
using System.Drawing;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
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

    private ControlMode _testMode = ControlMode.Presentation;
    private byte[]? _lastSentMessage;
    private bool _closingAfterStop;

    public MainForm(LocalCommandReceiver receiver, FileEventLog log, string testDeviceId, string testToken)
    {
        _receiver = receiver;
        _logPath = log.CurrentFilePath;
        _testDeviceId = testDeviceId;
        _testToken = testToken;

        Text = "SmartControl";
        Font = Typography.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(640));
        Size = new Size(LogicalToDeviceUnits(1180), LogicalToDeviceUnits(780));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Background;
        ForeColor = Palette.Text;

        BuildLayout();
        SelectTestMode(ControlMode.Presentation);

        _receiver.StatusChanged += (_, _) => RunOnUi(UpdateStatus);
        _receiver.CommandProcessed += (_, e) => RunOnUi(() => AddActivity(e));
        Load += (_, _) => StartReceiver();
        FormClosing += OnFormClosing;
        UpdateStatus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 12),
            BackColor = Palette.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(360)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        var status = BuildStatusCard();
        root.Controls.Add(status, 0, 1);
        root.SetColumnSpan(status, 2);
        root.Controls.Add(BuildActivityCard(), 0, 2);
        root.Controls.Add(BuildTestCard(), 1, 2);

        _technicalLabel.Dock = DockStyle.Fill;
        _technicalLabel.AutoEllipsis = true;
        _technicalLabel.Font = Typography.Small;
        _technicalLabel.ForeColor = Palette.TextMuted;
        _technicalLabel.Margin = new Padding(2, 10, 2, 0);
        _technicalLabel.Height = LogicalToDeviceUnits(22);
        root.Controls.Add(_technicalLabel, 0, 3);
        root.SetColumnSpan(_technicalLabel, 2);

        Controls.Add(root);
    }

    private static Control BuildHeader()
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
            ForeColor = Palette.Text,
            Margin = new Padding(0),
        }, 0, 0);
        header.Controls.Add(new Pill("Modo de prueba · las acciones se simulan", Palette.WarningSoft, Palette.WarningOnSoft)
        {
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 4, 0, 0),
        }, 1, 0);
        var subtitle = new Label
        {
            Text = "Controla tus presentaciones y tu música con gestos del reloj. Por ahora ninguna aplicación se controla de verdad.",
            AutoSize = true,
            ForeColor = Palette.TextMuted,
            Margin = new Padding(2, 2, 0, 0),
        };
        header.Controls.Add(subtitle, 0, 1);
        header.SetColumnSpan(subtitle, 2);
        return header;
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
            BackColor = Palette.Surface,
        };
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
        _stateTitle.ForeColor = Palette.Text;
        _stateTitle.Margin = new Padding(0);

        _stateDetail.AutoSize = true;
        _stateDetail.ForeColor = Palette.TextMuted;
        _stateDetail.Margin = new Padding(1, 2, 0, 0);

        _startStopButton.Name = "receiverToggle";
        _startStopButton.Size = new Size(LogicalToDeviceUnits(132), LogicalToDeviceUnits(38));
        _startStopButton.Anchor = AnchorStyles.Right;
        _startStopButton.Click += async (_, _) => await ToggleReceiverAsync();

        grid.Controls.Add(_stateDot, 0, 0);
        grid.SetRowSpan(_stateDot, 2);
        grid.Controls.Add(_stateTitle, 1, 0);
        grid.Controls.Add(_stateDetail, 1, 1);
        grid.Controls.Add(_startStopButton, 2, 0);
        grid.SetRowSpan(_startStopButton, 2);
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
            BackColor = Palette.Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0), BackColor = Palette.Surface };
        title.Controls.Add(new Label { Text = "Actividad", AutoSize = true, Font = Typography.Section, ForeColor = Palette.Text, Margin = new Padding(0) });
        title.Controls.Add(new Label { Text = "Lo más reciente arriba. Verde: se realizó. Rojo: se bloqueó y no se hizo nada.", AutoSize = true, ForeColor = Palette.TextMuted, Margin = new Padding(0, 2, 0, 0) });
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
            BackColor = Palette.Surface,
            Padding = new Padding(0, 0, 8, 0),
        };

        flow.Controls.Add(new Label { Text = "Probar sin el reloj", AutoSize = true, Font = Typography.Section, ForeColor = Palette.Text, Margin = new Padding(0) });
        flow.Controls.Add(MutedText("Envía un comando desde este equipo. No es un gesto del reloj.", new Padding(0, 2, 0, 14)));

        flow.Controls.Add(new Label { Text = "Modo", AutoSize = true, Font = Typography.Label, ForeColor = Palette.Text, Margin = new Padding(0, 0, 0, 6) });
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

        flow.Controls.Add(new Panel { Height = 1, BackColor = Palette.Outline, Margin = new Padding(0, 16, 0, 14) });

        flow.Controls.Add(new Label { Text = "Comprobar la seguridad", AutoSize = true, Font = Typography.Label, ForeColor = Palette.Text, Margin = new Padding(0) });
        flow.Controls.Add(MutedText("Deben bloquearse y aparecer en rojo en la actividad.", new Padding(0, 2, 0, 8)));

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
            ShowLastResponse("Primero envía un comando para poder repetirlo.", Palette.TextMuted);
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

    private static Label MutedText(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Palette.TextMuted,
        Margin = margin,
    };

    private TableLayoutPanel TwoColumns(Control left, Control right, int height, Padding margin)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Height = LogicalToDeviceUnits(height),
            Margin = margin,
            BackColor = Palette.Surface,
        };
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
            ShowLastResponse("SmartControl está en pausa. Reanúdalo para enviar pruebas.", Palette.DangerOnSoft);
            return;
        }
        try
        {
            await LoopbackTestClient.SendAsync(_receiver.Port, message, SendTimeout);
        }
        catch (Exception ex) when (ex is SocketException or System.IO.IOException or OperationCanceledException)
        {
            ShowLastResponse("No se pudo entregar el mensaje. Comprueba que SmartControl esté activo y vuelve a intentarlo.", Palette.DangerOnSoft);
        }
    }

    private void ShowLastResponse(string text, Color color)
    {
        _lastResponseLabel.Text = text;
        _lastResponseLabel.ForeColor = color;
    }

    private void AddActivity(CommandProcessedEventArgs e)
    {
        var entry = ActivityEntry.From(e);
        _activity.Add(entry);
        ShowLastResponse(
            entry.Accepted ? $"Hecho: {entry.Title} (simulado)." : $"Bloqueado: {entry.Title.ToLowerInvariant()}.",
            entry.Accepted ? Palette.SuccessOnSoft : Palette.DangerOnSoft);
    }

    private void UpdateStatus()
    {
        switch (_receiver.State)
        {
            case ReceiverState.Running:
                _stateDot.DotColor = Palette.Success;
                _stateTitle.Text = "Recibiendo comandos";
                _stateDetail.Text = "Por ahora solo desde este equipo: el reloj todavía no puede conectarse.";
                _startStopButton.Text = "Pausar";
                _startStopButton.Kind = ButtonKind.Secondary;
                _technicalLabel.Text = $"Detalles técnicos · escucha en 127.0.0.1:{_receiver.Port} · conexiones abiertas: {_receiver.ConnectedClients} · registro: {_logPath}";
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
