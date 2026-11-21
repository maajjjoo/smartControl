using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;

namespace SmartControl.Core.Tests;

/// <summary>
/// Recorrido completo con ventanas SINTÉTICAS y el modelo SINTÉTICO escrito a mano: comprueba
/// que la candidata pasa siempre por el CommandValidator existente y que todo falla de forma
/// segura. No mide la precisión con movimientos reales.
/// </summary>
internal static class RecognitionPipelineTests
{
    private const long NowMs = 1792633800125;
    private const string Device = "wearos-watch-01";
    // Valor ficticio solo para pruebas; no es una credencial real.
    private const string Token = "test-token-not-a-secret";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("reconocimiento: giros aceptados por el validador en cada modo", Sync(AcceptsGesturesThroughValidator));
        yield return ("reconocimiento: sin modelo se rechaza todo", Sync(FailsClosedWithoutModel));
        yield return ("reconocimiento: ventana inválida", Sync(RejectsInvalidWindow));
        yield return ("reconocimiento: sin gesto o con poca confianza no hay candidata", Sync(RejectsNoGestureAndLowConfidence));
        yield return ("reconocimiento: modo no soportado", Sync(RejectsUnsupportedContext));
        yield return ("reconocimiento: el validador sigue decidiendo", Sync(ValidatorStillDecides));
        yield return ("reconocimiento: cada candidata es un mensaje nuevo", Sync(UsesFreshMessageIds));
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    private static CommandValidator Validator(CommandValidatorOptions? options = null) =>
        new(new Dictionary<string, string> { [Device] = Token }, options, () => NowMs);

    private static RecognitionPipeline Pipeline(CommandValidator? validator = null, string token = Token, long clockMs = NowMs) =>
        new(new DenseClassifier(ModelFixtures.Parse(ModelFixtures.LogisticJson())), validator ?? Validator(), Device, token, () => clockMs);

    /// <summary>
    /// Ventana sintética: reposo, meseta de <paramref name="plateau"/> rad/s en X durante 5 muestras y reposo.
    /// Integral = plateau × 0,1 s. Con el modelo sintético: logits (20·i, −20·i, 2).
    /// </summary>
    private static List<SensorSample> TwistWindow(double plateau)
    {
        var samples = new List<SensorSample>();
        for (var i = 0; i < 25; i++)
        {
            var x = i is >= 5 and < 10 ? plateau : 0.0;
            samples.Add(new SensorSample(i * 20, x, 0.0, 0.0));
        }
        return samples;
    }

    private static void AcceptsGesturesThroughValidator()
    {
        var cases = new[]
        {
            (ControlMode.Presentation, 4.0, AllowedAction.SlideNext),
            (ControlMode.Presentation, -4.0, AllowedAction.SlidePrevious),
            (ControlMode.Multimedia, 4.0, AllowedAction.MediaNextTrack),
            (ControlMode.Multimedia, -4.0, AllowedAction.MediaPreviousTrack),
        };
        var pipeline = Pipeline();
        foreach (var (mode, plateau, expected) in cases)
        {
            var outcome = pipeline.Recognize(TwistWindow(plateau), null, mode);
            Check.Equal(RecognitionStatus.Accepted, outcome.Status, $"{mode} {plateau}");
            Check.Equal(ReasonCodes.Ok, outcome.ReasonCode);
            Check.Equal(expected, outcome.Candidate);
            Check.Equal(expected, outcome.ValidatedAction);
            Check.True(outcome.Validation!.IsAccepted, "el validador debe haberla aceptado");
            Check.True(outcome.Decision!.Confidence >= 0.80, "confianza por encima del umbral");
        }
    }

    private static void FailsClosedWithoutModel()
    {
        var pipeline = new RecognitionPipeline(null, Validator(), Device, Token, () => NowMs);
        Check.True(!pipeline.HasModel, "no debe tener modelo");
        var outcome = pipeline.Recognize(TwistWindow(4.0), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.ModelUnavailable, outcome.Status);
        Check.True(outcome.ValidatedAction is null && outcome.Candidate is null, "sin modelo no hay acción");
    }

