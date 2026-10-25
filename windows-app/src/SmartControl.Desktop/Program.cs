using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows.Forms;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;

namespace SmartControl.Desktop;

internal static class Program
{
    /// <summary>Dispositivo ficticio que usa el panel de comandos de prueba.</summary>
    internal const string TestPanelDeviceId = "smartcontrol-test-panel";

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // El token del panel de pruebas se genera en cada arranque y solo vive en memoria.
        // La vinculación real con el reloj llegará con el transporte definitivo.
        var testToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var validator = new CommandValidator(new Dictionary<string, string> { [TestPanelDeviceId] = testToken });
        var log = new FileEventLog(FileEventLog.DefaultDirectory);
        var receiver = new LocalCommandReceiver(validator, new SimulatedCommandExecutor(), log);

        log.Write(EventLevel.Info, "app", "started");
        Application.Run(new MainForm(receiver, log, TestPanelDeviceId, testToken));
        log.Write(EventLevel.Info, "app", "closed");
    }
}
