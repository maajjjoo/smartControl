using System.Text;
using System.Text.Json.Nodes;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Modelos SINTÉTICOS escritos a mano para las pruebas (dataSource = "synthetic-fixture").
/// No están entrenados con ningún dato y no dicen nada sobre la precisión real.
/// No se guardan como archivos del repositorio: se generan aquí, separados de cualquier
/// conjunto de datos real.
/// </summary>
internal static class ModelFixtures
{
    public const string SyntheticVersion = "fixture-handwritten-1";

    /// <summary>
    /// Regresión logística a mano: sin normalización efectiva (media 0, desviación 1).
    /// La salida TWIST_OUT crece con la integral de X, TWIST_IN con su opuesto, y
    /// NO_GESTURE tiene sesgo constante. Solo sirve para comprobar el código.
    /// </summary>
    public static JsonObject LogisticJson()
    {
        var integralX = IndexOf("gyro_x_integral");
        return Model(new JsonArray
        {
            Layer(new[] { OneHot(integralX, 20.0), OneHot(integralX, -20.0), Zeros() }, new[] { 0.0, 0.0, 2.0 }, "softmax"),
        });
    }

    /// <summary>MLP a mano con una capa oculta relu de 2 unidades.</summary>
    public static JsonObject MlpJson()
    {
        var integralX = IndexOf("gyro_x_integral");
        return Model(new JsonArray
        {
            Layer(new[] { OneHot(integralX, 10.0), OneHot(integralX, -10.0) }, new[] { 0.0, 0.0 }, "relu"),
            LayerRaw(new[] { new[] { 2.0, 0.0 }, new[] { 0.0, 2.0 }, new[] { 0.0, 0.0 } }, new[] { 0.0, 0.0, 1.0 }, "softmax"),
        });
    }

    public static byte[] Bytes(JsonObject json) => Encoding.UTF8.GetBytes(json.ToJsonString());

    public static GestureModel Parse(JsonObject json)
    {
        Check.True(GestureModel.TryParse(Bytes(json), allowSyntheticFixture: true, out var model, out var reason), $"fixture inválido: {reason}");
        return model!;
    }

    public static int IndexOf(string feature)
    {
        for (var i = 0; i < MovementFeatures.Count; i++)
            if (MovementFeatures.Names[i] == feature)
                return i;
        throw new TestFailure($"no existe la característica {feature}");
    }

    private static JsonObject Model(JsonArray layers) => new()
    {
        ["schemaVersion"] = GestureModel.SchemaVersion,
        ["modelVersion"] = SyntheticVersion,
        ["featureSet"] = MovementFeatures.FeatureSetId,
        ["classes"] = new JsonArray("TWIST_OUT", "TWIST_IN", "NO_GESTURE"),
        ["dataSource"] = "synthetic-fixture",
        ["training"] = new JsonObject { ["participants"] = 0, ["windows"] = 0, ["datasetSha256"] = null },
        ["decisionThreshold"] = 0.80,
        ["normalization"] = new JsonObject { ["mean"] = Vector(0.0), ["std"] = Vector(1.0) },
        ["layers"] = layers,
    };

    private static JsonObject Layer(double[][] weights, double[] bias, string activation) => LayerRaw(weights, bias, activation);

    private static JsonObject LayerRaw(double[][] weights, double[] bias, string activation)
    {
        var rows = new JsonArray();
        foreach (var row in weights)
            rows.Add(Array(row));
        return new JsonObject { ["weights"] = rows, ["bias"] = Array(bias), ["activation"] = activation };
    }

    private static double[] OneHot(int index, double value)
    {
        var row = new double[MovementFeatures.Count];
        row[index] = value;
        return row;
    }

    private static double[] Zeros() => new double[MovementFeatures.Count];

    private static JsonArray Vector(double value)
    {
        var values = new double[MovementFeatures.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = value;
        return Array(values);
    }

    public static JsonArray Array(double[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
