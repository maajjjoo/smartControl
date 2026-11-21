using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SmartControl.Core.Tests;

internal sealed class TestFailure : Exception
{
    public TestFailure(string message) : base(message) { }
}

internal static class Check
{
    public static void Equal<T>(T expected, T actual, string? context = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure($"se esperaba <{expected}> y se obtuvo <{actual}>{Suffix(context)}");
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new TestFailure(message);
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        throw new TestFailure($"se esperaba la excepción {typeof(TException).Name}");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        throw new TestFailure($"se esperaba la excepción {typeof(TException).Name}");
    }

    private static string Suffix(string? context) => context is null ? "" : $" ({context})";
}

/// <summary>Ejecutor mínimo de pruebas, sin dependencias, para compilar también sin el SDK.</summary>
internal static class TestRunner
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<Task> Body)>();
        tests.AddRange(ValidatorTests.All());
        tests.AddRange(ReceiverTests.All());
        tests.AddRange(RealExecutionTests.All());
        tests.AddRange(PairingTests.All());
        tests.AddRange(EventLogTests.All());
        tests.AddRange(SensorWindowTests.All());
        tests.AddRange(MovementFeaturesTests.All());
        tests.AddRange(FeatureParityTests.All());
        tests.AddRange(GestureModelTests.All());
        tests.AddRange(GestureModelLoaderTests.All());
        tests.AddRange(DenseClassifierTests.All());
        tests.AddRange(ModelParityTests.All());
        tests.AddRange(GestureDecisionTests.All());
        tests.AddRange(ContextualActionSelectorTests.All());
        tests.AddRange(RecognitionPipelineTests.All());
        tests.AddRange(RecognitionLatencyTests.All());

        var failed = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var (name, body) in tests)
        {
            try
            {
                await body();
                Console.WriteLine($"  ok    {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FALLA {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed} de {tests.Count} pruebas aprobadas en {stopwatch.Elapsed.TotalSeconds:F1} s.");
        return failed == 0 ? 0 : 1;
    }
}
