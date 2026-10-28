using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Medición de latencia en este equipo (docs/phase2-ai-evaluation.md §8.1), con una ventana y un
/// modelo SINTÉTICOS. Mide solo el cálculo en Windows; no incluye el transporte ni el reloj.
/// El límite comprobado es muy holgado: sirve para detectar regresiones graves, no como objetivo.
/// </summary>
internal static class RecognitionLatencyTests
{
    private const int Warmup = 1_000;
    private const int Iterations = 10_000;
    private const long NowMs = 1792633800125;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("latencia: características + inferencia y recorrido completo", () => { Measure(); return Task.CompletedTask; });
    }

    private static void Measure()
    {
        var window = Window();
        Check.True(SensorWindow.TryCreate(window, null, out var parsed, out _), "ventana válida");
        var classifier = new DenseClassifier(ModelFixtures.Parse(ModelFixtures.LogisticJson()));
        // Caché de repetición grande solo para la medición: 11 000 mensajes caben en la ventana de frescura.
        var validator = new CommandValidator(
            new Dictionary<string, string> { ["wearos-watch-01"] = "test-token-not-a-secret" },
            new CommandValidatorOptions { ReplayCacheSize = Warmup + Iterations + 10 },
            () => NowMs);
        var pipeline = new RecognitionPipeline(classifier, validator, "wearos-watch-01", "test-token-not-a-secret", () => NowMs);

        var inference = Time(() =>
        {
            var features = MovementFeatures.Extract(parsed!);
            classifier.TryPredict(features, out _);
        });
        var full = Time(() =>
        {
            var outcome = pipeline.Recognize(window, null, ControlMode.Presentation);
            if (outcome.Status != RecognitionStatus.Accepted)
                throw new TestFailure($"la medición debe recorrer el camino aceptado: {outcome.ReasonCode}");
        });

        Console.WriteLine($"        entorno: {Environment.ProcessorCount} procesadores lógicos, .NET {Environment.Version}, " +
                          $"{(Stopwatch.IsHighResolution ? "reloj de alta resolución" : "reloj de baja resolución")}");
        Console.WriteLine($"        ventana sintética de {window.Count} muestras, modelo sintético de 1 capa, {Iterations} repeticiones");
        Console.WriteLine($"        características + inferencia: {Format(inference)}");
        Console.WriteLine($"        recorrido completo con validador: {Format(full)}");

        Check.True(full[(int)(full.Length * 0.95)] < 5.0, "el p95 del recorrido completo debería estar muy por debajo de 5 ms");
    }

    private static List<SensorSample> Window()
    {
        var samples = new List<SensorSample>();
        for (var i = 0; i < 50; i++)
            samples.Add(new SensorSample(i * 20, i is >= 10 and < 15 ? 4.0 : 0.0, 0.1, -0.1));
        return samples;
    }

    /// <summary>Tiempos de cada repetición en milisegundos, ordenados.</summary>
    private static double[] Time(Action action)
    {
        for (var i = 0; i < Warmup; i++)
            action();
        var times = new double[Iterations];
        var stopwatch = new Stopwatch();
        for (var i = 0; i < Iterations; i++)
        {
            stopwatch.Restart();
            action();
            stopwatch.Stop();
            times[i] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return times;
    }

    private static string Format(double[] sorted) =>
        $"p50 {sorted[sorted.Length / 2] * 1000:F1} µs, p95 {sorted[(int)(sorted.Length * 0.95)] * 1000:F1} µs, máx {sorted[^1] * 1000:F1} µs";
}
