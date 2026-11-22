package com.smartcontrol.watch

import androidx.annotation.StringRes
import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.MIN_GESTURE_CONFIDENCE
import com.smartcontrol.watch.gesture.UnclearReason
import com.smartcontrol.watch.gesture.WristGesture
import com.smartcontrol.watch.link.CommandOutcome

/** Modos de control, con los mismos identificadores que el protocolo. */
/** [label] es el texto que ve el usuario, en su idioma (strings.xml de res/values y res/values-es). */
enum class ControlMode(@StringRes val label: Int) {
    PRESENTATION(R.string.mode_presentation),
    MULTIMEDIA(R.string.mode_multimedia),
}

/** Comandos permitidos por modo (lista blanca de docs/security-allowlist.md). */
enum class Command(val mode: ControlMode, @StringRes val label: Int) {
    SLIDE_NEXT(ControlMode.PRESENTATION, R.string.cmd_slide_next),
    SLIDE_PREVIOUS(ControlMode.PRESENTATION, R.string.cmd_slide_previous),
    MEDIA_PLAY(ControlMode.MULTIMEDIA, R.string.cmd_media_play),
    MEDIA_PAUSE(ControlMode.MULTIMEDIA, R.string.cmd_media_pause),
    MEDIA_NEXT_TRACK(ControlMode.MULTIMEDIA, R.string.cmd_media_next_track),
    MEDIA_PREVIOUS_TRACK(ControlMode.MULTIMEDIA, R.string.cmd_media_previous_track);

    companion object {
        fun forMode(mode: ControlMode): List<Command> = entries.filter { it.mode == mode }
    }
}

/**
 * Si hay una conexión con Windows lista para recibir comandos. Lo decide la conexión real
 * (WindowsLink): la interfaz nunca muestra una conexión que no existe.
 */
enum class ConnectionStatus(@StringRes val label: Int) {
    DISCONNECTED(R.string.conn_disconnected),
    CONNECTED(R.string.conn_connected),
}

/** Motivo de rechazo. [label] es el texto para el usuario (en su idioma); el nombre del enum va solo al registro. */
enum class RejectReason(@StringRes val label: Int) {
    /** Equivale a REJECTED_NOT_ALLOWED: el comando no pertenece al modo activo. */
    WRONG_MODE(R.string.reject_wrong_mode),
    NO_CONNECTION(R.string.reject_no_connection),
}

/**
 * Motivo por el que Windows no realizó un comando, ya traducido para el usuario. [code] es el
 * código de Windows y solo va al registro de diagnóstico.
 */
data class RemoteFailure(val code: String, @StringRes val label: Int) {
    companion object {
        fun from(outcome: CommandOutcome): RemoteFailure = when (outcome) {
            is CommandOutcome.Failed -> RemoteFailure(outcome.reasonCode, labelFor(outcome.reasonCode))
            CommandOutcome.NoResponse -> RemoteFailure("NO_RESPONSE", R.string.fail_no_response)
            CommandOutcome.NotConnected -> RemoteFailure("NOT_CONNECTED", R.string.fail_not_connected)
            is CommandOutcome.Executed -> RemoteFailure("OK", R.string.fail_generic)
        }

        @StringRes
        private fun labelFor(code: String): Int = when (code) {
            "NO_MEDIA_SESSION" -> R.string.fail_no_media
            "MEDIA_COMMAND_REFUSED" -> R.string.fail_media_refused
            "INPUT_BLOCKED" -> R.string.fail_input_blocked
            "UNAUTHORIZED_DEVICE", "INVALID_SESSION_TOKEN" -> R.string.fail_unauthorized
            "STALE_MESSAGE", "FUTURE_TIMESTAMP" -> R.string.fail_late
            "MODE_ACTION_MISMATCH", "RECEIVER_MODE_MISMATCH", "ACTION_NOT_IN_ALLOWLIST" -> R.string.reject_wrong_mode
            "BELOW_THRESHOLD" -> R.string.fail_unclear
            "NO_GESTURE" -> R.string.fail_ai_no_gesture
            "LOW_CONFIDENCE" -> R.string.fail_ai_unsure
            else -> R.string.fail_generic
        }
    }
}

