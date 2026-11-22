using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SmartControl.Desktop.Tests;

internal sealed class TestFailure : Exception
{
    public TestFailure(string message) : base(message) { }
}

internal static class Check
{
    public static void Equal<T>(T expected, T actual, string? context = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure($"se esperaba <{expected}> y se obtuvo <{actual}>{(context is null ? "" : $" ({context})")}");
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new TestFailure(message);
    }
}

/// <summary>
/// Ejecutor mínimo de pruebas de la ventana. Corre en un hilo STA, como la aplicación,
/// porque los controles de Windows Forms lo necesitan. Ninguna prueba muestra ventanas.
/// </summary>
internal static class TestRunner
{
    [STAThread]
    private static int Main()
    {
        var tests = new List<(string Name, Action Body)>();
        tests.AddRange(ActivityAccessibilityTests.All());
        tests.AddRange(AnnouncementTests.All());
        tests.AddRange(HighContrastTests.All());
        tests.AddRange(ExecutionTests.All());
        tests.AddRange(PairingWindowTests.All());
        tests.AddRange(AiWindowTests.All());
        tests.AddRange(EnvFileTests.All());

        var failed = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var (name, body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"  ok    {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FALLA {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed} de {tests.Count} pruebas de la ventana aprobadas en {stopwatch.Elapsed.TotalSeconds:F1} s.");
        return failed == 0 ? 0 : 1;
    }
}
