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
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.rotary.onRotaryScrollEvent
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.TextStyle
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
import com.smartcontrol.watch.ControlMode
import com.smartcontrol.watch.ControlState
import com.smartcontrol.watch.commandFor
import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.WristGesture
import kotlin.math.abs
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.launch

/** Pantallas completas que se recorren deslizando hacia arriba o abajo (o girando la corona). */
private enum class Page(val mode: ControlMode?) {
    HOME(null),
    PRESENTATION(ControlMode.PRESENTATION),
    MULTIMEDIA(ControlMode.MULTIMEDIA),
    HELP(null),
}

/** Duración de la ventana de confirmación antes de cerrarse sola. */
private const val FEEDBACK_MILLIS = 2200L

/** Tras cerrarse la ventana, la guía ignora toques durante este tiempo. */
private const val TOUCH_GUARD_MILLIS = 400L

/**
 * La interacción principal son los gestos de muñeca ([gestureEvents], del giroscopio). Tocar la
 * guía de gestos envía el mismo comando, solo como alternativa para pruebas.
 *
 * [simulationEnabled] se mantiene en la lógica para desarrollo y pruebas, pero no se muestra:
 * la interfaz solo indica si el comando se aceptó o no (ver MainActivity).
 */
@Composable
fun SmartControlApp(
    simulationEnabled: Boolean = true,
    gestureEvents: Flow<GestureEvent> = emptyFlow(),
    gesturesAvailable: Boolean = false,
) {
    var state by remember { mutableStateOf(ControlState(simulationEnabled = simulationEnabled)) }
    // Cambia en cada acción para volver a mostrar la confirmación aunque se repita el comando.
    var feedbackTick by remember { mutableIntStateOf(0) }
    // Los gestos solo cuentan en una pantalla de modo, nunca en Inicio ni en Ayuda.
    var onModePage by remember { mutableStateOf(false) }
    val haptics = LocalHapticFeedback.current

    fun apply(next: ControlState) {
        state = next
        feedbackTick++
        CommandLog.record(state)
        haptics.performHapticFeedback(
            if (state.lastResult is CommandResult.Simulated) {
                HapticFeedbackType.Confirm
            } else {
                HapticFeedbackType.Reject
            },
        )
    }

    LaunchedEffect(gestureEvents) {
        gestureEvents.collect { event -> if (onModePage) apply(state.onGesture(event)) }
    }

    SmartControlTheme {
        AppScaffold {
            SmartControlScreen(
                state = state,
                feedbackTick = feedbackTick,
                gesturesAvailable = gesturesAvailable,
                onSelectMode = { state = state.selectMode(it) },
                onModePageChange = { onModePage = it },
                onCommand = { command -> apply(state.sendCommand(command)) },
            )
        }
    }
}

@Composable
fun SmartControlScreen(
    state: ControlState,
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
            .drawBehind { drawRect(pageBackground(pagerState)) },
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
                    Page.HOME -> HomePage(state.connection)
                    Page.HELP -> HelpPage()
                    else -> GesturePage(
                        mode = page.mode!!,
                        connection = state.connection,
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

/** Color de fondo que se mezcla poco a poco entre la pantalla actual y la siguiente al deslizar. */
private fun pageBackground(pagerState: PagerState): Color {
    val position = (pagerState.currentPage + pagerState.currentPageOffsetFraction)
        .coerceIn(0f, (Page.entries.size - 1).toFloat())
    val from = position.toInt()
    val to = (from + 1).coerceAtMost(Page.entries.size - 1)
    return lerp(Page.entries[from].background, Page.entries[to].background, position - from)
}

private val Page.background: Color
    get() = mode?.let { SmartColors.forMode(it).soft } ?: SmartColors.Background

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

@Composable
private fun HomePage(connection: ConnectionStatus) {
    val connected = connection == ConnectionStatus.CONNECTED
    val tone = if (connected) SmartColors.Connected else SmartColors.Disconnected
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 24.dp)) {
        BasicText(
            text = "SmartControl",
            style = TextStyle(
                brush = Brush.linearGradient(SmartColors.TitleGradient),
                fontSize = 19.sp,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
            ),
        )
        Spacer(Modifier.height(6.dp))
        StatusRing(tone)
        Spacer(Modifier.height(6.dp))
        Text(connection.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = tone.main)
        Text(
            if (connected) "Equipo Windows" else "Equipo Windows sin vincular",
            fontSize = 12.sp,
            color = SmartColors.TextMuted,
        )
        Spacer(Modifier.height(10.dp))
        SwipeHint()
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
            .size(62.dp)
            .drawBehind {
                drawCircle(
                    color = tone.main.copy(alpha = lerp(0.12f, 0.35f, pulse)),
                    radius = size.minDimension / 2f - 2.dp.toPx(),
                    style = Stroke(width = lerp(2f, 5f, pulse).dp.toPx()),
                )
            },
    ) {
        IconBadge(SmartIcons.Computer, tint = tone.main, background = tone.soft, size = 46.dp)
    }
}

