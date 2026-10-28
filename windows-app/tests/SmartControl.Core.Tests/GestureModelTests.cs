using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>Validación del formato smartcontrol-gesture-model/v1 con modelos sintéticos (ADR-06).</summary>
internal static class GestureModelTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("modelo: acepta los fixtures sintéticos solo si se permiten", Sync(AcceptsSyntheticOnlyWhenAllowed));
        yield return ("modelo: un modelo de reloj real debe declarar sus datos", Sync(PhysicalWatchRequiresTrainingMetadata));
        yield return ("modelo: cabecera, campos y JSON estrictos", Sync(RejectsBadStructure));
        yield return ("modelo: clases cerradas y sin repetir", Sync(RejectsInvalidClasses));
        yield return ("modelo: el umbral nunca baja de 0.80", Sync(RejectsWeakerThreshold));
        yield return ("modelo: normalización con dimensiones correctas", Sync(RejectsBadNormalization));
        yield return ("modelo: capas, dimensiones y activaciones", Sync(RejectsBadLayers));
        yield return ("modelo: valores no numéricos o no finitos", Sync(RejectsNonFiniteValues));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static string Reason(JsonObject json, bool allowSynthetic = true)
    {
        var ok = GestureModel.TryParse(ModelFixtures.Bytes(json), allowSynthetic, out var model, out var reason);
        Check.Equal(ok, model is not null, "el resultado y el modelo deben coincidir");
        return reason;
    }

    private static string Reason(string raw)
    {
        GestureModel.TryParse(Encoding.UTF8.GetBytes(raw), true, out var model, out var reason);
        Check.True(model is null, "no debe aceptar el modelo");
        return reason;
    }

    private static JsonObject With(Action<JsonObject> change)
    {
        var json = ModelFixtures.LogisticJson();
        change(json);
        return json;
    }

    private static JsonObject FirstLayer(JsonObject json) => (JsonObject)json["layers"]![0]!;

    private static void AcceptsSyntheticOnlyWhenAllowed()
    {
        var model = ModelFixtures.Parse(ModelFixtures.LogisticJson());
        Check.Equal(ModelDataSource.SyntheticFixture, model.DataSource);
        Check.Equal(ModelFixtures.SyntheticVersion, model.ModelVersion);
        Check.Equal(GestureClass.TwistOut, model.Classes[0]);
        Check.Equal(1, model.Layers.Count);
        Check.Equal(2, ModelFixtures.Parse(ModelFixtures.MlpJson()).Layers.Count);

        Check.Equal(ModelReasons.SyntheticNotAllowed, Reason(ModelFixtures.LogisticJson(), allowSynthetic: false));
    }

    private static void PhysicalWatchRequiresTrainingMetadata()
    {
        // Metadatos ficticios: solo comprueban el analizador; ningún modelo real se entrenó así.
        var claimsRealWithoutData = With(j => j["dataSource"] = "physical-watch");
        Check.Equal(ModelReasons.InvalidTraining, Reason(claimsRealWithoutData, allowSynthetic: false));

        var declared = With(j =>
        {
            j["dataSource"] = "physical-watch";
            j["training"] = new JsonObject { ["participants"] = 3, ["windows"] = 300, ["datasetSha256"] = new string('a', 64) };
        });
        Check.Equal(ModelReasons.Ok, Reason(declared, allowSynthetic: false));

        Check.Equal(ModelReasons.InvalidTraining, Reason(With(j => j["training"]!["datasetSha256"] = "ABC")));
        Check.Equal(ModelReasons.InvalidTraining, Reason(With(j => j["training"]!["participants"] = -1)));
    }

    private static void RejectsBadStructure()
    {
        Check.Equal(ModelReasons.MalformedJson, Reason("{"));
        Check.Equal(ModelReasons.MalformedJson, Reason("[]"));
        Check.Equal(ModelReasons.UnsupportedSchema, Reason(With(j => j["schemaVersion"] = "smartcontrol-gesture-model/v2")));
        Check.Equal(ModelReasons.UnsupportedFeatureSet, Reason(With(j => j["featureSet"] = "gyro-accel-v1")));
        Check.Equal(ModelReasons.MissingField, Reason(With(j => j.Remove("training"))));
        Check.Equal(ModelReasons.UnknownField, Reason(With(j => j["executeCommand"] = "calc.exe")));
        Check.Equal(ModelReasons.InvalidFieldType, Reason(With(j => j["modelVersion"] = "")));
        Check.Equal(ModelReasons.InvalidDataSource, Reason(With(j => j["dataSource"] = "emulator")));

        var raw = ModelFixtures.LogisticJson().ToJsonString();
        var duplicated = raw.Replace("\"modelVersion\":", "\"modelVersion\":\"x\",\"modelVersion\":");
        Check.Equal(ModelReasons.MalformedJson, Reason(duplicated));
    }

    private static void RejectsInvalidClasses()
    {
        Check.Equal(ModelReasons.InvalidClasses, Reason(With(j => j["classes"] = new JsonArray("TWIST_OUT", "TWIST_IN"))));
        Check.Equal(ModelReasons.InvalidClasses, Reason(With(j => j["classes"] = new JsonArray("TWIST_OUT", "TWIST_OUT", "NO_GESTURE"))));
        Check.Equal(ModelReasons.InvalidClasses, Reason(With(j => j["classes"] = new JsonArray("SLIDE_NEXT", "TWIST_IN", "NO_GESTURE"))));
        Check.Equal(ModelReasons.InvalidClasses, Reason(With(j => j["classes"] = new JsonArray("twist_out", "TWIST_IN", "NO_GESTURE"))));

        var reordered = ModelFixtures.Parse(With(j => j["classes"] = new JsonArray("NO_GESTURE", "TWIST_IN", "TWIST_OUT")));
        Check.Equal(GestureClass.NoGesture, reordered.Classes[0]);
    }

    private static void RejectsWeakerThreshold()
    {
        Check.Equal(ModelReasons.InvalidThreshold, Reason(With(j => j["decisionThreshold"] = 0.79)));
        Check.Equal(ModelReasons.InvalidThreshold, Reason(With(j => j["decisionThreshold"] = 1.01)));
        Check.Equal(ModelReasons.InvalidThreshold, Reason(With(j => j["decisionThreshold"] = "0.9")));
        Check.Equal(0.95, ModelFixtures.Parse(With(j => j["decisionThreshold"] = 0.95)).DecisionThreshold, "un umbral más exigente se admite");
    }

    private static void RejectsBadNormalization()
    {
        Check.Equal(ModelReasons.DimensionMismatch,
            Reason(With(j => j["normalization"]!["mean"] = ModelFixtures.Array(new double[30]))));
        var zeroStd = new double[MovementFeatures.Count];
        Check.Equal(ModelReasons.InvalidNormalization, Reason(With(j => j["normalization"]!["std"] = ModelFixtures.Array(zeroStd))));
        Check.Equal(ModelReasons.UnknownField, Reason(With(j => j["normalization"]!["scale"] = 1)));
    }

    private static void RejectsBadLayers()
    {
        Check.Equal(ModelReasons.InvalidLayers, Reason(With(j => j["layers"] = new JsonArray())));
        Check.Equal(ModelReasons.DimensionMismatch,
            Reason(With(j => ((JsonArray)FirstLayer(j)["weights"]!)[0] = ModelFixtures.Array(new double[30]))));
        Check.Equal(ModelReasons.DimensionMismatch, Reason(With(j => FirstLayer(j)["bias"] = ModelFixtures.Array(new double[2]))));
        Check.Equal(ModelReasons.InvalidActivation, Reason(With(j => FirstLayer(j)["activation"] = "relu")), "la última capa debe ser softmax");
        Check.Equal(ModelReasons.InvalidActivation, Reason(With(j => FirstLayer(j)["activation"] = "sigmoid")));

        // Una capa oculta con softmax no se admite.
        var hiddenSoftmax = ModelFixtures.MlpJson();
        FirstLayer(hiddenSoftmax)["activation"] = "softmax";
        Check.Equal(ModelReasons.InvalidActivation, Reason(hiddenSoftmax));

        // La última capa debe producir una salida por clase.
        var twoOutputs = ModelFixtures.MlpJson();
        var last = (JsonObject)twoOutputs["layers"]![1]!;
        last["weights"] = new JsonArray(ModelFixtures.Array(new[] { 1.0, 0.0 }), ModelFixtures.Array(new[] { 0.0, 1.0 }));
        last["bias"] = ModelFixtures.Array(new[] { 0.0, 0.0 });
        Check.Equal(ModelReasons.DimensionMismatch, Reason(twoOutputs));

        var tooMany = ModelFixtures.MlpJson();
        var layers = (JsonArray)tooMany["layers"]!;
        for (var i = 0; i < GestureModel.MaxLayers; i++)
            layers.Insert(0, layers[0]!.DeepClone());
        Check.Equal(ModelReasons.InvalidLayers, Reason(tooMany));
    }

    private static void RejectsNonFiniteValues()
    {
        var raw = ModelFixtures.LogisticJson().ToJsonString();
        // JSON no admite NaN ni Infinity literales.
        Check.Equal(ModelReasons.MalformedJson, Reason(raw.Replace("\"bias\":[0,0,2]", "\"bias\":[NaN,0,2]")));
        // Un número que desborda un double nunca puede dar un modelo.
        var overflow = Reason(raw.Replace("\"bias\":[0,0,2]", "\"bias\":[1e400,0,2]"));
        Check.True(overflow is ModelReasons.NonFiniteValue or ModelReasons.InvalidLayers, $"desbordamiento: {overflow}");
        Check.Equal(ModelReasons.InvalidLayers, Reason(raw.Replace("\"bias\":[0,0,2]", "\"bias\":[\"0\",0,2]")));
        Check.True(raw.Contains("\"bias\":[0,0,2]"), "el reemplazo de las pruebas debe encontrar el sesgo");
    }
}
