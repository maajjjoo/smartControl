package com.smartcontrol.watch.ui

import android.os.SystemClock
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.VerticalPager
import androidx.compose.foundation.pager.PagerState
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.rotary.onRotaryScrollEvent
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.util.lerp
import androidx.wear.compose.material3.AppScaffold
import androidx.wear.compose.material3.Icon
import androidx.wear.compose.material3.Text
import androidx.wear.compose.ui.tooling.preview.WearPreviewDevices
import com.smartcontrol.watch.Command
import com.smartcontrol.watch.CommandLog
import com.smartcontrol.watch.CommandResult
import com.smartcontrol.watch.ConnectionStatus
import com.smartcontrol.watch.R
import com.smartcontrol.watch.SCREEN_TAP_ID
import com.smartcontrol.watch.gestureFor
import com.smartcontrol.watch.link.CommandOutcome
import com.smartcontrol.watch.link.LinkState
import com.smartcontrol.watch.ControlMode
import com.smartcontrol.watch.ControlState
import com.smartcontrol.watch.PendingCommand
import com.smartcontrol.watch.commandFor
import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.Movement
import com.smartcontrol.watch.recording.WindowRow
import com.smartcontrol.watch.settings.LanguageChoice
import com.smartcontrol.watch.settings.ThemeChoice
import com.smartcontrol.watch.settings.next
import com.smartcontrol.watch.gesture.WristGesture
import kotlin.math.abs
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.launch

/** Pantallas completas que se recorren deslizando hacia arriba o abajo (o girando la corona). */
private enum class Page(val mode: ControlMode?) {
    HOME(null),
    PRESENTATION(ControlMode.PRESENTATION),
    MULTIMEDIA(ControlMode.MULTIMEDIA),
    SETTINGS(null),
}

/* Medidas comunes: todas las pantallas usan la misma cabecera y filas del mismo tamaño. */
private val PAGE_PADDING = 28.dp
private val ROW_GAP = 6.dp
private val ROW_ICON = 28.dp
private val ROW_TEXT = 14.sp
private val ROW_PADDING = PaddingValues(start = 4.dp, end = 14.dp, top = 4.dp, bottom = 4.dp)

/** Cabecera de cada pantalla: icono pequeño y título, en una línea. */
@Composable
private fun PageHeader(icon: ImageVector, title: String) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(bottom = 8.dp)) {
        Icon(icon, contentDescription = null, tint = SmartColors.Brand.onSoft, modifier = Modifier.size(16.dp))
        Spacer(Modifier.width(6.dp))
        Text(title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = SmartColors.Brand.onSoft)
    }
}

/** Duración de la ventana de confirmación antes de cerrarse sola. */
private const val FEEDBACK_MILLIS = 2200L

/** Tras cerrarse la ventana, la guía ignora toques durante este tiempo. */
private const val TOUCH_GUARD_MILLIS = 400L

/**
 * La interacción principal son los gestos de muñeca ([gestureEvents], del giroscopio). Tocar la
 * guía de gestos envía el mismo comando, solo como alternativa para pruebas.
 *
 * [linkState] es la conexión real con Windows y [sendToWindows] envía un comando y devuelve la
 * respuesta de Windows. [simulationEnabled] solo sirve para pruebas sin Windows y no se muestra.
 */
