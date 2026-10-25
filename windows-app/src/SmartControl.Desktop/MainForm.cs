using System;
using System.Drawing;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;

namespace SmartControl.Desktop;

/// <summary>
/// Ventana principal. Solo presenta el estado del receptor y los comandos procesados;
/// la validación y la recepción viven en SmartControl.Core.
/// </summary>
internal sealed class MainForm : Form
{
    private const int MaxRows = 200;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

    private static readonly Color AcceptedColor = Color.FromArgb(0x1B, 0x7F, 0x3B);
    private static readonly Color RejectedColor = Color.FromArgb(0xB4, 0x23, 0x18);
    private static readonly Color IdleColor = Color.FromArgb(0x80, 0x80, 0x80);
    private static readonly Color MutedText = Color.FromArgb(0x5F, 0x63, 0x68);

    private readonly LocalCommandReceiver _receiver;
    private readonly string _testDeviceId;
    private readonly string _testToken;

    private readonly Label _stateDot = new();
    private readonly Label _stateLabel = new();
    private readonly Label _connectionLabel = new();
    private readonly Button _startStopButton = new();
    private readonly ListView _commandList = new();
    private readonly ComboBox _modeCombo = new();
    private readonly Label _lastResponseLabel = new();
    private readonly ToolStripStatusLabel _logStatus = new();

    private byte[]? _lastSentMessage;
    private bool _closingAfterStop;

    public MainForm(LocalCommandReceiver receiver, FileEventLog log, string testDeviceId, string testToken)
    {
        _receiver = receiver;
        _testDeviceId = testDeviceId;
        _testToken = testToken;

        Text = "SmartControl";
        Font = new Font("Segoe UI", 9.75f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(600));
        Size = new Size(LogicalToDeviceUnits(1180), LogicalToDeviceUnits(720));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        BuildLayout();
        _logStatus.Text = "Registro: " + log.CurrentFilePath;

        _receiver.StatusChanged += (_, _) => RunOnUi(UpdateStatus);
        _receiver.CommandProcessed += (_, e) => RunOnUi(() => AddCommandRow(e));
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
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 8),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(330)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        var status = BuildStatusPanel();
        root.Controls.Add(status, 0, 1);
        root.SetColumnSpan(status, 2);
        root.Controls.Add(BuildCommandList(), 0, 2);
        root.Controls.Add(BuildTestPanel(), 1, 2);

        var statusStrip = new StatusStrip { SizingGrip = false };
        _logStatus.ForeColor = MutedText;
        _logStatus.Spring = true;
        _logStatus.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(_logStatus);

