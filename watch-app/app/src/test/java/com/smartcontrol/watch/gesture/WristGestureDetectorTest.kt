package com.smartcontrol.watch.gesture

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Pruebas con series sintéticas de giroscopio (rad/s) muestreadas a 50 Hz (ver [SyntheticGestures]). */
class WristGestureDetectorTest {

    private val step = SyntheticGestures.STEP_NANOS

    /** Alimenta muestras a 50 Hz y recuerda por dónde va el reloj, para encadenar movimientos. */
    private class Feeder(val detector: WristGestureDetector = WristGestureDetector()) {
        var nowNanos = 1_000_000_000L
        fun feed(samples: List<SyntheticGestures.Sample>): List<GestureEvent> = samples.mapNotNull { s ->
            nowNanos += SyntheticGestures.STEP_NANOS
            detector.onSample(nowNanos, s.x, s.y, s.z)
        }
        fun skip(millis: Long) { nowNanos += millis * 1_000_000 }
    }

    private fun recognized(events: List<GestureEvent>): List<WristGesture> =
        events.filterIsInstance<GestureEvent.Recognized>().map { it.gesture }

    private fun rest(millis: Long) = SyntheticGestures.rest(millis)
    private fun twist(peak: Float, other: Float = 0f) = SyntheticGestures.twist(peak, other = other)

    // --- Reconocimiento básico ---

    @Test
    fun quickOutwardTwistIsRecognized() {
        val event = Feeder().feed(twist(5.5f)).single() as GestureEvent.Recognized
        assertEquals(WristGesture.TWIST_OUT, event.gesture)
        assertTrue(event.confidence >= MIN_GESTURE_CONFIDENCE)
    }

    @Test
    fun quickInwardTwistIsRecognized() {
        val event = Feeder().feed(twist(-5.5f)).single() as GestureEvent.Recognized
        assertEquals(WristGesture.TWIST_IN, event.gesture)
    }

    @Test
    fun eventsCarryTheSegmentBounds() {
        // twist(): 3 en reposo, 3.3 (empieza), 5.5 × 4, 1.65 y 0 (termina). Muestra i en 1 s + (i + 1) · 20 ms.
        val feeder = Feeder()
        val start = feeder.nowNanos + 4 * step
        val end = feeder.nowNanos + 10 * step
        val event = feeder.feed(twist(5.5f)).single()
        assertEquals(start, event.startNanos)
        assertEquals(end, event.endNanos)

        val weak = Feeder().feed(twist(3.0f)).single() as GestureEvent.Unclear
        assertTrue(weak.endNanos > weak.startNanos)
    }

    @Test
    fun smallMovementsProduceNothing() {
        val calm = List(50) { i -> SyntheticGestures.Sample(if (i % 2 == 0) 1.2f else -1.0f, 0.5f, 0.3f) }
        assertTrue(Feeder().feed(calm).isEmpty())
    }

    @Test
    fun weakTwistIsUnclear() {
        val event = Feeder().feed(twist(3.0f)).single() as GestureEvent.Unclear
        assertEquals(UnclearReason.WEAK, event.reason)
        assertTrue(event.confidence < MIN_GESTURE_CONFIDENCE)
    }

    @Test
    fun movementOnOtherAxesIsAmbiguous() {
        val event = Feeder().feed(twist(5.5f, other = 5.0f)).single() as GestureEvent.Unclear
        assertEquals(UnclearReason.AMBIGUOUS_AXIS, event.reason)
    }

    @Test
    fun backAndForthShakeIsRejected() {
        val event = Feeder().feed(SyntheticGestures.shake()).single() as GestureEvent.Unclear
        assertEquals(UnclearReason.BACK_AND_FORTH, event.reason)
    }

    @Test
    fun slowSustainedRotationIsRejected() {
        val event = Feeder().feed(SyntheticGestures.slow(millis = 1_200)).first() as GestureEvent.Unclear
        assertEquals(UnclearReason.TOO_SLOW, event.reason)
    }

    // --- Umbral de confianza (0.80) ---

    @Test
    fun confidenceExactlyAtThresholdIsAccepted() {
        // Pico de 4.0 rad/s sobre 5.0 → confianza 0.80 exacta.
        val event = Feeder().feed(twist(4.0f)).single() as GestureEvent.Recognized
        assertEquals(MIN_GESTURE_CONFIDENCE, event.confidence, 0f)
    }

    @Test
    fun confidenceJustBelowThresholdIsRejected() {
        val event = Feeder().feed(twist(3.99f)).single() as GestureEvent.Unclear
        assertEquals(UnclearReason.WEAK, event.reason)
        assertTrue(event.confidence < MIN_GESTURE_CONFIDENCE)
    }

    // --- Regreso de la muñeca ---

    @Test
    fun returnMovementDoesNotTriggerOppositeGesture() {
        val events = Feeder().feed(SyntheticGestures.twistAndReturn(5.5f))
        assertEquals(listOf(WristGesture.TWIST_OUT), recognized(events))
        assertEquals(1, events.size)
    }

    @Test
    fun returnAfterPauseWithWristTurnedIsIgnored() {
        for (pause in listOf(200L, 600L, 1_200L, 2_500L)) {
            val events = Feeder().feed(SyntheticGestures.twistAndReturn(5.5f, pauseMillis = pause))
            assertEquals("pausa $pause ms", listOf(WristGesture.TWIST_OUT), recognized(events))
            assertEquals("pausa $pause ms", 1, events.size)
        }
    }

