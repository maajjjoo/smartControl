package com.smartcontrol.watch.link

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Build
import android.provider.Settings
import android.util.Log
import com.smartcontrol.watch.BuildConfig
import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode
import com.smartcontrol.watch.recording.WindowRow
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException

/**
 * Conexión automática con SmartControl en Windows, sin que el usuario escriba nada:
 *
 * 1. Pide la red Wi-Fi (Wear OS la apaga para ahorrar batería cuando el reloj va por Bluetooth).
 * 2. Pregunta en la red quién tiene SmartControl (UDP) y elige su equipo vinculado, o el primero.
 * 3. Se conecta por TCP, se presenta con su clave o pide vincularse (código en ambas pantallas).
 * 4. Mantiene la conexión con un latido y, si se cae, vuelve a empezar solo.
 *
 * Solo funciona entre [start] y [stop] (la app en primer plano), igual que el giroscopio.
 */
class WindowsLink(context: Context, private val scope: CoroutineScope) {
    private val app = context.applicationContext
    private val connectivity = app.getSystemService(ConnectivityManager::class.java)
    private val storage = PreferencesPairingStorage(app)
    /** El nombre que el usuario ve en los ajustes del reloj (p. ej. «Galaxy Watch6»), o el modelo. */
    private val deviceName = LinkMessages.cleanName(
        runCatching { Settings.Global.getString(app.contentResolver, Settings.Global.DEVICE_NAME) }.getOrNull() ?: Build.MODEL,
    )

    private val _state = MutableStateFlow<LinkState>(LinkState.Searching)
    val state: StateFlow<LinkState> = _state.asStateFlow()

    @Volatile
    private var session: LinkSession? = null

    @Volatile
    private var wifi: Network? = null

    /** Socket abierto, para cerrarlo al salir: una lectura bloqueada no atiende la cancelación. */
    @Volatile
    private var openSocket: Socket? = null
    private var callback: ConnectivityManager.NetworkCallback? = null
    private var job: Job? = null

    fun start() {
        if (job != null) return
        requestWifi()
        job = scope.launch(Dispatchers.IO) { connectionLoop() }
    }

    fun stop() {
        val running = job ?: return
        job = null
        scope.launch {
            running.cancelAndJoin()
            _state.value = LinkState.Searching
        }
        session?.failPending()
        session = null
        openSocket?.let { runCatching { it.close() } }
        callback?.let { runCatching { connectivity?.unregisterNetworkCallback(it) } }
        callback = null
        wifi = null
    }

    /** Envía un comando a Windows y espera la respuesta. */
    suspend fun send(command: Command, mode: ControlMode, gestureId: String, confidence: Double): CommandOutcome {
        val current = session ?: return CommandOutcome.NotConnected
        return withContext(Dispatchers.IO) { current.send(command, mode, gestureId, confidence) }
    }

    /** Envía la ventana de un movimiento: Windows decide el gesto con su IA. */
    suspend fun sendWindow(mode: ControlMode, detectorEvent: String, detectorConfidence: Double, window: List<WindowRow>): CommandOutcome {
        val current = session ?: return CommandOutcome.NotConnected
        return withContext(Dispatchers.IO) { current.sendWindow(mode, detectorEvent, detectorConfidence, window) }
    }

    /**
     * Wear OS no garantiza que el Wi-Fi esté activo: hay que pedirlo. No se exige acceso a
     * Internet, porque basta con la red local.
     */
    private fun requestWifi() {
        val manager = connectivity ?: return
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        val cb = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                wifi = network
            }

