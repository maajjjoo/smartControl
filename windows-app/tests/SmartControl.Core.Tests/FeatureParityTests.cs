using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Compara MovementFeatures con las características que calcula la herramienta de entrenamiento
/// en Python (ai-module/training/fixtures/feature-parity.json, ventanas SINTÉTICAS). Si no
/// coinciden, un modelo entrenado en Python recibiría en Windows entradas distintas.
/// </summary>
internal static class FeatureParityTests
{
    private const double Tolerance = 1e-9;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("paridad: características iguales en C# y en Python", () => { MatchesPython(); return Task.CompletedTask; });
    }

    internal static string RepositoryFile(string relativePath)
    {
        // Las pruebas se ejecutan desde windows-app/bin/...; se sube hasta encontrar la raíz del repositorio.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new TestFailure($"no se encontró {relativePath}");
    }

    private static void MatchesPython()
    {
        var path = RepositoryFile(Path.Combine("ai-module", "training", "fixtures", "feature-parity.json"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Check.Equal("synthetic", root.GetProperty("dataSource").GetString());
        Check.Equal(MovementFeatures.FeatureSetId, root.GetProperty("featureSet").GetString());

        var names = root.GetProperty("names");
        Check.Equal(MovementFeatures.Count, names.GetArrayLength());
        var n = 0;
        foreach (var name in names.EnumerateArray())
            Check.Equal(MovementFeatures.Names[n++], name.GetString(), "orden de los nombres");

        var cases = 0;
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            var samples = new List<SensorSample>();
            foreach (var row in item.GetProperty("gyroscope").EnumerateArray())
            {
                var values = new double[4];
                var i = 0;
                foreach (var value in row.EnumerateArray())
                    values[i++] = value.GetDouble();
                samples.Add(new SensorSample((int)values[0], values[1], values[2], values[3]));
            }
            Check.True(SensorWindow.TryCreate(samples, null, out var window, out var reason), $"caso {cases}: {reason}");
            var actual = MovementFeatures.Extract(window!);

            var f = 0;
            foreach (var expected in item.GetProperty("features").EnumerateArray())
            {
                var difference = Math.Abs(expected.GetDouble() - actual[f]);
                if (difference > Tolerance)
                    throw new TestFailure($"caso {cases}, {MovementFeatures.Names[f]}: Python {expected.GetDouble():R}, C# {actual[f]:R}");
                f++;
            }
            Check.Equal(MovementFeatures.Count, f);
            cases++;
        }
        Check.True(cases >= 40, "el fixture debe tener al menos 40 casos");
    }
}
