using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SmartControl.Core.Execution;
using SmartControl.Core.Protocol;

namespace SmartControl.Desktop.Execution;

internal interface IMediaControl
{
    void Play();
    void Pause();
    void NextTrack();
    void PreviousTrack();
}

/// <summary>
/// Reproducir y pausar con el control multimedia de Windows (el que aparece junto al volumen),
/// sobre el reproductor activo: Spotify, YouTube en el navegador, el Reproductor multimedia, etc.
/// Son dos órdenes distintas, no un alternador (docs/security-allowlist.md §2): pausar nunca
/// reanuda.
///
/// La API es de Windows Runtime (Windows.Media.Control). Para no depender de paquetes externos
/// (el proyecto se compila sin el SDK de .NET), se llama por COM: identificadores de interfaz y
/// posiciones de método tomados de C:\Windows\System32\WinMetadata\Windows.Media.winmd.
/// </summary>
internal sealed class MediaSessionControl : IMediaControl
{
    private const string ManagerClass = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
    private static readonly Guid ManagerStaticsId = new("2050c4ee-11a0-57de-aed7-c97c70338245");
    private static readonly Guid AsyncInfoId = new("00000036-0000-0000-c000-000000000046");

    // Posiciones en la tabla de métodos: las 6 primeras son IUnknown e IInspectable.
    private const int RequestAsyncSlot = 6;        // IGlobalSystemMediaTransportControlsSessionManagerStatics
    private const int GetCurrentSessionSlot = 6;   // IGlobalSystemMediaTransportControlsSessionManager
    private const int SourceAppSlot = 6;           // IGlobalSystemMediaTransportControlsSession.get_SourceAppUserModelId
    private const int TryPlaySlot = 10;            // IGlobalSystemMediaTransportControlsSession.TryPlayAsync
    private const int TryPauseSlot = 11;           // IGlobalSystemMediaTransportControlsSession.TryPauseAsync
    private const int TrySkipNextSlot = 16;        // IGlobalSystemMediaTransportControlsSession.TrySkipNextAsync
    private const int TrySkipPreviousSlot = 17;    // IGlobalSystemMediaTransportControlsSession.TrySkipPreviousAsync
    private const int AsyncStatusSlot = 7;         // IAsyncInfo.get_Status
    private const int AsyncCancelSlot = 9;         // IAsyncInfo.Cancel
    private const int GetResultsSlot = 8;          // IAsyncOperation<T>.GetResults

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public void Play() => Send(TryPlaySlot);

    public void Pause() => Send(TryPauseSlot);

    public void NextTrack() => Send(TrySkipNextSlot);

    public void PreviousTrack() => Send(TrySkipPreviousSlot);

    /// <summary>
    /// Solo lectura: identificador de la aplicación que tiene el control multimedia, o nulo si no
    /// hay ninguna. No reproduce ni pausa nada.
    /// </summary>
    internal static string? CurrentApp()
    {
        using var session = CurrentSession();
        if (session is null)
            return null;
        Check(Method<HStringOut>(session.Pointer, SourceAppSlot)(session.Pointer, out var hstring));
        try
        {
            var text = WindowsGetStringRawBuffer(hstring, out var length);
            return text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static void Send(int slot)
    {
        using var session = CurrentSession() ?? throw new ActionUnavailableException(ReasonCodes.NoMediaSession);
        Check(Method<PointerOut>(session.Pointer, slot)(session.Pointer, out var operation));
        using var pending = new ComObject(operation);
        Wait(pending.Pointer);
        Check(Method<BoolOut>(pending.Pointer, GetResultsSlot)(pending.Pointer, out var accepted));
        if (accepted == 0)
            throw new ActionUnavailableException(ReasonCodes.MediaCommandRefused);
    }

    private static ComObject? CurrentSession()
    {
        EnsureWindowsRuntime();
        using var statics = new ComObject(ActivationFactory(ManagerClass, ManagerStaticsId));
        Check(Method<PointerOut>(statics.Pointer, RequestAsyncSlot)(statics.Pointer, out var request));
        using var pending = new ComObject(request);
        Wait(pending.Pointer);
        Check(Method<PointerOut>(pending.Pointer, GetResultsSlot)(pending.Pointer, out var manager));
        using var sessions = new ComObject(manager);
        Check(Method<PointerOut>(sessions.Pointer, GetCurrentSessionSlot)(sessions.Pointer, out var session));
        return session == IntPtr.Zero ? null : new ComObject(session);
    }

    /// <summary>Espera a que termine una operación asíncrona de Windows Runtime.</summary>
    private static void Wait(IntPtr operation)
    {
        var iid = AsyncInfoId;
        Check(Marshal.QueryInterface(operation, ref iid, out var infoPointer));
        using var info = new ComObject(infoPointer);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Check(Method<IntOut>(info.Pointer, AsyncStatusSlot)(info.Pointer, out var status));
            switch (status)
            {
                case 0: // Started
                    if (watch.Elapsed > Timeout)
                    {
                        Method<NoArgs>(info.Pointer, AsyncCancelSlot)(info.Pointer);
                        throw new TimeoutException("El control multimedia de Windows no respondió.");
                    }
                    Thread.Sleep(5);
                    continue;
                case 1: // Completed
                    return;
                default: // Canceled o Error
                    throw new InvalidOperationException("El control multimedia de Windows devolvió un error.");
            }
        }
    }

    private static IntPtr ActivationFactory(string className, Guid iid)
    {
        Check(WindowsCreateString(className, className.Length, out var hstring));
        try
        {
            Check(RoGetActivationFactory(hstring, ref iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>Inicializa Windows Runtime en este hilo si nadie lo hizo antes.</summary>
    private static void EnsureWindowsRuntime()
    {
        const int NotInitialized = unchecked((int)0x800401F0);
        if (CoGetApartmentType(out _, out _) == NotInitialized)
            Check(RoInitialize(1)); // RO_INIT_MULTITHREADED
    }

    private static void Check(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    /// <summary>Referencia COM que se libera al salir del bloque using.</summary>
    private sealed class ComObject : IDisposable
    {
        public ComObject(IntPtr pointer) => Pointer = pointer;

        public IntPtr Pointer { get; private set; }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
                return;
            Marshal.Release(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PointerOut(IntPtr self, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HStringOut(IntPtr self, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IntOut(IntPtr self, out int value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BoolOut(IntPtr self, out byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NoArgs(IntPtr self);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("ole32.dll")]
    private static extern int CoGetApartmentType(out int type, out int qualifier);
}