            override fun onLost(network: Network) {
                if (wifi == network) wifi = null
            }
        }
        callback = cb
        runCatching { manager.requestNetwork(request, cb) }
            .onFailure { log("no se pudo pedir el Wi-Fi: ${it.javaClass.simpleName}") }
    }

    private suspend fun connectionLoop() {
        var waitedForWifi = 0L
        while (currentCoroutineContext().isActive) {
            val network = wifi
            if (network == null) {
                // Unos segundos de margen: el Wi-Fi tarda en encenderse.
                if (waitedForWifi >= WIFI_GRACE_MS) _state.value = LinkState.NoWifi
                delay(500)
                waitedForWifi += 500
                continue
            }
            waitedForWifi = 0
            if (_state.value !is LinkState.Searching) _state.value = LinkState.Searching

            val pc = discover(network)
            if (pc == null) {
                delay(RETRY_MS)
                continue
            }
            try {
                connect(network, pc)
            } catch (e: IOException) {
                log("conexión perdida: ${e.javaClass.simpleName}")
            } finally {
                session?.failPending()
                session = null
            }
            if (_state.value is LinkState.PairingRefused) delay(REFUSED_PAUSE_MS) else delay(RETRY_MS)
            _state.value = LinkState.Searching
        }
    }

    /** Pregunta en la red quién tiene SmartControl y espera las respuestas un momento. */
    private fun discover(network: Network): PcAnnouncement? {
        val found = mutableListOf<PcAnnouncement>()
        val addresses = mutableListOf<InetSocketAddress>()
        try {
            DatagramSocket().use { socket ->
                network.bindSocket(socket)
                socket.broadcast = true
                socket.soTimeout = 400
                val query = LinkMessages.DISCOVERY_QUERY.toByteArray(Charsets.US_ASCII)
                val targets = buildList {
                    add(InetAddress.getByName("255.255.255.255"))
                    // En el emulador, el PC que lo ejecuta está en 10.0.2.2 y no recibe la difusión.
                    if (isEmulator()) add(InetAddress.getByName("10.0.2.2"))
                }
                targets.forEach { socket.send(DatagramPacket(query, query.size, it, LinkMessages.DISCOVERY_PORT)) }
                val buffer = ByteArray(1024)
                val deadline = System.currentTimeMillis() + DISCOVERY_WAIT_MS
                while (System.currentTimeMillis() < deadline) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        socket.receive(packet)
                    } catch (_: SocketTimeoutException) {
                        continue
                    }
                    val announcement = LinkMessages.parseAnnouncement(String(packet.data, 0, packet.length, Charsets.UTF_8)) ?: continue
                    if (found.none { it.pcId == announcement.pcId }) {
                        found += announcement
                        addresses += InetSocketAddress(packet.address, announcement.port)
                    }
                    if (announcement.pcId == storage.pairedPcId) break
                }
            }
        } catch (e: IOException) {
            log("búsqueda fallida: ${e.javaClass.simpleName}")
            return null
        }
        if (found.isEmpty()) return null
        val index = found.indexOfFirst { it.pcId == storage.pairedPcId }.takeIf { it >= 0 } ?: 0
        target = addresses[index]
        return found[index]
    }

    @Volatile
    private var target: InetSocketAddress? = null

    private suspend fun connect(network: Network, pc: PcAnnouncement) {
        val address = target ?: return
        network.socketFactory.createSocket().use { socket: Socket ->
            openSocket = socket
            socket.connect(address, CONNECT_TIMEOUT_MS)
            socket.tcpNoDelay = true
            // Mientras se espera la vinculación, la persona tiene hasta un minuto para pulsar «Vincular».
            socket.soTimeout = PAIRING_READ_TIMEOUT_MS
            val linkSession = LinkSession(
                socket.getInputStream().bufferedReader(Charsets.UTF_8),
                socket.getOutputStream(),
                pc,
                storage,
                deviceName,
            )
            when (val result = linkSession.handshake { code -> _state.value = LinkState.Pairing(code, pc.pcName) }) {
                is Handshake.Refused -> {
                    _state.value = LinkState.PairingRefused(result.pcName)
                    return
                }
                Handshake.Forgotten -> return // La próxima vuelta pedirá vincularse.
                is Handshake.Connected -> {
                    session = linkSession
                    _state.value = LinkState.Connected(result.pcName)
                    log("conectado")
                }
            }
            socket.soTimeout = 0
            try {
                coroutineScopeHeartbeat(socket, linkSession)
            } finally {
                openSocket = null
            }
        }
    }

    /** Latido cada pocos segundos; si Windows deja de responder, se cierra y se reconecta. */
    private suspend fun coroutineScopeHeartbeat(socket: Socket, linkSession: LinkSession) {
        val heartbeat = scope.launch(Dispatchers.IO) {
            while (isActive) {
                delay(PING_INTERVAL_MS)
                if (System.currentTimeMillis() - linkSession.lastHeardMs > SILENCE_LIMIT_MS) {
                    log("Windows no responde")
                    runCatching { socket.close() }
                    return@launch
                }
                try {
                    linkSession.ping()
                } catch (_: IOException) {
                    return@launch
                }
            }
        }
        try {
            linkSession.readLoop()
        } finally {
            heartbeat.cancel()
        }
    }

    private fun isEmulator(): Boolean =
        Build.HARDWARE.contains("ranchu") || Build.HARDWARE.contains("goldfish") || Build.FINGERPRINT.contains("generic")

    private fun log(text: String) {
        if (BuildConfig.DEBUG) Log.d(TAG, text)
    }

    private companion object {
        const val TAG = "SmartControlLink"
        const val WIFI_GRACE_MS = 8_000L
        const val DISCOVERY_WAIT_MS = 1_500L
        const val RETRY_MS = 2_000L
        const val REFUSED_PAUSE_MS = 4_000L
        const val CONNECT_TIMEOUT_MS = 3_000
        const val PAIRING_READ_TIMEOUT_MS = 75_000
        const val PING_INTERVAL_MS = 8_000L
        const val SILENCE_LIMIT_MS = 20_000L
    }
}
