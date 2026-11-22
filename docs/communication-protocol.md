# Protocolo de Comunicación Inalámbrica (Borrador Técnico)

**Estado:** Pendiente de validación experimental en la Fase 2 (Prueba de Concepto)  
**Requisitos relacionados:** RF-06, RF-09, RF-15, RF-16, RF-17, RNF-01, RNF-04, RNF-05, RNF-06, RNF-07, RNF-15, RNF-16.

> [!IMPORTANT]
> Este documento define las alternativas técnicas, la estructura prevista de los mensajes y las reglas de validación requeridas entre el smartwatch (**Kotlin / Wear OS**) y la aplicación de escritorio (**C# / .NET en Windows**). **No se declara ningún protocolo ni canal como definitivo** hasta completar y medir las pruebas de concepto de la Fase 2 (Sección 6 del [SRS](SRS.md)).

---

## 1. Decisiones Pendientes: Bluetooth vs. Wi-Fi Local

La comunicación entre el reloj y el computador debe operar de forma **100 % local**, sin requerir acceso a Internet (RNF-05). En la Fase 2 se evaluarán dos alternativas de transporte físico/enlace:

| Criterio de evaluación | Alternativa A: Bluetooth (RFCOMM / BLE GATT) | Alternativa B: Wi-Fi en Red Local (TCP / WebSockets / UDP sobre LAN) |
|---|---|---|
| **Dependencia de infraestructura** | Conexión directa punto a punto entre el reloj y el adaptador Bluetooth del PC; no requiere router ni red Wi-Fi común. | Requiere que el smartwatch y el PC estén conectados a la misma red local (LAN) o zona con cobertura (Hotspot local de Windows). |
| **Latencia esperada (RNF-01)** | Baja para paquetes cortos de comandos; menor ancho de banda si se transmiten ráfagas continuas de sensores en crudo. | Muy baja latencia y alto ancho de banda tanto para comandos discretos como para flujo de datos de sensores. |
| **Consumo energético (RNF-10)** | Bajo consumo en el smartwatch (especialmente con BLE). | Mayor consumo relativo en el radio Wi-Fi del smartwatch si la conexión permanece activa de forma continua. |
| **Seguridad y vinculación (RNF-07)** | Emparejamiento nativo del sistema operativo más validación de sesión en la aplicación. | Requiere mecanismo explícito de emparejamiento/token compartido y validación de mensajes en la capa de aplicación. |
| **Estabilidad y reconexión (RF-17, RNF-15)** | Depende de la interoperabilidad entre el stack Bluetooth de Wear OS y las APIs de Windows (`Windows.Devices.Bluetooth` / `32feet.NET`). | Reconexión estándar mediante sockets TCP/WebSockets en `.NET` y clientes de red en Kotlin (`OkHttp` / `Ktor` / `java.net.Socket`). |
| **Facilidad de prueba en emulador** | Limitada en el emulador de Wear OS sin passthrough de adaptador USB Bluetooth dedicado. | Directamente verificable tanto en emulador de Wear OS como en reloj físico conectado a la red local. |

### Diseño desacoplado del transporte (RNF-16)
Para cumplir con RNF-16 y RF-18, tanto la aplicación de Wear OS (Kotlin) como la de Windows (C# / .NET) implementarán una interfaz común de transporte (por ejemplo, `ICommunicationChannel` / `IMessageTransport`). De esta manera, el formato lógico de los mensajes y su validación serán independientes de si los bytes viajan por sockets Wi-Fi o por un canal Bluetooth.

---

## 2. Impacto de la Ubicación del Modelo de IA en el Protocolo

La carga útil del protocolo dependerá de la decisión técnica sobre dónde se ejecutará el modelo de reconocimiento (Sección 6 del [SRS](SRS.md)):

1. **Escenario 1 — IA ejecutada en el Smartwatch (Edge):**
   - El reloj captura los sensores, ejecuta el reconocimiento localmente y evalúa el umbral de confianza (RF-11, RF-IA-04).
   - Solo se transmiten a Windows **mensajes de comando** cuando un gesto supera el umbral de confianza.
2. **Escenario 2 — IA ejecutada en Windows (Host Local):**
   - El reloj envía **ventanas de muestras de sensores** (acelerómetro/giroscopio) únicamente cuando detecta el inicio de un gesto activo (RNF-10).
   - El módulo de IA en Windows clasifica el movimiento, valida la confianza y notifica al reloj el resultado (`ACK` con acción ejecutada o `REJECTED` por baja confianza) para actualizar la interfaz del smartwatch (RF-10, RF-IA-06).

---

## 3. Formato Previsto de los Mensajes

Se prevé el uso de mensajes estructurados en formato **JSON** codificados en UTF-8 (delimitados por longitud o salto de línea según el transporte elegido), por su legibilidad, facilidad de depuración y soporte nativo en Kotlin (`kotlinx.serialization`) y C# (`System.Text.Json`).

### 3.1. Mensaje de Comando (`COMMAND_REQUEST`)
Enviado cuando un gesto ha sido reconocido y asociado a una acción según el modo activo:

```json
{
  "protocolVersion": "1.0-draft",
  "messageId": "550e8400-e29b-41d4-a716-446655440000",
  "timestampUtc": 1792633800125,
  "deviceId": "wearos-watch-01",
  "sessionToken": "TOKEN_DE_VINCULACION_LOCAL",
  "messageType": "COMMAND_REQUEST",
  "payload": {
    "activeMode": "PRESENTATION",
    "gestureId": "WRIST_TWIST_OUT",
    "confidence": 0.94,
    "requestedAction": "SLIDE_NEXT"
  }
}
```

**Gestos del reloj (implementación actual, `watch-app`).** Reconocedor por reglas, sin aprendizaje automático (primera etapa de ADR-03):

| `gestureId` | Movimiento | `PRESENTATION` | `MULTIMEDIA` |
|---|---|---|---|
| `WRIST_TWIST_OUT` | Giro rápido de la muñeca "hacia fuera" (ωx > 0) | `SLIDE_NEXT` | `MEDIA_NEXT_TRACK` |
| `WRIST_TWIST_IN` | Giro rápido de la muñeca "hacia dentro" (ωx < 0) | `SLIDE_PREVIOUS` | `MEDIA_PREVIOUS_TRACK` |

En `MULTIMEDIA`, `MEDIA_PLAY` y `MEDIA_PAUSE` no tienen gesto: son dos botones en la pantalla del reloj y se envían con `gestureId` `SCREEN_TAP` y `confidence` 1.

El sentido "hacia fuera / hacia dentro" está pendiente de validar en un reloj real (depende de la muñeca y de la posición de la corona). En el reloj, `confidence` es una puntuación heurística en [0, 1] (intensidad del pico × dominancia del eje), no una probabilidad calibrada; el reloj no genera comandos por debajo de 0,80. Desde el 2026-11-20 el reloj envía estos mensajes a Windows por Wi-Fi local (TCP, ver §3.4).

### 3.2. Mensaje de Respuesta / Confirmación (`COMMAND_RESPONSE`)
Enviado desde Windows hacia el smartwatch para confirmar la ejecución o informar el motivo de rechazo (RF-09, RF-10, RF-IA-06):

```json
{
  "protocolVersion": "1.0-draft",
  "messageId": "661f9511-f30c-52e5-b827-557766551111",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "timestampUtc": 1792633800160,
  "messageType": "COMMAND_RESPONSE",
  "payload": {
    "status": "EXECUTED",
    "executedAction": "SLIDE_NEXT",
    "reasonCode": "OK",
    "message": "Diapositiva avanzada correctamente"
  }
}
```

Valores previstos para `status`:
- `EXECUTED`: Acción validada y ejecutada en Windows.
- `REJECTED_LOW_CONFIDENCE`: Gesto descartado por no alcanzar el umbral mínimo (RF-11, RF-IA-04).
- `REJECTED_NOT_ALLOWED`: Comando o combinación modo/acción no incluida en la lista blanca (RF-15, RNF-06).
- `REJECTED_INVALID_MESSAGE`: Mensaje incompleto, malformado o con sesión inválida (RNF-04, RNF-07).
- `EXECUTION_ERROR`: Error al invocar la acción en el sistema operativo (RF-10).

### 3.3. Mensaje de Ventana de Sensores (`SENSOR_WINDOW`, implementado el 2026-11-21)
Enfoque híbrido aprobado (ADR-01 C, ADR-03, ADR-07): el reloj **segmenta** cada movimiento con sus reglas y envía la ventana del giroscopio; Windows decide el gesto con la **IA**: un modelo local entrenado si está instalado o, si no, **Gemini** (con clave en `.env`). Sin IA, o si Gemini no responde a tiempo, Windows usa lo que dijeron las reglas del reloj (`detectorEvent`).

```json
{
  "protocolVersion": "1.0-draft",
  "messageId": "772a0622-a41d-43f6-c938-668877662222",
  "timestampUtc": 1792633800500,
  "deviceId": "watch-1a2b3c4d5e6f7a8b",
  "sessionToken": "TOKEN_DE_VINCULACION",
  "messageType": "SENSOR_WINDOW",
  "payload": {
    "activeMode": "PRESENTATION",
    "detectorEvent": "TWIST_OUT",
    "detectorConfidence": 0.91,
    "gyroscope": [[0, 0.0123, -0.0012, 0.0004], [20, 0.4512, 0.1021, -0.0103]]
  }
}
```

- `detectorEvent`: `TWIST_OUT`, `TWIST_IN` o `UNCLEAR` (las reglas vieron movimiento pero no un gesto claro). Se envían las dos clases, porque la IA puede decidir distinto que las reglas.
- `gyroscope`: filas `[tOffsetMs, x, y, z]` en rad/s, con las reglas de [recording-format.md §3](recording-format.md) (8 a 256 muestras, 200 ms previos al movimiento, hueco ≤ 100 ms, ≤ 2000 ms). Sin acelerómetro en esta versión.
- Campos exactos; tamaño máximo 16 KB (los `COMMAND_REQUEST` siguen limitados a 4 KB). Primero se comprueba la vinculación (`deviceId` + `sessionToken`) y después la ventana; la acción candidata pasa por el validador de §4 con el mismo `messageId` y `timestampUtc`, así que una ventana repetida o vieja se rechaza.
- La respuesta es un `COMMAND_RESPONSE` (§3.2) con tres campos más: `recognizedBy` (`AI` o `RULES`), `gesture` (`TWIST_OUT`, `TWIST_IN`, `NO_GESTURE` o null) y `confidence`. Si la IA decide que no era un gesto, `status` es `REJECTED_LOW_CONFIDENCE` con `reasonCode` `NO_GESTURE` o `LOW_CONFIDENCE`.
- Sin ventana (por ejemplo, si el reloj aún no tenía 200 ms de lecturas previas) el reloj envía el `COMMAND_REQUEST` de las reglas, como antes.

### 3.4. Mensajes de Control de Conexión (implementados el 2026-11-20)
Para que la conexión sea automática (RF-16, RF-17, RNF-15) sin que el usuario escriba direcciones. Canal: **Wi-Fi local**, JSON por líneas sobre TCP en el puerto 47800 (el mismo que `COMMAND_REQUEST`). Misma cabecera que §3.1 y campos exactos; los desconocidos se rechazan.

1. **Búsqueda (UDP 47801).** El reloj difunde el texto exacto `SMARTCONTROL_DISCOVER/1`. Cada Windows con SmartControl responde solo a quien preguntó: `{"service":"smartcontrol","protocolVersion":"1.0-draft","pcName":…,"pcId":…,"port":47800}`. El reloj prefiere el equipo con el que ya está vinculado (`pcId`). En el emulador también pregunta a `10.0.2.2`.
2. **Vinculación (`PAIR_REQUEST`, solo la primera vez).** Cabecera sin `sessionToken`, `payload: {deviceName}`. Windows responde `PAIR_PENDING {code, pcName}` con un código aleatorio de 4 cifras, que aparece en las dos pantallas, y espera hasta 60 s a que la persona pulse «Vincular» en Windows. Entonces responde `PAIR_ACCEPTED {sessionToken, pcName, serverTimeUtc}` con una clave aleatoria de 256 bits. Si se rechaza o caduca, responde `PAIR_REJECTED {reasonCode}` (`USER_REJECTED`, `PAIRING_TIMEOUT`, `PAIRING_BUSY`). Solo se atiende una solicitud a la vez.
3. **Saludo (`HELLO`, en cada conexión).** Con `deviceId` y `sessionToken`. Windows responde `HELLO_ACK {pcName, serverTimeUtc}` o `HELLO_REJECTED {reasonCode}` y cierra; si la clave ya no vale (el reloj se olvidó en Windows), el reloj borra la suya y vuelve a pedir la vinculación.
4. **Latido (`PING` → `PONG {serverTimeUtc}`).** Cada 8 s, solo tras el saludo. Si Windows calla 20 s o se corta la conexión, el reloj vuelve a buscar y se reconecta solo.
5. **Hora.** El reloj fecha los comandos con la hora de Windows (`serverTimeUtc`), así una diferencia de hora entre los dos aparatos no hace que se rechacen por antiguos.

Seguridad y límites: sin vincular no se acepta ningún comando; las claves se guardan cifradas con DPAPI en Windows y en el almacenamiento privado de la app en el reloj; «Olvidar» en Windows las invalida en el acto. El canal **no está cifrado** (sin TLS): alguien en la misma red podría leer los mensajes o suplantar una conexión ya abierta. Es aceptable para el MVP en una red de confianza y queda como mejora pendiente.

---

## 4. Validaciones Obligatorias en la Recepción

Antes de que la aplicación de Windows (C# / .NET) ejecute cualquier acción, todo mensaje entrante deberá superar secuencialmente las siguientes validaciones:

1. **Integridad sintáctica y de esquema (RNF-04):**
   - El paquete debe ser un JSON válido y completo, con límite estricto de tamaño máximo (p. ej., $\le 4\text{ KB}$ para comandos) para prevenir desbordamientos o tramas truncadas.
   - Presencia obligatoria de todos los campos de cabecera (`protocolVersion`, `messageId`, `timestampUtc`, `deviceId`, `sessionToken`, `messageType`, `payload`).
2. **Compatibilidad de versión:**
   - `protocolVersion` debe coincidir con una versión soportada por el receptor.
3. **Autenticación y vinculación del dispositivo (RNF-07):**
   - `deviceId` y `sessionToken` deben corresponder a una sesión previamente vinculada y autorizada localmente.
4. **Frescura y prevención de repetición (Confiabilidad RNF-04):**
   - `messageId` no debe haberse procesado previamente en la ventana reciente.
   - `timestampUtc` no debe exceder una ventana máxima de antigüedad permitida (evitando que comandos encolados durante una desconexión se ejecuten tardíamente de forma inesperada).
5. **Umbral de confianza (RF-11, RF-IA-04):**
   - `confidence` debe ser un valor numérico en el rango $[0.0, 1.0]$ y ser mayor o igual al umbral mínimo configurado (por ejemplo, $\ge 0.80$). El reloj aplica el mismo umbral de 0,80 antes de generar un comando; Windows debe validarlo de nuevo de forma independiente.
6. **Validación de modo y lista blanca de seguridad (RF-04, RF-15, RNF-06):**
   - `activeMode` debe ser exclusivamente `PRESENTATION` o `MULTIMEDIA`.
   - `requestedAction` debe existir en la lista blanca definida en [security-allowlist.md](security-allowlist.md) y corresponder al `activeMode` declarado.
   - Si cualquiera de las validaciones falla, **no se ejecutará ninguna acción** en el sistema operativo, se registrará el evento y se responderá con el estado de rechazo correspondiente.
