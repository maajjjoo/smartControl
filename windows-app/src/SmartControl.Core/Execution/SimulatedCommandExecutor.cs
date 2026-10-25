using System;
using System.Collections.Generic;
using SmartControl.Core.Protocol;

namespace SmartControl.Core.Execution;

/// <summary>
/// Frontera del ejecutor: solo recibe un valor de la enumeración cerrada
/// <see cref="AllowedAction"/>, nunca texto procedente de la red.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>Ejecuta la acción y devuelve una descripción breve del efecto.</summary>
    string Execute(AllowedAction action);
}

/// <summary>
/// Ejecutor SIMULADO. No envía teclas, no llama a APIs de Windows y no inicia
/// procesos: solo registra qué acción se habría ejecutado. Las acciones reales
/// llegarán en una etapa propia, con pruebas y revisión de seguridad.
/// </summary>
public sealed class SimulatedCommandExecutor : ICommandExecutor
{
    private readonly object _lock = new();
    private readonly List<AllowedAction> _history = new();

    public IReadOnlyList<AllowedAction> History
    {
        get { lock (_lock) return _history.ToArray(); }
    }

    public string Execute(AllowedAction action)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action), "Acción fuera de la lista blanca");

        // Mapeo estático: no puede parametrizarse desde los mensajes recibidos.
        var effect = action switch
        {
            AllowedAction.SlideNext => "Simulado: avanzar diapositiva",
            AllowedAction.SlidePrevious => "Simulado: retroceder diapositiva",
            AllowedAction.MediaPlay => "Simulado: reproducir (acción explícita, no alternador)",
            AllowedAction.MediaPause => "Simulado: pausar (acción explícita, no alternador)",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        lock (_lock) _history.Add(action);
        return effect;
    }
}
