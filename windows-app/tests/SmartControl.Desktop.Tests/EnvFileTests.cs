using System;
using System.Collections.Generic;
using System.IO;
using SmartControl.Desktop.Configuration;

namespace SmartControl.Desktop.Tests;

/// <summary>Lectura de la clave de Gemini desde archivos .env (con valores ficticios).</summary>
internal static class EnvFileTests
{
    public static IEnumerable<(string, Action)> All()
    {
        yield return ("env: lee claves con comentarios, comillas y export", ReadsCommonFormats);
        yield return ("env: el primer archivo que tiene la clave gana", FirstFileWins);
    }

    private static string TempFile(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "smartcontrol-env-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, text);
        return path;
    }

    private static void ReadsCommonFormats()
    {
        var path = TempFile("# Clave de prueba, no real\nGEMINI_API_KEY = \"clave-ficticia\"\nexport GEMINI_MODEL='gemini-2.5-flash'\nLINEA SIN IGUAL\n=sin-clave\n");
        try
        {
            var values = EnvFile.Read(path);
            Check.Equal("clave-ficticia", values["GEMINI_API_KEY"]);
            Check.Equal("gemini-2.5-flash", values["GEMINI_MODEL"]);
            Check.Equal(2, values.Count, "las líneas inválidas se ignoran");
            Check.Equal(0, EnvFile.Read(path + ".no-existe").Count, "un archivo que no existe está vacío");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void FirstFileWins()
    {
        var empty = TempFile("OTRA=1\n");
        var first = TempFile("SMARTCONTROL_TEST_ONLY_KEY=primero\n");
        var second = TempFile("SMARTCONTROL_TEST_ONLY_KEY=segundo\n");
        try
        {
            Check.Equal("primero", EnvFile.Get("SMARTCONTROL_TEST_ONLY_KEY", new[] { empty, first, second }));
            Check.Equal(null, EnvFile.Get("SMARTCONTROL_TEST_NO_EXISTE", new[] { empty }));
        }
        finally
        {
            File.Delete(empty);
            File.Delete(first);
            File.Delete(second);
        }
    }
}
