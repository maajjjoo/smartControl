using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Reglas de la ventana de sensores (docs/phase2-ai-evaluation.md §6.2).
/// Las series son sintéticas y solo comprueban el código, no movimientos reales.
/// </summary>
internal static class SensorWindowTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("ventana: acepta una serie válida y la copia", Sync(AcceptsValidWindowAndCopiesIt));
        yield return ("ventana: límites de número de muestras", Sync(EnforcesSampleCountLimits));
        yield return ("ventana: el tiempo empieza en 0 y siempre crece", Sync(RequiresIncreasingTimeFromZero));
        yield return ("ventana: hueco máximo y duración máxima", Sync(EnforcesGapAndDuration));
        yield return ("ventana: rechaza valores no finitos o fuera de rango", Sync(RejectsInvalidValues));
        yield return ("ventana: acelerómetro opcional y validado aparte", Sync(ValidatesOptionalAccelerometer));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    /// <summary>Serie a 50 Hz (cada 20 ms) con el valor dado en X.</summary>
    internal static List<SensorSample> Series(int count, double x = 0.0, int stepMs = 20)
    {
        var samples = new List<SensorSample>(count);
        for (var i = 0; i < count; i++)
            samples.Add(new SensorSample(i * stepMs, x, 0.0, 0.0));
        return samples;
    }

    private static string Reason(IReadOnlyList<SensorSample>? gyroscope, IReadOnlyList<SensorSample>? accelerometer = null)
    {
        var ok = SensorWindow.TryCreate(gyroscope, accelerometer, out var window, out var reason);
        Check.Equal(ok, window is not null, "el resultado y la ventana deben coincidir");
        return reason;
    }

    private static void AcceptsValidWindowAndCopiesIt()
    {
        var samples = Series(50, x: 1.5);
        Check.True(SensorWindow.TryCreate(samples, null, out var window, out var reason), "debe aceptar la serie");
        Check.Equal(SensorWindowReasons.Ok, reason);
        Check.Equal(50, window!.Gyroscope.Count);
        Check.Equal(980, window.DurationMs);
        Check.True(window.Accelerometer is null, "sin acelerómetro debe quedar null");

        samples[3] = new SensorSample(60, 99.0, 0.0, 0.0);
        Check.Equal(1.5, window.Gyroscope[3].X, "cambiar la lista original no debe afectar a la ventana");
    }

    private static void EnforcesSampleCountLimits()
    {
        Check.Equal(SensorWindowReasons.MissingSamples, Reason(null));
        Check.Equal(SensorWindowReasons.TooFewSamples, Reason(Series(SensorWindow.MinSamples - 1)));
        Check.Equal(SensorWindowReasons.Ok, Reason(Series(SensorWindow.MinSamples)));
        Check.Equal(SensorWindowReasons.Ok, Reason(Series(SensorWindow.MaxSamples, stepMs: 5)));
        Check.Equal(SensorWindowReasons.TooManySamples, Reason(Series(SensorWindow.MaxSamples + 1, stepMs: 5)));
    }

    private static void RequiresIncreasingTimeFromZero()
    {
        var shifted = Series(10);
        shifted[0] = new SensorSample(5, 0.0, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.FirstOffsetNotZero, Reason(shifted));

        var repeated = Series(10);
        repeated[4] = new SensorSample(repeated[3].TOffsetMs, 0.0, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.NonIncreasingTime, Reason(repeated));

        var backwards = Series(10);
        backwards[6] = new SensorSample(10, 0.0, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.NonIncreasingTime, Reason(backwards));
    }

    private static void EnforcesGapAndDuration()
    {
        var gap = Series(10);
        gap[9] = new SensorSample(gap[8].TOffsetMs + SensorWindow.MaxGapMs, 0.0, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.Ok, Reason(gap), "un hueco igual al máximo se admite");
        gap[9] = new SensorSample(gap[8].TOffsetMs + SensorWindow.MaxGapMs + 1, 0.0, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.GapTooLarge, Reason(gap));

        // 101 muestras cada 20 ms = 2000 ms exactos; una más supera el máximo.
        Check.Equal(SensorWindowReasons.Ok, Reason(Series(101)));
        Check.Equal(SensorWindowReasons.WindowTooLong, Reason(Series(102)));
    }

    private static void RejectsInvalidValues()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var samples = Series(10);
            samples[2] = new SensorSample(40, 0.0, bad, 0.0);
            Check.Equal(SensorWindowReasons.NonFiniteValue, Reason(samples));
        }

        Check.Equal(SensorWindowReasons.Ok, Reason(Series(10, x: SensorWindow.MaxGyroscopeAbs)), "el límite exacto se admite");
        Check.Equal(SensorWindowReasons.Ok, Reason(Series(10, x: -SensorWindow.MaxGyroscopeAbs)));
        Check.Equal(SensorWindowReasons.ValueOutOfRange, Reason(Series(10, x: SensorWindow.MaxGyroscopeAbs + 0.001)));

        var zAxis = Series(10);
        zAxis[5] = new SensorSample(100, 0.0, 0.0, -40.0);
        Check.Equal(SensorWindowReasons.ValueOutOfRange, Reason(zAxis));
    }

    private static void ValidatesOptionalAccelerometer()
    {
        Check.True(SensorWindow.TryCreate(Series(20), Series(20, x: 9.81), out var window, out _), "debe aceptar acelerómetro válido");
        Check.Equal(20, window!.Accelerometer!.Count);

        // El acelerómetro admite valores mayores que el giroscopio (hasta 16 g).
        Check.Equal(SensorWindowReasons.Ok, Reason(Series(20), Series(20, x: 100.0)));
        Check.Equal(SensorWindowReasons.AccelerometerPrefix + SensorWindowReasons.ValueOutOfRange,
            Reason(Series(20), Series(20, x: 161.0)));
        Check.Equal(SensorWindowReasons.AccelerometerPrefix + SensorWindowReasons.TooFewSamples,
            Reason(Series(20), Series(3)));
        // Un giroscopio inválido se informa primero.
        Check.Equal(SensorWindowReasons.TooFewSamples, Reason(Series(3), Series(3)));
    }
}
