using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Carga con GestureModelLoader el modelo SINTÉTICO exportado por la herramienta de Python
/// (ai-module/training/fixtures/model-parity) y comprueba que la inferencia en C# da las mismas
/// probabilidades que en Python. No mide ninguna precisión.
/// </summary>
internal static class ModelParityTests
{
    private const double Tolerance = 1e-9;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("paridad: modelo exportado en Python con las mismas probabilidades en C#", () => { MatchesPython(); return Task.CompletedTask; });
    }

    private static void MatchesPython()
    {
        var modelPath = FeatureParityTests.RepositoryFile(Path.Combine("ai-module", "training", "fixtures", "model-parity", "model.json"));
        Check.True(GestureModelLoader.TryLoad(modelPath, allowSyntheticFixture: true, out var model, out var reason), $"no carga: {reason}");
        Check.Equal(ModelDataSource.SyntheticFixture, model!.DataSource);
        Check.True(!GestureModelLoader.TryLoad(modelPath, allowSyntheticFixture: false, out _, out _), "fuera de las pruebas debe rechazarse");
        var classifier = new DenseClassifier(model);

        var featuresPath = FeatureParityTests.RepositoryFile(Path.Combine("ai-module", "training", "fixtures", "feature-parity.json"));
        var expectedPath = FeatureParityTests.RepositoryFile(Path.Combine("ai-module", "training", "fixtures", "model-parity", "expected.json"));
        using var cases = JsonDocument.Parse(File.ReadAllBytes(featuresPath));
        using var expected = JsonDocument.Parse(File.ReadAllBytes(expectedPath));
        var rows = expected.RootElement.GetProperty("probabilities").EnumerateArray().GetEnumerator();

        var index = 0;
        foreach (var item in cases.RootElement.GetProperty("cases").EnumerateArray())
        {
            var samples = new List<SensorSample>();
            foreach (var row in item.GetProperty("gyroscope").EnumerateArray())
            {
                var v = new double[4];
                var i = 0;
                foreach (var value in row.EnumerateArray())
                    v[i++] = value.GetDouble();
                samples.Add(new SensorSample((int)v[0], v[1], v[2], v[3]));
            }
            Check.True(SensorWindow.TryCreate(samples, null, out var window, out _), $"caso {index}");
            Check.True(classifier.TryPredict(MovementFeatures.Extract(window!), out var prediction), $"caso {index}: sin predicción");

            Check.True(rows.MoveNext(), "faltan probabilidades esperadas");
            var p = new double[3];
            var k = 0;
            foreach (var value in rows.Current.EnumerateArray())
                p[k++] = value.GetDouble();
            Near(p[0], prediction.TwistOut, index, "TWIST_OUT");
            Near(p[1], prediction.TwistIn, index, "TWIST_IN");
            Near(p[2], prediction.NoGesture, index, "NO_GESTURE");
            index++;
        }
        Check.True(!rows.MoveNext(), "sobran probabilidades esperadas");
        Check.True(index >= 40, "al menos 40 casos");
    }

    private static void Near(double expected, double actual, int index, string name)
    {
        if (Math.Abs(expected - actual) > Tolerance)
            throw new TestFailure($"caso {index}, {name}: Python {expected:R}, C# {actual:R}");
    }
}
