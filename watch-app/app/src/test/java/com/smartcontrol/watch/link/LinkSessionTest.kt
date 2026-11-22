package com.smartcontrol.watch.link

import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode
import com.smartcontrol.watch.recording.WindowRow
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.BufferedReader
import java.io.PrintWriter
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

/**
 * Sesión con un Windows FALSO por TCP en 127.0.0.1: comprueba el diálogo del reloj (saludo,
 * vinculación, comandos, hora). La red Wi-Fi y la búsqueda se prueban en el emulador.
 */
class LinkSessionTest {

    private class MemoryStorage(override val deviceId: String = "watch-test") : PairingStorage {
        val tokens = mutableMapOf<String, String>()
        override val pairedPcId: String? get() = tokens.keys.firstOrNull()
        override fun tokenFor(pcId: String) = tokens[pcId]
        override fun save(pcId: String, pcName: String, token: String) { tokens[pcId] = token }
        override fun forget(pcId: String) { tokens.remove(pcId) }
    }

    /** Windows falso: lo que recibe va a [received]; [reply] responde a cada línea recibida. */
    private class FakeWindows(private val reply: (Map<String, Any?>, PrintWriter) -> Unit) {
        val server = ServerSocket(0, 1, InetAddress.getLoopbackAddress())
        val received = LinkedBlockingQueue<Map<String, Any?>>()

        init {
            thread(isDaemon = true) {
                server.accept().use { socket ->
                    val reader = socket.getInputStream().bufferedReader()
                    val writer = PrintWriter(socket.getOutputStream().writer(), true)
                    while (true) {
                        val line = reader.readLine() ?: break
                        val message = Json.parseObject(line)
                        received.add(message)
                        reply(message, writer)
                    }
                }
            }
        }

        fun next(): Map<String, Any?> = received.poll(5, TimeUnit.SECONDS) ?: error("Windows no recibió nada")
    }

    private val sockets = mutableListOf<Socket>()
    private val pc = PcAnnouncement("PORTATIL", "pc-1", 0)

    @After
    fun close() = sockets.forEach { it.close() }

    private fun session(windows: FakeWindows, storage: PairingStorage, clock: () -> Long = System::currentTimeMillis): LinkSession {
        val socket = Socket(InetAddress.getLoopbackAddress(), windows.server.localPort).also { sockets += it }
        socket.soTimeout = 5000
        return LinkSession(BufferedReader(socket.getInputStream().reader()), socket.getOutputStream(), pc, storage, "Reloj de prueba", clock)
    }

    private fun reply(type: String, correlationId: Any?, payload: String) =
        """{"protocolVersion":"1.0-draft","messageId":"r","correlationId":${if (correlationId == null) "null" else "\"$correlationId\""},"timestampUtc":1,"messageType":"$type","payload":$payload}"""

    @Test
    fun newWatchPairsShowingTheCodeAndKeepsTheToken() {
        val windows = FakeWindows { message, out ->
            if (message["messageType"] == "PAIR_REQUEST") {
                out.println(reply("PAIR_PENDING", message["messageId"], """{"code":"0427","pcName":"PORTATIL"}"""))
                out.println(reply("PAIR_ACCEPTED", message["messageId"], """{"sessionToken":"clave-nueva","pcName":"PORTATIL","serverTimeUtc":1000}"""))
            }
        }
        val storage = MemoryStorage()
        var shownCode: String? = null
        val result = session(windows, storage).handshake { shownCode = it }

        assertEquals(Handshake.Connected("PORTATIL"), result)
        assertEquals("0427", shownCode)
        assertEquals("clave-nueva", storage.tokenFor("pc-1"))
        val request = windows.next()
        assertEquals("PAIR_REQUEST", request["messageType"])
        assertEquals("watch-test", request["deviceId"])
        assertEquals("Reloj de prueba", (request["payload"] as Map<*, *>)["deviceName"])
    }

    @Test
    fun refusedPairingSavesNothing() {
        val windows = FakeWindows { message, out ->
            out.println(reply("PAIR_PENDING", message["messageId"], """{"code":"1111","pcName":"PORTATIL"}"""))
            out.println(reply("PAIR_REJECTED", message["messageId"], """{"reasonCode":"USER_REJECTED"}"""))
        }
        val storage = MemoryStorage()
        assertEquals(Handshake.Refused("PORTATIL"), session(windows, storage).handshake {})
        assertNull(storage.tokenFor("pc-1"))
    }

    @Test
    fun pairedWatchSaysHelloWithItsToken() {
        val windows = FakeWindows { message, out ->
            out.println(reply("HELLO_ACK", message["messageId"], """{"pcName":"PORTATIL","serverTimeUtc":1000}"""))
        }
        val storage = MemoryStorage().apply { save("pc-1", "PORTATIL", "clave-guardada") }
        assertEquals(Handshake.Connected("PORTATIL"), session(windows, storage).handshake { error("no debe pedir código") })
        val hello = windows.next()
        assertEquals("HELLO", hello["messageType"])
        assertEquals("clave-guardada", hello["sessionToken"])
    }

    @Test
    fun forgottenWatchDropsItsTokenToPairAgain() {
        val windows = FakeWindows { message, out ->
            out.println(reply("HELLO_REJECTED", message["messageId"], """{"reasonCode":"UNAUTHORIZED_DEVICE"}"""))
        }
        val storage = MemoryStorage().apply { save("pc-1", "PORTATIL", "clave-vieja") }
        assertEquals(Handshake.Forgotten, session(windows, storage).handshake {})
        assertNull(storage.tokenFor("pc-1"))
    }