/** Invitación a deslizar, con una flecha que sube y baja suavemente. */
@Composable
private fun SwipeHint() {
    val nudge = rememberInfiniteTransition()
    val shift by nudge.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(900, easing = FastOutSlowInEasing), RepeatMode.Reverse),
    )
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(SmartColors.Surface)
            .border(1.dp, SmartColors.Outline, RoundedCornerShape(50))
            .padding(start = 8.dp, end = 12.dp, top = 4.dp, bottom = 4.dp),
    ) {
        Icon(
            SmartIcons.Up,
            contentDescription = null,
            tint = SmartColors.Brand.main,
            modifier = Modifier
                .size(18.dp)
                .graphicsLayer { translationY = -shift * 3.dp.toPx() },
        )
        Spacer(Modifier.width(4.dp))
        Text("Desliza hacia arriba", fontSize = 12.sp, fontWeight = FontWeight.Medium, color = SmartColors.Brand.onSoft)
    }
}

/**
 * Pantalla de un modo: indicación de usar gestos, guía con los dos gestos y estado de conexión.
 * Cada fila de la guía también responde al toque (alternativa de prueba, sin indicarlo en pantalla).
 */
@Composable
private fun GesturePage(
    mode: ControlMode,
    connection: ConnectionStatus,
    gesturesAvailable: Boolean,
    lastResult: CommandResult?,
    feedbackTick: Int,
    onCommand: (Command) -> Unit,
) {
    val tone = SmartColors.forMode(mode)
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 26.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(mode.icon, contentDescription = null, tint = tone.main, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(6.dp))
            Text(mode.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = tone.onSoft)
        }
        Spacer(Modifier.height(2.dp))
        Text(
            if (gesturesAvailable) "Realiza un gesto para controlar tu equipo" else "Este reloj no tiene sensor de giro",
            fontSize = 12.sp,
            color = SmartColors.TextMuted,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(8.dp))
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            WristGesture.entries.forEach { gesture ->
                val command = commandFor(mode, gesture)
                val justDone = (lastResult as? CommandResult.Simulated)?.command == command
                GestureRow(
                    gesture = gesture,
                    command = command,
                    flashKey = if (justDone) feedbackTick else null,
                    onClick = { onCommand(command) },
                )
            }
        }
        Spacer(Modifier.height(8.dp))
        ConnectionChip(connection)
    }
}

/** Fila de la guía: el icono hace el giro que representa y la fila se ilumina cuando se reconoce. */
@Composable
private fun GestureRow(gesture: WristGesture, command: Command, flashKey: Int?, onClick: () -> Unit) {
    val tone = SmartColors.forCommand(command)
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
            .border(1.dp, lerp(SmartColors.Outline, tone.main, flash.value), RoundedCornerShape(50))
            .clickable(onClick = onClick, role = Role.Button)
            .padding(start = 4.dp, end = 12.dp, top = 4.dp, bottom = 4.dp),
    ) {
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .size(34.dp)
                .clip(CircleShape)
                .background(Brush.verticalGradient(listOf(tone.main, tone.dark))),
        ) {
            Icon(
                gesture.icon,
                contentDescription = null,
                tint = tone.onMain,
                modifier = Modifier
                    .size(20.dp)
                    // Demostración del gesto: el icono gira suavemente en su sentido.
                    .graphicsLayer { rotationZ = gesture.direction * lerp(-18f, 18f, swing) },
            )
        }
        Spacer(Modifier.width(8.dp))
        Column {
            Text(command.label, fontSize = 14.sp, fontWeight = FontWeight.Bold, color = tone.onSoft, maxLines = 1)
            Text(gesture.label, fontSize = 12.sp, color = SmartColors.TextMuted, maxLines = 1)
        }
    }
}

private val WristGesture.label: String
    get() = when (this) {
        WristGesture.TWIST_OUT -> "Gira hacia fuera"
        WristGesture.TWIST_IN -> "Gira hacia dentro"
    }

private val WristGesture.icon: ImageVector
    get() = when (this) {
        WristGesture.TWIST_OUT -> SmartIcons.TwistOut
        WristGesture.TWIST_IN -> SmartIcons.TwistIn
    }