/**
 * Lo enviado a Windows que espera respuesta: un comando concreto, o un movimiento ([command] null)
 * cuyo gesto decide la IA de Windows.
 */
data class PendingCommand(val command: Command?, val sequence: Int)

/** Quién reconoció el gesto en Windows: la IA (con su confianza) o las reglas del reloj. */
data class Recognition(val byAi: Boolean, val confidence: Double?)

/** Resultado de la última acción. Nada sale del reloj. */
sealed interface CommandResult {
    /** Validado contra la lista blanca y ejecutado solo en modo simulación (pruebas sin Windows). */
    data class Simulated(val command: Command, val sequence: Int) : CommandResult

    /** Windows confirmó que realizó la acción. [recognition] dice quién reconoció el gesto, si fue un movimiento. */
    data class Executed(val command: Command, val sequence: Int, val recognition: Recognition? = null) : CommandResult

    /** Windows no lo realizó, o no respondió. [command] es null si era un movimiento sin acción decidida. */
    data class Failed(val command: Command?, val failure: RemoteFailure) : CommandResult

    /** Windows (su IA o las reglas) decidió que el movimiento no era un gesto: no se hizo nada. */
    data class MovementIgnored(val failure: RemoteFailure) : CommandResult
    data class Rejected(val command: Command, val reason: RejectReason) : CommandResult

    /** Equivale a REJECTED_LOW_CONFIDENCE: el movimiento no se reconoció como gesto; no se envía nada. */
    data class GestureUnrecognized(val confidence: Float, val reason: UnclearReason) : CommandResult
}

/**
 * Gesto que produce [command] (los toques en la guía equivalen a ese gesto), o null si el
 * comando no tiene gesto: reproducir y pausar se tocan en la pantalla.
 */
fun gestureFor(command: Command): WristGesture? = when (command) {
    Command.SLIDE_NEXT, Command.MEDIA_NEXT_TRACK -> WristGesture.TWIST_OUT
    Command.SLIDE_PREVIOUS, Command.MEDIA_PREVIOUS_TRACK -> WristGesture.TWIST_IN
    Command.MEDIA_PLAY, Command.MEDIA_PAUSE -> null
}

/** Identificador que acompaña a un comando tocado en la pantalla en lugar de hecho con un gesto. */
const val SCREEN_TAP_ID = "SCREEN_TAP"

/** Qué comando produce cada gesto según el modo activo (RF-04, RF-IA-03). */
fun commandFor(mode: ControlMode, gesture: WristGesture): Command = when (mode) {
    ControlMode.PRESENTATION -> when (gesture) {
        WristGesture.TWIST_OUT -> Command.SLIDE_NEXT
        WristGesture.TWIST_IN -> Command.SLIDE_PREVIOUS
    }
    // En Multimedia los giros cambian de canción; reproducir y pausar son botones de la pantalla.
    ControlMode.MULTIMEDIA -> when (gesture) {
        WristGesture.TWIST_OUT -> Command.MEDIA_NEXT_TRACK
        WristGesture.TWIST_IN -> Command.MEDIA_PREVIOUS_TRACK
    }
}

