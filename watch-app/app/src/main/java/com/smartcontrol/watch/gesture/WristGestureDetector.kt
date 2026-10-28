package com.smartcontrol.watch.gesture

import kotlin.math.abs
import kotlin.math.max
import kotlin.math.sqrt

/**
 * Confianza mínima para actuar ante un gesto (umbral del protocolo, RF-11). Es el único valor del
 * umbral: lo usan el reconocedor y la validación del estado.
 */
const val MIN_GESTURE_CONFIDENCE = 0.80f

/**
 * Gestos de muñeca predefinidos. Un giro rápido de la muñeca alrededor del eje del antebrazo,
 * que se supone alineado con el eje X del giroscopio (la línea de las 9 a las 3 de la esfera con
 * la orientación de pantalla por defecto).
 *
 * "Hacia fuera" corresponde a velocidad angular positiva en X. Es una convención SIN VALIDAR: el
 * mismo movimiento anatómico da el signo contrario según la muñeca (izquierda o derecha) y la
 * posición de la corona, y no se ha medido en un reloj real. No se debe ajustar sin datos.
 */
enum class WristGesture(val id: String) {
    TWIST_OUT("WRIST_TWIST_OUT"),
    TWIST_IN("WRIST_TWIST_IN"),
}

/**
 * Resultado de un movimiento. [startNanos] y [endNanos] son las marcas de tiempo del sensor en las
 * que empezó y terminó el tramo (0 si el evento no procede del detector); el grabador de
 * depuración las usa para recortar la ventana que se guarda.
 */
sealed interface GestureEvent {
    val startNanos: Long
    val endNanos: Long

    /** Movimiento claro de un único gesto, con confianza en [0, 1]. */
    data class Recognized(
        val gesture: WristGesture,
        val confidence: Float,
        override val startNanos: Long = 0,
        override val endNanos: Long = 0,
    ) : GestureEvent

    /** Hubo un movimiento con intención aparente, pero no cumple las reglas: no se actúa. */
    data class Unclear(
        val confidence: Float,
        val reason: UnclearReason,
        override val startNanos: Long = 0,
        override val endNanos: Long = 0,
    ) : GestureEvent
}

enum class UnclearReason { WEAK, AMBIGUOUS_AXIS, BACK_AND_FORTH, TOO_SLOW }

/**
 * Reconocedor por reglas (primera etapa del enfoque híbrido de ADR-03), alimentado con muestras
 * del giroscopio en rad/s. No usa aprendizaje automático.
 *
 * Un gesto empieza cuando |ωx| supera [Config.startThreshold] y termina cuando vuelve por debajo
 * de la mitad de ese valor. Se acepta solo si:
 * - la confianza (pico respecto a [Config.strongPeak], penalizada por el movimiento en otros ejes)
 *   llega a [MIN_GESTURE_CONFIDENCE];
 * - el giro va en un solo sentido (una sacudida de ida y vuelta se descarta);
 * - dura como mucho [Config.maxDurationMillis].
 *
 * Tras cada evento el reconocedor queda DESARMADO y no empieza otro gesto hasta que:
 * - pasa la espera mínima [Config.cooldownMillis], y
 * - la muñeca está en reposo (|ω| total < [Config.restThreshold]) durante [Config.restMillis], y
 * - ha vuelto cerca de la orientación en la que empezó el gesto (±[Config.returnToleranceRad],
 *   integrando ωx), o lleva [Config.resettleMillis] quieta en la nueva posición, que pasa a ser la
 *   neutra.
 * Así, el regreso de la muñeca tras un giro (aunque haga una pausa girada) no cuenta como el gesto
 * contrario. Si la pausa supera [Config.resettleMillis], el regreso sí puede contar: ese tiempo
 * debe calibrarse con datos de un reloj real.
 */
class WristGestureDetector(private val config: Config = Config()) {

    data class Config(
        val startThreshold: Float = 2.5f,
        val strongPeak: Float = 5.0f,
        val minDominance: Float = 0.80f,
        val maxDurationMillis: Long = 600,
        val cooldownMillis: Long = 700,
        val restThreshold: Float = 0.8f,
        val restMillis: Long = 200,
        val returnToleranceRad: Float = 0.35f, // ~20°
        val resettleMillis: Long = 3_000,
        /** Huecos mayores entre muestras no se integran (sensor parado, lotes, reinicios). */
        val maxIntegrationGapMillis: Long = 100,
    )

    // Gesto en curso
    private var active = false
    private var startNanos = 0L
    private var peak = 0f
    private var maxOther = 0f
    private var reversed = false

