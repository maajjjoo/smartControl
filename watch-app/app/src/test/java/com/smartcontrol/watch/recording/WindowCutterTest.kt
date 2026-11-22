package com.smartcontrol.watch.recording

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Recorte de ventanas con muestras SINTÉTICAS a 50 Hz. */
class WindowCutterTest {

    private val step = 20_000_000L
    private val base = 5_000_000_000L

    /** [count] muestras desde [base], con el valor de X dado por [x]. */
    private fun buffer(count: Int, capacity: Int = 512, x: (Int) -> Float = { 0f }): SampleBuffer =
        SampleBuffer(capacity).apply {
            for (i in 0 until count) add(TimedSample(base + i * step, x(i), 0f, 0f))
        }

    private fun at(i: Int) = base + i * step

    @Test
    fun cutsWithPrerollAndRelativeOffsets() {
        val result = WindowCutter.cut(buffer(60) { if (it in 20..25) 5f else 0f }, at(20), at(26))
        val rows = (result as CutResult.Window).rows
        // 200 ms antes del inicio = 10 muestras: desde la 10 hasta la 26.
        assertEquals(17, rows.size)
        assertEquals(0, rows.first().tOffsetMs)
        assertEquals(320, rows.last().tOffsetMs)
        assertEquals(5f, rows[10].x)
        assertTrue(rows.zipWithNext().all { (a, b) -> b.tOffsetMs > a.tOffsetMs })
    }

    @Test
    fun skipsWhenBufferLostTheBeginning() {
        // Capacidad 20: el principio del tramo (con su margen previo) ya no está.
        val result = WindowCutter.cut(buffer(60, capacity = 20), at(45), at(55))
        assertEquals(CutResult.Skipped("INCOMPLETE"), result)
        assertEquals(CutResult.Skipped("EMPTY_BUFFER"), WindowCutter.cut(SampleBuffer(), at(10), at(20)))
        assertEquals(CutResult.Skipped("NO_BOUNDS"), WindowCutter.cut(buffer(60), 0, 0))
    }

    @Test
    fun appliesFormatRules() {
        // Muestras cada 100 ms: de 100 a 400 ms (margen previo incluido) solo hay 4.
        val sparse = SampleBuffer().apply { for (i in 0 until 10) add(TimedSample(base + i * 100_000_000L, 0f, 0f, 0f)) }
        assertEquals(CutResult.Skipped("TOO_FEW_SAMPLES"), WindowCutter.cut(sparse, base + 300_000_000L, base + 400_000_000L))

        val outOfRange = WindowCutter.cut(buffer(60) { if (it == 22) 36f else 0f }, at(20), at(26))
        assertEquals(CutResult.Skipped("VALUE_OUT_OF_RANGE"), outOfRange)

        val nan = WindowCutter.cut(buffer(60) { if (it == 22) Float.NaN else 0f }, at(20), at(26))
        assertEquals(CutResult.Skipped("NON_FINITE_VALUE"), nan)

        val long = WindowCutter.cut(buffer(200), at(20), at(130))
        assertEquals(CutResult.Skipped("WINDOW_TOO_LONG"), long)
    }

    @Test
    fun skipsGapsAndDropsDuplicatedMilliseconds() {
        // Desde la muestra 20 todo llega 150 ms tarde: entre la 19 (380 ms) y la 20 (550 ms) hay 170 ms.
        // El tramo termina en at(30) = 600 ms para que el hueco quede dentro de la ventana.
        val gap = SampleBuffer().apply {
            for (i in 0 until 30) add(TimedSample(base + i * step + if (i >= 20) 150_000_000L else 0L, 0f, 0f, 0f))
        }
        assertEquals(CutResult.Skipped("GAP_TOO_LARGE"), WindowCutter.cut(gap, at(15), at(30)))

        val duplicated = SampleBuffer().apply {
            for (i in 0 until 40) {
                add(TimedSample(base + i * step, i.toFloat() / 10, 0f, 0f))
                add(TimedSample(base + i * step + 300_000L, 99f, 0f, 0f)) // 0,3 ms después: mismo milisegundo
            }
        }
        val rows = (WindowCutter.cut(duplicated, at(20), at(26)) as CutResult.Window).rows
        assertTrue(rows.none { it.x == 99f })
    }
}
