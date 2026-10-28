namespace SmartControl.Core.Recognition;

public enum GestureDecisionStatus
{
    Recognized,
    /// <summary>La clase más probable es NO_GESTURE: un movimiento que no es un gesto.</summary>
    NoGesture,
    /// <summary>Ninguna clase alcanza el umbral: no se actúa.</summary>
    LowConfidence,
}

/// <summary>Resultado de decidir sobre una predicción. <see cref="Gesture"/> solo tiene valor si se reconoció.</summary>
public sealed record GestureDecision(GestureDecisionStatus Status, GestureClass? Gesture, double Confidence)
{
    public bool IsRecognized => Status == GestureDecisionStatus.Recognized && Gesture is not null;

    /// <summary>
    /// Decide con el umbral del modelo (nunca inferior a <see cref="GestureModel.MinimumThreshold"/>).
    /// Con tres clases y un umbral ≥ 0,80, la segunda clase no puede superar 0,20, así que no hace
    /// falta una regla de margen aparte.
    /// </summary>
    public static GestureDecision Decide(GesturePrediction prediction, double threshold)
    {
        if (threshold < GestureModel.MinimumThreshold)
            threshold = GestureModel.MinimumThreshold;

        var best = GestureClass.TwistOut;
        var confidence = prediction.TwistOut;
        if (prediction.TwistIn > confidence)
        {
            best = GestureClass.TwistIn;
            confidence = prediction.TwistIn;
        }
        // En caso de empate gana NO_GESTURE: ante la duda, no actuar.
        if (prediction.NoGesture >= confidence)
        {
            best = GestureClass.NoGesture;
            confidence = prediction.NoGesture;
        }

        if (best == GestureClass.NoGesture)
            return new GestureDecision(GestureDecisionStatus.NoGesture, null, confidence);
        if (!(confidence >= threshold))
            return new GestureDecision(GestureDecisionStatus.LowConfidence, null, confidence);
        return new GestureDecision(GestureDecisionStatus.Recognized, best, confidence);
    }
}
