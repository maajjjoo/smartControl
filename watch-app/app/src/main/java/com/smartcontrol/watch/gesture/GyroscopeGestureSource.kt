package com.smartcontrol.watch.gesture

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.util.Log
import com.smartcontrol.watch.BuildConfig
import com.smartcontrol.watch.recording.CutResult
import com.smartcontrol.watch.recording.SampleBuffer
import com.smartcontrol.watch.recording.TimedSample
import com.smartcontrol.watch.recording.WindowCutter
import com.smartcontrol.watch.recording.WindowRow

/**
 * Un movimiento segmentado por las reglas del reloj, con las lecturas del giroscopio del tramo
 * ([window], o null si no se pudo recortar). Windows usa la ventana para que decida la IA.
 */
data class Movement(val event: GestureEvent, val window: List<WindowRow>?)

/**
 * Conecta el giroscopio del reloj con [WristGestureDetector]. Solo escucha entre [start] y [stop]
 * (la actividad lo hace en onResume/onPause), para no gastar batería en segundo plano (RNF-10).
 * Guarda las últimas lecturas para recortar la ventana de cada movimiento.
 * Los eventos llegan en el hilo principal.
 */
class GyroscopeGestureSource(
    context: Context,
    private val onEvent: (Movement) -> Unit,
) : SensorEventListener {

    private val sensorManager = context.getSystemService(SensorManager::class.java)
    private val gyroscope: Sensor? = sensorManager?.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val detector = WristGestureDetector()
    private val recent = SampleBuffer()

    /** false si el reloj no tiene giroscopio: la interfaz debe decirlo en vez de fingir gestos. */
    val isAvailable: Boolean get() = gyroscope != null

    fun start() {
        val sensor = gyroscope ?: return
        // Descarta un gesto a medias pero conserva la espera y el estado desarmado.
        detector.onSensorRestart()
        recent.clear()
        // SENSOR_DELAY_GAME (~50 Hz) basta para giros rápidos sin pedir permisos de alta frecuencia.
        sensorManager?.registerListener(this, sensor, SensorManager.SENSOR_DELAY_GAME)
    }

    fun stop() {
        sensorManager?.unregisterListener(this)
    }

    override fun onSensorChanged(event: SensorEvent) {
        val (x, y, z) = event.values
        lastTimestampNanos = event.timestamp
        if (traceSamples) Log.v(TRACE_TAG, "t=${event.timestamp / 1_000_000} x=$x y=$y z=$z")
        onSample(event.timestamp, x, y, z)
    }

    private fun onSample(timestampNanos: Long, x: Float, y: Float, z: Float) {
        recent.add(TimedSample(timestampNanos, x, y, z))
        detector.onSample(timestampNanos, x, y, z)?.let { event ->
            val window = (WindowCutter.cut(recent, event.startNanos, event.endNanos) as? CutResult.Window)?.rows
            onEvent(Movement(event, window))
        }
    }

    private var lastTimestampNanos = 0L

    /**
     * Solo para depuración: pasa una serie sintética por el MISMO reconocedor, como si viniera del
     * sensor, a continuación de la última muestra real. Se ejecuta de una vez en el hilo principal,
     * así que no se mezcla con muestras reales; las muestras reales con marca de tiempo anterior al
     * final de la serie se ignoran hasta alcanzarla.
     */
    fun replay(samples: List<SyntheticGestures.Sample>) {
        if (!BuildConfig.DEBUG) return
        val start = lastTimestampNanos + SyntheticGestures.STEP_NANOS
        samples.forEachIndexed { i, s ->
            onSample(start + i * SyntheticGestures.STEP_NANOS, s.x, s.y, s.z)
        }
        lastTimestampNanos = start + samples.size * SyntheticGestures.STEP_NANOS
    }

    /**
     * Traza de muestras para calibrar, solo en depuración y desactivada por defecto:
     *   adb shell setprop log.tag.SmartControlSensor VERBOSE   (y reabrir la app)
     */
    private val traceSamples = BuildConfig.DEBUG && Log.isLoggable(TRACE_TAG, Log.VERBOSE)

    private companion object {
        const val TRACE_TAG = "SmartControlSensor"
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit
}
