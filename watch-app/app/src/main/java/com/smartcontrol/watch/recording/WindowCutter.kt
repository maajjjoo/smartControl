package com.smartcontrol.watch.recording

import kotlin.math.abs

/** Muestra de giroscopio con su marca de tiempo del sensor (ns). */
data class TimedSample(val timestampNanos: Long, val x: Float, val y: Float, val z: Float)

/**
 * Búfer circular con las últimas muestras del giroscopio, para poder recortar la ventana de un
 * movimiento cuando el detector ya lo ha dado por terminado. La ventana se envía a Windows, donde
 * decide la IA, y también servirá para grabar datos de entrenamiento del equipo.
 */
class SampleBuffer(private val capacity: Int = 512) {
    private val samples = ArrayDeque<TimedSample>(capacity)

    fun add(sample: TimedSample) {
        if (samples.size == capacity) samples.removeFirst()
        samples.addLast(sample)
    }

    fun clear() = samples.clear()

    val oldestNanos: Long? get() = samples.firstOrNull()?.timestampNanos

    fun between(fromNanos: Long, toNanos: Long): List<TimedSample> =
        samples.filter { it.timestampNanos in fromNanos..toNanos }
}

/** Fila del formato sensor-data/v1: [tOffsetMs, x, y, z]. */
data class WindowRow(val tOffsetMs: Int, val x: Float, val y: Float, val z: Float)

sealed interface CutResult {
    data class Window(val rows: List<WindowRow>) : CutResult
    data class Skipped(val reason: String) : CutResult
}

/**
 * Recorta la ventana de un movimiento con las reglas de docs/recording-format.md §3 (las mismas
 * que SensorWindow en Windows). Si no las cumple, la ventana se descarta: nunca se corrigen ni se
 * recortan valores, para no alterar los datos.
 */
object WindowCutter {
    const val PREROLL_MILLIS = 200L
    const val MIN_SAMPLES = 8
    const val MAX_SAMPLES = 256
    const val MAX_DURATION_MS = 2000
    const val MAX_GAP_MS = 100
    const val MAX_GYROSCOPE_ABS = 35f

    fun cut(buffer: SampleBuffer, startNanos: Long, endNanos: Long): CutResult {
        if (startNanos <= 0 || endNanos < startNanos) return CutResult.Skipped("NO_BOUNDS")
        val from = startNanos - PREROLL_MILLIS * 1_000_000
        // Si el búfer ya no conserva el principio, la ventana estaría incompleta.
        val oldest = buffer.oldestNanos ?: return CutResult.Skipped("EMPTY_BUFFER")
        if (oldest > from) return CutResult.Skipped("INCOMPLETE")

        val samples = buffer.between(from, endNanos)
        if (samples.isEmpty()) return CutResult.Skipped("TOO_FEW_SAMPLES")
        val t0 = samples.first().timestampNanos
        val rows = ArrayList<WindowRow>(samples.size)
        for (sample in samples) {
            val offset = ((sample.timestampNanos - t0) / 1_000_000).toInt()
            // Dos muestras en el mismo milisegundo: se conserva la primera (el tiempo debe crecer).
            if (rows.isNotEmpty() && offset <= rows.last().tOffsetMs) continue
            if (rows.isNotEmpty() && offset - rows.last().tOffsetMs > MAX_GAP_MS) return CutResult.Skipped("GAP_TOO_LARGE")
            if (!sample.x.isFinite() || !sample.y.isFinite() || !sample.z.isFinite()) return CutResult.Skipped("NON_FINITE_VALUE")
            if (abs(sample.x) > MAX_GYROSCOPE_ABS || abs(sample.y) > MAX_GYROSCOPE_ABS || abs(sample.z) > MAX_GYROSCOPE_ABS) {
                return CutResult.Skipped("VALUE_OUT_OF_RANGE")
            }
            rows.add(WindowRow(offset, sample.x, sample.y, sample.z))
        }
        return when {
            rows.size < MIN_SAMPLES -> CutResult.Skipped("TOO_FEW_SAMPLES")
            rows.size > MAX_SAMPLES -> CutResult.Skipped("TOO_MANY_SAMPLES")
            rows.last().tOffsetMs > MAX_DURATION_MS -> CutResult.Skipped("WINDOW_TOO_LONG")
            else -> CutResult.Window(rows)
        }
    }
}
