package com.smartcontrol.watch

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import android.content.Context
import android.content.res.Configuration
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.toArgb
import androidx.lifecycle.lifecycleScope
import com.smartcontrol.watch.gesture.Movement
import com.smartcontrol.watch.gesture.GyroscopeGestureSource
import com.smartcontrol.watch.gesture.SyntheticGestures
import com.smartcontrol.watch.link.WindowsLink
import com.smartcontrol.watch.settings.AppSettings
import com.smartcontrol.watch.settings.ThemeChoice
import com.smartcontrol.watch.ui.DarkPalette
import com.smartcontrol.watch.ui.LightPalette
import com.smartcontrol.watch.ui.SmartControlApp
import kotlinx.coroutines.flow.MutableSharedFlow

class MainActivity : ComponentActivity() {
    private val gestureEvents = MutableSharedFlow<Movement>(extraBufferCapacity = 8)
    private lateinit var gestures: GyroscopeGestureSource
    private lateinit var windows: WindowsLink
    private val settings by lazy { AppSettings(this) }
    private var theme by mutableStateOf(ThemeChoice.SYSTEM)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        theme = settings.theme
        paintWindowBackground()
        gestures = GyroscopeGestureSource(this) { movement ->
            CommandLog.record(movement.event)
            gestureEvents.tryEmit(movement)
        }
        windows = WindowsLink(this, lifecycleScope)
        setContent {
            SmartControlApp(
                simulationEnabled = simulationFromIntent(),
                gestureEvents = gestureEvents,
                gesturesAvailable = gestures.isAvailable,
                linkState = windows.state,
                sendToWindows = windows::send,
                sendWindowToWindows = windows::sendWindow,
                themeChoice = theme,
                onThemeChange = { choice ->
                    settings.theme = choice
                    theme = choice
                    paintWindowBackground()
                },
                languageChoice = settings.language,
                onLanguageChange = { choice -> settings.language = choice },
            )
        }
    }

    /** Fondo de la ventana (se ve un instante al abrir o girar la app), igual que el tema elegido. */
    private fun paintWindowBackground() {
        val systemDark = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        val dark = theme == ThemeChoice.DARK || (theme == ThemeChoice.SYSTEM && systemDark)
        window.decorView.setBackgroundColor((if (dark) DarkPalette else LightPalette).background.toArgb())
    }

    // La conexión con Windows se busca y se mantiene sola mientras la app está visible.
    override fun onStart() {
        super.onStart()
        windows.start()
    }

    override fun onStop() {
        windows.stop()
        super.onStop()
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
     * Sin conexión, los comandos se rechazan («Windows no está conectado»). Solo en depuración se
     * puede activar una simulación para probar la interfaz sin Windows:
     *   adb shell am start -n com.smartcontrol.watch/.MainActivity --ez simulation true
     * En release el extra se ignora.
     */
    private fun simulationFromIntent(): Boolean =
        BuildConfig.DEBUG && intent.getBooleanExtra(EXTRA_SIMULATION, false)

    private companion object {
        const val EXTRA_SIMULATION = "simulation"
        const val EXTRA_REPLAY_GESTURE = "replay_gesture"
    }
}
