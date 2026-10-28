using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Características gyro-v1 con valores calculados a mano sobre series sintéticas cortas.
/// Comprueban los cálculos, no la utilidad de las características con movimientos reales.
/// </summary>
internal static class MovementFeaturesTests
{
    private const double Tolerance = 1e-9;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("características: 31 nombres únicos en orden fijo", Sync(HasStableNames));
        yield return ("características: valores calculados a mano", Sync(MatchesHandComputedValues));
        yield return ("características: cruces por cero con banda muerta", Sync(CountsZeroCrossingsWithDeadBand));
        yield return ("características: integral con pasos irregulares", Sync(IntegratesWithUnevenSteps));
        yield return ("características: magnitud y dominancia del eje X", Sync(ComputesMagnitudeAndDominance));
        yield return ("características: no usan el acelerómetro", Sync(IgnoreAccelerometer));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > Tolerance)
            throw new TestFailure($"{name}: se esperaba {expected:R} y se obtuvo {actual:R}");
    }

    private static SensorWindow Window(IReadOnlyList<SensorSample> gyroscope, IReadOnlyList<SensorSample>? accelerometer = null)
    {
        Check.True(SensorWindow.TryCreate(gyroscope, accelerometer, out var window, out var reason), $"ventana inválida: {reason}");
        return window!;
    }

    private static IReadOnlyList<SensorSample> XSeries(params double[] x) =>
        x.Select((value, i) => new SensorSample(i * 20, value, 0.0, 0.0)).ToList();

    private static Dictionary<string, double> Named(double[] values) =>
        MovementFeatures.Names.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => values[p.i]);

    private static void HasStableNames()
    {
        Check.Equal(31, MovementFeatures.Count);
        Check.Equal(31, MovementFeatures.Names.Distinct().Count());
        Check.Equal("gyro_x_mean", MovementFeatures.Names[0]);
        Check.Equal("gyro_y_mean", MovementFeatures.Names[9]);
        Check.Equal("gyro_z_energy", MovementFeatures.Names[26]);
        Check.Equal("x_dominance", MovementFeatures.Names[30]);
        Check.Equal("gyro-v1", MovementFeatures.FeatureSetId);
    }

    private static void MatchesHandComputedValues()
    {
        // x = 0,1,2,3,3,2,1,0 cada 20 ms; y = z = 0.
        var values = MovementFeatures.Extract(Window(XSeries(0, 1, 2, 3, 3, 2, 1, 0)));
        Check.Equal(MovementFeatures.Count, values.Length);
        var f = Named(values);

        Near(1.5, f["gyro_x_mean"], "media");                       // 12 / 8
        Near(Math.Sqrt(1.25), f["gyro_x_std"], "desviación");       // 28/8 - 1.5²
        Near(0.0, f["gyro_x_min"], "mínimo");
        Near(3.0, f["gyro_x_max"], "máximo");
        Near(3.0, f["gyro_x_peak_abs"], "pico");
        Near(60.0 / 140.0, f["gyro_x_peak_position"], "posición del pico"); // primer 3 en t = 60 ms
        Near(0.24, f["gyro_x_integral"], "integral");               // 12 × 0,02 s
        Near(0.0, f["gyro_x_zero_crossings"], "cruces");
        Near(3.5, f["gyro_x_energy"], "energía");                   // 28 / 8

        foreach (var axis in new[] { "y", "z" })
            foreach (var name in new[] { "mean", "std", "min", "max", "peak_abs", "peak_position", "integral", "zero_crossings", "energy" })
                Near(0.0, f[$"gyro_{axis}_{name}"], $"{axis} {name}");

        Near(3.0, f["gyro_magnitude_max"], "magnitud máxima");
        Near(1.5, f["gyro_magnitude_mean"], "magnitud media");
        Near(0.14, f["duration_s"], "duración");
        Near(1.0, f["x_dominance"], "dominancia");
    }

    private static void CountsZeroCrossingsWithDeadBand()
    {
        // Solo cuentan los valores con |v| ≥ 0,2: +, −, (0,1), +, (−0,1), −, +, + → 4 cambios.
        var crossings = Named(MovementFeatures.Extract(Window(XSeries(0.5, -0.5, 0.1, 0.5, -0.1, -0.5, 0.5, 0.5))))["gyro_x_zero_crossings"];
        Near(4.0, crossings, "cruces con banda muerta");

        // Ruido por debajo de la banda: ningún cruce.
        var noise = Named(MovementFeatures.Extract(Window(XSeries(0.1, -0.1, 0.15, -0.19, 0.1, -0.1, 0.05, -0.05))))["gyro_x_zero_crossings"];
        Near(0.0, noise, "ruido");
    }

    private static void IntegratesWithUnevenSteps()
    {
        int[] times = { 0, 10, 30, 60, 70, 90, 120, 130 };
        var samples = times.Select(t => new SensorSample(t, 2.0, -1.0, 0.0)).ToList();
        var f = Named(MovementFeatures.Extract(Window(samples)));
        Near(0.26, f["gyro_x_integral"], "integral de x");   // 2 rad/s × 0,13 s
        Near(-0.13, f["gyro_y_integral"], "integral de y");
        Near(0.13, f["duration_s"], "duración");
    }

    private static void ComputesMagnitudeAndDominance()
    {
        var both = Enumerable.Range(0, 8).Select(i => new SensorSample(i * 20, 3.0, 4.0, 0.0)).ToList();
        var f = Named(MovementFeatures.Extract(Window(both)));
        Near(5.0, f["gyro_magnitude_max"], "magnitud");
        Near(5.0, f["gyro_magnitude_mean"], "magnitud media");
        Near(3.0 / 7.0, f["x_dominance"], "dominancia con x e y");

        var still = Named(MovementFeatures.Extract(Window(XSeries(0, 0, 0, 0, 0, 0, 0, 0))));
        Near(0.0, still["x_dominance"], "sin movimiento");
        Near(0.0, still["gyro_x_peak_position"], "pico sin movimiento");
        Check.True(still.Values.All(double.IsFinite), "sin movimiento todo debe ser finito");
    }

    private static void IgnoreAccelerometer()
    {
        var gyroscope = XSeries(0, 1, 2, 3, 3, 2, 1, 0);
        var accelerometer = Enumerable.Range(0, 8).Select(i => new SensorSample(i * 20, 0.0, 0.0, 9.81)).ToList();
        var without = MovementFeatures.Extract(Window(gyroscope));
        var with = MovementFeatures.Extract(Window(gyroscope, accelerometer));
        Check.True(without.SequenceEqual(with), "gyro-v1 no debe cambiar con el acelerómetro");
    }
}
