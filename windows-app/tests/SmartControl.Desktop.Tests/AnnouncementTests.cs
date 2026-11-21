using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Ui;

namespace SmartControl.Desktop.Tests;

/// <summary>Avisos de UI Automation que recibe un lector de pantalla.</summary>
internal static class AnnouncementTests
{
    private const string DeviceId = "smartcontrol-test-panel";
    private const string Token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    public static IEnumerable<(string, Action)> All()
    {
        yield return ("avisos: el estado se lee una vez por cambio", StatusIsReadOncePerChange);
        yield return ("avisos: la ventana lee el estado del receptor y la respuesta a un comando", WindowAnnouncesStatusAndResponse);
    }

    private static void StatusIsReadOncePerChange()
    {
        var status = new StatusAnnouncement();
        Check.Equal("Recibiendo comandos. Por ahora solo desde este equipo.", status.Next("Recibiendo comandos", "Por ahora solo desde este equipo."));
        Check.Equal(null, status.Next("Recibiendo comandos", "Por ahora solo desde este equipo."), "sin cambios no se repite");
        Check.Equal("No se pudo iniciar. El puerto 47800 ya está en uso.", status.Next("No se pudo iniciar", "El puerto 47800 ya está en uso"));
    }

    /// <summary>
    /// Ventana real (sin mostrarla) con un receptor real en un puerto libre de 127.0.0.1:
    /// arrancar, recibir un comando de prueba y detener deben producir un aviso cada uno.
    /// </summary>
    private static void WindowAnnouncesStatusAndResponse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var heard = new List<string>();
        void Listen(Control _, string message) => heard.Add(message);
        Announcer.Announced += Listen;
        try
        {
            var log = new FileEventLog(directory);
            var validator = new CommandValidator(new Dictionary<string, string> { [DeviceId] = Token });
            var receiver = new LocalCommandReceiver(validator, new SimulatedCommandExecutor(), log, port: 0);
            using var form = new MainForm(receiver, log, DeviceId, Token);
            _ = form.Handle;
            Check.Equal(0, heard.Count, "nada que leer antes de arrancar");

            receiver.Start();
            PumpUntil(() => heard.Count >= 1);
            Check.Equal("Esperando al reloj. Abre SmartControl en el reloj. Tiene que estar en la misma red Wi-Fi que este equipo.", heard.LastOrDefault());

            var request = new CommandRequest
            {
                DeviceId = DeviceId,
                SessionToken = Token,
                ActiveMode = CommandAllowlist.ToWireName(ControlMode.Presentation),
                RequestedAction = CommandAllowlist.ToWireName(AllowedAction.SlideNext),
                Confidence = 1.0,
            };
            var send = Task.Run(() => LoopbackTestClient.SendAsync(receiver.Port, request.ToUtf8Json(), TimeSpan.FromSeconds(3)));
            PumpUntil(() => send.IsCompleted && heard.Contains("Hecho: Siguiente diapositiva."));
            Check.True(heard.Contains("Hecho: Siguiente diapositiva."), $"falta la respuesta; se oyó: {string.Join(" | ", heard)}");
            Check.Equal(1, heard.Count(h => h.StartsWith("Esperando al reloj", StringComparison.Ordinal)),
                "abrir y cerrar la conexión de prueba no debe repetir el estado");

            var stop = Task.Run(receiver.StopAsync);
            PumpUntil(() => stop.IsCompleted && heard.LastOrDefault()?.StartsWith("En pausa", StringComparison.Ordinal) == true);
            Check.Equal("En pausa. No se recibe ningún comando hasta que lo reanudes.", heard.LastOrDefault());
        }
        finally
        {
            Announcer.Announced -= Listen;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Atiende los mensajes de la ventana hasta que se cumpla la condición (máximo 5 s).</summary>
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }
}
