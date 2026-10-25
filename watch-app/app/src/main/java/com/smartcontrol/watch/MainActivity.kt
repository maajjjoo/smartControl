package com.smartcontrol.watch

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.ui.graphics.toArgb
import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.GyroscopeGestureSource
import com.smartcontrol.watch.gesture.SyntheticGestures
import com.smartcontrol.watch.ui.SmartColors
import com.smartcontrol.watch.ui.SmartControlApp
import kotlinx.coroutines.flow.MutableSharedFlow

class MainActivity : ComponentActivity() {
    private val gestureEvents = MutableSharedFlow<GestureEvent>(extraBufferCapacity = 8)
    private lateinit var gestures: GyroscopeGestureSource

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.decorView.setBackgroundColor(SmartColors.Background.toArgb())
        gestures = GyroscopeGestureSource(this) { event ->
            CommandLog.record(event)
            gestureEvents.tryEmit(event)
        }
        setContent {
            SmartControlApp(
                simulationEnabled = simulationFromIntent(),
                gestureEvents = gestureEvents,
                gesturesAvailable = gestures.isAvailable,
            )
        }
    }

    // El giroscopio solo se escucha con la app en primer plano.
    override fun onResume() {
        super.onResume()
        gestures.start()
    }

    override fun onPause() {
        gestures.stop()
        super.onPause()
    }

    /**
     * Solo en depuración: reproduce un movimiento sintético a través del reconocedor real,
     * porque la consola del emulador no puede imitar un giro rápido. Con la app abierta:
     *   adb shell am start -n com.smartcontrol.watch/.MainActivity -f 0x20000000 --es replay_gesture out
     * Valores: out, in, weak, shake, slow, cross. En release se ignora.
     */
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        if (!BuildConfig.DEBUG) return
        intent.getStringExtra(EXTRA_REPLAY_GESTURE)
            ?.let(SyntheticGestures::byName)
            ?.let(gestures::replay)
    }

    /**
     * La simulación no se controla desde la interfaz. Solo en depuración se puede desactivar al
     * abrir la app, para probar el rechazo sin conexión:
     *   adb shell am start -n com.smartcontrol.watch/.MainActivity --ez simulation false
     * En release el extra se ignora.
     */
    private fun simulationFromIntent(): Boolean =
        if (BuildConfig.DEBUG) intent.getBooleanExtra(EXTRA_SIMULATION, true) else true

    private companion object {
        const val EXTRA_SIMULATION = "simulation"
        const val EXTRA_REPLAY_GESTURE = "replay_gesture"
    }
}
