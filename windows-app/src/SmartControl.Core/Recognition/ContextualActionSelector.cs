using System;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Recognition;

/// <summary>
/// Selección contextual (ADR-03): el modo activo es contexto de confianza que aporta la
/// aplicación, nunca el modelo. Con el modo y el gesto reconocido se elige una acción
/// candidata con una tabla fija, y solo si pertenece a la lista blanca de ese modo.
/// Las asociaciones son las mismas del reloj; el sentido de los giros aún no se ha validado
/// en un reloj físico, así que no se cambian sin esa evidencia.
/// La acción candidata todavía debe pasar por <see cref="CommandValidator"/>.
/// </summary>
public static class ContextualActionSelector
{
    public static bool TrySelect(ControlMode mode, GestureClass gesture, out AllowedAction action)
    {
        action = default;
        if (!Enum.IsDefined(mode))
            return false;

        AllowedAction? candidate = (mode, gesture) switch
        {
            (ControlMode.Presentation, GestureClass.TwistOut) => AllowedAction.SlideNext,
            (ControlMode.Presentation, GestureClass.TwistIn) => AllowedAction.SlidePrevious,
            (ControlMode.Multimedia, GestureClass.TwistOut) => AllowedAction.MediaPlay,
            (ControlMode.Multimedia, GestureClass.TwistIn) => AllowedAction.MediaPause,
            _ => null,
        };
        if (candidate is null)
            return false;

        // Defensa adicional: la tabla y la lista blanca deben coincidir siempre.
        foreach (var allowed in CommandAllowlist.ActionsFor(mode))
        {
            if (allowed == candidate.Value)
            {
                action = allowed;
                return true;
            }
        }
        return false;
    }
}