    private static void RejectsInvalidWindow()
    {
        var outcome = Pipeline().Recognize(TwistWindow(4.0).GetRange(0, 3), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.InvalidWindow, outcome.Status);
        Check.Equal(SensorWindowReasons.TooFewSamples, outcome.ReasonCode);

        var nan = TwistWindow(4.0);
        nan[7] = new SensorSample(140, double.NaN, 0.0, 0.0);
        Check.Equal(SensorWindowReasons.NonFiniteValue, Pipeline().Recognize(nan, null, ControlMode.Presentation).ReasonCode);
        Check.Equal(SensorWindowReasons.MissingSamples, Pipeline().Recognize(null, null, ControlMode.Presentation).ReasonCode);
    }

    private static void RejectsNoGestureAndLowConfidence()
    {
        // Integral 0,03 → logits (0,6, −0,6, 2): gana NO_GESTURE.
        var still = Pipeline().Recognize(TwistWindow(0.3), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.NoGesture, still.Status);
        Check.True(still.Candidate is null && still.Validation is null, "no debe llegar al validador");

        // Integral 0,12 → logits (2,4, −2,4, 2): TWIST_OUT gana con ≈ 0,60 < 0,80.
        var weak = Pipeline().Recognize(TwistWindow(1.2), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.LowConfidence, weak.Status);
        Check.True(weak.Decision!.Confidence < 0.80, "confianza por debajo del umbral");
        Check.True(weak.Candidate is null && weak.Validation is null, "no debe llegar al validador");
    }

    private static void RejectsUnsupportedContext()
    {
        var outcome = Pipeline().Recognize(TwistWindow(4.0), null, (ControlMode)99);
        Check.Equal(RecognitionStatus.UnsupportedContext, outcome.Status);
        Check.True(outcome.ValidatedAction is null, "sin contexto válido no hay acción");
    }

    private static void ValidatorStillDecides()
    {
        // Sesión no vinculada.
        var wrongToken = Pipeline(token: "otro-token-de-prueba").Recognize(TwistWindow(4.0), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.RejectedByValidator, wrongToken.Status);
        Check.Equal(ReasonCodes.UnauthorizedDevice, wrongToken.ReasonCode);
        Check.True(wrongToken.ValidatedAction is null, "rechazada por el validador no debe dar acción");

        // El receptor está fijado en otro modo.
        var validator = Validator();
        validator.ReceiverMode = ControlMode.Multimedia;
        var mismatch = Pipeline(validator).Recognize(TwistWindow(4.0), null, ControlMode.Presentation);
        Check.Equal(ReasonCodes.ReceiverModeMismatch, mismatch.ReasonCode);

        // El validador aplica su propio umbral aunque el modelo haya decidido.
        var strict = Pipeline(Validator(new CommandValidatorOptions { ConfidenceThreshold = 0.999 }))
            .Recognize(TwistWindow(4.0), null, ControlMode.Presentation);
        Check.Equal(RecognitionStatus.RejectedByValidator, strict.Status);
        Check.Equal(ReasonCodes.BelowThreshold, strict.ReasonCode);

        // Un reloj desfasado produce mensajes caducados.
        var stale = Pipeline(clockMs: NowMs - 10_000).Recognize(TwistWindow(4.0), null, ControlMode.Presentation);
        Check.Equal(ReasonCodes.StaleMessage, stale.ReasonCode);
    }

    private static void UsesFreshMessageIds()
    {
        var pipeline = Pipeline();
        for (var i = 0; i < 5; i++)
            Check.Equal(RecognitionStatus.Accepted, pipeline.Recognize(TwistWindow(4.0), null, ControlMode.Presentation).Status, $"intento {i}");
    }
}
