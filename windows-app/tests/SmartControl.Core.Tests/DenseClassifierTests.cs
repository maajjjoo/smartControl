using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Inferencia con modelos SINTÉTICOS escritos a mano. Los valores esperados se calculan aquí con
/// la fórmula del softmax sobre los logits conocidos; comprueban el cálculo, no la precisión real.
/// </summary>
internal static class DenseClassifierTests
{
    private const double Tolerance = 1e-12;

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("inferencia: regresión logística con logits conocidos", Sync(MatchesLogisticReference));
        yield return ("inferencia: aplica la normalización del modelo", Sync(AppliesNormalization));
        yield return ("inferencia: MLP con capa oculta relu", Sync(MatchesMlpReference));
        yield return ("inferencia: capa oculta tanh", Sync(MatchesTanhReference));
        yield return ("inferencia: salida en orden fijo aunque el modelo cambie el orden", Sync(MapsClassesByName));
        yield return ("inferencia: estable con logits grandes", Sync(StaysStableWithLargeLogits));
        yield return ("inferencia: rechaza entradas inválidas", Sync(RejectsInvalidInput));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > Tolerance)
            throw new TestFailure($"{name}: se esperaba {expected:R} y se obtuvo {actual:R}");
    }

    private static double[] Softmax(params double[] logits)
    {
        double sum = 0;
        foreach (var logit in logits)
            sum += Math.Exp(logit);
        var result = new double[logits.Length];
        for (var i = 0; i < logits.Length; i++)
            result[i] = Math.Exp(logits[i]) / sum;
        return result;
    }

    /// <summary>Vector de características con todo a 0 salvo la integral de X.</summary>
    private static double[] WithIntegralX(double value)
    {
        var features = new double[MovementFeatures.Count];
        features[ModelFixtures.IndexOf("gyro_x_integral")] = value;
        return features;
    }

    private static GesturePrediction Predict(JsonObject json, double[] features)
    {
        var classifier = new DenseClassifier(ModelFixtures.Parse(json));
        Check.True(classifier.TryPredict(features, out var prediction), "debe predecir");
        Near(1.0, prediction.TwistOut + prediction.TwistIn + prediction.NoGesture, "suma de probabilidades");
        return prediction;
    }

    private static void ExpectSoftmax(GesturePrediction prediction, double[] logits)
    {
        var expected = Softmax(logits);
        Near(expected[0], prediction.TwistOut, "TWIST_OUT");
        Near(expected[1], prediction.TwistIn, "TWIST_IN");
        Near(expected[2], prediction.NoGesture, "NO_GESTURE");
    }

    private static void MatchesLogisticReference()
    {
        // Logits = (20·i, −20·i, 2) con i = integral de X.
        ExpectSoftmax(Predict(ModelFixtures.LogisticJson(), WithIntegralX(0.1)), new[] { 2.0, -2.0, 2.0 });
        ExpectSoftmax(Predict(ModelFixtures.LogisticJson(), WithIntegralX(0.3)), new[] { 6.0, -6.0, 2.0 });
        ExpectSoftmax(Predict(ModelFixtures.LogisticJson(), WithIntegralX(-0.3)), new[] { -6.0, 6.0, 2.0 });
        ExpectSoftmax(Predict(ModelFixtures.LogisticJson(), WithIntegralX(0.0)), new[] { 0.0, 0.0, 2.0 });
    }

    private static void AppliesNormalization()
    {
        var json = ModelFixtures.LogisticJson();
        var index = ModelFixtures.IndexOf("gyro_x_integral");
        ((JsonArray)json["normalization"]!["mean"]!)[index] = 0.1;
        ((JsonArray)json["normalization"]!["std"]!)[index] = 0.5;
        // z = (0.3 − 0.1) / 0.5 = 0.4 → logits (8, −8, 2).
        ExpectSoftmax(Predict(json, WithIntegralX(0.3)), new[] { 8.0, -8.0, 2.0 });
    }

    private static void MatchesMlpReference()
    {
        // Oculta: h1 = relu(10·i), h2 = relu(−10·i). Salida: (2·h1, 2·h2, 1).
        ExpectSoftmax(Predict(ModelFixtures.MlpJson(), WithIntegralX(0.2)), new[] { 4.0, 0.0, 1.0 });
        ExpectSoftmax(Predict(ModelFixtures.MlpJson(), WithIntegralX(-0.2)), new[] { 0.0, 4.0, 1.0 });
    }

    private static void MatchesTanhReference()
    {
        var json = ModelFixtures.MlpJson();
        ((JsonObject)json["layers"]![0]!)["activation"] = "tanh";
        var h = Math.Tanh(10 * 0.05);
        ExpectSoftmax(Predict(json, WithIntegralX(0.05)), new[] { 2 * h, 2 * Math.Tanh(-10 * 0.05), 1.0 });
    }

    private static void MapsClassesByName()
    {
        // Mismo modelo con clases y filas de salida en otro orden: el resultado no debe cambiar.
        var json = ModelFixtures.LogisticJson();
        json["classes"] = new JsonArray("NO_GESTURE", "TWIST_OUT", "TWIST_IN");
        var layer = (JsonObject)json["layers"]![0]!;
        var weights = (JsonArray)layer["weights"]!;
        var reordered = new JsonArray(weights[2]!.DeepClone(), weights[0]!.DeepClone(), weights[1]!.DeepClone());
        layer["weights"] = reordered;
        layer["bias"] = ModelFixtures.Array(new[] { 2.0, 0.0, 0.0 });
        ExpectSoftmax(Predict(json, WithIntegralX(0.3)), new[] { 6.0, -6.0, 2.0 });
    }

    private static void StaysStableWithLargeLogits()
    {
        // Logits (1000, −1000, 2): sin restar el máximo, exp desbordaría.
        var prediction = Predict(ModelFixtures.LogisticJson(), WithIntegralX(50.0));
        Near(1.0, prediction.TwistOut, "TWIST_OUT");
        Check.True(double.IsFinite(prediction.TwistIn) && double.IsFinite(prediction.NoGesture), "todo debe ser finito");
    }

    private static void RejectsInvalidInput()
    {
        var classifier = new DenseClassifier(ModelFixtures.Parse(ModelFixtures.LogisticJson()));
        Check.True(!classifier.TryPredict(new double[MovementFeatures.Count - 1], out _), "longitud incorrecta");
        Check.True(!classifier.TryPredict(new double[MovementFeatures.Count + 1], out _), "longitud incorrecta");

        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var features = new double[MovementFeatures.Count];
            features[3] = bad;
            Check.True(!classifier.TryPredict(features, out _), $"valor {bad}");
        }
    }
}