@Composable
fun SmartControlApp(
    simulationEnabled: Boolean = true,
    gestureEvents: Flow<Movement> = emptyFlow(),
    gesturesAvailable: Boolean = false,
    linkState: StateFlow<LinkState> = MutableStateFlow(LinkState.Searching),
    sendToWindows: suspend (Command, ControlMode, String, Double) -> CommandOutcome = { _, _, _, _ -> CommandOutcome.NotConnected },
    sendWindowToWindows: suspend (ControlMode, String, Double, List<WindowRow>) -> CommandOutcome = { _, _, _, _ -> CommandOutcome.NotConnected },
    themeChoice: ThemeChoice = ThemeChoice.SYSTEM,
    onThemeChange: (ThemeChoice) -> Unit = {},
    languageChoice: LanguageChoice = LanguageChoice.SYSTEM,
    onLanguageChange: (LanguageChoice) -> Unit = {},
) {
    var state by remember { mutableStateOf(ControlState(simulationEnabled = simulationEnabled)) }
    val link by linkState.collectAsState()
    val scope = rememberCoroutineScope()
    // Cambia en cada acción para volver a mostrar la confirmación aunque se repita el comando.
    var feedbackTick by remember { mutableIntStateOf(0) }
    // Los gestos solo cuentan en una pantalla de modo, nunca en Inicio ni en Ayuda.
    var onModePage by remember { mutableStateOf(false) }
    val haptics = LocalHapticFeedback.current

    /** Muestra el resultado: ventana de confirmación, vibración y registro. */
    fun show(next: ControlState) {
        state = next
        feedbackTick++
        CommandLog.record(state)
        val success = state.lastResult is CommandResult.Simulated || state.lastResult is CommandResult.Executed
        haptics.performHapticFeedback(if (success) HapticFeedbackType.Confirm else HapticFeedbackType.Reject)
    }

    /**
     * Aplica el resultado de validar un gesto o un toque. Si el comando hay que enviarlo a Windows,
     * el resultado se muestra cuando llega su respuesta.
     */
    fun apply(next: ControlState, send: suspend (PendingCommand) -> CommandOutcome) {
        val sent = next.pending
        when {
            sent != null && sent != state.pending -> {
                state = next
                scope.launch { show(state.complete(sent.sequence, send(sent))) }
            }
            // Ya hay un comando esperando respuesta: el nuevo se ignora para no encadenar acciones.
            sent != null -> Unit
            else -> show(next)
        }
    }

    LaunchedEffect(link) {
        state = state.setConnection(if (link is LinkState.Connected) ConnectionStatus.CONNECTED else ConnectionStatus.DISCONNECTED)
    }

    LaunchedEffect(gestureEvents) {
        gestureEvents.collect { movement ->
            if (!onModePage) return@collect
            val event = movement.event
            val window = movement.window
            val recognized = event as? GestureEvent.Recognized
            if (window != null && state.connection == ConnectionStatus.CONNECTED) {
                // Decide la IA de Windows con la ventana del movimiento (o sus reglas, sin modelo).
                val detector = recognized?.gesture?.name ?: "UNCLEAR"
                val confidence = when (event) {
                    is GestureEvent.Recognized -> event.confidence
                    is GestureEvent.Unclear -> event.confidence
                }
                apply(state.sendMovement(event)) { sendWindowToWindows(state.mode, detector, confidence.toDouble(), window) }
            } else {
                // Sin ventana o sin conexión: el reloj decide con sus reglas, como antes.
                apply(state.onGesture(event)) { sent ->
                    sendToWindows(sent.command!!, state.mode, recognized?.gesture?.id ?: "", recognized?.confidence?.toDouble() ?: 0.0)
                }
            }
        }
    }

    val dark = when (themeChoice) {
        ThemeChoice.SYSTEM -> isSystemInDarkTheme()
        ThemeChoice.LIGHT -> false
        ThemeChoice.DARK -> true
    }
    SmartControlTheme(dark = dark) {
        AppScaffold {
            SmartControlScreen(
                state = state,
                link = link,
                settings = SettingsState(themeChoice, onThemeChange, languageChoice, onLanguageChange),
                feedbackTick = feedbackTick,
                gesturesAvailable = gesturesAvailable,
                onSelectMode = { state = state.selectMode(it) },
                onModePageChange = { onModePage = it },
                onCommand = { command ->
                    apply(state.sendCommand(command)) { sendToWindows(command, state.mode, gestureFor(command)?.id ?: SCREEN_TAP_ID, 1.0) }
                },
            )
        }
    }
}

