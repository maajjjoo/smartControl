package com.smartcontrol.watch.settings

import android.content.Context

/** Tema elegido en Ajustes. [SYSTEM] sigue al reloj. */
enum class ThemeChoice { SYSTEM, LIGHT, DARK }

/** Idioma elegido en Ajustes. [SYSTEM] usa el del reloj (español; cualquier otro, inglés). */
enum class LanguageChoice(val tag: String?) {
    SYSTEM(null),
    SPANISH("es"),
    ENGLISH("en"),
}

/** Siguiente opción al tocar la fila de Ajustes (da la vuelta al llegar a la última). */
fun <T : Enum<T>> T.next(all: List<T>): T = all[(all.indexOf(this) + 1) % all.size]

/** Ajustes de la app, guardados en su almacenamiento privado. */
class AppSettings(context: Context) {
    private val prefs = context.getSharedPreferences("smartcontrol_settings", Context.MODE_PRIVATE)

    var theme: ThemeChoice
        get() = prefs.getString(KEY_THEME, null)?.let { runCatching { ThemeChoice.valueOf(it) }.getOrNull() } ?: ThemeChoice.SYSTEM
        set(value) = prefs.edit().putString(KEY_THEME, value.name).apply()

    var language: LanguageChoice
        get() = prefs.getString(KEY_LANGUAGE, null)?.let { runCatching { LanguageChoice.valueOf(it) }.getOrNull() } ?: LanguageChoice.SYSTEM
        // commit (no apply): justo después se recrea la actividad y debe leer el valor nuevo.
        set(value) {
            prefs.edit().putString(KEY_LANGUAGE, value.name).commit()
        }

    private companion object {
        const val KEY_THEME = "theme"
        const val KEY_LANGUAGE = "language"
    }
}
