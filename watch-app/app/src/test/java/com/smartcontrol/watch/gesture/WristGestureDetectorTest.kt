package com.smartcontrol.watch.gesture

import org.junit.Assert.assertEquals
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
}