data class ControlState(
    val mode: ControlMode = ControlMode.PRESENTATION,
    val connection: ConnectionStatus = ConnectionStatus.DISCONNECTED,
    /** Permite probar los comandos sin conexión real. No se muestra en la interfaz (ver MainActivity). */
    val simulationEnabled: Boolean = true,
    val lastResult: CommandResult? = null,
    val sentCount: Int = 0,
    /** Comando enviado a Windows que todavía no tiene respuesta. */
    val pending: PendingCommand? = null,
) {
    val availableCommands: List<Command> get() = Command.forMode(mode)

    fun selectMode(newMode: ControlMode): ControlState =
        if (newMode == mode) this else copy(mode = newMode, lastResult = null)

    fun setSimulation(enabled: Boolean): ControlState =
        if (enabled == simulationEnabled) this else copy(simulationEnabled = enabled, lastResult = null)

    fun setConnection(status: ConnectionStatus): ControlState =
        if (status == connection) this else copy(connection = status)

    /**
     * Valida el comando. Primero se comprueba la lista blanca del modo activo. Con conexión, el
     * comando queda en [pending] para enviarlo a Windows (la interfaz lo envía y llama a
     * [complete]); mientras hay uno pendiente no se envía otro. Sin conexión, solo se acepta si la
     * simulación de pruebas está activada.
     */
    fun sendCommand(command: Command): ControlState = when {
        command.mode != mode ->
            copy(lastResult = CommandResult.Rejected(command, RejectReason.WRONG_MODE))
        connection == ConnectionStatus.CONNECTED ->
            if (pending != null) this else copy(sentCount = sentCount + 1, pending = PendingCommand(command, sentCount + 1))
        !simulationEnabled ->
            copy(lastResult = CommandResult.Rejected(command, RejectReason.NO_CONNECTION))
        else -> {
            val next = sentCount + 1
            copy(sentCount = next, lastResult = CommandResult.Simulated(command, next))
        }
    }

    /**
     * Un movimiento con su ventana, cuando hay conexión: se envía a Windows y su IA decide el gesto
     * (o, sin modelo entrenado, lo que dijeron las reglas). Sin conexión se valida aquí como antes.
     */
    fun sendMovement(event: GestureEvent): ControlState = when {
        connection != ConnectionStatus.CONNECTED -> onGesture(event)
        pending != null -> this
        else -> copy(sentCount = sentCount + 1, pending = PendingCommand(null, sentCount + 1))
    }

    /** Respuesta de Windows al envío [sequence]. Una respuesta de otro envío se ignora. */
    fun complete(sequence: Int, outcome: CommandOutcome): ControlState {
        val sent = pending?.takeIf { it.sequence == sequence } ?: return this
        val result = when (outcome) {
            is CommandOutcome.Executed -> {
                val command = sent.command ?: Command.entries.firstOrNull { it.name == outcome.action }
                val recognition = outcome.recognizedBy?.let { Recognition(it == "AI", outcome.confidence) }
                if (command != null) {
                    CommandResult.Executed(command, sequence, recognition)
                } else {
                    CommandResult.Failed(null, RemoteFailure("UNKNOWN_ACTION", R.string.fail_generic))
                }
            }
            is CommandOutcome.Failed ->
                if (sent.command == null && outcome.reasonCode in NOT_A_GESTURE) {
                    CommandResult.MovementIgnored(RemoteFailure.from(outcome))
                } else {
                    CommandResult.Failed(sent.command, RemoteFailure.from(outcome))
                }
            else -> CommandResult.Failed(sent.command, RemoteFailure.from(outcome))
        }
        return copy(pending = null, lastResult = result)
    }

    private companion object {
        /** Motivos con los que Windows dice que un movimiento no era un gesto. */
        val NOT_A_GESTURE = setOf("NO_GESTURE", "LOW_CONFIDENCE", "BELOW_THRESHOLD")
    }

    /**
     * Interpreta un gesto. Si no se reconoce o no llega al umbral de confianza, no se genera
     * ningún comando. Si se reconoce, el comando pasa por la misma validación que [sendCommand].
     * El umbral se vuelve a comprobar aquí (mismo [MIN_GESTURE_CONFIDENCE] que el reconocedor) por
     * si el evento llega de otra fuente.
     */
    fun onGesture(event: GestureEvent): ControlState = when (event) {
        is GestureEvent.Unclear ->
            copy(lastResult = CommandResult.GestureUnrecognized(event.confidence, event.reason))
        is GestureEvent.Recognized ->
            if (event.confidence < MIN_GESTURE_CONFIDENCE) {
                copy(lastResult = CommandResult.GestureUnrecognized(event.confidence, UnclearReason.WEAK))
            } else {
                sendCommand(commandFor(mode, event.gesture))
            }
    }
}
