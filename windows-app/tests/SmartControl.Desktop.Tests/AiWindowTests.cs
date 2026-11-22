using System;
using System.Collections.Generic;
using System.IO;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Ui;

namespace SmartControl.Desktop.Tests;

/// <summary>Cómo se ve la IA en la ventana: su estado y quién decidió cada gesto.</summary>
internal static class AiWindowTests
{
    public static IEnumerable<(string, Action)> All()
    {
        yield return ("ia: sin modelo la ventana dice que deciden las reglas", WindowSaysRulesWithoutModel);
        yield return ("ia: la actividad dice si decidió la IA y con qué confianza", ActivityShowsWhoDecided);
        yield return ("ia: un movimiento normal descartado por la IA se explica", ActivityExplainsNoGesture);
    }

    private static readonly DateTimeOffset At = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static void WindowSaysRulesWithoutModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var log = new FileEventLog(directory);
            var registry = new PairedDeviceRegistry();
            var validator = new CommandValidator(registry);
            var recognizer = new GestureRecognizer(registry, validator, classifier: null);
            var receiver = new LocalCommandReceiver(validator, new SimulatedCommandExecutor(), log, port: 0, recognizer: recognizer);
            using var form = new MainForm(receiver, log, "panel", new string('A', 64), recognizer: recognizer);
            Check.Equal("IA sin configurar · por ahora deciden las reglas del reloj", form.AiStatusText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ActivityShowsWhoDecided()
    {
        var accepted = new ValidationResult(CommandStatus.Accepted, ReasonCodes.Ok, AllowedAction.MediaNextTrack, ControlMode.Multimedia);
        var byModel = ActivityEntry.From(new CommandProcessedEventArgs(accepted, null, At, new RecognitionInfo(RecognitionInfo.ByModel, "TWIST_OUT", 0.934)));
        Check.Equal("Multimedia · IA 93 % · enviado al reproductor activo", byModel.Detail);

        var byRules = ActivityEntry.From(new CommandProcessedEventArgs(accepted, null, At, new RecognitionInfo(RecognitionInfo.ByRules, "TWIST_OUT", 0.9)));
        Check.Equal("Multimedia · reglas del reloj · enviado al reproductor activo", byRules.Detail);

        var fromPanel = ActivityEntry.From(new CommandProcessedEventArgs(accepted, null, At));
        Check.Equal("Multimedia · enviado al reproductor activo", fromPanel.Detail, "un comando del panel no dice quién reconoció nada");
    }

    private static void ActivityExplainsNoGesture()
    {
        var rejected = ValidationResult.Reject(CommandStatus.RejectedLowConfidence, RecognitionReasons.NoGesture);
        var entry = ActivityEntry.From(new CommandProcessedEventArgs(rejected, null, At, new RecognitionInfo(RecognitionInfo.ByModel, "NO_GESTURE", 0.88)));
        Check.Equal("Movimiento normal", entry.Title);
        Check.Equal("La IA decidió que no era un gesto. No se hizo nada.", entry.Detail);
    }
}
