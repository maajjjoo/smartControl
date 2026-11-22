package com.smartcontrol.watch.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
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
 * Diseño minimalista con un solo color principal (decisión del 2026-11-22): vinotinto para todo lo
 * que se toca o está activo, su versión rosada para fondos suaves, y el resto en grises neutros.
 * La única excepción es el error: un rojo más vivo, siempre sobre gris y con una X, para que no se
 * confunda con el vinotinto de un acierto (que va sobre rosado y con una ✓).
 *
 * Hay dos paletas, clara y oscura, con los mismos significados. En la oscura el fondo es casi negro
 * (ahorra batería en pantallas OLED) y el vinotinto se aclara a un rosa vino que se lee sobre negro.
 */

/** Una paleta completa: base neutra y las tres familias de color. */
@Immutable
data class SmartPalette(
    val background: Color,
    val surface: Color,
    val text: Color,
    val textMuted: Color,
    val outline: Color,
    val wine: Tone,
    val neutral: Tone,
    val error: Tone,
)

/** Paleta clara. Contraste del texto vinotinto sobre blanco ≥ 9:1. */
val LightPalette = SmartPalette(
    background = Color(0xFFFFFFFF),
    surface = Color(0xFFF4F4F5),
    text = Color(0xFF18181B),
    textMuted = Color(0xFF63636B),
    outline = Color(0xFFE4E4E7),
    wine = Tone(
        main = Color(0xFF8E1C3A), dark = Color(0xFF761530),
        soft = Color(0xFFFBEAF0), onMain = Color.White, onSoft = Color(0xFF6E1530),
    ),
    neutral = Tone(
        main = Color(0xFF71717A), dark = Color(0xFF52525B),
        soft = Color(0xFFF4F4F5), onMain = Color.White, onSoft = Color(0xFF3F3F46),
    ),
    // Error discreto: X roja ([onMain]) sobre un círculo blanco ([main]), fondo y texto grises.
    error = Tone(
        main = Color.White, dark = Color.White,
        soft = Color(0xFFF4F4F5), onMain = Color(0xFFC62828), onSoft = Color(0xFF3F3F46),
    ),
)

/** Paleta oscura. Rosa vino sobre casi negro ≥ 7:1; texto claro sobre fondo oscuro ≥ 15:1. */
val DarkPalette = SmartPalette(
    background = Color(0xFF0E0E10),
    surface = Color(0xFF1C1C1F),
    text = Color(0xFFF4F4F5),
    textMuted = Color(0xFFA1A1AA),
    outline = Color(0xFF2E2E33),
    wine = Tone(
        main = Color(0xFFEC7A99), dark = Color(0xFFD9607F),
        soft = Color(0xFF3A1622), onMain = Color(0xFF1A0710), onSoft = Color(0xFFF7C6D4),
    ),
    neutral = Tone(
        main = Color(0xFF8E8E96), dark = Color(0xFF71717A),
        soft = Color(0xFF1C1C1F), onMain = Color(0xFF0E0E10), onSoft = Color(0xFFD4D4D8),
    ),
    error = Tone(
        main = Color(0xFF2A2A2E), dark = Color(0xFF2A2A2E),
        soft = Color(0xFF1C1C1F), onMain = Color(0xFFFF6B6B), onSoft = Color(0xFFD4D4D8),
    ),
)

private val LocalPalette = staticCompositionLocalOf { LightPalette }

/**
 * ÚNICO punto de acceso a los colores. Cada valor sale de la paleta activa (clara u oscura), así que
 * el resto de la interfaz escribe `SmartColors.Brand` igual en los dos temas.
 */
object SmartColors {
    val Background: Color @Composable @ReadOnlyComposable get() = LocalPalette.current.background
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
    primaryContainer = p.wine.soft,
    onPrimary = p.wine.onMain,
    onPrimaryContainer = p.wine.onSoft,
    secondary = p.wine.main,
    secondaryContainer = p.wine.soft,
    onSecondary = p.wine.onMain,
    onSecondaryContainer = p.wine.onSoft,
    surfaceContainerLow = p.surface,
    surfaceContainer = p.surface,
    surfaceContainerHigh = p.wine.soft,
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
