package com.smartcontrol.watch.link

import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeoutOrNull
import java.io.BufferedReader
import java.io.IOException
import java.io.OutputStream
import java.util.concurrent.ConcurrentHashMap

/** Estado de la conexión con Windows que ve la interfaz. */
sealed interface LinkState {
    /** El reloj no tiene Wi-Fi: sin ella no puede llegar al equipo. */
    data object NoWifi : LinkState

    /** Buscando un equipo con SmartControl en la red. */
    data object Searching : LinkState

    /** Esperando a que alguien pulse «Vincular» en Windows; los dos muestran [code]. */
    data class Pairing(val code: String, val pcName: String) : LinkState

    /** Listo: los gestos llegan a [pcName]. */
    data class Connected(val pcName: String) : LinkState

    /** En Windows se rechazó la vinculación (o caducó). Se vuelve a buscar en unos segundos. */
    data class PairingRefused(val pcName: String) : LinkState
}

/** Qué pasó con un comando enviado a Windows. */
sealed interface CommandOutcome {
    /** Windows lo validó y realizó la acción. */
    data object Executed : CommandOutcome

    /** Windows lo rechazó o no pudo realizarlo; [reasonCode] es el motivo de Windows. */
    data class Failed(val reasonCode: String) : CommandOutcome

    /** No hay conexión con Windows: no se envió nada. */
    data object NotConnected : CommandOutcome

    /** Se envió, pero Windows no respondió a tiempo. */
    data object NoResponse : CommandOutcome
}

/** Lo que el reloj recuerda de su vinculación. */
interface PairingStorage {
    /** Identificador anónimo de este reloj, generado una vez. */
    val deviceId: String

    /** Clave para [pcId], o null si este reloj no está vinculado con ese equipo. */
    fun tokenFor(pcId: String): String?

    fun save(pcId: String, pcName: String, token: String)

    fun forget(pcId: String)

    /** Equipo con el que está vinculado, para preferirlo si hay varios en la red. */
    val pairedPcId: String?
}

/** Resultado de presentarse a Windows al conectarse. */
sealed interface Handshake {
    data class Connected(val pcName: String) : Handshake
    data class Refused(val pcName: String) : Handshake

    /** Windows no reconoce la clave guardada (se olvidó el reloj): hay que volver a vincular. */
    data object Forgotten : Handshake
}

/**
 * Una conexión abierta con Windows: presentación (HELLO o vinculación), comandos con su respuesta
 * y latido. No sabe nada de Android: recibe los flujos del socket, así que se prueba en la JVM
 * contra un Windows falso.
 *
 * Las marcas de tiempo usan la hora de Windows (la que llega en HELLO_ACK, PAIR_ACCEPTED y PONG),
 * para que una diferencia de hora entre el reloj y el PC no haga que Windows rechace los comandos
 * por antiguos.
 */
class LinkSession(
    private val input: BufferedReader,
    private val output: OutputStream,
    private val pc: PcAnnouncement,
    private val storage: PairingStorage,
    private val deviceName: String,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val pending = ConcurrentHashMap<String, CompletableDeferred<Incoming.CommandResponse>>()
    private val writeLock = Any()
    private var token: String? = null

    @Volatile
    private var clockOffsetMs = 0L

    @Volatile
    var lastHeardMs: Long = clock()
        private set

    private fun now() = clock() + clockOffsetMs

    private fun syncClock(serverTimeUtc: Long) {
        clockOffsetMs = serverTimeUtc - clock()
    }

    /**
     * Se presenta con la clave guardada o pide vincularse. Bloquea mientras espera la respuesta de
     * la persona en Windows; [onPairingCode] recibe el código que hay que mostrar.
     */
    fun handshake(onPairingCode: (String) -> Unit): Handshake {
        val saved = storage.tokenFor(pc.pcId)
        if (saved != null) {
            writeLine(LinkMessages.hello(storage.deviceId, saved, deviceName, clock()))
            return when (val reply = readIncoming()) {
                is Incoming.HelloAck -> {
                    syncClock(reply.serverTimeUtc)
                    token = saved
                    Handshake.Connected(reply.pcName)
                }
                is Incoming.HelloRejected -> {
                    storage.forget(pc.pcId)
                    Handshake.Forgotten
                }
                else -> throw IOException("respuesta inesperada al saludo")
            }
        }

        writeLine(LinkMessages.pairRequest(storage.deviceId, deviceName, clock()))
        while (true) {
            when (val reply = readIncoming()) {
                is Incoming.PairPending -> onPairingCode(reply.code)
                is Incoming.PairAccepted -> {
                    storage.save(pc.pcId, reply.pcName, reply.sessionToken)
                    syncClock(reply.serverTimeUtc)
                    token = reply.sessionToken
                    return Handshake.Connected(reply.pcName)
                }
                is Incoming.PairRejected -> return Handshake.Refused(pc.pcName)
                // Un HELLO_REJECTED aquí es un mensaje mal formado: no se reintenta en bucle.
                else -> throw IOException("respuesta inesperada a la vinculación")
            }
        }
    }

    /**
     * Lee lo que llega de Windows hasta que se cierre la conexión (lanza [IOException]). Entrega
     * cada respuesta al comando que la espera.
     */
    fun readLoop() {
        while (true) {
            when (val message = readIncoming()) {
                is Incoming.CommandResponse -> message.correlationId?.let { pending.remove(it)?.complete(message) }
                is Incoming.Pong -> syncClock(message.serverTimeUtc)
                // Windows cierra la conexión tras un rechazo; el bucle de conexión decide qué hacer.
                is Incoming.HelloRejected -> throw IOException("Windows rechazó la sesión: ${message.reasonCode}")
                else -> Unit
            }
        }
    }

    fun ping() = writeLine(LinkMessages.ping(now()))

    /** Envía un comando y espera la respuesta de Windows (como mucho [timeoutMs]). */
    suspend fun send(command: Command, mode: ControlMode, gestureId: String, confidence: Double, timeoutMs: Long = 3000): CommandOutcome {
        val key = token ?: return CommandOutcome.NotConnected
        val messageId = LinkMessages.newMessageId()
        val answer = CompletableDeferred<Incoming.CommandResponse>()
        pending[messageId] = answer
        try {
            writeLine(LinkMessages.command(storage.deviceId, key, mode, gestureId, confidence, command, now(), messageId))
        } catch (_: IOException) {
            pending.remove(messageId)
            return CommandOutcome.NotConnected
        }
        val response = try {
            withTimeoutOrNull(timeoutMs) { answer.await() }
        } catch (_: IOException) {
            return CommandOutcome.NotConnected
        } finally {
            pending.remove(messageId)
        }
        return when {
            response == null -> CommandOutcome.NoResponse
            response.status == "EXECUTED" -> CommandOutcome.Executed
            else -> CommandOutcome.Failed(response.reasonCode)
        }
    }

    /** Despierta a los comandos que esperaban respuesta cuando se pierde la conexión. */
    fun failPending() {
        pending.values.forEach { it.completeExceptionally(IOException("se perdió la conexión con Windows")) }
        pending.clear()
    }

    private fun readIncoming(): Incoming {
        while (true) {
            val line = input.readLine() ?: throw IOException("Windows cerró la conexión")
            lastHeardMs = clock()
            LinkMessages.parse(line)?.let { return it }
        }
    }

    private fun writeLine(json: String) {
        val bytes = (json + "\n").toByteArray(Charsets.UTF_8)
        synchronized(writeLock) {
            output.write(bytes)
            output.flush()
        }
    }
}
