package com.smartcontrol.watch.link

import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode
import java.util.UUID

/** Respuesta de un PC con SmartControl a la búsqueda en la red. */
data class PcAnnouncement(val pcName: String, val pcId: String, val port: Int)

/** Mensajes que llegan de Windows (docs/communication-protocol.md §3.2 y §3.4). */
sealed interface Incoming {
    val correlationId: String?

    data class PairPending(override val correlationId: String?, val code: String, val pcName: String) : Incoming
    data class PairAccepted(override val correlationId: String?, val sessionToken: String, val pcName: String, val serverTimeUtc: Long) : Incoming
    data class PairRejected(override val correlationId: String?, val reasonCode: String) : Incoming
    data class HelloAck(override val correlationId: String?, val pcName: String, val serverTimeUtc: Long) : Incoming
    data class HelloRejected(override val correlationId: String?, val reasonCode: String) : Incoming
    data class Pong(override val correlationId: String?, val serverTimeUtc: Long) : Incoming
    data class CommandResponse(override val correlationId: String?, val status: String, val reasonCode: String) : Incoming
}

/**
 * Construye y lee los mensajes del canal con Windows. Los mensajes salientes llevan exactamente
 * los campos que acepta el validador de Windows; los entrantes se leen con cuidado y lo que no se
 * entiende se descarta (devuelve null), sin fallar.
 */
object LinkMessages {
    const val PROTOCOL_VERSION = "1.0-draft"
    const val DISCOVERY_QUERY = "SMARTCONTROL_DISCOVER/1"
    const val DISCOVERY_PORT = 47801
    const val MAX_NAME_LENGTH = 40

    fun newMessageId(): String = UUID.randomUUID().toString()

    fun pairRequest(deviceId: String, deviceName: String, nowMs: Long, messageId: String = newMessageId()): String =
        Json.write(
            linkedMapOf(
                "protocolVersion" to PROTOCOL_VERSION,
                "messageId" to messageId,
                "timestampUtc" to nowMs,
                "deviceId" to deviceId,
                "messageType" to "PAIR_REQUEST",
                "payload" to linkedMapOf("deviceName" to deviceName),
            ),
        )

    fun hello(deviceId: String, sessionToken: String, deviceName: String, nowMs: Long, messageId: String = newMessageId()): String =
        Json.write(
            linkedMapOf(
                "protocolVersion" to PROTOCOL_VERSION,
                "messageId" to messageId,
                "timestampUtc" to nowMs,
                "deviceId" to deviceId,
                "sessionToken" to sessionToken,
                "messageType" to "HELLO",
                "payload" to linkedMapOf("deviceName" to deviceName),
            ),
        )

    fun ping(nowMs: Long, messageId: String = newMessageId()): String =
        Json.write(
            linkedMapOf(
                "protocolVersion" to PROTOCOL_VERSION,
                "messageId" to messageId,
                "timestampUtc" to nowMs,
                "messageType" to "PING",
                "payload" to linkedMapOf<String, Any?>(),
            ),
        )

    /** COMMAND_REQUEST (protocolo §3.1). [gestureId] es el del gesto que produjo el comando. */
    fun command(
        deviceId: String,
        sessionToken: String,
        mode: ControlMode,
        gestureId: String,
        confidence: Double,
        command: Command,
        nowMs: Long,
        messageId: String = newMessageId(),
    ): String =
        Json.write(
            linkedMapOf(
                "protocolVersion" to PROTOCOL_VERSION,
                "messageId" to messageId,
                "timestampUtc" to nowMs,
                "deviceId" to deviceId,
                "sessionToken" to sessionToken,
                "messageType" to "COMMAND_REQUEST",
                "payload" to linkedMapOf(
                    "activeMode" to mode.name,
                    "gestureId" to gestureId,
                    "confidence" to confidence.coerceIn(0.0, 1.0),
                    "requestedAction" to command.name,
                ),
            ),
        )

    /** Lee una línea de Windows. Devuelve null si no es un mensaje conocido y bien formado. */
    fun parse(line: String): Incoming? {
        val root = try {
            Json.parseObject(line)
        } catch (_: IllegalArgumentException) {
            return null
        }
        if (root["protocolVersion"] != PROTOCOL_VERSION) return null
        val payload = root["payload"] as? Map<*, *> ?: return null
        val correlationId = root["correlationId"] as? String
        fun text(name: String) = payload[name] as? String
        fun number(name: String) = payload[name] as? Long
        return when (root["messageType"]) {
            "PAIR_PENDING" -> {
                val code = text("code")?.takeIf { it.length == 4 && it.all(Char::isDigit) } ?: return null
                Incoming.PairPending(correlationId, code, cleanName(text("pcName")))
            }
            "PAIR_ACCEPTED" -> Incoming.PairAccepted(
                correlationId,
                text("sessionToken")?.takeIf { it.isNotEmpty() && it.length <= 256 } ?: return null,
                cleanName(text("pcName")),
                number("serverTimeUtc") ?: return null,
            )
            "PAIR_REJECTED" -> Incoming.PairRejected(correlationId, text("reasonCode") ?: return null)
            "HELLO_ACK" -> Incoming.HelloAck(correlationId, cleanName(text("pcName")), number("serverTimeUtc") ?: return null)
            "HELLO_REJECTED" -> Incoming.HelloRejected(correlationId, text("reasonCode") ?: return null)
            "PONG" -> Incoming.Pong(correlationId, number("serverTimeUtc") ?: return null)
            "COMMAND_RESPONSE" -> Incoming.CommandResponse(
                correlationId,
                text("status") ?: return null,
                text("reasonCode") ?: return null,
            )
            else -> null
        }
    }

    /** Lee la respuesta a la búsqueda. Null si no es de SmartControl o no es compatible. */
    fun parseAnnouncement(text: String): PcAnnouncement? {
        val root = try {
            Json.parseObject(text)
        } catch (_: IllegalArgumentException) {
            return null
        }
        if (root["service"] != "smartcontrol" || root["protocolVersion"] != PROTOCOL_VERSION) return null
        val port = (root["port"] as? Long)?.takeIf { it in 1..65535 } ?: return null
        val pcId = (root["pcId"] as? String)?.takeIf { it.isNotEmpty() && it.length <= 64 } ?: return null
        return PcAnnouncement(cleanName(root["pcName"] as? String), pcId, port.toInt())
    }

    /** Los nombres se muestran en el reloj: solo caracteres imprimibles y longitud limitada. */
    fun cleanName(name: String?): String {
        val clean = name.orEmpty().filter { it.isLetterOrDigit() || it in " -_.()" }.trim().take(MAX_NAME_LENGTH).trim()
        return clean.ifEmpty { "Equipo Windows" }
    }
}
