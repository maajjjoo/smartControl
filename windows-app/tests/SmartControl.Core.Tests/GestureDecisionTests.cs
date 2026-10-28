using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>Decisión sobre probabilidades escritas a mano (no proceden de ningún modelo entrenado).</summary>
internal static class GestureDecisionTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("decisión: reconoce cada giro por encima del umbral", Sync(RecognizesConfidentGestures));
        yield return ("decisión: límite exacto del umbral de 0.80", Sync(AppliesThresholdBoundary));
        yield return ("decisión: NO_GESTURE nunca produce un gesto", Sync(NoGestureNeverRecognized));
        yield return ("decisión: umbral del modelo más exigente", Sync(RespectsStricterThreshold));
        yield return ("decisión: un umbral menor que 0.80 no se aplica", Sync(NeverGoesBelowMinimum));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static void RecognizesConfidentGestures()
    {
        var outward = GestureDecision.Decide(new GesturePrediction(0.9, 0.05, 0.05), 0.80);
        Check.True(outward.IsRecognized, "debe reconocer TWIST_OUT");
        Check.Equal(GestureClass.TwistOut, outward.Gesture);
        Check.Equal(0.9, outward.Confidence);

        var inward = GestureDecision.Decide(new GesturePrediction(0.02, 0.97, 0.01), 0.80);
        Check.Equal(GestureClass.TwistIn, inward.Gesture);
    }

    private static void AppliesThresholdBoundary()
    {
        Check.Equal(GestureDecisionStatus.Recognized, GestureDecision.Decide(new GesturePrediction(0.80, 0.10, 0.10), 0.80).Status);
        var below = GestureDecision.Decide(new GesturePrediction(0.7999, 0.1001, 0.10), 0.80);
        Check.Equal(GestureDecisionStatus.LowConfidence, below.Status);
        Check.True(below.Gesture is null, "sin confianza suficiente no hay gesto");
        Check.Equal(GestureDecisionStatus.LowConfidence, GestureDecision.Decide(new GesturePrediction(0.45, 0.40, 0.15), 0.80).Status);
    }

    private static void NoGestureNeverRecognized()
    {
        var clear = GestureDecision.Decide(new GesturePrediction(0.01, 0.01, 0.98), 0.80);
        Check.Equal(GestureDecisionStatus.NoGesture, clear.Status);
        Check.True(clear.Gesture is null, "NO_GESTURE no debe devolver gesto");

        // Empate entre un giro y NO_GESTURE: se elige no actuar.
        Check.Equal(GestureDecisionStatus.NoGesture, GestureDecision.Decide(new GesturePrediction(0.45, 0.10, 0.45), 0.80).Status);
    }

    private static void RespectsStricterThreshold()
    {
        Check.Equal(GestureDecisionStatus.LowConfidence, GestureDecision.Decide(new GesturePrediction(0.90, 0.05, 0.05), 0.95).Status);
        Check.Equal(GestureDecisionStatus.Recognized, GestureDecision.Decide(new GesturePrediction(0.96, 0.02, 0.02), 0.95).Status);
    }

    private static void NeverGoesBelowMinimum()
    {
        Check.Equal(GestureDecisionStatus.LowConfidence, GestureDecision.Decide(new GesturePrediction(0.70, 0.20, 0.10), 0.50).Status);
        Check.Equal(GestureDecisionStatus.LowConfidence, GestureDecision.Decide(new GesturePrediction(0.70, 0.20, 0.10), double.NaN).Status);
    }
}
