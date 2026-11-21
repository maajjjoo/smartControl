using System;
using SmartControl.Core.Execution;
using SmartControl.Core.Protocol;

namespace SmartControl.Desktop.Execution;

/// <summary>
/// Ejecutor real de Windows. Solo recibe un <see cref="AllowedAction"/> ya validado y lo traduce
/// con un mapeo fijo: flechas para las diapositivas y el control multimedia para la música.
/// </summary>
internal sealed class WindowsCommandExecutor : ICommandExecutor
{
    private readonly ISlideKeys _keys;
    private readonly IMediaControl _media;

    public WindowsCommandExecutor() : this(new SlideKeys(), new MediaSessionControl()) { }

    internal WindowsCommandExecutor(ISlideKeys keys, IMediaControl media)
    {
        _keys = keys;
        _media = media;
    }

    public bool PerformsRealActions => true;

    public string Execute(AllowedAction action)
    {
        switch (action)
        {
            case AllowedAction.SlideNext:
                _keys.Press(SlideKey.Next);
                return "Se pulsó la flecha derecha en la ventana activa";
            case AllowedAction.SlidePrevious:
                _keys.Press(SlideKey.Previous);
                return "Se pulsó la flecha izquierda en la ventana activa";
            case AllowedAction.MediaPlay:
                _media.Play();
                return "El reproductor activo recibió la orden de reproducir";
            case AllowedAction.MediaPause:
                _media.Pause();
                return "El reproductor activo recibió la orden de pausar";
            default:
                throw new ArgumentOutOfRangeException(nameof(action), "Acción fuera de la lista blanca");
        }
    }
}
