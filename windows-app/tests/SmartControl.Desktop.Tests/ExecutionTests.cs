using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SmartControl.Core.Execution;
using SmartControl.Core.Protocol;
using SmartControl.Desktop.Execution;

namespace SmartControl.Desktop.Tests;

/// <summary>
/// Ejecutor real de Windows. Ninguna prueba pulsa teclas ni reproduce o pausa música: el teclado
/// y el reproductor se sustituyen por versiones falsas, y el control multimedia real solo se lee.
/// </summary>
internal static class ExecutionTests
{
    public static IEnumerable<(string, Action)> All()
    {
        yield return ("ejecutor: cada acción va a su tecla o a su orden multimedia", EachActionHasOneTarget);
        yield return ("ejecutor: un fallo conocido sale como motivo, no como error genérico", FailuresKeepReason);
        yield return ("ejecutor: las flechas se pulsan y se sueltan como teclas extendidas", ArrowsAreExtendedKeyPresses);
        yield return ("ejecutor: el control multimedia de Windows responde (solo lectura)", MediaSessionIsReadable);
    }

    private sealed class FakeKeys : ISlideKeys
    {
        public List<SlideKey> Pressed { get; } = new();
        public Exception? Failure { get; set; }

        public void Press(SlideKey key)
        {
            if (Failure is not null) throw Failure;
            Pressed.Add(key);
        }
    }

    private sealed class FakeMedia : IMediaControl
    {
        public List<string> Calls { get; } = new();
        public Exception? Failure { get; set; }

        public void Play() { if (Failure is not null) throw Failure; Calls.Add("play"); }
        public void Pause() { if (Failure is not null) throw Failure; Calls.Add("pause"); }
        public void NextTrack() { if (Failure is not null) throw Failure; Calls.Add("next"); }
        public void PreviousTrack() { if (Failure is not null) throw Failure; Calls.Add("previous"); }
    }

    private static void EachActionHasOneTarget()
    {
        var keys = new FakeKeys();
        var media = new FakeMedia();
        var executor = new WindowsCommandExecutor(keys, media);
        Check.True(executor.PerformsRealActions, "debe declararse como ejecutor real");

        executor.Execute(AllowedAction.SlideNext);
        executor.Execute(AllowedAction.SlidePrevious);
        executor.Execute(AllowedAction.MediaPlay);
        executor.Execute(AllowedAction.MediaPause);
        executor.Execute(AllowedAction.MediaNextTrack);
        executor.Execute(AllowedAction.MediaPreviousTrack);

        Check.Equal("Next,Previous", string.Join(",", keys.Pressed));
        Check.Equal("play,pause,next,previous", string.Join(",", media.Calls), "cada orden multimedia es distinta, sin alternador");
        var undefined = false;
        try { executor.Execute((AllowedAction)99); } catch (ArgumentOutOfRangeException) { undefined = true; }
        Check.True(undefined, "un valor fuera de la enumeración se rechaza");
        Check.Equal(2, keys.Pressed.Count, "y no pulsa nada");
    }

    private static void FailuresKeepReason()
    {
        var media = new FakeMedia { Failure = new ActionUnavailableException(ReasonCodes.NoMediaSession) };
        var executor = new WindowsCommandExecutor(new FakeKeys(), media);
        try
        {
            executor.Execute(AllowedAction.MediaPlay);
            throw new TestFailure("debía fallar");
        }
        catch (ActionUnavailableException ex)
        {
            Check.Equal(ReasonCodes.NoMediaSession, ex.ReasonCode);
        }
    }

    private static void ArrowsAreExtendedKeyPresses()
    {
        Check.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<SlideKeys.Input>(), "tamaño de INPUT de Windows");
        foreach (var (key, vk) in new[] { (SlideKey.Next, 0x27), (SlideKey.Previous, 0x25) })
        {
            var inputs = SlideKeys.BuildInputs(key);
            Check.Equal(2, inputs.Length, $"{key}: pulsar y soltar");
            Check.Equal(1u, inputs[0].Type, "teclado");
            Check.Equal((ushort)vk, inputs[0].Data.Keyboard.VirtualKey, $"{key}: tecla");
            Check.Equal((ushort)vk, inputs[1].Data.Keyboard.VirtualKey, $"{key}: tecla al soltar");
            Check.Equal(0x1u, inputs[0].Data.Keyboard.Flags, $"{key}: pulsar, extendida");
            Check.Equal(0x3u, inputs[1].Data.Keyboard.Flags, $"{key}: soltar, extendida");
            Check.True(inputs[0].Data.Keyboard.ScanCode != 0, $"{key}: código de exploración");
        }
    }

    /// <summary>
    /// Recorre la misma vía que reproducir y pausar (activar el gestor, esperar la operación
    /// asíncrona, pedir la sesión actual) pero solo lee qué aplicación tiene el control.
    /// </summary>
    private static void MediaSessionIsReadable()
    {
        var app = MediaSessionControl.CurrentApp();
        Console.WriteLine($"        (control multimedia actual: {app ?? "ninguno"})");
    }
}
