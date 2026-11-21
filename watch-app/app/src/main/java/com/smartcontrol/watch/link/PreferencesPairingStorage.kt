package com.smartcontrol.watch.link

import android.content.Context
import java.security.SecureRandom

/**
 * Vinculación guardada en el almacenamiento privado de la app (otras apps no pueden leerlo).
 * El reloj recuerda un solo equipo: vincular otro sustituye al anterior. allowBackup=false en el
 * manifiesto evita que la clave salga en copias de seguridad.
 */
class PreferencesPairingStorage(context: Context) : PairingStorage {
    private val prefs = context.getSharedPreferences("smartcontrol_pairing", Context.MODE_PRIVATE)

    override val deviceId: String = prefs.getString(KEY_DEVICE_ID, null) ?: newDeviceId().also {
        prefs.edit().putString(KEY_DEVICE_ID, it).apply()
    }

    override val pairedPcId: String? get() = prefs.getString(KEY_PC_ID, null)

    override fun tokenFor(pcId: String): String? =
        if (prefs.getString(KEY_PC_ID, null) == pcId) prefs.getString(KEY_TOKEN, null) else null

    override fun save(pcId: String, pcName: String, token: String) {
        prefs.edit()
            .putString(KEY_PC_ID, pcId)
            .putString(KEY_PC_NAME, pcName)
            .putString(KEY_TOKEN, token)
            .apply()
    }

    override fun forget(pcId: String) {
        if (prefs.getString(KEY_PC_ID, null) != pcId) return
        prefs.edit().remove(KEY_PC_ID).remove(KEY_PC_NAME).remove(KEY_TOKEN).apply()
    }

    private fun newDeviceId(): String {
        val bytes = ByteArray(8).also { SecureRandom().nextBytes(it) }
        return "watch-" + bytes.joinToString("") { "%02x".format(it) }
    }

    private companion object {
        const val KEY_DEVICE_ID = "device_id"
        const val KEY_PC_ID = "pc_id"
        const val KEY_PC_NAME = "pc_name"
        const val KEY_TOKEN = "token"
    }
}