    // Estado tras un evento: se conserva entre reinicios del sensor.
    private var armed = true
    private var cooldownUntilNanos = Long.MIN_VALUE
    private var restSinceNanos = NONE

    /** Giro acumulado en X (rad) desde que empezó el último gesto. */
    private var angle = 0f
    private var lastNanos = NONE

    /** Procesa una muestra; devuelve un evento cuando termina un movimiento relevante. */
    fun onSample(timestampNanos: Long, x: Float, y: Float, z: Float): GestureEvent? {
        // Marcas de tiempo que retroceden (p. ej. tras reproducir una serie sintética): se ignoran.
        if (lastNanos != NONE && timestampNanos <= lastNanos) return null
        val gapMillis = if (lastNanos == NONE) Long.MAX_VALUE else (timestampNanos - lastNanos) / 1_000_000
        if (gapMillis <= config.maxIntegrationGapMillis) {
            angle += x * ((timestampNanos - lastNanos) / 1e9f)
        }
        lastNanos = timestampNanos

        if (!active && !armed) {
            updateArming(timestampNanos, x, y, z)
            return null
        }

        val other = sqrt(y * y + z * z)
        if (!active) {
            if (abs(x) >= config.startThreshold) {
                active = true
                startNanos = timestampNanos
                peak = x
                maxOther = other
                reversed = false
                angle = 0f
            }
            return null
        }

        if (x * peak > 0 && abs(x) > abs(peak)) peak = x
        // Un giro fuerte en el sentido contrario dentro del mismo movimiento es una sacudida.
        if (x * peak < 0 && abs(x) >= config.startThreshold) reversed = true
        maxOther = max(maxOther, other)

        val elapsedMillis = (timestampNanos - startNanos) / 1_000_000
        return when {
            elapsedMillis > config.maxDurationMillis -> finish(timestampNanos, tooSlow = true)
            abs(x) < config.startThreshold / 2 -> finish(timestampNanos, tooSlow = false)
            else -> null
        }
    }

    /**
     * El sensor se ha detenido y vuelto a registrar (onPause/onResume). Se descarta un gesto a
     * medias, porque faltan muestras, pero se CONSERVAN la espera y el estado desarmado: el resto de
     * un movimiento empezado antes de la pausa no puede contar como gesto nuevo.
     */
    fun onSensorRestart() {
        if (active) {
            active = false
            armed = false
        }
        restSinceNanos = NONE
    }

    private fun updateArming(timestampNanos: Long, x: Float, y: Float, z: Float) {
        val still = sqrt(x * x + y * y + z * z) < config.restThreshold
        if (!still) {
            restSinceNanos = NONE
            return
        }
        if (restSinceNanos == NONE) restSinceNanos = timestampNanos
        val restedMillis = (timestampNanos - restSinceNanos) / 1_000_000
        val returned = abs(angle) <= config.returnToleranceRad
        val settled = restedMillis >= config.resettleMillis
        if (timestampNanos >= cooldownUntilNanos && restedMillis >= config.restMillis && (returned || settled)) {
            armed = true
            angle = 0f
        }
    }

    private fun finish(timestampNanos: Long, tooSlow: Boolean): GestureEvent {
        active = false
        armed = false
        restSinceNanos = NONE
        cooldownUntilNanos = timestampNanos + config.cooldownMillis * 1_000_000

        val strength = (abs(peak) / config.strongPeak).coerceAtMost(1f)
        val dominance = abs(peak) / sqrt(peak * peak + maxOther * maxOther)
        val confidence = strength * dominance
        val start = startNanos
        return when {
            tooSlow -> GestureEvent.Unclear(confidence, UnclearReason.TOO_SLOW, start, timestampNanos)
            reversed -> GestureEvent.Unclear(confidence, UnclearReason.BACK_AND_FORTH, start, timestampNanos)
            dominance < config.minDominance -> GestureEvent.Unclear(confidence, UnclearReason.AMBIGUOUS_AXIS, start, timestampNanos)
            confidence < MIN_GESTURE_CONFIDENCE -> GestureEvent.Unclear(confidence, UnclearReason.WEAK, start, timestampNanos)
            else -> GestureEvent.Recognized(
                gesture = if (peak > 0) WristGesture.TWIST_OUT else WristGesture.TWIST_IN,
                confidence = confidence,
                startNanos = start,
                endNanos = timestampNanos,
            )
        }
    }

    private companion object {
        const val NONE = Long.MIN_VALUE
    }
}