@Composable
fun SmartControlScreen(
    state: ControlState,
    link: LinkState,
    settings: SettingsState = SettingsState(),
    feedbackTick: Int,
    gesturesAvailable: Boolean,
    onSelectMode: (ControlMode) -> Unit,
    onModePageChange: (Boolean) -> Unit,
    onCommand: (Command) -> Unit,
    initialPage: Int = Page.HOME.ordinal,
) {
    val pagerState = rememberPagerState(initialPage = initialPage, pageCount = { Page.entries.size })
    val scope = rememberCoroutineScope()
    val focusRequester = remember { FocusRequester() }
    var touchGuardUntil by remember { mutableLongStateOf(0L) }
    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    // El código de vinculación se muestra en Inicio: si aparece, se vuelve allí.
    val pairing = link is LinkState.Pairing
    LaunchedEffect(pairing) {
        if (pairing) pagerState.animateScrollToPage(Page.HOME.ordinal)
    }

    // Al detenerse en una pantalla de modo, ese modo pasa a ser el activo y se escuchan gestos.
    LaunchedEffect(pagerState) {
        snapshotFlow { pagerState.settledPage }.collect { page ->
            val mode = Page.entries[page].mode
            mode?.let(onSelectMode)
            onModePageChange(mode != null)
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(SmartColors.BackgroundBrush),
    ) {
        VerticalPager(
            state = pagerState,
            modifier = Modifier
                .fillMaxSize()
                // La corona del reloj también cambia de pantalla, de una en una.
                .onRotaryScrollEvent { event ->
                    if (!pagerState.isScrollInProgress) {
                        val step = if (event.verticalScrollPixels > 0) 1 else -1
                        val target = (pagerState.currentPage + step).coerceIn(0, Page.entries.size - 1)
                        scope.launch { pagerState.animateScrollToPage(target) }
                    }
                    true
                }
                .focusRequester(focusRequester)
                .focusable(),
        ) { index ->
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .fillMaxSize()
                    .pageTransition(pagerState, index),
            ) {
                when (val page = Page.entries[index]) {
                    Page.HOME -> HomePage(link)
                    Page.SETTINGS -> SettingsPage(settings)
                    else -> GesturePage(
                        mode = page.mode!!,
                        link = link,
                        gesturesAvailable = gesturesAvailable,
                        lastResult = state.lastResult,
                        feedbackTick = feedbackTick,
                        // Un toque justo al cerrarse la ventana no debe caer en la guía y enviar otro comando.
                        onCommand = { if (SystemClock.uptimeMillis() >= touchGuardUntil) onCommand(it) },
                    )
                }
            }
        }

        PageIndicator(
            pagerState = pagerState,
            modifier = Modifier
                .align(Alignment.CenterEnd)
                .padding(end = 7.dp),
        )

        FeedbackOverlay(
            result = state.lastResult,
            tick = feedbackTick,
            onHidden = { touchGuardUntil = SystemClock.uptimeMillis() + TOUCH_GUARD_MILLIS },
        )
    }
}

/** Las pantallas que se alejan del centro se encogen, se desvanecen y se quedan un poco atrás. */
private fun Modifier.pageTransition(pagerState: PagerState, page: Int): Modifier = graphicsLayer {
    val signed = (pagerState.currentPage - page) + pagerState.currentPageOffsetFraction
    val offset = abs(signed).coerceIn(0f, 1f)
    val scale = lerp(1f, 0.8f, offset)
    scaleX = scale
    scaleY = scale
    alpha = lerp(1f, 0f, offset)
    // Efecto de profundidad: el contenido se mueve algo más despacio que la pantalla.
    translationY = signed * size.height * 0.25f
}

/** Textos de la conexión para la pantalla de Inicio. */
private data class LinkText(val title: String, val detail: String, val short: String, val connected: Boolean)

