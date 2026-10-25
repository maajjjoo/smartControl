package com.smartcontrol.watch

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.ui.graphics.toArgb
import com.smartcontrol.watch.ui.SmartColors
import com.smartcontrol.watch.ui.SmartControlApp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.decorView.setBackgroundColor(SmartColors.Background.toArgb())
        setContent { SmartControlApp(simulationEnabled = simulationFromIntent()) }
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
    }
}
