# PoC 1 — Validador de comandos y lista blanca

> **Prototipo temporal.** Está escrito en Python 3.12 (solo biblioteca estándar) porque es la herramienta disponible hoy. **No** es la tecnología definitiva: `windows-app` seguirá siendo C#/.NET. Este código no controla aplicaciones, no envía pulsaciones y no inicia procesos.

## Contenido

| Archivo | Propósito |
|---|---|
| `validator.py` | `CommandValidator`: aplica en orden las validaciones de [communication-protocol.md §4](../../docs/communication-protocol.md) y [security-allowlist.md §3](../../docs/security-allowlist.md). Devuelve `ValidationResult` con un `AllowedAction` (enum cerrado) solo si el mensaje es aceptable. |
| `simulated_executor.py` | Frontera del ejecutor: solo acepta `AllowedAction`, nunca texto. Registra la acción que *se habría* ejecutado. |
| `test_validator.py` | 33 pruebas (positivas y negativas) con `unittest`. |

## Ejecución

Desde la raíz del repositorio:

```powershell
python -B -m unittest discover -s poc/command-validator -v
```

`-B` evita generar `__pycache__`.

## Orden de validación

1. Tamaño (≤ 4096 bytes), UTF-8 estricto y JSON válido. Se rechazan `NaN`/`Infinity` y claves duplicadas: el módulo `json` de Python los acepta por defecto (comprobado en esta sesión).
2. Estructura: los 7 campos de cabecera y los 4 del `payload`, sin campos adicionales. Tipos exactos (`bool` no se acepta como número).
3. `protocolVersion` soportada (`1.0-draft`).
4. `deviceId` vinculado y `sessionToken` correcto (comparación en tiempo constante).
5. Frescura (≤ 5000 ms de antigüedad, ≤ 2000 ms en el futuro) y anti-repetición por `messageId`. Si la caché se llena de identificadores vigentes, rechaza en lugar de olvidar uno (falla en modo cerrado).
6. `confidence` ≥ umbral (0.80 por defecto).
7. `activeMode` válido, `requestedAction` en la lista blanca (coincidencia exacta) y coherente con el modo. Opcionalmente, coherente con el modo del receptor.

Los valores numéricos (4096 bytes, 5000 ms, 2000 ms, 0.80, 1024 entradas) son **parámetros de ensayo**, no decisiones.

## Decisiones propias de la PoC (para revisión)

- **Campos desconocidos se rechazan.** El protocolo no define qué hacer con ellos; se eligió la opción estricta. Puede afectar a la compatibilidad futura.
- **`messageId` debe ser un UUID canónico en minúsculas**, como en el ejemplo del protocolo.
- **`gestureId` se valida solo por formato** (`^[A-Z][A-Z0-9_]{0,63}$`). El SRS todavía no define el catálogo de gestos.
- **`reasonCode`** tiene valores específicos (`MODE_ACTION_MISMATCH`, `STALE_MESSAGE`, …). El protocolo solo muestra `OK`.

## Limitaciones

- El token de las pruebas es ficticio. El esquema de token en claro no protege frente a un atacante que pueda leer la red local; se recomienda firmar los mensajes con HMAC (pendiente de documentar en el protocolo).
- La frescura depende de que los relojes del smartwatch y de Windows estén sincronizados. Esto no se ha medido.
- Solo se valida `COMMAND_REQUEST`. `SENSOR_WINDOW`, `HANDSHAKE_*` y `PING`/`PONG` quedan fuera.
