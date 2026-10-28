using System;

namespace SmartControl.Core.Recognition;

/// <summary>Probabilidad de cada clase, en orden fijo e independiente del orden del modelo.</summary>
public readonly record struct GesturePrediction(double TwistOut, double TwistIn, double NoGesture)
{
    public double Probability(GestureClass gesture) => gesture switch
    {
        GestureClass.TwistOut => TwistOut,
        GestureClass.TwistIn => TwistIn,
        GestureClass.NoGesture => NoGesture,
        _ => throw new ArgumentOutOfRangeException(nameof(gesture)),
    };
}

/// <summary>
/// Inferencia local de un <see cref="GestureModel"/>: normaliza las características, aplica las
/// capas densas y termina en softmax. C# puro, sin dependencias (ADR-05). Sin estado mutable,
/// así que se puede usar desde varios hilos.
/// </summary>
public sealed class DenseClassifier
{
    private readonly GestureModel _model;

    public DenseClassifier(GestureModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public GestureModel Model => _model;

    /// <summary>
    /// Calcula las probabilidades. Devuelve false (sin predicción) si el número de
    /// características no encaja o si aparece algún valor no finito.
    /// </summary>
    public bool TryPredict(ReadOnlySpan<double> features, out GesturePrediction prediction)
    {
        prediction = default;
        if (features.Length != _model.Mean.Count)
            return false;

        var values = new double[features.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(features[i]))
                return false;
            values[i] = (features[i] - _model.Mean[i]) / _model.Std[i];
        }

        foreach (var layer in _model.Layers)
            values = Apply(layer, values);

        double twistOut = 0, twistIn = 0, noGesture = 0, total = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i]))
                return false;
            total += values[i];
            switch (_model.Classes[i])
            {
                case GestureClass.TwistOut: twistOut = values[i]; break;
                case GestureClass.TwistIn: twistIn = values[i]; break;
                case GestureClass.NoGesture: noGesture = values[i]; break;
            }
        }
        if (Math.Abs(total - 1.0) > 1e-9)
            return false;

        prediction = new GesturePrediction(twistOut, twistIn, noGesture);
        return true;
    }

    private static double[] Apply(DenseLayer layer, double[] input)
    {
        var output = new double[layer.Outputs];
        for (var o = 0; o < output.Length; o++)
        {
            var row = layer.Weights[o];
            var sum = layer.Bias[o];
            for (var i = 0; i < input.Length; i++)
                sum += row[i] * input[i];
            output[o] = sum;
        }

        switch (layer.Activation)
        {
            case LayerActivation.Relu:
                for (var o = 0; o < output.Length; o++)
                    output[o] = Math.Max(0.0, output[o]);
                break;
            case LayerActivation.Tanh:
                for (var o = 0; o < output.Length; o++)
                    output[o] = Math.Tanh(output[o]);
                break;
            case LayerActivation.Softmax:
                Softmax(output);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
        return output;
    }

    /// <summary>Softmax estable: se resta el máximo para que exp no desborde.</summary>
    private static void Softmax(double[] values)
    {
        var max = double.NegativeInfinity;
        foreach (var value in values)
            max = Math.Max(max, value);
        double sum = 0;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Math.Exp(values[i] - max);
            sum += values[i];
        }
        for (var i = 0; i < values.Length; i++)
            values[i] /= sum;
    }
}
