using System;
using System.Net;
using System.Security.Cryptography;
using System.Windows.Forms;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Execution;
using SmartControl.Desktop.Pairing;

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

        var log = new FileEventLog(FileEventLog.DefaultDirectory);

        // Relojes vinculados en sesiones anteriores, guardados cifrados para esta cuenta de Windows.
        var store = new PairingStore();
        var (pcId, saved) = store.Load();
        var registry = new PairedDeviceRegistry();
        registry.Load(saved);
        registry.Changed += (_, _) =>
        {
            try
            {
                store.Save(pcId, registry.Saved);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or CryptographicException)
            {
                log.Write(EventLevel.Error, "pairing", $"save_failed error={ex.GetType().Name}");
            }
        };

        // El token del panel de pruebas se genera en cada arranque y solo vive en memoria.
        var testToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        registry.AddSessionDevice(TestPanelDeviceId, testToken, "Panel de prueba");

        var pairing = new PairingCoordinator(registry);
        // Escucha en la red local para que el reloj llegue por Wi-Fi y responde a su búsqueda.
        // Solo un reloj vinculado (con su clave) puede enviar comandos.
        var receiver = new LocalCommandReceiver(new CommandValidator(registry), new WindowsCommandExecutor(), log,
            pairing: pairing,
            options: new ReceiverOptions
            {
                ListenAddress = IPAddress.Any,
                PcId = pcId,
                PcName = Environment.MachineName,
                DiscoveryPort = DiscoveryResponder.DefaultPort,
            });

        log.Write(EventLevel.Info, "app", "started");
        Application.Run(new MainForm(receiver, log, TestPanelDeviceId, testToken, pairing));
        log.Write(EventLevel.Info, "app", "closed");
    }
}
