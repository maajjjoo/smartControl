using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartControl.Core.Logging;

namespace SmartControl.Core.Tests;

internal static class EventLogTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("registro: una entrada por línea y retención de 7 días", () => { WritesSingleLinesAndPrunesOldFiles(); return Task.CompletedTask; });
    }

    private static void WritesSingleLinesAndPrunesOldFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            File.WriteAllText(Path.Combine(directory, "smartcontrol-20261010.log"), "old");
            File.WriteAllText(Path.Combine(directory, "smartcontrol-20261020.log"), "recent");
            File.WriteAllText(Path.Combine(directory, "unrelated-20200101.log"), "not ours");

            var log = new FileEventLog(directory, retentionDays: 7, clock: () => now);
            log.Write(EventLevel.Info, "test", "linea\r\ninyectada");

            Check.True(!File.Exists(Path.Combine(directory, "smartcontrol-20261010.log")), "debe borrar archivos antiguos propios");
            Check.True(File.Exists(Path.Combine(directory, "smartcontrol-20261020.log")), "debe conservar archivos recientes");
            Check.True(File.Exists(Path.Combine(directory, "unrelated-20200101.log")), "no debe tocar archivos ajenos");

            var lines = File.ReadAllLines(log.CurrentFilePath);
            Check.Equal(1, lines.Length);
            Check.Equal("2026-10-25T12:00:00.000Z INFO test linea  inyectada", lines.Single());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
