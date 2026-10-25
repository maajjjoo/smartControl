package com.smartcontrol.watch

import android.util.Log
import com.smartcontrol.watch.gesture.GestureEvent

/**
 * Diagnóstico para desarrolladores: los detalles técnicos (identificador del gesto y del comando,
 * confianza, número de secuencia, motivo interno) van a logcat y nunca a la interfaz. Solo se
 * registra en compilaciones de depuración. Filtrar con: adb logcat -s SmartControl
 */
object CommandLog {
    private const val TAG = "SmartControl"

    fun record(state: ControlState) {
        if (!BuildConfig.DEBUG) return
        val detail = when (val result = state.lastResult) {
            null -> return
            is CommandResult.Simulated -> "command=${result.command.name} -> SIMULATED seq=${result.sequence}"
            is CommandResult.Rejected -> "command=${result.command.name} -> REJECTED reason=${result.reason.name}"
            is CommandResult.GestureUnrecognized ->
                "no command -> REJECTED_LOW_CONFIDENCE reason=${result.reason.name} confidence=${"%.2f".format(result.confidence)}"
        }
        Log.d(
            TAG,
            "$detail mode=${state.mode.name} simulation=${state.simulationEnabled} connection=${state.connection.name}",
        )
    }

    fun record(event: GestureEvent) {
        if (!BuildConfig.DEBUG) return
        val text = when (event) {
            is GestureEvent.Recognized -> "gesture=${event.gesture.id} confidence=${"%.2f".format(event.confidence)}"
            is GestureEvent.Unclear -> "gesture=UNCLEAR reason=${event.reason.name} confidence=${"%.2f".format(event.confidence)}"
        }
        Log.d(TAG, text)
    }
}
