using System;
using System.Runtime.InteropServices;
using SmartControl.Core.Execution;
using SmartControl.Core.Protocol;

namespace SmartControl.Desktop.Execution;

/// <summary>Las dos únicas teclas que SmartControl puede pulsar.</summary>
internal enum SlideKey
{
    /// <summary>Flecha derecha: siguiente diapositiva.</summary>
    Next,
    /// <summary>Flecha izquierda: diapositiva anterior.</summary>
    Previous,
}

internal interface ISlideKeys
{
    /// <summary>Pulsa y suelta la tecla en la ventana que esté al frente.</summary>
    void Press(SlideKey key);
}

/// <summary>
/// Pulsa → o ← con SendInput, como si se tocara el teclado. PowerPoint, Google Slides, Canva y
/// los visores de PDF a pantalla completa avanzan y retroceden con esas teclas. El código de tecla
/// es fijo: desde la red solo llega <see cref="SlideKey"/>, nunca una tecla arbitraria
/// (docs/security-allowlist.md §4).
/// </summary>
internal sealed class SlideKeys : ISlideKeys
{
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    public void Press(SlideKey key)
    {
        var inputs = BuildInputs(key);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        // Windows devuelve menos eventos de los pedidos si otro proceso bloquea la entrada
        // (por ejemplo, con la pantalla bloqueada).
        if (sent != inputs.Length)
            throw new ActionUnavailableException(ReasonCodes.InputBlocked);
    }

    /// <summary>Pulsar y soltar la tecla. Las flechas son teclas extendidas del teclado.</summary>
    internal static Input[] BuildInputs(SlideKey key)
    {
        var vk = key switch
        {
            SlideKey.Next => VkRight,
            SlideKey.Previous => VkLeft,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        var scan = (ushort)MapVirtualKey(vk, 0);
        return new[]
        {
            Keyboard(vk, scan, KeyEventExtendedKey),
            Keyboard(vk, scan, KeyEventExtendedKey | KeyEventKeyUp),
        };
    }

    private static Input Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = vk, ScanCode = scan, Flags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputData Data;
    }

    /// <summary>Unión de INPUT. Incluye el ratón, que es el miembro más grande, para que el tamaño sea el de Windows.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputData
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
