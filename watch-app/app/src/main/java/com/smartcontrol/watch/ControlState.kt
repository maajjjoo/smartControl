package com.smartcontrol.watch

import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.MIN_GESTURE_CONFIDENCE
import com.smartcontrol.watch.gesture.UnclearReason
import com.smartcontrol.watch.gesture.WristGesture
import com.smartcontrol.watch.link.CommandOutcome

/** Modos de control, con los mismos identificadores que el protocolo. */
enum class ControlMode(val label: String) {
    PRESENTATION("Presentación"),
    MULTIMEDIA("Multimedia"),
}

/** Comandos permitidos por modo (lista blanca de docs/security-allowlist.md). */
enum class Command(val mode: ControlMode, val label: String) {
    SLIDE_NEXT(ControlMode.PRESENTATION, "Siguiente"),
    SLIDE_PREVIOUS(ControlMode.PRESENTATION, "Anterior"),
    MEDIA_PLAY(ControlMode.MULTIMEDIA, "Reproducir"),
    MEDIA_PAUSE(ControlMode.MULTIMEDIA, "Pausar"),
    MEDIA_NEXT_TRACK(ControlMode.MULTIMEDIA, "Siguiente canción"),
    MEDIA_PREVIOUS_TRACK(ControlMode.MULTIMEDIA, "Canción anterior");

    companion object {
        fun forMode(mode: ControlMode): List<Command> = entries.filter { it.mode == mode }
    }
}

/**
 * Si hay una conexión con Windows lista para recibir comandos. Lo decide la conexión real
 * (WindowsLink): la interfaz nunca muestra una conexión que no existe.
 */
enum class ConnectionStatus(val label: String) {
    DISCONNECTED("Desconectado"),
    CONNECTED("Conectado"),
}

/** Motivo de rechazo. [label] es el texto para el usuario; el nombre del enum va solo al registro. */
enum class RejectReason(val label: String) {
    /** Equivale a REJECTED_NOT_ALLOWED: el comando no pertenece al modo activo. */
    WRONG_MODE("No disponible en este modo"),
    NO_CONNECTION("Windows no está conectado"),
}

/**
 * Motivo por el que Windows no realizó un comando, ya traducido para el usuario. [code] es el
 * código de Windows y solo va al registro de diagnóstico.
 */
data class RemoteFailure(val code: String, val label: String) {
    companion object {
        fun from(outcome: CommandOutcome): RemoteFailure = when (outcome) {
            is CommandOutcome.Failed -> RemoteFailure(outcome.reasonCode, labelFor(outcome.reasonCode))
            CommandOutcome.NoResponse -> RemoteFailure("NO_RESPONSE", "Windows no respondió")
            CommandOutcome.NotConnected -> RemoteFailure("NOT_CONNECTED", "Se perdió la conexión")
            CommandOutcome.Executed -> RemoteFailure("OK", "")
        }

        private fun labelFor(code: String): String = when (code) {
            "NO_MEDIA_SESSION" -> "Abre la música en el equipo"
            "MEDIA_COMMAND_REFUSED" -> "El reproductor no respondió"
            "INPUT_BLOCKED" -> "Windows bloqueó la tecla"
            "UNAUTHORIZED_DEVICE", "INVALID_SESSION_TOKEN" -> "Hay que volver a vincular"
            "STALE_MESSAGE", "FUTURE_TIMESTAMP" -> "Llegó tarde; inténtalo otra vez"
            "MODE_ACTION_MISMATCH", "RECEIVER_MODE_MISMATCH", "ACTION_NOT_IN_ALLOWLIST" -> "No disponible en este modo"
            "BELOW_THRESHOLD" -> "Gesto poco claro"
            else -> "Windows no pudo hacerlo"
        }
    }
}

/** Comando enviado a Windows que espera su respuesta. */
data class PendingCommand(val command: Command, val sequence: Int)

/** Resultado de la última acción. Nada sale del reloj. */
sealed interface CommandResult {
    /** Validado contra la lista blanca y ejecutado solo en modo simulación (pruebas sin Windows). */
    data class Simulated(val command: Command, val sequence: Int) : CommandResult

    /** Windows confirmó que realizó la acción. */
    data class Executed(val command: Command, val sequence: Int) : CommandResult

    /** Windows no lo realizó, o no respondió. */
    data class Failed(val command: Command, val failure: RemoteFailure) : CommandResult
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

    /** Respuesta de Windows al comando [sequence]. Una respuesta de otro comando se ignora. */
    fun complete(sequence: Int, outcome: CommandOutcome): ControlState {
        val sent = pending?.takeIf { it.sequence == sequence } ?: return this
        val result = if (outcome == CommandOutcome.Executed) {
            CommandResult.Executed(sent.command, sequence)
        } else {
            CommandResult.Failed(sent.command, RemoteFailure.from(outcome))
        }
        return copy(pending = null, lastResult = result)
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