@Composable
private fun linkText(link: LinkState): LinkText = when (link) {
    is LinkState.Connected -> LinkText(stringResource(R.string.link_connected), link.pcName, stringResource(R.string.link_connected), true)
    LinkState.Searching -> LinkText(
        stringResource(R.string.link_searching), stringResource(R.string.link_searching_detail), stringResource(R.string.link_searching_short), false,
    )
    LinkState.NoWifi -> LinkText(
        stringResource(R.string.link_no_wifi), stringResource(R.string.link_no_wifi_detail), stringResource(R.string.link_no_wifi), false,
    )
    is LinkState.Pairing -> LinkText(
        stringResource(R.string.link_pair), stringResource(R.string.link_pair_detail, link.pcName), stringResource(R.string.link_pairing_short), false,
    )
    is LinkState.PairingRefused -> LinkText(
        stringResource(R.string.link_refused), stringResource(R.string.link_refused_detail), stringResource(R.string.link_refused), false,
    )
}

@Composable
private fun HomePage(link: LinkState) {
    val text = linkText(link)
    val tone = if (text.connected) SmartColors.Connected else SmartColors.Disconnected
    if (link is LinkState.Pairing) {
        PairingCode(link)
        return
    }
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 24.dp)) {
        StatusRing(tone)
        Spacer(Modifier.height(10.dp))
        Text(text.title, fontSize = 16.sp, fontWeight = FontWeight.Bold, color = tone.onSoft, textAlign = TextAlign.Center)
        // Solo el nombre del equipo cuando ya está conectado; si falta algo, el título lo dice.
        if (text.connected) {
            Text(text.detail, fontSize = 13.sp, color = SmartColors.TextMuted, textAlign = TextAlign.Center, maxLines = 1)
        }
        Spacer(Modifier.height(14.dp))
        SwipeHint()
    }
}

/**
 * Vinculación: el mismo código aparece en Windows. Basta con comprobar que coincide y pulsar
 * «Vincular» allí; en el reloj no hay que tocar nada.
 */
@Composable
private fun PairingCode(link: LinkState.Pairing) {
    val spokenCode = stringResource(R.string.pairing_code_a11y, link.code.toCharArray().joinToString(" "))
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 26.dp)) {
        Text(stringResource(R.string.pairing_title), fontSize = 13.sp, fontWeight = FontWeight.Medium, color = SmartColors.TextMuted)
        Spacer(Modifier.height(4.dp))
        Text(
            link.code,
            fontSize = 34.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 4.sp,
            color = SmartColors.Brand.onSoft,
            // Las cifras separadas para que el lector de pantalla las lea una a una.
            modifier = Modifier.semantics { contentDescription = spokenCode },
        )
        Spacer(Modifier.height(6.dp))
        Text(
            stringResource(R.string.pairing_hint, link.pcName),
            fontSize = 12.sp,
            color = SmartColors.Text,
            textAlign = TextAlign.Center,
            maxLines = 3,
        )
    }
}

/** Icono del equipo dentro de un anillo que "respira" para mostrar el estado de un vistazo. */
@Composable
private fun StatusRing(tone: Tone) {
    val breathing = rememberInfiniteTransition()
    val pulse by breathing.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1400, easing = FastOutSlowInEasing), RepeatMode.Reverse),
    )
    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .size(50.dp)
            .drawBehind {
                drawCircle(
                    color = tone.main.copy(alpha = lerp(0.12f, 0.35f, pulse)),
                    radius = size.minDimension / 2f - 2.dp.toPx(),
                    style = Stroke(width = lerp(2f, 5f, pulse).dp.toPx()),
                )
            },
    ) {
        IconBadge(SmartIcons.Computer, tint = tone.onMain, background = tone.main, size = 36.dp)
    }
}

/** Invitación a deslizar: solo una flecha que sube y baja suavemente. */
@Composable
private fun SwipeHint() {
    val nudge = rememberInfiniteTransition()
    val shift by nudge.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(900, easing = FastOutSlowInEasing), RepeatMode.Reverse),
    )
    Icon(
        SmartIcons.Up,
        contentDescription = stringResource(R.string.swipe_up),
        tint = SmartColors.Brand.onSoft,
        modifier = Modifier
            .size(18.dp)
            .graphicsLayer { translationY = -shift * 4.dp.toPx() },
    )
}

/**
 * Pantalla de un modo: los dos gestos (el icono hace el giro) y, solo si falta, el aviso de conexión.
 * Cada fila de la guía también responde al toque (alternativa de prueba, sin indicarlo en pantalla).
 */