/** Sentido de la animación de demostración: horario hacia fuera, antihorario hacia dentro. */
private val WristGesture.direction: Float
    get() = if (this == WristGesture.TWIST_OUT) 1f else -1f

/** Ayuda: tres pasos para usar la app, cada uno con su icono. */
@Composable
private fun HelpPage() {
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(horizontal = 24.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(SmartIcons.Help, contentDescription = null, tint = SmartColors.Brand.main, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(6.dp))
            Text("Cómo usarlo", fontSize = 17.sp, fontWeight = FontWeight.Bold, color = SmartColors.Brand.onSoft)
        }
        Spacer(Modifier.height(10.dp))
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            HelpStep(SmartIcons.Swipe, SmartColors.Blue, "Elige un modo")
            HelpStep(SmartIcons.TwistOut, SmartColors.Violet, "Gira la muñeca")
            HelpStep(SmartIcons.Check, SmartColors.Green, "Mira el resultado")
        }
    }
}

@Composable
private fun HelpStep(icon: ImageVector, tone: Tone, text: String) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(SmartColors.Surface)
            .padding(start = 4.dp, end = 12.dp, top = 4.dp, bottom = 4.dp),
    ) {
        IconBadge(icon, tint = tone.main, background = tone.soft, size = 30.dp)
        Spacer(Modifier.width(8.dp))
        Text(text, fontSize = 13.sp, fontWeight = FontWeight.Medium, color = SmartColors.Text)
    }
}

@Composable
private fun ConnectionChip(connection: ConnectionStatus) {
    val tone = if (connection == ConnectionStatus.CONNECTED) SmartColors.Connected else SmartColors.Disconnected
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(SmartColors.Surface)
            .padding(horizontal = 10.dp, vertical = 3.dp),
    ) {
        BreathingDot(tone.main)
        Spacer(Modifier.width(5.dp))
        Text(connection.label, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = tone.main)
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
                page.mode?.let { SmartColors.forMode(it).main } ?: SmartColors.Brand.main
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
 * (o al tocarla). Verde si el comando se aceptó, roja y con temblor si no se pudo realizar o si
 * el gesto no se reconoció.
 * No afirma que Windows haya ejecutado nada; los detalles técnicos van a [CommandLog].
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
        if (result !is CommandResult.Simulated) {
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
                    size = 64.dp,
                    modifier = Modifier.graphicsLayer {
                        scaleX = pop.value
                        scaleY = pop.value
                    },
                )
                Spacer(Modifier.height(10.dp))
                Text(
                    style.title,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.Bold,
                    color = style.tone.onSoft,
                    textAlign = TextAlign.Center,
                    maxLines = 2,
                )
                Spacer(Modifier.height(2.dp))
                Text(style.detail, fontSize = 13.sp, color = SmartColors.Text, textAlign = TextAlign.Center, maxLines = 2)
            }
        }
    }
}

private data class ResultStyle(
    val icon: ImageVector,
    val tone: Tone,
    val title: String,
    val detail: String,
)

private fun resultStyle(result: CommandResult?): ResultStyle? = when (result) {
    null -> null
    // "Aceptado" = pasó la validación; nunca se dice que Windows lo haya ejecutado.
    is CommandResult.Simulated -> ResultStyle(
        icon = SmartIcons.Check,
        tone = SmartColors.Success,
        title = "Comando aceptado",
        detail = result.command.feedbackLabel,
    )
    is CommandResult.Rejected -> ResultStyle(
        icon = SmartIcons.Close,
        tone = SmartColors.Rejected,
        title = "No se pudo realizar la acción",
        detail = result.reason.label,
    )
    // Un movimiento que no supera las reglas o el umbral de confianza no genera ningún comando.
    is CommandResult.GestureUnrecognized -> ResultStyle(
        icon = SmartIcons.Close,
        tone = SmartColors.Rejected,
        title = "Gesto no reconocido",
        detail = "Prueba con un giro más claro",
    )
}

/** Descripción natural de cada acción para los mensajes de resultado. */
private val Command.feedbackLabel: String
    get() = when (this) {
        Command.SLIDE_NEXT -> "Diapositiva siguiente"
        Command.SLIDE_PREVIOUS -> "Diapositiva anterior"
        Command.MEDIA_PLAY -> "Reproducción"
        Command.MEDIA_PAUSE -> "Pausa"
    }

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
            feedbackTick = 0,
            gesturesAvailable = true,
            onSelectMode = {},
            onModePageChange = {},
            onCommand = {},
            initialPage = Page.PRESENTATION.ordinal,
        )
    }
}
