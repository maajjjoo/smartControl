using System;
using System.Net;
using System.Security.Cryptography;
using System.Windows.Forms;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Configuration;
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

        // IA (ADR-01 C, ADR-03): el modelo entrenado se instala en %LOCALAPPDATA%\SmartControl\model.
        // Sin un modelo válido (o si no está entrenado con datos reales) deciden las reglas del reloj.
        var validator = new CommandValidator(registry);
        var classifier = LoadModel(log);
        var recognizer = new GestureRecognizer(registry, validator, classifier, LoadGemini(log));
        // Escucha en la red local para que el reloj llegue por Wi-Fi y responde a su búsqueda.
        // Solo un reloj vinculado (con su clave) puede enviar comandos.
        var receiver = new LocalCommandReceiver(validator, new WindowsCommandExecutor(), log,
            pairing: pairing,
            recognizer: recognizer,
            options: new ReceiverOptions
            {
                ListenAddress = IPAddress.Any,
                PcId = pcId,
                PcName = Environment.MachineName,
                DiscoveryPort = DiscoveryResponder.DefaultPort,
            });

        log.Write(EventLevel.Info, "app", "started");
        Application.Run(new MainForm(receiver, log, TestPanelDeviceId, testToken, pairing, recognizer));
        log.Write(EventLevel.Info, "app", "closed");
    }

    /// <summary>
    /// Gemini (ADR-07), si hay GEMINI_API_KEY en el entorno o en un .env. Sin clave, o con un nombre
    /// de modelo no válido, no hay IA remota. La clave nunca se registra.
    /// </summary>
    private static IRemoteGestureClassifier? LoadGemini(IEventLog log)
    {
        var key = EnvFile.Get(EnvFile.GeminiApiKey);
        if (key is null)
        {
            log.Write(EventLevel.Info, "ai", "gemini_missing_key");
            return null;
        }
        try
        {
            var gemini = new GeminiGestureClassifier(key, EnvFile.Get(EnvFile.GeminiModel));
            log.Write(EventLevel.Info, "ai", $"gemini_configured model={gemini.Model}");
            return gemini;
        }
        catch (ArgumentException)
        {
            log.Write(EventLevel.Warning, "ai", "gemini_invalid_model");
            return null;
        }
    }

    /// <summary>Carpeta donde se instala el modelo entrenado (model.json y model.json.sha256).</summary>
    internal static string ModelPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartControl", "model", "model.json");

    /// <summary>
    /// Carga el modelo si existe. Los modelos sintéticos se rechazan aquí: solo sirven en pruebas,
    /// nunca para controlar el equipo (ADR-06).
    /// </summary>
    private static DenseClassifier? LoadModel(IEventLog log)
    {
        if (!System.IO.File.Exists(ModelPath))
        {
            log.Write(EventLevel.Info, "ai", "model_missing using=RULES");
            return null;
        }
        if (GestureModelLoader.TryLoad(ModelPath, allowSyntheticFixture: false, out var model, out var reason))
        {
            log.Write(EventLevel.Info, "ai", $"model_loaded version={model!.ModelVersion}");
            return new DenseClassifier(model);
        }
        log.Write(EventLevel.Warning, "ai", $"model_rejected reason={reason} using=RULES");
        return null;
    }
}