    @Test
    fun fastReturnIsIgnoredToo() {
        val out = twist(5.5f)
        val events = Feeder().feed(out + SyntheticGestures.returnOf(out, speedFraction = 1f))
        assertEquals(listOf(WristGesture.TWIST_OUT), recognized(events))
    }

    @Test
    fun newGestureIsAcceptedAfterReturningToRest() {
        val feeder = Feeder()
        val first = feeder.feed(SyntheticGestures.twistAndReturn(5.5f, restAfterMillis = 600))
        val second = feeder.feed(SyntheticGestures.twistAndReturn(-5.5f))
        assertEquals(listOf(WristGesture.TWIST_OUT), recognized(first))
        assertEquals(listOf(WristGesture.TWIST_IN), recognized(second))
    }

    @Test
    fun returningToRestDoesNotSkipTheCooldown() {
        val feeder = Feeder()
        // Giro, regreso y 300 ms de reposo: aún dentro de los 700 ms de espera desde el gesto.
        feeder.feed(SyntheticGestures.twistAndReturn(5.5f, restAfterMillis = 300))
        assertTrue(feeder.feed(twist(-5.5f)).isEmpty())
    }

    @Test
    fun stillnessWithoutReturningDoesNotRearmUntilResettle() {
        val feeder = Feeder()
        feeder.feed(twist(5.5f)) // la muñeca se queda girada
        feeder.feed(rest(2_000))
        // Un giro hacia dentro antes del tiempo de reasentamiento es el regreso: se ignora.
        assertTrue(feeder.feed(twist(-5.5f)).isEmpty())
    }

    @Test
    fun longPauseInNewPositionBecomesNeutral() {
        // Limitación documentada: tras 3 s quieta en la nueva posición, el siguiente giro sí cuenta.
        val feeder = Feeder()
        feeder.feed(twist(5.5f))
        feeder.feed(rest(3_200))
        assertEquals(listOf(WristGesture.TWIST_IN), recognized(feeder.feed(twist(-5.5f))))
    }

    @Test
    fun continuousMovementKeepsRecognizerDisarmed() {
        val feeder = Feeder()
        feeder.feed(SyntheticGestures.twistAndReturn(5.5f, restAfterMillis = 0))
        // Movimiento constante por debajo del umbral de inicio pero por encima del de reposo.
        val restless = List(150) { i -> SyntheticGestures.Sample(if (i % 2 == 0) 1.0f else -1.0f, 0f, 0f) }
        feeder.feed(restless)
        assertTrue(feeder.feed(twist(5.5f)).isEmpty())
    }

    // --- Espera mínima y reinicios del sensor ---

    @Test
    fun secondTwistDuringCooldownIsIgnored() {
        val feeder = Feeder()
        assertEquals(1, feeder.feed(SyntheticGestures.twistAndReturn(5.5f, restAfterMillis = 0)).size)
        assertTrue(feeder.feed(twist(5.5f)).isEmpty())
    }

    @Test
    fun cooldownSurvivesSensorRestart() {
        val feeder = Feeder()
        feeder.feed(twist(5.5f) + SyntheticGestures.returnOf(twist(5.5f)))
        feeder.detector.onSensorRestart()
        // Justo después del reinicio, aún dentro de la espera y sin reposo suficiente.
        assertTrue(feeder.feed(twist(5.5f)).isEmpty())
    }

    @Test
    fun disarmedStateSurvivesSensorRestart() {
        val feeder = Feeder()
        feeder.feed(twist(5.5f)) // muñeca girada, desarmado
        feeder.detector.onSensorRestart()
        feeder.skip(1_000) // el sensor estuvo parado 1 s
        feeder.feed(rest(500))
        // El regreso tras el reinicio no cuenta como gesto contrario.
        assertTrue(feeder.feed(SyntheticGestures.returnOf(twist(5.5f))).isEmpty())
    }

    @Test
    fun sensorRestartDiscardsPartialGestureAndRequiresRest() {
        val feeder = Feeder()
        assertTrue(feeder.feed(List(3) { SyntheticGestures.Sample(5.5f, 0f, 0f) }).isEmpty())
        feeder.detector.onSensorRestart()
        // El resto del movimiento interrumpido no produce un evento.
        assertTrue(feeder.feed(listOf(SyntheticGestures.Sample(5.5f, 0f, 0f), SyntheticGestures.Sample(0f, 0f, 0f))).isEmpty())
        // Tras reposar el tiempo de reasentamiento, vuelve a funcionar.
        feeder.feed(rest(3_200))
        assertEquals(listOf(WristGesture.TWIST_IN), recognized(feeder.feed(twist(-5.5f))))
    }

    @Test
    fun timestampsGoingBackwardsAreIgnored() {
        val detector = WristGestureDetector()
        detector.onSample(10 * step, 0f, 0f, 0f)
        // Muestras "del pasado" (p. ej. reales tras una serie sintética) no inician ni cierran gestos.
        assertNull(detector.onSample(5 * step, 5.5f, 0f, 0f))
        assertNull(detector.onSample(6 * step, 0f, 0f, 0f))
        val feeder = Feeder(detector).apply { nowNanos = 10 * step }
        assertEquals(listOf(WristGesture.TWIST_OUT), recognized(feeder.feed(twist(5.5f))))
    }
}
