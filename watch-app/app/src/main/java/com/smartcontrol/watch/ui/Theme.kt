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

/** Familia de un color: tono principal, versión oscura (degradados), fondo suave y textos encima. */
@Immutable
data class Tone(
    val main: Color,
    val dark: Color,
    val soft: Color,
    val onMain: Color,
    val onSoft: Color,
)

object SmartColors {
    // Base clara
    val Background = Color(0xFFF2F5FB)
    val Surface = Color(0xFFFFFFFF)
    val Text = Color(0xFF151C2B)
    val TextMuted = Color(0xFF5E687A)
    val Outline = Color(0xFFDCE2EC)
    val Transparent = Color.Transparent

    // Familias de color
    val Blue = Tone(
        main = Color(0xFF2563EB), dark = Color(0xFF1D4ED8),
        soft = Color(0xFFE3ECFF), onMain = Color.White, onSoft = Color(0xFF1E3A8A),
    )
    val Indigo = Tone(
        main = Color(0xFF4F6BF0), dark = Color(0xFF3B4FD0),
        soft = Color(0xFFE6EAFE), onMain = Color.White, onSoft = Color(0xFF2B378F),
    )
    val Violet = Tone(
        main = Color(0xFF7C4DEB), dark = Color(0xFF6236CF),
        soft = Color(0xFFEFE8FF), onMain = Color.White, onSoft = Color(0xFF4B2596),
    )
    val Green = Tone(
        main = Color(0xFF16A35A), dark = Color(0xFF0F8547),
        soft = Color(0xFFE1F6EA), onMain = Color.White, onSoft = Color(0xFF0B5E33),
    )
    val Amber = Tone(
        main = Color(0xFFF08C00), dark = Color(0xFFD27400),
        soft = Color(0xFFFFF1DC), onMain = Color.White, onSoft = Color(0xFF8A4B00),
    )
    val Red = Tone(
        main = Color(0xFFDC3B30), dark = Color(0xFFB92C23),
        soft = Color(0xFFFDE8E6), onMain = Color.White, onSoft = Color(0xFF9A2019),
    )
    val Gray = Tone(
        main = Color(0xFF8A94A6), dark = Color(0xFF6B7488),
        soft = Color(0xFFF0F2F6), onMain = Color.White, onSoft = Color(0xFF4A5366),
    )

    // Significado de cada color en la app
    val Brand = Blue
    val Success = Green
    val Rejected = Red
    val Idle = Gray
    val Disconnected = Red
    val Connected = Green

    /** Degradado del título "SmartControl". */
    val TitleGradient = listOf(Blue.main, Violet.main)

    fun forMode(mode: ControlMode): Tone = when (mode) {
        ControlMode.PRESENTATION -> Blue
        ControlMode.MULTIMEDIA -> Violet
    }

    fun forCommand(command: Command): Tone = when (command) {
        Command.SLIDE_NEXT -> Blue
        Command.SLIDE_PREVIOUS -> Indigo
        Command.MEDIA_PLAY -> Green
        Command.MEDIA_PAUSE -> Amber
    }
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
    error = SmartColors.Red.main,
    errorContainer = SmartColors.Red.soft,
    onError = SmartColors.Red.onMain,
    onErrorContainer = SmartColors.Red.onSoft,
)

@Composable
fun SmartControlTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = LightColors, content = content)
}
