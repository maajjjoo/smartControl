package com.smartcontrol.watch.link

/**
 * JSON mínimo para los mensajes con Windows, sin bibliotecas externas (org.json de Android no se
 * puede usar en las pruebas de JVM). Solo lo necesario: objetos, textos, números, booleanos y null.
 */
internal object Json {

    /** Escribe un objeto. Los valores pueden ser String, Number, Boolean, null o Map<String, Any?>. */
    fun write(values: Map<String, Any?>): String = buildString { writeValue(this, values) }

    private fun writeValue(out: StringBuilder, value: Any?) {
        when (value) {
            null -> out.append("null")
            is String -> writeString(out, value)
            is Boolean -> out.append(value)
            is Int, is Long -> out.append(value)
            is Double -> {
                require(value.isFinite()) { "número no finito" }
                out.append(value.toString())
            }
            is Float -> writeValue(out, value.toDouble())
            is Map<*, *> -> {
                out.append('{')
                var first = true
                for ((key, item) in value) {
                    if (!first) out.append(',')
                    first = false
                    writeString(out, key as String)
                    out.append(':')
                    writeValue(out, item)
                }
                out.append('}')
            }
            else -> throw IllegalArgumentException("tipo no admitido: ${value::class.simpleName}")
        }
    }

    private fun writeString(out: StringBuilder, text: String) {
        out.append('"')
        for (c in text) {
            when {
                c == '"' -> out.append("\\\"")
                c == '\\' -> out.append("\\\\")
                c == '\n' -> out.append("\\n")
                c == '\r' -> out.append("\\r")
                c == '\t' -> out.append("\\t")
                c < ' ' || c == ' ' || c == ' ' -> out.append("\\u%04x".format(c.code))
                else -> out.append(c)
            }
        }
        out.append('"')
    }

    /**
     * Lee un objeto JSON. Los números enteros llegan como Long y los decimales como Double.
     * Lanza [IllegalArgumentException] si el texto no es un objeto JSON válido.
     */
    fun parseObject(text: String): Map<String, Any?> {
        val reader = Reader(text)
        reader.skipSpace()
        val value = reader.readObject(depth = 0)
        reader.skipSpace()
        require(reader.atEnd) { "texto después del objeto" }
        return value
    }

    private class Reader(private val text: String) {
        private var pos = 0
        val atEnd get() = pos >= text.length

        fun skipSpace() {
            while (pos < text.length && text[pos] in " \t\r\n") pos++
        }

        private fun expect(c: Char) {
            require(pos < text.length && text[pos] == c) { "se esperaba '$c' en $pos" }
            pos++
        }

        fun readObject(depth: Int): Map<String, Any?> {
            require(depth < 8) { "demasiado anidado" }
            expect('{')
            val result = LinkedHashMap<String, Any?>()
            skipSpace()
            if (pos < text.length && text[pos] == '}') {
                pos++
                return result
            }
            while (true) {
                skipSpace()
                val key = readString()
                require(key !in result) { "clave repetida: $key" }
                skipSpace()
                expect(':')
                skipSpace()
                result[key] = readValue(depth)
                skipSpace()
                require(pos < text.length) { "objeto sin cerrar" }
                when (text[pos++]) {
                    ',' -> continue
                    '}' -> return result
                    else -> throw IllegalArgumentException("se esperaba ',' o '}'")
                }
            }
        }

        private fun readValue(depth: Int): Any? {
            require(pos < text.length) { "falta un valor" }
            return when (val c = text[pos]) {
                '{' -> readObject(depth + 1)
                '"' -> readString()
                't' -> literal("true", true)
                'f' -> literal("false", false)
                'n' -> literal("null", null)
                else -> if (c == '-' || c.isDigit()) readNumber() else throw IllegalArgumentException("valor no admitido en $pos")
            }
        }

        private fun literal(word: String, value: Any?): Any? {
            require(text.startsWith(word, pos)) { "se esperaba $word" }
            pos += word.length
            return value
        }

        private fun readNumber(): Any {
            val start = pos
            if (text[pos] == '-') pos++
            while (pos < text.length && (text[pos].isDigit() || text[pos] in ".eE+-")) pos++
            val raw = text.substring(start, pos)
            return raw.toLongOrNull() ?: raw.toDoubleOrNull()?.takeIf { it.isFinite() }
                ?: throw IllegalArgumentException("número inválido: $raw")
        }

        fun readString(): String {
            expect('"')
            val out = StringBuilder()
            while (true) {
                require(pos < text.length) { "texto sin cerrar" }
                val c = text[pos++]
                when {
                    c == '"' -> return out.toString()
                    c == '\\' -> {
                        require(pos < text.length) { "escape incompleto" }
                        when (val e = text[pos++]) {
                            '"', '\\', '/' -> out.append(e)
                            'n' -> out.append('\n')
                            'r' -> out.append('\r')
                            't' -> out.append('\t')
                            'b' -> out.append('\b')
                            'f' -> out.append('\u000C')
                            'u' -> {
                                require(pos + 4 <= text.length) { "escape \\u incompleto" }
                                out.append(text.substring(pos, pos + 4).toInt(16).toChar())
                                pos += 4
                            }
                            else -> throw IllegalArgumentException("escape no válido")
                        }
                    }
                    c < ' ' -> throw IllegalArgumentException("carácter de control en un texto")
                    else -> out.append(c)
                }
            }
        }
    }
}