@Composable
private fun GesturePage(
    mode: ControlMode,
    link: LinkState,
    gesturesAvailable: Boolean,
    lastResult: CommandResult?,
    feedbackTick: Int,
    onCommand: (Command) -> Unit,
) {
    val tone = SmartColors.forMode(mode)
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = PAGE_PADDING)) {
        PageHeader(mode.icon, stringResource(mode.label))
        if (mode == ControlMode.MULTIMEDIA) {
            // Reproducir y pausar no tienen gesto: dos botones, órdenes distintas (sin alternar).
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp), modifier = Modifier.padding(bottom = ROW_GAP)) {
                listOf(Command.MEDIA_PLAY, Command.MEDIA_PAUSE).forEach { command ->
                    val done = (lastResult as? CommandResult.Executed)?.command == command ||
                        (lastResult as? CommandResult.Simulated)?.command == command
                    PlaybackButton(command, flashKey = if (done) feedbackTick else null, onClick = { onCommand(command) })
                }
            }
        } else if (!gesturesAvailable) {
            Text(
                stringResource(R.string.no_gyroscope),
                fontSize = 12.sp,
                color = SmartColors.TextMuted,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(bottom = ROW_GAP),
            )
        }
        Column(verticalArrangement = Arrangement.spacedBy(ROW_GAP)) {
            WristGesture.entries.forEach { gesture ->
                val command = commandFor(mode, gesture)
                val justDone = (lastResult as? CommandResult.Simulated)?.command == command ||
                    (lastResult as? CommandResult.Executed)?.command == command
                GestureRow(
                    gesture = gesture,
                    command = command,
                    flashKey = if (justDone) feedbackTick else null,
                    onClick = { onCommand(command) },
                )
            }
        }
        if (link !is LinkState.Connected) {
            Spacer(Modifier.height(8.dp))
            ConnectionChip(link)
        }
    }
}

/** Fila de la guía: el icono hace el giro que representa y la fila se ilumina cuando se reconoce. */
@Composable
private fun GestureRow(gesture: WristGesture, command: Command, flashKey: Int?, onClick: () -> Unit) {
    val tone = SmartColors.forCommand(command)
    val description = "${command.rowLabel}, ${gesture.label}"
    val demo = rememberInfiniteTransition()
    val swing by demo.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1300, easing = FastOutSlowInEasing), RepeatMode.Reverse),
    )
    val flash = remember { Animatable(0f) }
    LaunchedEffect(flashKey) {
        if (flashKey != null) {
            flash.snapTo(1f)
            flash.animateTo(0f, tween(900, easing = FastOutSlowInEasing))
        }
    }
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .graphicsLayer {
                val grow = 1f + 0.05f * flash.value
                scaleX = grow
                scaleY = grow
            }
            .clip(RoundedCornerShape(50))
            .background(lerp(SmartColors.Surface, tone.soft, flash.value))
            .border(1.dp, lerp(SmartColors.Surface, tone.main, flash.value), RoundedCornerShape(50))
            .clickable(onClick = onClick, role = Role.Button)
            .semantics(mergeDescendants = true) { contentDescription = description }
            .padding(ROW_PADDING),
    ) {
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .size(ROW_ICON)
                .clip(CircleShape)
                .background(tone.main),
        ) {
            Icon(
                gesture.icon,
                contentDescription = null,
                tint = tone.onMain,
                modifier = Modifier
                    .size(ROW_ICON * 0.55f)
                    // Demostración del gesto: el icono gira suavemente en su sentido.
                    .graphicsLayer { rotationZ = gesture.direction * lerp(-18f, 18f, swing) },
            )
        }
        Spacer(Modifier.width(10.dp))
        Text(command.rowLabel, fontSize = ROW_TEXT, fontWeight = FontWeight.Bold, color = tone.onSoft, maxLines = 1)
    }
}