        Controls.Add(root);
        Controls.Add(statusStrip);
    }

    private static Control BuildHeader()
    {
        var header = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
        };
        header.Controls.Add(new Label
        {
            Text = "SmartControl",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20f),
            Margin = new Padding(0),
        });
        header.Controls.Add(new Label
        {
            Text = "Complemento para Windows · versión de desarrollo. Las acciones se simulan: no se controla ninguna aplicación.",
            AutoSize = true,
            ForeColor = MutedText,
            Margin = new Padding(2, 0, 0, 0),
        });
        return header;
    }

    private Control BuildStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10, 8, 10, 8),
            BackColor = SystemColors.Control,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _stateDot.Text = "●";
        _stateDot.AutoSize = true;
        _stateDot.Font = new Font("Segoe UI", 14f);
        _stateDot.Margin = new Padding(0, 0, 6, 0);
        _stateDot.AccessibleName = "Indicador de estado";

        _stateLabel.AutoSize = true;
        _stateLabel.Font = new Font("Segoe UI Semibold", 10.5f);
        _stateLabel.Anchor = AnchorStyles.Left;

        _connectionLabel.AutoSize = true;
        _connectionLabel.ForeColor = MutedText;
        _connectionLabel.Margin = new Padding(3, 0, 3, 0);

        _startStopButton.AutoSize = true;
        _startStopButton.Padding = new Padding(10, 2, 10, 2);
        _startStopButton.Anchor = AnchorStyles.Right;
        _startStopButton.Click += async (_, _) => await ToggleReceiverAsync();

        panel.Controls.Add(_stateDot, 0, 0);
        panel.SetRowSpan(_stateDot, 2);
        panel.Controls.Add(_stateLabel, 1, 0);
        panel.Controls.Add(_connectionLabel, 1, 1);
        panel.Controls.Add(_startStopButton, 2, 0);
        panel.SetRowSpan(_startStopButton, 2);
        return panel;
    }

    private Control BuildCommandList()
    {
        var group = new GroupBox { Text = "Comandos recibidos", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _commandList.Dock = DockStyle.Fill;
        _commandList.View = View.Details;
        _commandList.FullRowSelect = true;
        _commandList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _commandList.BorderStyle = BorderStyle.None;
        _commandList.Columns.Add("Hora", LogicalToDeviceUnits(70));
        _commandList.Columns.Add("Resultado", LogicalToDeviceUnits(180));
        _commandList.Columns.Add("Acción", LogicalToDeviceUnits(120));
        _commandList.Columns.Add("Modo", LogicalToDeviceUnits(110));
        _commandList.Columns.Add("Motivo", LogicalToDeviceUnits(220));
        _commandList.Columns.Add("Detalle", LogicalToDeviceUnits(300));
        // La última columna ocupa el espacio restante.
        _commandList.ClientSizeChanged += (_, _) =>
        {
            var used = 0;
            for (var i = 0; i < _commandList.Columns.Count - 1; i++)
                used += _commandList.Columns[i].Width;
            _commandList.Columns[^1].Width = Math.Max(LogicalToDeviceUnits(200), _commandList.ClientSize.Width - used);
        };
        group.Controls.Add(_commandList);
        return group;
    }

    private Control BuildTestPanel()
    {
        var group = new GroupBox { Text = "Comandos de prueba", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(10, 3, 0, 3) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };
        var width = LogicalToDeviceUnits(280);

        flow.Controls.Add(new Label
        {
            Text = "Envían mensajes reales al receptor por 127.0.0.1. Son comandos explícitos, no gestos reconocidos.",
            ForeColor = MutedText,
            MaximumSize = new Size(width, 0),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });

        flow.Controls.Add(new Label { Text = "Modo declarado en el mensaje", AutoSize = true });
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Width = width;
        _modeCombo.Items.AddRange(new object[] { "Presentación (PRESENTATION)", "Multimedia (MULTIMEDIA)" });
        _modeCombo.SelectedIndex = 0;
        _modeCombo.Margin = new Padding(0, 2, 0, 8);
        flow.Controls.Add(_modeCombo);

        foreach (var action in new[] { AllowedAction.SlideNext, AllowedAction.SlidePrevious, AllowedAction.MediaPlay, AllowedAction.MediaPause })
        {
            var wireName = CommandAllowlist.ToWireName(action);
            flow.Controls.Add(MakeButton(wireName, width, () => SendTestCommandAsync(wireName)));
        }

        flow.Controls.Add(new Label { Text = "Casos que deben rechazarse", AutoSize = true, Margin = new Padding(0, 12, 0, 2) });
        flow.Controls.Add(MakeButton("Acción fuera de la lista blanca", width, () => SendTestCommandAsync("cmd.exe /c calc")));
        flow.Controls.Add(MakeButton("Confianza baja (0.40)", width, () => SendTestCommandAsync(CommandAllowlist.ToWireName(AllowedAction.SlideNext), 0.40)));
        flow.Controls.Add(MakeButton("Mensaje mal formado", width, () => SendRawAsync(System.Text.Encoding.UTF8.GetBytes("{no es json"))));
        flow.Controls.Add(MakeButton("Repetir el último mensaje", width, () =>
        {
            if (_lastSentMessage is not null)
                return SendRawAsync(_lastSentMessage);
            ShowLastResponse("Todavía no se ha enviado ningún mensaje.", IdleColor);
            return Task.CompletedTask;
        }));

        _lastResponseLabel.AutoSize = true;
        _lastResponseLabel.MaximumSize = new Size(width, 0);
        _lastResponseLabel.Margin = new Padding(0, 12, 0, 0);
        flow.Controls.Add(_lastResponseLabel);

        // Los controles ocupan todo el ancho disponible, sin barra de desplazamiento horizontal.
        flow.ClientSizeChanged += (_, _) =>
        {
            var available = flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            foreach (Control child in flow.Controls)
            {
                if (child is Label label) label.MaximumSize = new Size(available, 0);
                else child.Width = available;
            }
        };

        group.Controls.Add(flow);
        return group;
    }

    private Button MakeButton(string text, int width, Func<Task> onClick)
    {
        var button = new Button { Text = text, Width = width, Height = LogicalToDeviceUnits(32), Margin = new Padding(0, 0, 0, 6) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private Task SendTestCommandAsync(string requestedAction, double confidence = 1.0)
    {
        var request = new CommandRequest
        {
            DeviceId = _testDeviceId,
            SessionToken = _testToken,
            ActiveMode = _modeCombo.SelectedIndex == 1 ? "MULTIMEDIA" : "PRESENTATION",
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
            ShowLastResponse("El receptor está detenido. Inícialo para enviar comandos de prueba.", RejectedColor);
            return;
        }
        try
        {
            await LoopbackTestClient.SendAsync(_receiver.Port, message, SendTimeout);
        }
        catch (Exception ex) when (ex is SocketException or System.IO.IOException or OperationCanceledException)
        {
            ShowLastResponse("No se pudo entregar el mensaje al receptor local.", RejectedColor);
        }
    }

    private void ShowLastResponse(string text, Color color)
    {
        _lastResponseLabel.Text = text;
        _lastResponseLabel.ForeColor = color;
    }

    private void AddCommandRow(CommandProcessedEventArgs e)
    {
        var result = e.Result;
        var accepted = result.Status == CommandStatus.Accepted;
        // Solo se muestran valores validados o generados por la aplicación, nunca texto recibido.
        var item = new ListViewItem(e.ReceivedAt.ToString("HH:mm:ss"))
        {
            ForeColor = accepted ? AcceptedColor : RejectedColor,
        };
        item.SubItems.Add(DescribeStatus(result.Status));
        item.SubItems.Add(result.Action is { } action ? CommandAllowlist.ToWireName(action) : "—");
        item.SubItems.Add(result.Mode is { } mode ? CommandAllowlist.ToWireName(mode) : "—");
        item.SubItems.Add(result.ReasonCode);
        item.SubItems.Add(e.Effect ?? "No se ejecutó ninguna acción");

        _commandList.BeginUpdate();
        _commandList.Items.Insert(0, item);
        while (_commandList.Items.Count > MaxRows)
            _commandList.Items.RemoveAt(_commandList.Items.Count - 1);
        _commandList.EndUpdate();

        ShowLastResponse(
            accepted ? $"Aceptado: {e.Effect}" : $"Rechazado ({result.ReasonCode}). No se ejecutó ninguna acción.",
            accepted ? AcceptedColor : RejectedColor);
    }

    private static string DescribeStatus(CommandStatus status) => status switch
    {
        CommandStatus.Accepted => "Aceptado (simulado)",
        CommandStatus.RejectedLowConfidence => "Rechazado: confianza baja",
        CommandStatus.RejectedNotAllowed => "Rechazado: no permitido",
        CommandStatus.RejectedInvalidMessage => "Rechazado: mensaje inválido",
        CommandStatus.ExecutionError => "Error de ejecución",
        _ => status.ToString(),
    };

    private void UpdateStatus()
    {
        switch (_receiver.State)
        {
            case ReceiverState.Running:
                _stateDot.ForeColor = AcceptedColor;
                _stateLabel.Text = $"Receptor activo en 127.0.0.1:{_receiver.Port}";
                _startStopButton.Text = "Detener receptor";
                break;
            case ReceiverState.Faulted:
                _stateDot.ForeColor = RejectedColor;
                _stateLabel.Text = "Error: " + (_receiver.LastError ?? "no se pudo iniciar el receptor");
                _startStopButton.Text = "Reintentar";
                break;
            default:
                _stateDot.ForeColor = IdleColor;
                _stateLabel.Text = "Receptor detenido";
                _startStopButton.Text = "Iniciar receptor";
                break;
        }

        var clients = _receiver.ConnectedClients;
        _connectionLabel.Text = _receiver.State == ReceiverState.Running
            ? $"Conexiones locales activas: {clients}. Solo acepta conexiones de este equipo; el reloj aún no puede conectarse."
            : "Sin conexiones.";
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
