package com.smartcontrol.watch.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.wear.compose.material3.ColorScheme
import androidx.wear.compose.material3.MaterialTheme
import com.smartcontrol.watch.Command
import com.smartcontrol.watch.ControlMode

/*
 * ÚNICO archivo de colores de la app. Todos los colores de la interfaz salen de aquí:
 * cambia un valor y se actualiza en toda la pantalla. Los demás archivos de ui/ no
 * definen colores propios.
 */

/** Familia de un color: tono principal, versión oscura (pulsado), fondo suave y textos encima. */
@Immutable
data class Tone(
    val main: Color,
    val dark: Color,
    val soft: Color,
    val onMain: Color,
    val onSoft: Color,
)

/*
 * Diseño con un solo color que llena toda la pantalla: el vinotinto es el fondo (un degradado con un
 * brillo suave en el centro), los textos son blancos, las filas son de "cristal" (blanco
 * translúcido) y los círculos son blancos con el icono en vinotinto. El error usa gris grafito y
 * una X roja, para que nunca se confunda con un acierto.
 *
 * Dos paletas con el mismo vinotinto: la clara es más luminosa y la oscura más profunda (gasta
 * menos batería en pantallas OLED).
 */

private val Wine = Color(0xFF8E1C3A)

/** Una paleta completa: fondo degradado ([glow] en el centro, [background] en el borde) y colores. */
@Immutable
data class SmartPalette(
    val background: Color,
    val glow: Color,
    val surface: Color,
    val text: Color,
    val textMuted: Color,
    val outline: Color,
    val wine: Tone,
    val neutral: Tone,
    val error: Tone,
)

/** Paleta clara: vinotinto luminoso. Texto blanco sobre vinotinto ≥ 8:1. */
val LightPalette = SmartPalette(
    background = Color(0xFF6E1530),
    glow = Color(0xFFB02A52),
    surface = Color.White.copy(alpha = 0.14f),
    text = Color.White,
    textMuted = Color.White.copy(alpha = 0.75f),
    outline = Color.White.copy(alpha = 0.30f),
    wine = Tone(
        main = Color.White, dark = Color(0xFFF4F4F5),
        soft = Wine, onMain = Wine, onSoft = Color.White,
    ),
    neutral = Tone(
        main = Color.White.copy(alpha = 0.22f), dark = Color.White.copy(alpha = 0.30f),
        soft = Color.White.copy(alpha = 0.14f), onMain = Color.White, onSoft = Color.White.copy(alpha = 0.85f),
    ),
    error = Tone(
        main = Color.White, dark = Color.White,
        soft = Color(0xFF26262A), onMain = Color(0xFFC62828), onSoft = Color.White,
    ),
)

/** Paleta oscura: el mismo vinotinto, más profundo. */
val DarkPalette = SmartPalette(
    background = Color(0xFF1E0610),
    glow = Color(0xFF5E1229),
    surface = Color.White.copy(alpha = 0.09f),
    text = Color.White,
    textMuted = Color.White.copy(alpha = 0.70f),
    outline = Color.White.copy(alpha = 0.22f),
    wine = Tone(
        main = Color.White, dark = Color(0xFFF4F4F5),
        soft = Color(0xFF4A0E20), onMain = Wine, onSoft = Color.White,
    ),
    neutral = Tone(
        main = Color.White.copy(alpha = 0.16f), dark = Color.White.copy(alpha = 0.24f),
        soft = Color.White.copy(alpha = 0.09f), onMain = Color.White, onSoft = Color.White.copy(alpha = 0.85f),
    ),
    error = Tone(
        main = Color(0xFF2E2E33), dark = Color(0xFF2E2E33),
        soft = Color(0xFF141416), onMain = Color(0xFFFF6B6B), onSoft = Color.White,
    ),
)

private val LocalPalette = staticCompositionLocalOf { LightPalette }

/**
 * ÚNICO punto de acceso a los colores. Cada valor sale de la paleta activa (clara u oscura), así que
 * el resto de la interfaz escribe `SmartColors.Brand` igual en los dos temas.
 */
object SmartColors {
    val Background: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.background

    /** Fondo de pantalla: degradado radial vinotinto, más brillante en el centro. */
    val BackgroundBrush: Brush
        @Composable @ReadOnlyComposable get() = LocalPalette.current.let { Brush.radialGradient(listOf(it.glow, it.background)) }
    val Surface: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.surface
    val Text: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.text
    val TextMuted: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.textMuted
    val Outline: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.outline
    val Transparent = Color.Transparent

    // Significado de cada color en la app
    val Brand: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.wine
    val Success: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.wine
    val Connected: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.wine
    val Rejected: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.error
    val Idle: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.neutral
    val Disconnected: Tone @Composable @ReadOnlyComposable get() = LocalPalette.current.neutral

    /** Mismo color para los dos modos: la pantalla los distingue por título e icono. */
    @Composable
    @ReadOnlyComposable
    fun forMode(@Suppress("UNUSED_PARAMETER") mode: ControlMode): Tone = LocalPalette.current.wine

    /** Mismo color para todos los comandos: el texto y el icono dicen cuál es. */
    @Composable
    @ReadOnlyComposable
    fun forCommand(@Suppress("UNUSED_PARAMETER") command: Command): Tone = LocalPalette.current.wine
}

private fun colorScheme(p: SmartPalette) = ColorScheme(
    primary = p.wine.main,
    primaryDim = p.wine.dark,
    primaryContainer = p.surface,
    onPrimary = p.wine.onMain,
    onPrimaryContainer = p.wine.onSoft,
    secondary = p.wine.main,
    secondaryContainer = p.wine.soft,
    onSecondary = p.wine.onMain,
    onSecondaryContainer = p.wine.onSoft,
    surfaceContainerLow = p.surface,
    surfaceContainer = p.surface,
    surfaceContainerHigh = p.surface,
    onSurface = p.text,
    onSurfaceVariant = p.textMuted,
    outline = p.outline,
    outlineVariant = p.outline,
    background = p.background,
    onBackground = p.text,
    error = p.error.onMain,
    errorContainer = p.error.soft,
    onError = p.background,
    onErrorContainer = p.error.onSoft,
)

/** Tema de la app: oscuro o claro según [dark] (que decide el ajuste «Tema» o el sistema). */
@Composable
fun SmartControlTheme(dark: Boolean = false, content: @Composable () -> Unit) {
    val palette = if (dark) DarkPalette else LightPalette
    CompositionLocalProvider(LocalPalette provides palette) {
        MaterialTheme(colorScheme = colorScheme(palette), content = content)
    }
}