/** Botón redondo de reproducir o pausar; se ilumina cuando Windows lo confirma. */
@Composable
private fun PlaybackButton(command: Command, flashKey: Int?, onClick: () -> Unit) {
    val tone = SmartColors.forCommand(command)
    val description = stringResource(command.label)
    val flash = remember { Animatable(0f) }
    LaunchedEffect(flashKey) {
        if (flashKey != null) {
            flash.snapTo(1f)
            flash.animateTo(0f, tween(900, easing = FastOutSlowInEasing))
        }
    }
    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .size(32.dp)
            .graphicsLayer {
                val grow = 1f + 0.12f * flash.value
                scaleX = grow
                scaleY = grow
            }
            .clip(CircleShape)
            .background(tone.main)
            .clickable(onClick = onClick, role = Role.Button)
            .semantics { contentDescription = description },
    ) {
        Icon(
            if (command == Command.MEDIA_PLAY) SmartIcons.Play else SmartIcons.Pause,
            contentDescription = null,
            tint = tone.onMain,
            modifier = Modifier.size(16.dp),
        )
    }
}

/**
 * Etiqueta corta de las filas de la guía: el título de la pantalla ya dice si son diapositivas o
 * canciones, y así las dos filas caben enteras en la pantalla redonda.
 */
private val Command.rowLabel: String
    @Composable get() = stringResource(
        when (this) {
            Command.SLIDE_NEXT, Command.MEDIA_NEXT_TRACK -> R.string.row_next
            Command.SLIDE_PREVIOUS, Command.MEDIA_PREVIOUS_TRACK -> R.string.row_previous
            else -> label
        },
    )

private val WristGesture.label: String
    @Composable get() = stringResource(
        when (this) {
            WristGesture.TWIST_OUT -> R.string.gesture_twist_out
            WristGesture.TWIST_IN -> R.string.gesture_twist_in
        },
    )

private val WristGesture.icon: ImageVector
    get() = when (this) {
        WristGesture.TWIST_OUT -> SmartIcons.TwistOut
        WristGesture.TWIST_IN -> SmartIcons.TwistIn
    }

/** Sentido de la animación de demostración: horario hacia fuera, antihorario hacia dentro. */
private val WristGesture.direction: Float
    get() = if (this == WristGesture.TWIST_OUT) 1f else -1f

/** Ajustes actuales y cómo cambiarlos (los guarda la actividad). */
data class SettingsState(
    val theme: ThemeChoice = ThemeChoice.SYSTEM,
    val onTheme: (ThemeChoice) -> Unit = {},
    val language: LanguageChoice = LanguageChoice.SYSTEM,
    val onLanguage: (LanguageChoice) -> Unit = {},
)

/** Ajustes: tema e idioma. Cada fila cambia a la siguiente opción al tocarla. */
@Composable
private fun SettingsPage(settings: SettingsState) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = PAGE_PADDING)) {
        PageHeader(SmartIcons.Settings, stringResource(R.string.settings_title))
        Column(verticalArrangement = Arrangement.spacedBy(ROW_GAP)) {
            SettingRow(SmartIcons.Theme, stringResource(R.string.settings_theme), settings.theme.label) {
                settings.onTheme(settings.theme.next(ThemeChoice.entries))
            }
            SettingRow(SmartIcons.Language, stringResource(R.string.settings_language), settings.language.label) {
                settings.onLanguage(settings.language.next(LanguageChoice.entries))
            }
        }
    }
}

private val ThemeChoice.label: String
    @Composable get() = stringResource(
        when (this) {
            ThemeChoice.SYSTEM -> R.string.theme_system
            ThemeChoice.LIGHT -> R.string.theme_light
            ThemeChoice.DARK -> R.string.theme_dark
        },
    )

private val LanguageChoice.label: String
    @Composable get() = stringResource(
        when (this) {
            LanguageChoice.SYSTEM -> R.string.language_system
            LanguageChoice.SPANISH -> R.string.language_spanish
            LanguageChoice.ENGLISH -> R.string.language_english
        },
    )

