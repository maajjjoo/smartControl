package com.smartcontrol.watch

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
    MEDIA_PAUSE(ControlMode.MULTIMEDIA, "Pausar");

    companion object {
        fun forMode(mode: ControlMode): List<Command> = entries.filter { it.mode == mode }
    }
}

/**
 * Estado de la conexión con Windows. Todavía no existe transporte (ni Bluetooth ni red),
 * así que siempre es DISCONNECTED: la interfaz nunca debe mostrar una conexión que no existe.
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

/** Resultado de la última acción. Nada sale del reloj. */
sealed interface CommandResult {
    /** Validado contra la lista blanca y ejecutado solo en modo simulación. */
    data class Simulated(val command: Command, val sequence: Int) : CommandResult
    data class Rejected(val command: Command, val reason: RejectReason) : CommandResult
}

data class ControlState(
    val mode: ControlMode = ControlMode.PRESENTATION,
    val connection: ConnectionStatus = ConnectionStatus.DISCONNECTED,
    /** Permite probar los comandos sin conexión real. No se muestra en la interfaz (ver MainActivity). */
    val simulationEnabled: Boolean = true,
    val lastResult: CommandResult? = null,
    val sentCount: Int = 0,
) {
    val availableCommands: List<Command> get() = Command.forMode(mode)

    fun selectMode(newMode: ControlMode): ControlState =
        if (newMode == mode) this else copy(mode = newMode, lastResult = null)

    fun setSimulation(enabled: Boolean): ControlState =
        if (enabled == simulationEnabled) this else copy(simulationEnabled = enabled, lastResult = null)

    /**
     * Valida el comando y lo simula. Primero se comprueba la lista blanca del modo activo;
     * después, sin conexión real, solo se acepta si la simulación está activada.
     */
    fun sendCommand(command: Command): ControlState = when {
        command.mode != mode ->
            copy(lastResult = CommandResult.Rejected(command, RejectReason.WRONG_MODE))
        !simulationEnabled ->
            copy(lastResult = CommandResult.Rejected(command, RejectReason.NO_CONNECTION))
        else -> {
            val next = sentCount + 1
            copy(sentCount = next, lastResult = CommandResult.Simulated(command, next))
        }
    }

}
