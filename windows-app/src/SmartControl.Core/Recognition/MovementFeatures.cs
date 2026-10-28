using System;
using System.Collections.Generic;

namespace SmartControl.Core.Recognition;

/// <summary>
/// Conjunto de características gyro-v1 (docs/phase2-ai-evaluation.md §6.3): 31 valores
/// calculados solo con el giroscopio de una <see cref="SensorWindow"/>. Es determinista y
/// no usa el acelerómetro. Un modelo declara qué conjunto espera; si se cambia el orden o
/// el cálculo, hay que crear otro identificador en lugar de modificar este.
/// </summary>
public static class MovementFeatures
{
    public const string FeatureSetId = "gyro-v1";

    /// <summary>Por debajo de este valor (rad/s) una muestra no cuenta para los cruces por cero.</summary>
    public const double ZeroCrossingDeadBand = 0.2;

    private static readonly string[] PerAxis =
        { "mean", "std", "min", "max", "peak_abs", "peak_position", "integral", "zero_crossings", "energy" };

    /// <summary>Nombres en el mismo orden que <see cref="Extract"/>.</summary>
    public static IReadOnlyList<string> Names { get; } = BuildNames();

    public static int Count => Names.Count;

    public static double[] Extract(SensorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var samples = window.Gyroscope;
        var features = new double[Count];
        var index = 0;
        var absIntegrals = new double[3];

        for (var axis = 0; axis < 3; axis++)
        {
            var stats = AxisStats(samples, axis);
            features[index++] = stats.Mean;
            features[index++] = stats.Std;
            features[index++] = stats.Min;
            features[index++] = stats.Max;
            features[index++] = stats.PeakAbs;
            features[index++] = stats.PeakPosition;
            features[index++] = stats.Integral;
            features[index++] = stats.ZeroCrossings;
            features[index++] = stats.Energy;
            absIntegrals[axis] = stats.AbsIntegral;
        }

        double magnitudeMax = 0, magnitudeSum = 0;
        foreach (var s in samples)
        {
            var magnitude = Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z);
            magnitudeMax = Math.Max(magnitudeMax, magnitude);
            magnitudeSum += magnitude;
        }
        features[index++] = magnitudeMax;
        features[index++] = magnitudeSum / samples.Count;
        features[index++] = window.DurationMs / 1000.0;

        var absTotal = absIntegrals[0] + absIntegrals[1] + absIntegrals[2];
        // Sin movimiento no hay eje dominante.
        features[index++] = absTotal > 0 ? absIntegrals[0] / absTotal : 0.0;
        return features;
    }

    private readonly record struct Stats(
        double Mean, double Std, double Min, double Max, double PeakAbs, double PeakPosition,
        double Integral, double ZeroCrossings, double Energy, double AbsIntegral);

    private static Stats AxisStats(IReadOnlyList<SensorSample> samples, int axis)
    {
        var count = samples.Count;
        double sum = 0, sumSquares = 0, min = double.MaxValue, max = double.MinValue, peakAbs = -1;
        double integral = 0, absIntegral = 0;
        var peakIndex = 0;
        var crossings = 0;
        var lastSign = 0;

        for (var i = 0; i < count; i++)
        {
            var value = Value(samples[i], axis);
            sum += value;
            sumSquares += value * value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            // Primer instante del pico, por eso la comparación es estricta.
            if (Math.Abs(value) > peakAbs)
            {
                peakAbs = Math.Abs(value);
                peakIndex = i;
            }
            if (Math.Abs(value) >= ZeroCrossingDeadBand)
            {
                var sign = Math.Sign(value);
                if (lastSign != 0 && sign != lastSign)
                    crossings++;
                lastSign = sign;
            }
            if (i > 0)
            {
                var previous = Value(samples[i - 1], axis);
                var dt = (samples[i].TOffsetMs - samples[i - 1].TOffsetMs) / 1000.0;
                integral += (value + previous) / 2 * dt;
                absIntegral += (Math.Abs(value) + Math.Abs(previous)) / 2 * dt;
            }
        }

        var mean = sum / count;
        var energy = sumSquares / count;
        // Varianza poblacional; Max(0, …) evita un negativo minúsculo por redondeo.
        var std = Math.Sqrt(Math.Max(0, energy - mean * mean));
        var duration = samples[count - 1].TOffsetMs;
        var peakPosition = (double)samples[peakIndex].TOffsetMs / duration;
        return new Stats(mean, std, min, max, peakAbs, peakPosition, integral, crossings, energy, absIntegral);
    }

    private static double Value(SensorSample sample, int axis) => axis switch
    {
        0 => sample.X,
        1 => sample.Y,
        _ => sample.Z,
    };

    private static string[] BuildNames()
    {
        var names = new List<string>();
        foreach (var axis in new[] { "x", "y", "z" })
            foreach (var name in PerAxis)
                names.Add($"gyro_{axis}_{name}");
        names.Add("gyro_magnitude_max");
        names.Add("gyro_magnitude_mean");
        names.Add("duration_s");
        names.Add("x_dominance");
        return names.ToArray();
    }
}