/** Fila de ajuste: icono y valor actual; el nombre solo lo lee el lector de pantalla. Tocarla cambia el valor. */
@Composable
private fun SettingRow(icon: ImageVector, name: String, value: String, onClick: () -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(50))
            .background(SmartColors.Surface)
            .clickable(onClick = onClick, role = Role.Button)
            .semantics(mergeDescendants = true) { contentDescription = "$name: $value" }
            .padding(ROW_PADDING),
    ) {
        IconBadge(icon, tint = SmartColors.Brand.onMain, background = SmartColors.Brand.main, size = ROW_ICON)
        Spacer(Modifier.width(10.dp))
        Text(value, fontSize = ROW_TEXT, fontWeight = FontWeight.Bold, color = SmartColors.Brand.onSoft, maxLines = 1)
    }
}

@Composable
private fun ConnectionChip(link: LinkState) {
    val text = linkText(link)
    val tone = if (text.connected) SmartColors.Connected else SmartColors.Disconnected
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(SmartColors.Surface)
            .padding(horizontal = 10.dp, vertical = 3.dp),
    ) {
        BreathingDot(SmartColors.Text)
        Spacer(Modifier.width(5.dp))
        Text(text.short, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = SmartColors.TextMuted)
    }
}

/** Punto de estado que "respira" suavemente para llamar la atención sin distraer. */
@Composable
private fun BreathingDot(color: Color) {
    val breathing = rememberInfiniteTransition()
    val alpha by breathing.animateFloat(
        initialValue = 0.35f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(900, easing = FastOutSlowInEasing), RepeatMode.Reverse),
    )
    Box(
        Modifier
            .size(8.dp)
            .graphicsLayer { this.alpha = alpha }
            .clip(CircleShape)
            .background(color),
    )
}

/** Puntos de pantalla en el borde derecho: el activo se alarga y toma el color de su pantalla. */
@Composable
private fun PageIndicator(pagerState: PagerState, modifier: Modifier = Modifier) {
    Column(verticalArrangement = Arrangement.spacedBy(5.dp), modifier = modifier) {
        Page.entries.forEach { page ->
            val selected = pagerState.currentPage == page.ordinal
            val length by animateDpAsState(if (selected) 18.dp else 6.dp, spring(dampingRatio = 0.7f))
            val color = if (selected) {
                SmartColors.Brand.main
            } else {
                SmartColors.Outline
            }
            Box(
                Modifier
                    .width(6.dp)
                    .height(length)
                    .clip(RoundedCornerShape(50))
                    .background(color),
            )
        }
    }
}

/**
 * Ventana de confirmación que aparece sobre la pantalla tras cada gesto y se cierra sola
 * (o al tocarla). Verde si Windows lo hizo, roja y con temblor si no se pudo realizar o si el
 * gesto no se reconoció. Los detalles técnicos van a [CommandLog].
 */
@Composable
private fun FeedbackOverlay(result: CommandResult?, tick: Int, onHidden: () -> Unit) {
    var visible by remember { mutableStateOf(false) }
    LaunchedEffect(visible) { if (!visible && tick > 0) onHidden() }
    val shake = remember { Animatable(0f) }
    val pop = remember { Animatable(1f) }
    LaunchedEffect(tick) {
        if (tick == 0 || result == null) return@LaunchedEffect
        visible = true
        launch {
            pop.snapTo(0.3f)
            pop.animateTo(1f, spring(dampingRatio = 0.4f, stiffness = Spring.StiffnessMediumLow))
        }
        if (result !is CommandResult.Simulated && result !is CommandResult.Executed) {
            launch {
                shake.animateTo(
                    targetValue = 0f,
                    animationSpec = keyframes {
                        durationMillis = 420
                        -10f at 60
                        9f at 130
                        -6f at 200
                        4f at 270
                        -2f at 340
                    },
                )
            }
        }
        delay(FEEDBACK_MILLIS)
        visible = false
    }

    val style = resultStyle(result)
    AnimatedVisibility(
        visible = visible && style != null,
        enter = fadeIn(tween(180)) + scaleIn(tween(260), initialScale = 0.85f),
        exit = fadeOut(tween(220)) + scaleOut(tween(220), targetScale = 1.08f),
    ) {
        if (style == null) return@AnimatedVisibility
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .fillMaxSize()
                .background(style.tone.soft)
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {
                    visible = false
                },
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                modifier = Modifier
                    .padding(horizontal = 28.dp)
                    .graphicsLayer { translationX = shake.value.dp.toPx() },
            ) {
                IconBadge(
                    icon = style.icon,
                    tint = style.tone.onMain,
                    background = style.tone.main,
                    size = 48.dp,
                    modifier = Modifier.graphicsLayer {
                        scaleX = pop.value
                        scaleY = pop.value
                    },
                )
                Spacer(Modifier.height(12.dp))
                Text(
                    style.text,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.Bold,
                    color = style.tone.onSoft,
                    textAlign = TextAlign.Center,
                    maxLines = 2,
                )
            }
        }
    }
}

