using System;
using System.Collections.Generic;

namespace SmartControl.Core.Protocol;

/// <summary>Modos de control definidos en docs/security-allowlist.md.</summary>
public enum ControlMode
{
    Presentation,
    Multimedia,
}

/// <summary>
/// Únicas acciones que el receptor puede entregar al ejecutor. El ejecutor nunca
/// recibe texto procedente de la red, solo un valor de esta enumeración cerrada.
/// </summary>
public enum AllowedAction
{
    SlideNext,
    SlidePrevious,
    MediaPlay,
    MediaPause,
}

/// <summary>
/// Lista blanca: traducción exacta (sensible a mayúsculas) entre los nombres del
/// protocolo y las enumeraciones internas, y el modo al que pertenece cada acción.
/// </summary>
public static class CommandAllowlist
{
    private static readonly AllowedAction[] PresentationActions = { AllowedAction.SlideNext, AllowedAction.SlidePrevious };
    private static readonly AllowedAction[] MultimediaActions = { AllowedAction.MediaPlay, AllowedAction.MediaPause };

    public static bool TryParseMode(string value, out ControlMode mode)
    {
        switch (value)
        {
            case "PRESENTATION": mode = ControlMode.Presentation; return true;
            case "MULTIMEDIA": mode = ControlMode.Multimedia; return true;
            default: mode = default; return false;
        }
    }

    public static bool TryParseAction(string value, out AllowedAction action)
    {
        switch (value)
        {
            case "SLIDE_NEXT": action = AllowedAction.SlideNext; return true;
            case "SLIDE_PREVIOUS": action = AllowedAction.SlidePrevious; return true;
            case "MEDIA_PLAY": action = AllowedAction.MediaPlay; return true;
            case "MEDIA_PAUSE": action = AllowedAction.MediaPause; return true;
            default: action = default; return false;
        }
    }

    public static ControlMode RequiredMode(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext or AllowedAction.SlidePrevious => ControlMode.Presentation,
        AllowedAction.MediaPlay or AllowedAction.MediaPause => ControlMode.Multimedia,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static IReadOnlyList<AllowedAction> ActionsFor(ControlMode mode) => mode switch
    {
        ControlMode.Presentation => PresentationActions,
        ControlMode.Multimedia => MultimediaActions,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string ToWireName(ControlMode mode) => mode switch
    {
        ControlMode.Presentation => "PRESENTATION",
        ControlMode.Multimedia => "MULTIMEDIA",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string ToWireName(AllowedAction action) => action switch
    {
        AllowedAction.SlideNext => "SLIDE_NEXT",
        AllowedAction.SlidePrevious => "SLIDE_PREVIOUS",
        AllowedAction.MediaPlay => "MEDIA_PLAY",
        AllowedAction.MediaPause => "MEDIA_PAUSE",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
