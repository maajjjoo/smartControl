using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SmartControl.Core.Recognition;

/// <summary>Clases cerradas que puede producir un modelo. El modelo nunca produce acciones.</summary>
public enum GestureClass
{
    TwistOut,
    TwistIn,
    NoGesture,
}

/// <summary>Procedencia de los datos con los que se obtuvo el modelo.</summary>
public enum ModelDataSource
{
    /// <summary>Modelo escrito a mano o con datos sintéticos, solo para pruebas. No mide nada real.</summary>
    SyntheticFixture,
    /// <summary>Entrenado con grabaciones de un reloj físico.</summary>
    PhysicalWatch,
}

public enum LayerActivation
{
    Relu,
    Tanh,
    Softmax,
}

public sealed record DenseLayer(double[][] Weights, double[] Bias, LayerActivation Activation)
{
    public int Inputs => Weights[0].Length;
    public int Outputs => Weights.Length;
}

/// <summary>Datos de entrenamiento declarados por el modelo (docs/phase2-ai-evaluation.md §7).</summary>
public sealed record ModelTraining(int Participants, int Windows, string? DatasetSha256);

/// <summary>Códigos de rechazo de un modelo. Son constantes propias: nunca contienen datos del archivo.</summary>
public static class ModelReasons
{
    public const string Ok = "OK";
    public const string FileNotFound = "MODEL_FILE_NOT_FOUND";
    public const string FileTooLarge = "MODEL_FILE_TOO_LARGE";
    public const string ChecksumMissing = "MODEL_CHECKSUM_MISSING";
    public const string ChecksumMismatch = "MODEL_CHECKSUM_MISMATCH";
    public const string ReadError = "MODEL_READ_ERROR";
    public const string MalformedJson = "MODEL_MALFORMED_JSON";
    public const string MissingField = "MODEL_MISSING_FIELD";
    public const string UnknownField = "MODEL_UNKNOWN_FIELD";
    public const string InvalidFieldType = "MODEL_INVALID_FIELD_TYPE";
    public const string UnsupportedSchema = "MODEL_UNSUPPORTED_SCHEMA";
    public const string UnsupportedFeatureSet = "MODEL_UNSUPPORTED_FEATURE_SET";
    public const string InvalidClasses = "MODEL_INVALID_CLASSES";
    public const string InvalidDataSource = "MODEL_INVALID_DATA_SOURCE";
    public const string SyntheticNotAllowed = "MODEL_SYNTHETIC_NOT_ALLOWED";
    public const string InvalidTraining = "MODEL_INVALID_TRAINING";
    public const string InvalidThreshold = "MODEL_INVALID_THRESHOLD";
    public const string InvalidNormalization = "MODEL_INVALID_NORMALIZATION";
    public const string InvalidLayers = "MODEL_INVALID_LAYERS";
    public const string DimensionMismatch = "MODEL_DIMENSION_MISMATCH";
    public const string NonFiniteValue = "MODEL_NON_FINITE_VALUE";
    public const string InvalidActivation = "MODEL_INVALID_ACTIVATION";
}

/// <summary>
/// Modelo de reconocimiento ya validado (formato smartcontrol-gesture-model/v1, ADR-06).
/// Solo se obtiene con <see cref="TryParse"/>, así que toda instancia es coherente:
/// clases cerradas, dimensiones encajadas, valores finitos y umbral no inferior al mínimo.
/// </summary>
public sealed class GestureModel
{
    public const string SchemaVersion = "smartcontrol-gesture-model/v1";

    /// <summary>Umbral mínimo del sistema (RF-11). Un modelo solo puede exigir más, nunca menos.</summary>
    public const double MinimumThreshold = 0.80;

    public const int MaxLayers = 4;
    public const int MaxLayerWidth = 256;
    private const int MaxJsonDepth = 8;
    private const int MaxModelVersionLength = 64;

    private static readonly string[] TopFields =
        { "schemaVersion", "modelVersion", "featureSet", "classes", "dataSource", "training", "decisionThreshold", "normalization", "layers" };
    private static readonly string[] TrainingFields = { "participants", "windows", "datasetSha256" };
    private static readonly string[] NormalizationFields = { "mean", "std" };
    private static readonly string[] LayerFields = { "weights", "bias", "activation" };

    private GestureModel(
        string modelVersion, GestureClass[] classes, ModelDataSource dataSource, ModelTraining training,
        double decisionThreshold, double[] mean, double[] std, DenseLayer[] layers)
    {
        ModelVersion = modelVersion;
        Classes = classes;
        DataSource = dataSource;
        Training = training;
        DecisionThreshold = decisionThreshold;
        Mean = mean;
        Std = std;
        Layers = layers;
    }

    public string ModelVersion { get; }
    /// <summary>Orden de las salidas del modelo.</summary>
    public IReadOnlyList<GestureClass> Classes { get; }
    public ModelDataSource DataSource { get; }
    public ModelTraining Training { get; }
    public double DecisionThreshold { get; }
    public IReadOnlyList<double> Mean { get; }
    public IReadOnlyList<double> Std { get; }
    public IReadOnlyList<DenseLayer> Layers { get; }

