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
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Pairing;

namespace SmartControl.Desktop.Tests;

/// <summary>Vinculación en la ventana y guardado de los relojes vinculados.</summary>
internal static class PairingWindowTests
{
    public static IEnumerable<(string, Action)> All()
    {
        yield return ("vinculación: la ventana muestra el código y «Vincular» guarda el reloj", WindowShowsCodeAndAccepts);
        yield return ("vinculación: si el reloj se va, el aviso desaparece", WindowHidesExpiredRequest);
        yield return ("guardado: los relojes vinculados sobreviven a un reinicio, cifrados", StoreRoundTrip);
        yield return ("guardado: un archivo dañado empieza de cero", DamagedStoreStartsEmpty);
    }

    private static (MainForm Form, PairingCoordinator Coordinator, string Directory) NewWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = new FileEventLog(directory);
        var registry = new PairedDeviceRegistry();
        var coordinator = new PairingCoordinator(registry, TimeSpan.FromSeconds(5));
        var receiver = new LocalCommandReceiver(new CommandValidator(registry), new SimulatedCommandExecutor(), log, port: 0, pairing: coordinator);
        var form = new MainForm(receiver, log, "panel", new string('A', 64), coordinator);
        _ = form.Handle;
        return (form, coordinator, directory);
    }

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

    private static void WindowShowsCodeAndAccepts()
    {
        var (form, coordinator, directory) = NewWindow();
        try
        {
            var codeSentToWatch = "";
            var pairing = Task.Run(() => coordinator.RequestAsync("watch-01", "Galaxy Watch", code => { codeSentToWatch = code; return Task.CompletedTask; }, CancellationToken.None));
            PumpUntil(() => form.PendingPairing is not null);
            Check.True(form.PendingPairing is not null, "la ventana debe mostrar la solicitud");
            Check.Equal(codeSentToWatch, form.PairingCodeText, "la ventana muestra el mismo código que recibe el reloj");
            Check.Equal("Galaxy Watch", form.PendingPairing!.DeviceName);

            form.AnswerPairing(accept: true);
            PumpUntil(() => pairing.IsCompleted);
            Check.Equal(PairingOutcome.Accepted, pairing.Result.Outcome);
            Check.Equal("Galaxy Watch", coordinator.Registry.Saved.Single().Name);
            Check.True(form.PendingPairing is null, "el aviso se cierra");
        }
        finally
        {
            form.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WindowHidesExpiredRequest()
    {
        var (form, coordinator, directory) = NewWindow();
        try
        {
            using var watchLeaves = new CancellationTokenSource();
            var pairing = Task.Run(() => coordinator.RequestAsync("watch-02", "Reloj", _ => Task.CompletedTask, watchLeaves.Token));
            PumpUntil(() => form.PendingPairing is not null);
            watchLeaves.Cancel();
            PumpUntil(() => pairing.IsCompleted && form.PendingPairing is null);
            Check.Equal(PairingOutcome.Cancelled, pairing.Result.Outcome);
            Check.True(form.PendingPairing is null, "el aviso desaparece");
            Check.Equal(0, coordinator.Registry.Saved.Count);
        }
        finally
        {
            form.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void StoreRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "smartcontrol-pairing-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var store = new PairingStore(path);
            var (pcId, empty) = store.Load();
            Check.Equal(0, empty.Count);
            Check.True(pcId.StartsWith("pc-", StringComparison.Ordinal), "identificador del equipo");

            var registry = new PairedDeviceRegistry();
            registry.AddSessionDevice("panel", "no-se-guarda", "Panel");
            var device = registry.Pair("watch-01", "Galaxy Watch");
            store.Save(pcId, registry.Saved);

            var raw = File.ReadAllText(path);
            Check.True(!raw.Contains(device.Token, StringComparison.Ordinal) && !raw.Contains("Galaxy", StringComparison.Ordinal), "el archivo está cifrado");

            var (pcIdAgain, loaded) = new PairingStore(path).Load();
            Check.Equal(pcId, pcIdAgain, "el equipo conserva su identificador");
            Check.Equal(1, loaded.Count, "solo el reloj, no el panel de prueba");
            Check.Equal(device, loaded[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void DamagedStoreStartsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "smartcontrol-pairing-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            File.WriteAllText(path, "no es un archivo cifrado");
            var (pcId, devices) = new PairingStore(path).Load();
            Check.Equal(0, devices.Count);
            Check.True(pcId.Length > 3, "genera un identificador nuevo");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
