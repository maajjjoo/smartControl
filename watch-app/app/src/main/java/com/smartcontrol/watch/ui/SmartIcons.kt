package com.smartcontrol.watch.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.unit.dp

/**
 * Iconos propios de 24x24, de trazo redondeado (estilo lineal moderno), dibujados aquí para no
 * añadir dependencias. Se tiñen con `tint` al dibujarlos, así que el color sale de SmartColors.
 */
object SmartIcons {
    // Debe declararse antes que los iconos: se usa al construirlos.
    // El negro es solo la base: al dibujar, el tint (de SmartColors) lo sustituye.
    private val base = SolidColor(Color.Black)

    val Up = stroke("Up", "M6.5 14.5L12 9l5.5 5.5", width = 2.4f)

    /** Pantalla de proyección sobre un soporte, con una diapositiva dentro. */
    val Presentation = stroke(
        "Presentation",
        "M3 4h18 M4.5 4v9.5a1.5 1.5 0 0 0 1.5 1.5h12a1.5 1.5 0 0 0 1.5-1.5V4 M12 15v3 M8.5 21l3.5-3 3.5 3 M8.5 9.5l2.5-2 2 1.6 2.5-2",
    )

    /** Dos notas musicales unidas. */
    val Multimedia = stroke(
        "Multimedia",
        "M9 17.5V6l11-2v11.5 M9 17.5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z M20 15.5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z",
    )

    /** Monitor de escritorio. */
    val Computer = stroke(
        "Computer",
        "M4.5 4.5h15a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 15V6a1.5 1.5 0 0 1 1.5-1.5z M9 20.5h6 M12 16.5v4",
    )

    val Check = stroke("Check", "M5.5 12.5l4.2 4.2L18.5 8", width = 2.8f)
    val Close = stroke("Close", "M7.5 7.5l9 9 M16.5 7.5l-9 9", width = 2.8f)

    /** Giro de muñeca hacia fuera: flecha circular en el sentido de las agujas del reloj. */
    val TwistOut = stroke(
        "TwistOut",
        "M20 12a8 8 0 1 1-2.34-5.66 M20 4v4.5h-4.5",
        width = 2.4f,
    )

    /** Giro de muñeca hacia dentro: flecha circular en sentido contrario. */
    val TwistIn = stroke(
        "TwistIn",
        "M4 12a8 8 0 1 0 2.34-5.66 M4 4v4.5h4.5",
        width = 2.4f,
    )

    /** Triángulo: reproducir. */
    val Play = stroke("Play", "M8 5.5v13l10.5-6.5z", width = 2.4f)

    /** Dos barras: pausar. */
    val Pause = stroke("Pause", "M9 6v12 M15 6v12", width = 3f)

    /** Flechas arriba y abajo: cambiar de pantalla. */
    val Swipe = stroke("Swipe", "M8 20V4 M4.5 7.5L8 4l3.5 3.5 M16 4v16 M12.5 16.5L16 20l3.5-3.5")

    /** Interrogación dentro de un círculo: ayuda. */
    val Help = stroke(
        "Help",
        "M12 21a9 9 0 1 1 0-18a9 9 0 1 1 0 18z M9.6 9.3a2.5 2.5 0 0 1 4.8.9c0 1.7-2.4 2.3-2.4 3.8 M12 17h.01",
    )

    private fun builder(name: String) = ImageVector.Builder(
        name = name,
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = 24f,
        viewportHeight = 24f,
    )

    /** Icono lineal: trazo con puntas y uniones redondeadas. */
    private fun stroke(name: String, pathData: String, width: Float = 2f): ImageVector =
        builder(name).addPath(
            pathData = addPathNodes(pathData),
            stroke = base,
            strokeLineWidth = width,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
        ).build()

}
