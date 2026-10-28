using System;
using System.Collections.Generic;

namespace SmartControl.Core.Recognition;

/// <summary>Muestra de un sensor de 3 ejes. <see cref="TOffsetMs"/> es el desfase desde el inicio de la ventana.</summary>
public readonly record struct SensorSample(int TOffsetMs, double X, double Y, double Z);

/// <summary>Códigos de rechazo de una ventana. Son constantes propias: nunca contienen datos recibidos.</summary>
public static class SensorWindowReasons
{
    public const string Ok = "OK";
    public const string MissingSamples = "MISSING_SAMPLES";
    public const string TooFewSamples = "TOO_FEW_SAMPLES";
    public const string TooManySamples = "TOO_MANY_SAMPLES";
    public const string FirstOffsetNotZero = "FIRST_OFFSET_NOT_ZERO";
    public const string NonIncreasingTime = "NON_INCREASING_TIME";
    public const string GapTooLarge = "GAP_TOO_LARGE";
    public const string WindowTooLong = "WINDOW_TOO_LONG";
    public const string NonFiniteValue = "NON_FINITE_VALUE";
    public const string ValueOutOfRange = "VALUE_OUT_OF_RANGE";

    /// <summary>Prefijo de los códigos del acelerómetro, para distinguirlos de los del giroscopio.</summary>
    public const string AccelerometerPrefix = "ACCELEROMETER_";
}

/// <summary>
/// Tramo de movimiento ya segmentado por el reloj (docs/phase2-ai-evaluation.md §6.2).
/// El giroscopio es obligatorio; el acelerómetro es opcional y el conjunto de
/// características gyro-v1 no lo usa. Solo se puede construir con <see cref="TryCreate"/>,
/// así que toda instancia cumple las reglas. Es inmutable.
/// </summary>
public sealed class SensorWindow
{
    public const int MinSamples = 8;
    public const int MaxSamples = 256;
    public const int MaxDurationMs = 2000;
    public const int MaxGapMs = 100;
    /// <summary>Rango típico de un giroscopio de reloj (unos 2000 °/s).</summary>
    public const double MaxGyroscopeAbs = 35.0;
    /// <summary>16 g.</summary>
    public const double MaxAccelerometerAbs = 160.0;

    private SensorWindow(SensorSample[] gyroscope, SensorSample[]? accelerometer)
    {
        Gyroscope = gyroscope;
        Accelerometer = accelerometer;
    }

    /// <summary>Velocidad angular en rad/s.</summary>
    public IReadOnlyList<SensorSample> Gyroscope { get; }

    /// <summary>Aceleración en m/s², o null si el reloj no la envió.</summary>
    public IReadOnlyList<SensorSample>? Accelerometer { get; }

    public int DurationMs => Gyroscope[Gyroscope.Count - 1].TOffsetMs;

    /// <summary>
    /// Comprueba las reglas y crea la ventana. Nunca lanza excepciones por datos inválidos:
    /// devuelve false y un código de <see cref="SensorWindowReasons"/>.
    /// Las listas se copian, así que cambiarlas después no afecta a la ventana.
    /// </summary>
    public static bool TryCreate(
        IReadOnlyList<SensorSample>? gyroscope,
        IReadOnlyList<SensorSample>? accelerometer,
        out SensorWindow? window,
        out string reason)
    {
        window = null;
        reason = Check(gyroscope, MaxGyroscopeAbs);
        if (reason != SensorWindowReasons.Ok)
            return false;

        if (accelerometer is not null)
        {
            var accelerometerReason = Check(accelerometer, MaxAccelerometerAbs);
            if (accelerometerReason != SensorWindowReasons.Ok)
            {
                reason = SensorWindowReasons.AccelerometerPrefix + accelerometerReason;
                return false;
            }
        }

        window = new SensorWindow(Copy(gyroscope!), accelerometer is null ? null : Copy(accelerometer));
        return true;
    }

    private static string Check(IReadOnlyList<SensorSample>? samples, double maxAbs)
    {
        if (samples is null)
            return SensorWindowReasons.MissingSamples;
        if (samples.Count < MinSamples)
            return SensorWindowReasons.TooFewSamples;
        if (samples.Count > MaxSamples)
            return SensorWindowReasons.TooManySamples;
        if (samples[0].TOffsetMs != 0)
            return SensorWindowReasons.FirstOffsetNotZero;

        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (i > 0)
            {
                var gap = (long)sample.TOffsetMs - samples[i - 1].TOffsetMs;
                if (gap <= 0)
                    return SensorWindowReasons.NonIncreasingTime;
                if (gap > MaxGapMs)
                    return SensorWindowReasons.GapTooLarge;
            }
            if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || !double.IsFinite(sample.Z))
                return SensorWindowReasons.NonFiniteValue;
            if (Math.Abs(sample.X) > maxAbs || Math.Abs(sample.Y) > maxAbs || Math.Abs(sample.Z) > maxAbs)
                return SensorWindowReasons.ValueOutOfRange;
        }

        if (samples[samples.Count - 1].TOffsetMs > MaxDurationMs)
            return SensorWindowReasons.WindowTooLong;
        return SensorWindowReasons.Ok;
    }

    private static SensorSample[] Copy(IReadOnlyList<SensorSample> samples)
    {
        var copy = new SensorSample[samples.Count];
        for (var i = 0; i < copy.Length; i++)
            copy[i] = samples[i];
        return copy;
    }
}