    @Test
    fun commandsGetWindowsAnswerAndUseWindowsTime() = runBlocking {
        val windowsTime = 5_000_000_000_000L
        val windows = FakeWindows { message, out ->
            when (message["messageType"]) {
                "HELLO" -> out.println(reply("HELLO_ACK", message["messageId"], """{"pcName":"PORTATIL","serverTimeUtc":$windowsTime}"""))
                "COMMAND_REQUEST" -> {
                    val action = (message["payload"] as Map<*, *>)["requestedAction"]
                    val payload = if (action == "MEDIA_PLAY") {
                        """{"status":"EXECUTION_ERROR","executedAction":null,"validatedAction":"MEDIA_PLAY","reasonCode":"NO_MEDIA_SESSION","message":"x"}"""
                    } else {
                        """{"status":"EXECUTED","executedAction":"$action","validatedAction":"$action","reasonCode":"OK","message":"x"}"""
                    }
                    // Una respuesta de otro mensaje antes: debe ignorarse.
                    out.println(reply("COMMAND_RESPONSE", "otro", payload))
                    out.println(reply("COMMAND_RESPONSE", message["messageId"], payload))
                }
            }
        }
        val storage = MemoryStorage().apply { save("pc-1", "PORTATIL", "clave") }
        // El reloj va 2 horas atrasado respecto a Windows.
        val watchClock = { System.currentTimeMillis() }
        val link = session(windows, storage, watchClock)
        val offsetAtHello = windowsTime - watchClock()
        link.handshake {}
        thread(isDaemon = true) { runCatching { link.readLoop() } }

        assertEquals(CommandOutcome.Executed("SLIDE_NEXT"), link.send(Command.SLIDE_NEXT, ControlMode.PRESENTATION, "WRIST_TWIST_OUT", 0.9))
        assertEquals(CommandOutcome.Failed("NO_MEDIA_SESSION"), link.send(Command.MEDIA_PLAY, ControlMode.MULTIMEDIA, "WRIST_TWIST_OUT", 0.9))

        windows.next() // HELLO
        val command = windows.next()
        val stamped = command["timestampUtc"] as Long
        assertTrue("la hora del comando es la de Windows", kotlin.math.abs(stamped - (watchClock() + offsetAtHello)) < 2_000)
        assertEquals("clave", command["sessionToken"])
    }

    @Test
    fun movementWindowsAreDecidedByWindowsAI() = runBlocking {
        val windows = FakeWindows { message, out ->
            when (message["messageType"]) {
                "HELLO" -> out.println(reply("HELLO_ACK", message["messageId"], """{"pcName":"P","serverTimeUtc":1}"""))
                "SENSOR_WINDOW" -> out.println(
                    reply(
                        "COMMAND_RESPONSE", message["messageId"],
                        """{"status":"EXECUTED","executedAction":"MEDIA_NEXT_TRACK","validatedAction":"MEDIA_NEXT_TRACK","reasonCode":"OK","message":"x","recognizedBy":"AI","gesture":"TWIST_OUT","confidence":0.934}""",
                    ),
                )
            }
        }
        val storage = MemoryStorage().apply { save("pc-1", "P", "clave") }
        val link = session(windows, storage)
        link.handshake {}
        thread(isDaemon = true) { runCatching { link.readLoop() } }
        val rows = (0 until 12).map { WindowRow(it * 20, if (it in 4..7) 4.123456f else 0f, 0f, 0f) }

        val outcome = link.sendWindow(ControlMode.MULTIMEDIA, "TWIST_IN", 0.85, rows)
        assertEquals(CommandOutcome.Executed("MEDIA_NEXT_TRACK", "AI", 0.934), outcome)

        windows.next() // HELLO
        val sent = windows.next()
        assertEquals("SENSOR_WINDOW", sent["messageType"])
        val payload = sent["payload"] as Map<*, *>
        assertEquals(setOf("activeMode", "detectorEvent", "detectorConfidence", "gyroscope"), payload.keys)
        assertEquals("TWIST_IN", payload["detectorEvent"])
        val gyroscope = payload["gyroscope"] as List<*>
        assertEquals(12, gyroscope.size)
        assertEquals(listOf(100L, 4.1235, 0.0, 0.0), gyroscope[5])
    }

    @Test
    fun noAnswerIsReportedAndNoTokenMeansNotConnected() = runBlocking {
        val windows = FakeWindows { message, out ->
            if (message["messageType"] == "HELLO") out.println(reply("HELLO_ACK", message["messageId"], """{"pcName":"P","serverTimeUtc":1}"""))
        }
        val storage = MemoryStorage().apply { save("pc-1", "P", "clave") }
        val link = session(windows, storage)
        assertEquals(CommandOutcome.NotConnected, link.send(Command.SLIDE_NEXT, ControlMode.PRESENTATION, "WRIST_TWIST_OUT", 0.9))
        link.handshake {}
        thread(isDaemon = true) { runCatching { link.readLoop() } }
        assertEquals(CommandOutcome.NoResponse, link.send(Command.SLIDE_NEXT, ControlMode.PRESENTATION, "WRIST_TWIST_OUT", 0.9, timeoutMs = 300))
    }
}
