using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

internal static class ContextualActionSelectorTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("selección: asociaciones existentes por modo", Sync(UsesExistingMappings));
        yield return ("selección: siempre dentro de la lista blanca del modo", Sync(StaysWithinAllowlist));
        yield return ("selección: NO_GESTURE y modos desconocidos no dan acción", Sync(RejectsNoGestureAndUnknownMode));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static AllowedAction Select(ControlMode mode, GestureClass gesture)
    {
        Check.True(ContextualActionSelector.TrySelect(mode, gesture, out var action), $"debe seleccionar para {mode}/{gesture}");
        return action;
    }

    private static void UsesExistingMappings()
    {
        Check.Equal(AllowedAction.SlideNext, Select(ControlMode.Presentation, GestureClass.TwistOut));
        Check.Equal(AllowedAction.SlidePrevious, Select(ControlMode.Presentation, GestureClass.TwistIn));
        Check.Equal(AllowedAction.MediaPlay, Select(ControlMode.Multimedia, GestureClass.TwistOut));
        Check.Equal(AllowedAction.MediaPause, Select(ControlMode.Multimedia, GestureClass.TwistIn));
    }

    private static void StaysWithinAllowlist()
    {
        foreach (var mode in Enum.GetValues<ControlMode>())
            foreach (var gesture in new[] { GestureClass.TwistOut, GestureClass.TwistIn })
            {
                var action = Select(mode, gesture);
                Check.Equal(mode, CommandAllowlist.RequiredMode(action), $"{gesture} en {mode}");
                Check.True(CommandAllowlist.ActionsFor(mode).Contains(action), "debe estar en la lista blanca del modo");
            }
    }

    private static void RejectsNoGestureAndUnknownMode()
    {
        foreach (var mode in Enum.GetValues<ControlMode>())
            Check.True(!ContextualActionSelector.TrySelect(mode, GestureClass.NoGesture, out _), $"NO_GESTURE en {mode}");
        Check.True(!ContextualActionSelector.TrySelect((ControlMode)99, GestureClass.TwistOut, out _), "modo fuera de la enumeración");
        Check.True(!ContextualActionSelector.TrySelect(ControlMode.Presentation, (GestureClass)99, out _), "clase fuera de la enumeración");
    }
}