/** Resultado en pantalla: un icono (visto o X) y una sola frase. Los detalles van a [CommandLog]. */
private data class ResultStyle(val icon: ImageVector, val tone: Tone, val text: String)

@Composable
private fun resultStyle(result: CommandResult?): ResultStyle? = when (result) {
    null -> null
    is CommandResult.Executed -> ResultStyle(SmartIcons.Check, SmartColors.Success, result.command.feedbackLabel)
    // Solo en pruebas sin Windows: pasó la validación, pero no se envió a ningún equipo.
    is CommandResult.Simulated -> ResultStyle(SmartIcons.Check, SmartColors.Success, result.command.feedbackLabel)
    is CommandResult.MovementIgnored -> ResultStyle(SmartIcons.Close, SmartColors.Rejected, stringResource(R.string.result_not_recognized))
    is CommandResult.Failed -> ResultStyle(SmartIcons.Close, SmartColors.Rejected, stringResource(result.failure.label))
    is CommandResult.Rejected -> ResultStyle(SmartIcons.Close, SmartColors.Rejected, stringResource(result.reason.label))
    // Un movimiento que no supera las reglas o el umbral de confianza no genera ningún comando.
    is CommandResult.GestureUnrecognized -> ResultStyle(SmartIcons.Close, SmartColors.Rejected, stringResource(R.string.result_not_recognized))
}

/** Descripción natural de cada acción para los mensajes de resultado. */
private val Command.feedbackLabel: String
    @Composable get() = stringResource(
        when (this) {
            Command.SLIDE_NEXT -> R.string.feedback_slide_next
            Command.SLIDE_PREVIOUS -> R.string.feedback_slide_previous
            Command.MEDIA_PLAY -> R.string.feedback_play
            Command.MEDIA_PAUSE -> R.string.feedback_pause
            Command.MEDIA_NEXT_TRACK -> R.string.feedback_next_track
            Command.MEDIA_PREVIOUS_TRACK -> R.string.feedback_previous_track
        },
    )

@Composable
private fun IconBadge(
    icon: ImageVector,
    tint: Color,
    background: Color,
    size: Dp,
    modifier: Modifier = Modifier,
) {
    Box(
        contentAlignment = Alignment.Center,
        modifier = modifier
            .size(size)
            .clip(CircleShape)
            .background(background),
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(size * 0.55f))
    }
}

private val ControlMode.icon: ImageVector
    get() = when (this) {
        ControlMode.PRESENTATION -> SmartIcons.Presentation
        ControlMode.MULTIMEDIA -> SmartIcons.Multimedia
    }

@WearPreviewDevices
@Composable
private fun SmartControlPreview() {
    SmartControlApp()
}

@WearPreviewDevices
@Composable
private fun PresentationPreview() {
    SmartControlTheme {
        SmartControlScreen(
            state = ControlState(),
            link = LinkState.Connected("PORTÁTIL"),
            feedbackTick = 0,
            gesturesAvailable = true,
            onSelectMode = {},
            onModePageChange = {},
            onCommand = {},
            initialPage = Page.PRESENTATION.ordinal,
        )
    }
}