    public static string ToWireName(GestureClass gesture) => gesture switch
    {
        GestureClass.TwistOut => "TWIST_OUT",
        GestureClass.TwistIn => "TWIST_IN",
        GestureClass.NoGesture => "NO_GESTURE",
        _ => throw new ArgumentOutOfRangeException(nameof(gesture)),
    };

    /// <summary>
    /// Valida el contenido de un modelo. Nunca lanza excepciones por datos inválidos.
    /// Los modelos sintéticos solo se aceptan con <paramref name="allowSyntheticFixture"/> (pruebas).
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> json, bool allowSyntheticFixture, out GestureModel? model, out string reason)
    {
        model = null;
        try
        {
            reason = Parse(json, allowSyntheticFixture, out model);
        }
        catch (Exception)
        {
            // Cualquier fallo inesperado termina en rechazo, nunca en un modelo a medias.
            model = null;
            reason = ModelReasons.MalformedJson;
        }
        return model is not null;
    }

    private static string Parse(ReadOnlyMemory<byte> json, bool allowSyntheticFixture, out GestureModel? model)
    {
        model = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        }
        catch (JsonException)
        {
            return ModelReasons.MalformedJson;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ModelReasons.MalformedJson;
            var problem = CheckFields(root, TopFields);
            if (problem is not null)
                return problem;

            if (!TryString(root, "schemaVersion", out var schema) || schema != SchemaVersion)
                return ModelReasons.UnsupportedSchema;
            if (!TryString(root, "modelVersion", out var modelVersion) || modelVersion.Length == 0 || modelVersion.Length > MaxModelVersionLength)
                return ModelReasons.InvalidFieldType;
            if (!TryString(root, "featureSet", out var featureSet) || featureSet != MovementFeatures.FeatureSetId)
                return ModelReasons.UnsupportedFeatureSet;

            var classes = ParseClasses(root.GetProperty("classes"));
            if (classes is null)
                return ModelReasons.InvalidClasses;

            if (!TryString(root, "dataSource", out var sourceText))
                return ModelReasons.InvalidDataSource;
            ModelDataSource dataSource;
            switch (sourceText)
            {
                case "synthetic-fixture": dataSource = ModelDataSource.SyntheticFixture; break;
                case "physical-watch": dataSource = ModelDataSource.PhysicalWatch; break;
                default: return ModelReasons.InvalidDataSource;
            }
            if (dataSource == ModelDataSource.SyntheticFixture && !allowSyntheticFixture)
                return ModelReasons.SyntheticNotAllowed;

            var trainingProblem = ParseTraining(root.GetProperty("training"), dataSource, out var training);
            if (trainingProblem is not null)
                return trainingProblem;

            var thresholdElement = root.GetProperty("decisionThreshold");
            if (thresholdElement.ValueKind != JsonValueKind.Number || !thresholdElement.TryGetDouble(out var threshold))
                return ModelReasons.InvalidThreshold;
            if (!double.IsFinite(threshold) || threshold < MinimumThreshold || threshold > 1.0)
                return ModelReasons.InvalidThreshold;

            var normalization = root.GetProperty("normalization");
            if (normalization.ValueKind != JsonValueKind.Object)
                return ModelReasons.InvalidNormalization;
            problem = CheckFields(normalization, NormalizationFields);
            if (problem is not null)
                return problem;
            var meanProblem = TryVector(normalization.GetProperty("mean"), out var mean);
            if (meanProblem is not null)
                return meanProblem == ModelReasons.InvalidFieldType ? ModelReasons.InvalidNormalization : meanProblem;
            var stdProblem = TryVector(normalization.GetProperty("std"), out var std);
            if (stdProblem is not null)
                return stdProblem == ModelReasons.InvalidFieldType ? ModelReasons.InvalidNormalization : stdProblem;
            if (mean!.Length != MovementFeatures.Count || std!.Length != MovementFeatures.Count)
                return ModelReasons.DimensionMismatch;
            foreach (var value in std)
                if (value <= 0)
                    return ModelReasons.InvalidNormalization;

            var layersProblem = ParseLayers(root.GetProperty("layers"), classes.Length, out var layers);
            if (layersProblem is not null)
                return layersProblem;

            model = new GestureModel(modelVersion, classes, dataSource, training!, threshold, mean, std, layers!);
            return ModelReasons.Ok;
        }
    }

    private static GestureClass[]? ParseClasses(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
            return null;
        var classes = new GestureClass[3];
        var seen = new HashSet<GestureClass>();
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;
            GestureClass gesture;
            switch (item.GetString())
            {
                case "TWIST_OUT": gesture = GestureClass.TwistOut; break;
                case "TWIST_IN": gesture = GestureClass.TwistIn; break;
                case "NO_GESTURE": gesture = GestureClass.NoGesture; break;
                default: return null;
            }
            if (!seen.Add(gesture))
                return null;
            classes[i++] = gesture;
        }
        return classes;
    }

    private static string? ParseTraining(JsonElement element, ModelDataSource source, out ModelTraining? training)
    {
        training = null;
        if (element.ValueKind != JsonValueKind.Object || CheckFields(element, TrainingFields) is not null)
            return ModelReasons.InvalidTraining;
        var participantsElement = element.GetProperty("participants");
        var windowsElement = element.GetProperty("windows");
        if (participantsElement.ValueKind != JsonValueKind.Number || !participantsElement.TryGetInt32(out var participants) || participants < 0
            || windowsElement.ValueKind != JsonValueKind.Number || !windowsElement.TryGetInt32(out var windows) || windows < 0)
            return ModelReasons.InvalidTraining;

        var hashElement = element.GetProperty("datasetSha256");
        string? hash = null;
        if (hashElement.ValueKind == JsonValueKind.String)
        {
            hash = hashElement.GetString()!;
            if (!IsLowerHexSha256(hash))
                return ModelReasons.InvalidTraining;
        }
        else if (hashElement.ValueKind != JsonValueKind.Null)
        {
            return ModelReasons.InvalidTraining;
        }

        // Un modelo que dice venir de un reloj real debe declarar con qué datos se entrenó.
        if (source == ModelDataSource.PhysicalWatch && (participants == 0 || windows == 0 || hash is null))
            return ModelReasons.InvalidTraining;

        training = new ModelTraining(participants, windows, hash);
        return null;
    }

    private static string? ParseLayers(JsonElement element, int classCount, out DenseLayer[]? layers)
    {
        layers = null;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0 || element.GetArrayLength() > MaxLayers)
            return ModelReasons.InvalidLayers;

        var result = new List<DenseLayer>();
        var inputs = MovementFeatures.Count;
        foreach (var layerElement in element.EnumerateArray())
        {
            if (layerElement.ValueKind != JsonValueKind.Object)
                return ModelReasons.InvalidLayers;
            var problem = CheckFields(layerElement, LayerFields);
            if (problem is not null)
                return problem;

            if (!TryString(layerElement, "activation", out var activationText))
                return ModelReasons.InvalidActivation;
            LayerActivation activation;
            switch (activationText)
            {
                case "relu": activation = LayerActivation.Relu; break;
                case "tanh": activation = LayerActivation.Tanh; break;
                case "softmax": activation = LayerActivation.Softmax; break;
                default: return ModelReasons.InvalidActivation;
            }

            var weightsElement = layerElement.GetProperty("weights");
            if (weightsElement.ValueKind != JsonValueKind.Array || weightsElement.GetArrayLength() == 0
                || weightsElement.GetArrayLength() > MaxLayerWidth)
                return ModelReasons.InvalidLayers;
            var weights = new double[weightsElement.GetArrayLength()][];
            var row = 0;
            foreach (var rowElement in weightsElement.EnumerateArray())
            {
                var rowProblem = TryVector(rowElement, out var values);
                if (rowProblem is not null)
                    return rowProblem == ModelReasons.InvalidFieldType ? ModelReasons.InvalidLayers : rowProblem;
                if (values!.Length != inputs)
                    return ModelReasons.DimensionMismatch;
                weights[row++] = values;
            }

            var biasProblem = TryVector(layerElement.GetProperty("bias"), out var bias);
            if (biasProblem is not null)
                return biasProblem == ModelReasons.InvalidFieldType ? ModelReasons.InvalidLayers : biasProblem;
            if (bias!.Length != weights.Length)
                return ModelReasons.DimensionMismatch;

            result.Add(new DenseLayer(weights, bias, activation));
            inputs = weights.Length;
        }

        // Solo la última capa usa softmax, y produce una probabilidad por clase.
        for (var i = 0; i < result.Count; i++)
        {
            var isLast = i == result.Count - 1;
            if ((result[i].Activation == LayerActivation.Softmax) != isLast)
                return ModelReasons.InvalidActivation;
        }
        if (result[^1].Outputs != classCount)
            return ModelReasons.DimensionMismatch;

        layers = result.ToArray();
        return null;
    }

    /// <summary>Vector de números finitos. Devuelve null si es correcto.</summary>
    private static string? TryVector(JsonElement element, out double[]? values)
    {
        values = null;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0 || element.GetArrayLength() > MaxLayerWidth)
            return ModelReasons.InvalidFieldType;
        var result = new double[element.GetArrayLength()];
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var value))
                return ModelReasons.InvalidFieldType;
            if (!double.IsFinite(value))
                return ModelReasons.NonFiniteValue;
            result[i++] = value;
        }
        values = result;
        return null;
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        var element = obj.GetProperty(name);
        value = element.ValueKind == JsonValueKind.String ? element.GetString()! : "";
        return element.ValueKind == JsonValueKind.String;
    }

    private static bool IsLowerHexSha256(string text)
    {
        if (text.Length != 64)
            return false;
        foreach (var c in text)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        return true;
    }

    /// <summary>Exige exactamente los campos esperados, sin repetidos ni desconocidos.</summary>
    private static string? CheckFields(JsonElement obj, string[] expected)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
            if (!present.Add(property.Name))
                return ModelReasons.MalformedJson;
        foreach (var field in expected)
            if (!present.Contains(field))
                return ModelReasons.MissingField;
        return present.Count != expected.Length ? ModelReasons.UnknownField : null;
    }
}
