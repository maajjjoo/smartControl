package com.smartcontrol.watch.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
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
 */
object SmartColors {
    // Base neutra
    val Background = Color(0xFFFFFFFF)
    val Surface = Color(0xFFF4F4F5)
    val Text = Color(0xFF18181B)
    val TextMuted = Color(0xFF63636B)
    val Outline = Color(0xFFE4E4E7)
    val Transparent = Color.Transparent

    /** El color principal: vinotinto, con rosado de fondo. Contraste del texto ≥ 9:1. */
    val Wine = Tone(
        main = Color(0xFF8E1C3A), dark = Color(0xFF761530),
        soft = Color(0xFFFBEAF0), onMain = Color.White, onSoft = Color(0xFF6E1530),
    )

    /** Grises: estados inactivos (buscando, sin Wi-Fi). */
    val Neutral = Tone(
        main = Color(0xFF71717A), dark = Color(0xFF52525B),
        soft = Color(0xFFF4F4F5), onMain = Color.White, onSoft = Color(0xFF3F3F46),
    )

    /**
     * Error discreto: la X en rojo sobre un círculo blanco, con fondo y texto grises. [main] es el
     * fondo del círculo y [onMain] el color de la X.
     */
    val Error = Tone(
        main = Color.White, dark = Color.White,
        soft = Color(0xFFF4F4F5), onMain = Color(0xFFC62828), onSoft = Color(0xFF3F3F46),
    )

    // Significado de cada color en la app
    val Brand = Wine
    val Success = Wine
    val Connected = Wine
    val Rejected = Error
    val Idle = Neutral
    val Disconnected = Neutral

    /** Mismo color para los dos modos: la pantalla los distingue por título e icono. */
    fun forMode(@Suppress("UNUSED_PARAMETER") mode: ControlMode): Tone = Wine

    /** Mismo color para todos los comandos: el texto y el icono dicen cuál es. */
    fun forCommand(@Suppress("UNUSED_PARAMETER") command: Command): Tone = Wine
}

private val LightColors = ColorScheme(
    primary = SmartColors.Brand.main,
    primaryDim = SmartColors.Brand.dark,
    primaryContainer = SmartColors.Brand.soft,
    onPrimary = SmartColors.Brand.onMain,
    onPrimaryContainer = SmartColors.Brand.onSoft,
    secondary = SmartColors.Brand.main,
    secondaryContainer = SmartColors.Brand.soft,
    onSecondary = SmartColors.Brand.onMain,
    onSecondaryContainer = SmartColors.Brand.onSoft,
    surfaceContainerLow = SmartColors.Surface,
    surfaceContainer = SmartColors.Surface,
    surfaceContainerHigh = SmartColors.Brand.soft,
    onSurface = SmartColors.Text,
    onSurfaceVariant = SmartColors.TextMuted,
    outline = SmartColors.Outline,
    outlineVariant = SmartColors.Outline,
    background = SmartColors.Background,
    onBackground = SmartColors.Text,
    error = SmartColors.Error.onMain,
    errorContainer = SmartColors.Error.soft,
    onError = Color.White,
    onErrorContainer = SmartColors.Error.onSoft,
)

@Composable
fun SmartControlTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = LightColors, content = content)
}
