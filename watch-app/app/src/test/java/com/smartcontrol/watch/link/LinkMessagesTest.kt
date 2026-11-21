package com.smartcontrol.watch.link

import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class LinkMessagesTest {

    @Test
    fun jsonRoundTripWithEscapes() {
        val text = Json.write(linkedMapOf("a" to "comillas \" barra \\ salto\n", "n" to 42L, "d" to 0.5, "b" to true, "z" to null, "o" to linkedMapOf("x" to 1L)))
        val back = Json.parseObject(text)
        assertEquals("comillas \" barra \\ salto\n", back["a"])
        assertEquals(42L, back["n"])
        assertEquals(0.5, back["d"])
        assertEquals(true, back["b"])
        assertTrue(back.containsKey("z") && back["z"] == null)
        assertEquals(1L, (back["o"] as Map<*, *>)["x"])
    }

    @Test
    fun jsonRejectsBrokenInput() {
        for (bad in listOf("", "[]", "{", "{\"a\":1,\"a\":2}", "{\"a\":1} x", "{\"a\":nul}", "{\"a\":\"\u0001\"}")) {
            try {
                Json.parseObject(bad)
                fail("debía rechazar: $bad")
            } catch (_: IllegalArgumentException) {
            }
        }
    }

    /** Los campos deben ser exactamente los que acepta el validador de Windows (protocolo §3.1). */
    @Test
    fun commandHasExactlyTheProtocolFields() {
        val json = Json.parseObject(
            LinkMessages.command("watch-01", "token", ControlMode.MULTIMEDIA, "WRIST_TWIST_IN", 0.93, Command.MEDIA_PAUSE, 1_700_000_000_000L, "m-1"),
        )
        assertEquals(setOf("protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload"), json.keys)
        val payload = json["payload"] as Map<*, *>
        assertEquals(setOf("activeMode", "gestureId", "confidence", "requestedAction"), payload.keys)
        assertEquals("COMMAND_REQUEST", json["messageType"])
        assertEquals("MULTIMEDIA", payload["activeMode"])
        assertEquals("MEDIA_PAUSE", payload["requestedAction"])
        assertEquals(0.93, payload["confidence"])
        assertEquals(1_700_000_000_000L, json["timestampUtc"])
    }

    @Test
    fun sessionMessagesHaveTheFieldsWindowsExpects() {
        val pair = Json.parseObject(LinkMessages.pairRequest("watch-01", "Galaxy Watch", 1L))
        assertEquals(setOf("protocolVersion", "messageId", "timestampUtc", "deviceId", "messageType", "payload"), pair.keys)
        assertEquals(setOf("deviceName"), (pair["payload"] as Map<*, *>).keys)
        val hello = Json.parseObject(LinkMessages.hello("watch-01", "t", "Galaxy Watch", 1L))
        assertEquals(setOf("protocolVersion", "messageId", "timestampUtc", "deviceId", "sessionToken", "messageType", "payload"), hello.keys)
        val ping = Json.parseObject(LinkMessages.ping(1L))
        assertEquals(setOf("protocolVersion", "messageId", "timestampUtc", "messageType", "payload"), ping.keys)
        assertTrue((ping["payload"] as Map<*, *>).isEmpty())
    }

    @Test
    fun parsesWindowsReplies() {
        fun msg(type: String, payload: String, correlation: String = "c-1") =
            """{"protocolVersion":"1.0-draft","messageId":"x","correlationId":"$correlation","timestampUtc":5,"messageType":"$type","payload":$payload}"""

        assertEquals(Incoming.PairPending("c-1", "0427", "PORTATIL"), LinkMessages.parse(msg("PAIR_PENDING", """{"code":"0427","pcName":"PORTATIL"}""")))
        assertEquals(Incoming.PairAccepted("c-1", "abc", "PORTATIL", 99), LinkMessages.parse(msg("PAIR_ACCEPTED", """{"sessionToken":"abc","pcName":"PORTATIL","serverTimeUtc":99}""")))
        assertEquals(Incoming.HelloRejected("c-1", "UNAUTHORIZED_DEVICE"), LinkMessages.parse(msg("HELLO_REJECTED", """{"reasonCode":"UNAUTHORIZED_DEVICE"}""")))
        assertEquals(
            Incoming.CommandResponse("c-1", "EXECUTED", "OK"),
            LinkMessages.parse(msg("COMMAND_RESPONSE", """{"status":"EXECUTED","executedAction":"SLIDE_NEXT","validatedAction":"SLIDE_NEXT","reasonCode":"OK","message":"x"}""")),
        )
        assertNull("código que no es de 4 cifras", LinkMessages.parse(msg("PAIR_PENDING", """{"code":"12a4","pcName":"P"}""")))
        assertNull("otra versión", LinkMessages.parse(msg("PONG", """{"serverTimeUtc":1}""").replace("1.0-draft", "2.0")))
        assertNull("tipo desconocido", LinkMessages.parse(msg("OTRA_COSA", "{}")))
        assertNull("no es JSON", LinkMessages.parse("hola"))
    }

    @Test
    fun parsesDiscoveryAnnouncement() {
        val reply = """{"service":"smartcontrol","protocolVersion":"1.0-draft","pcName":"PORTATIL-ANA","pcId":"pc-1a2b","port":47800}"""
        assertEquals(PcAnnouncement("PORTATIL-ANA", "pc-1a2b", 47800), LinkMessages.parseAnnouncement(reply))
        assertNull(LinkMessages.parseAnnouncement(reply.replace("smartcontrol", "otro")))
        assertNull(LinkMessages.parseAnnouncement(reply.replace("47800", "70000")))
    }

    @Test
    fun namesShownOnTheWatchAreCleaned() {
        assertEquals("PORTATIL-ANA", LinkMessages.cleanName("PORTATIL-ANA"))
        assertEquals("Equipo Windows", LinkMessages.cleanName("‮<>"))
        assertEquals(LinkMessages.MAX_NAME_LENGTH, LinkMessages.cleanName("x".repeat(100)).length)
    }
}
