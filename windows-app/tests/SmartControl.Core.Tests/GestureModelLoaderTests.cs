using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>Carga de modelos desde disco con un modelo SINTÉTICO escrito en una carpeta temporal.</summary>
internal static class GestureModelLoaderTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("carga: modelo con resumen correcto", Sync(() => InTempDir(LoadsWithValidChecksum)));
        yield return ("carga: falta el archivo o su resumen", Sync(() => InTempDir(RejectsMissingFiles)));
        yield return ("carga: resumen incorrecto o modelo alterado", Sync(() => InTempDir(RejectsTamperedModel)));
        yield return ("carga: archivo demasiado grande", Sync(() => InTempDir(RejectsOversizedFile)));
        yield return ("carga: modelo sintético fuera de las pruebas", Sync(() => InTempDir(RejectsSyntheticWhenNotAllowed)));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static void InTempDir(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-model-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WriteFixture(string directory)
    {
        var path = Path.Combine(directory, "fixture-model.json");
        File.WriteAllBytes(path, ModelFixtures.Bytes(ModelFixtures.LogisticJson()));
        GestureModelLoader.WriteChecksum(path);
        return path;
    }

    private static string Reason(string path, bool allowSynthetic = true)
    {
        var ok = GestureModelLoader.TryLoad(path, allowSynthetic, out var model, out var reason);
        Check.Equal(ok, model is not null, "el resultado y el modelo deben coincidir");
        return reason;
    }

    private static void LoadsWithValidChecksum(string directory)
    {
        var path = WriteFixture(directory);
        Check.True(GestureModelLoader.TryLoad(path, allowSyntheticFixture: true, out var model, out var reason), $"debe cargar: {reason}");
        Check.Equal(ModelDataSource.SyntheticFixture, model!.DataSource);

        // También se admite el resumen solo, sin nombre de archivo y en mayúsculas.
        var hash = File.ReadAllText(path + GestureModelLoader.ChecksumExtension).Split(' ')[0];
        File.WriteAllText(path + GestureModelLoader.ChecksumExtension, hash.ToUpperInvariant());
        Check.Equal(ModelReasons.Ok, Reason(path));
    }

    private static void RejectsMissingFiles(string directory)
    {
        Check.Equal(ModelReasons.FileNotFound, Reason(Path.Combine(directory, "no-existe.json")));

        var path = WriteFixture(directory);
        File.Delete(path + GestureModelLoader.ChecksumExtension);
        Check.Equal(ModelReasons.ChecksumMissing, Reason(path));

        File.WriteAllText(path + GestureModelLoader.ChecksumExtension, "no es un resumen");
        Check.Equal(ModelReasons.ChecksumMissing, Reason(path));
    }

    private static void RejectsTamperedModel(string directory)
    {
        var path = WriteFixture(directory);
        File.WriteAllText(path + GestureModelLoader.ChecksumExtension, new string('0', 64));
        Check.Equal(ModelReasons.ChecksumMismatch, Reason(path));

        // Se cambia el modelo después de calcular su resumen.
        GestureModelLoader.WriteChecksum(path);
        var text = File.ReadAllText(path).Replace("\"decisionThreshold\":0.8", "\"decisionThreshold\":0.9");
        File.WriteAllText(path, text);
        Check.Equal(ModelReasons.ChecksumMismatch, Reason(path));
    }

    private static void RejectsOversizedFile(string directory)
    {
        var path = Path.Combine(directory, "big.json");
        File.WriteAllBytes(path, new byte[GestureModelLoader.MaxModelBytes + 1]);
        GestureModelLoader.WriteChecksum(path);
        Check.Equal(ModelReasons.FileTooLarge, Reason(path));
    }

    private static void RejectsSyntheticWhenNotAllowed(string directory)
    {
        var path = WriteFixture(directory);
        Check.Equal(ModelReasons.SyntheticNotAllowed, Reason(path, allowSynthetic: false));
    }
}
