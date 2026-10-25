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
| `WRIST_TWIST_OUT` | Giro rápido de la muñeca "hacia fuera" (ωx > 0) | `SLIDE_NEXT` | `MEDIA_PLAY` |
| `WRIST_TWIST_IN` | Giro rápido de la muñeca "hacia dentro" (ωx < 0) | `SLIDE_PREVIOUS` | `MEDIA_PAUSE` |

El sentido "hacia fuera / hacia dentro" está pendiente de validar en un reloj real (depende de la muñeca y de la posición de la corona). En el reloj, `confidence` es una puntuación heurística en [0, 1] (intensidad del pico × dominancia del eje), no una probabilidad calibrada; el reloj no genera comandos por debajo de 0,80. Todavía no se envía ningún mensaje: no hay transporte.

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

### 3.3. Mensaje de Ventana de Sensores (`SENSOR_WINDOW` — Borrador sujeto a PoC)
En caso de que la evaluación técnica de la Fase 2 determine ejecutar el modelo de reconocimiento en Windows (Escenario 2 de la Sección 2), se contempla un mensaje preliminar para transmitir una ventana acotada de lecturas de acelerómetro y giroscopio asociada al modo activo. La frecuencia de muestreo, la cantidad de muestras por ventana y los límites definitivos de tamaño se establecerán únicamente a partir de las mediciones experimentales de la prueba de concepto:

```json
{
  "protocolVersion": "1.0-draft",
  "messageId": "772a0622-a41d-43f6-c938-668877662222",
  "timestampUtc": 1792633800500,
  "deviceId": "wearos-watch-01",
  "sessionToken": "TOKEN_DE_VINCULACION_LOCAL",
  "messageType": "SENSOR_WINDOW",
  "payload": {
    "activeMode": "PRESENTATION",
    "accelerometer": [
      { "tOffsetMs": 0, "x": 0.12, "y": -0.05, "z": 9.81 },
      { "tOffsetMs": 20, "x": 1.45, "y": -0.30, "z": 9.60 }
    ],
    "gyroscope": [
      { "tOffsetMs": 0, "x": 0.01, "y": 0.02, "z": -0.01 },
      { "tOffsetMs": 20, "x": 0.35, "y": 1.20, "z": -0.15 }
    ]
  }
}
```

### 3.4. Mensajes de Control de Conexión (`HEARTBEAT` / `HANDSHAKE`)
Para detectar caídas de conexión (RF-16) y permitir la reconexión sin reiniciar las aplicaciones (RF-17, RNF-15):
- `HANDSHAKE_INIT` / `HANDSHAKE_ACK`: Vinculación inicial y verificación de versión del protocolo.
- `PING` / `PONG`: Comprobación periódica de disponibilidad del enlace.

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
