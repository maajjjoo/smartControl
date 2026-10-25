package com.smartcontrol.watch.gesture

/**
 * Series sintéticas de giroscopio (rad/s, una muestra cada [STEP_NANOS]) con la forma de cada
 * movimiento. Las usan las pruebas unitarias y, solo en depuración, la reproducción en el emulador
 * (cuya consola solo actualiza el sensor una vez por segundo y no puede imitar un giro rápido).
 * No son datos de un reloj real.
 */
object SyntheticGestures {
    const val STEP_NANOS = 20_000_000L // 50 Hz

    data class Sample(val x: Float, val y: Float, val z: Float)

    private val rest = Sample(0f, 0f, 0f)

    fun rest(millis: Long): List<Sample> = List((millis * 1_000_000 / STEP_NANOS).toInt()) { rest }

    /** Giro rápido en X SIN regreso: sube hasta [peak], se mantiene [holdSamples] muestras y para. */
    fun twist(peak: Float, holdSamples: Int = 4, other: Float = 0f): List<Sample> =
        List(3) { rest } +
            Sample(peak * 0.6f, other, 0f) +
            List(holdSamples) { Sample(peak, other, 0f) } +
            listOf(Sample(peak * 0.3f, 0f, 0f), rest)

    /**
     * Regreso de la muñeca a la posición inicial: deshace exactamente el giro de [twist] (misma
     * área), más despacio y en sentido contrario.
     */
    fun returnOf(twist: List<Sample>, speedFraction: Float = 0.6f): List<Sample> {
        val turned = twist.sumOf { it.x.toDouble() }.toFloat() // en muestras × rad/s
        val back = -turned.sign() * twist.maxOf { kotlin.math.abs(it.x) } * speedFraction
        val samples = kotlin.math.round(kotlin.math.abs(turned / back)).toInt()
        return List(samples) { Sample(back, 0f, 0f) } + rest
    }

    /** Giro natural: ida rápida, regreso a la posición inicial y reposo. */
    fun twistAndReturn(peak: Float, pauseMillis: Long = 0, restAfterMillis: Long = 300): List<Sample> {
        val out = twist(peak)
        return out + rest(pauseMillis) + returnOf(out) + rest(restAfterMillis)
    }

    /** Sacudida de ida y vuelta. */
    fun shake(peak: Float = 5.5f): List<Sample> =
        listOf(rest, Sample(peak, 0f, 0f), Sample(peak, 0f, 0f), Sample(-peak, 0f, 0f), Sample(-peak, 0f, 0f), rest)

    /** Rotación sostenida durante [millis]: demasiado lenta para ser un gesto. */
    fun slow(peak: Float = 4.5f, millis: Long = 1_000): List<Sample> =
        List((millis * 1_000_000 / STEP_NANOS).toInt()) { Sample(peak, 0f, 0f) } + rest

    /**
     * Nombre usado por el extra de depuración `replay_gesture`. "out" e "in" son giros naturales
     * (con regreso); "out_hold" gira hacia fuera y deja la muñeca girada; "return" es solo el regreso
     * de un giro hacia fuera (debe ignorarse si sigue a "out_hold").
     */
    fun byName(name: String): List<Sample>? = when (name) {
        // Con reposo final suficiente para superar la espera de 700 ms y quedar rearmado.
        "out" -> twistAndReturn(5.5f, restAfterMillis = 800)
        "in" -> twistAndReturn(-5.5f, restAfterMillis = 800)
        "out_hold" -> twist(5.5f)
        "return" -> returnOf(twist(5.5f)) + rest(300)
        "weak" -> twist(3.0f)
        "shake" -> shake()
        "slow" -> slow()
        "cross" -> twist(5.5f, other = 5.0f)
        else -> null
    }

    private fun Float.sign(): Float = if (this > 0) 1f else if (this < 0) -1f else 0f
}
