# Aplicación de Smartwatch (`watch-app`)

Componente para **Wear OS** previsto en **Kotlin**, encargado de:
- Mostrar el modo activo (`PRESENTATION` / `MULTIMEDIA`) y el estado de conexión con Windows ([RF-05](../docs/SRS.md), [RF-09](../docs/SRS.md), [RNF-09](../docs/SRS.md)).
- Capturar señales de sensores de movimiento (acelerómetro y giroscopio) de forma eficiente ([RF-02](../docs/SRS.md), [RNF-10](../docs/SRS.md)).
- Ejecutar o solicitar el reconocimiento local de gestos y mostrar retroalimentación visual al usuario ([RF-10](../docs/SRS.md), [RF-IA-06](../docs/SRS.md)).
- Transmitir comandos validados al computador y gestionar la reconexión ([RF-06](../docs/SRS.md), [RF-16](../docs/SRS.md), [RF-17](../docs/SRS.md)).

## Estado actual (etapa 2: control por gestos de muñeca)

Proyecto Android de **Wear OS** en Kotlin con **Compose for Wear OS (Material 3)**, interfaz en **modo claro**. La interacción principal son **gestos de muñeca** detectados con el giroscopio del reloj. Se **conecta solo** con SmartControl en Windows por la red Wi-Fi: busca el equipo, se vincula la primera vez con un código que aparece en las dos pantallas (se pulsa «Vincular» en Windows) y después se reconecta sin preguntar. Cada gesto se envía y la pantalla muestra la respuesta real de Windows («Hecho» o el motivo).

| Archivo | Contenido |
|---|---|
| `gesture/WristGestureDetector.kt` | Reconocedor **por reglas** (primera etapa del enfoque híbrido de ADR-03, sin aprendizaje automático). Kotlin puro. |
| `gesture/GyroscopeGestureSource.kt` | Lee el giroscopio a ~50 Hz solo con la app en primer plano y alimenta el reconocedor. |
| `gesture/SyntheticGestures.kt` | Series sintéticas de cada movimiento, para pruebas unitarias y para el emulador. |
| `ControlState.kt` | Modos, lista blanca, validación, simulación y traducción gesto → comando según el modo. Kotlin puro. |
| `CommandLog.kt` | Registro de diagnóstico solo en depuración (`adb logcat -s SmartControl`). |
| `link/WindowsLink.kt` | Conexión automática: pide el Wi-Fi, busca el equipo por UDP, se conecta, mantiene el latido y se reconecta. Diagnóstico: `adb logcat -s SmartControlLink`. |
| `link/LinkSession.kt` | Diálogo con Windows: saludo, vinculación, comandos con su respuesta y hora de Windows. Kotlin puro. |
| `link/LinkMessages.kt`, `link/Json.kt` | Mensajes del protocolo y JSON mínimo, sin bibliotecas externas. |
| `link/PreferencesPairingStorage.kt` | Identificador del reloj y clave de vinculación, en el almacenamiento privado de la app. |
| `ui/SmartControlApp.kt` | Pantallas verticales (Inicio, Presentación, Multimedia, Ayuda) y ventana de confirmación. |
| `ui/Theme.kt` / `ui/SmartIcons.kt` | Único archivo de colores / iconos propios de trazo redondeado. |

### Gestos

| Gesto | Presentación | Multimedia |
|---|---|---|
| **Giro rápido de la muñeca hacia fuera** | Siguiente diapositiva | Siguiente canción |
| **Giro rápido de la muñeca hacia dentro** | Diapositiva anterior | Canción anterior |

En Multimedia, **Reproducir** y **Pausar** son dos botones redondos en la pantalla (órdenes distintas, sin alternar).

Reglas del reconocedor (eje X del giroscopio, que se supone alineado con el antebrazo):
- Empieza cuando la velocidad angular supera 2,5 rad/s y termina cuando baja de 1,25 rad/s; debe durar como máximo 600 ms.
- Confianza = intensidad del pico (respecto a 5 rad/s) × dominancia del eje X. Es una puntuación heurística, no una probabilidad calibrada. Se actúa solo si es **≥ 0,80**: un único valor (`MIN_GESTURE_CONFIDENCE`, en `gesture/WristGestureDetector.kt`) que usan el reconocedor y la validación del estado.
- Se rechazan las sacudidas de ida y vuelta, los movimientos en otros ejes y los giros lentos.
- **Regreso a reposo.** Tras cada gesto (reconocido o no) el reconocedor queda desarmado y no acepta otro hasta que: (1) pasan 700 ms; (2) la muñeca está quieta (|ω| < 0,8 rad/s) durante 200 ms; y (3) ha vuelto a ±20° de la orientación en la que empezó el gesto, o lleva 3 s quieta en la nueva posición (que pasa a ser la neutra). Así, el regreso de la muñeca, incluso tras una pausa girada, no cuenta como el gesto contrario. Si la pausa supera 3 s, el regreso sí puede contar: ese tiempo debe calibrarse en un reloj real.
- **Reinicios del sensor.** Al volver a registrar el giroscopio (onResume) se descarta un gesto a medias, pero se conservan la espera y el estado desarmado. Las muestras con marca de tiempo anterior a la última procesada se ignoran.
- Los gestos solo cuentan en las pantallas de modo, nunca en Inicio ni en Ayuda.
- Un gesto reconocido pasa por **la misma validación** que antes (lista blanca del modo y conexión).
- **El sentido no está validado.** "Hacia fuera" = velocidad angular positiva en X es una convención. El mismo movimiento anatómico da el signo contrario según la muñeca (izquierda o derecha) y la posición de la corona, y la app no tiene en cuenta el ajuste de Wear OS que gira la pantalla 180°. No se ajustará sin datos de un reloj real.

### Ciclo de vida del sensor y pantalla apagada

- El giroscopio se registra en `onResume` y se libera en `onPause` (a ~50 Hz, `SENSOR_DELAY_GAME`, sin permisos de alta frecuencia). No hay servicio en segundo plano ni modo ambiente.
- **Comportamiento observado en el emulador**: con la pantalla apagada (estado *Dozing*), la actividad sigue en primer plano y **el giroscopio sigue registrado**, así que un gesto podría actuar con la pantalla oscura y sin que se vea la confirmación. El emulador no tiene tiempo de apagado de pantalla.
- **Sin comprobar en un reloj real**: lo habitual es que, tras el tiempo de espera, el reloj vuelva a la esfera y la app pase a segundo plano; entonces los gestos dejarían de funcionar hasta volver a abrirla.
- Decisión pendiente, que requiere medir batería y revisar las restricciones de Wear OS: pausar los gestos con la pantalla apagada, o mantenerlos (modo ambiente o actividad en curso). No se ha añadido ninguna operación en segundo plano.

Pantallas (deslizando hacia arriba o abajo, o girando la corona):
1. **Inicio**: *SmartControl*, *Desconectado · Equipo Windows sin vincular* y *Desliza hacia arriba*.
2. **Presentación** y 3. **Multimedia**: *Realiza un gesto para controlar tu equipo*, guía con los dos gestos (los iconos muestran el giro y la fila se ilumina al reconocerlo) y estado de conexión. Si el reloj no tiene giroscopio, lo dice en lugar de la indicación.
4. **Ayuda** (*Cómo usarlo*): *Elige un modo*, *Gira la muñeca*, *Mira el resultado*.

Tras cada gesto aparece una ventana que se cierra sola: **Comando aceptado** (verde), **No se pudo realizar la acción** (rojo, con el motivo) o **Gesto no reconocido** (rojo). Tocar una fila de la guía envía el mismo comando, solo como alternativa de prueba; no se indica en pantalla.

### Qué está probado y qué falta

- Probado **con series sintéticas** (pruebas unitarias y reproducción en el emulador por el mismo reconocedor): reconocimiento de los dos giros, rechazo de giros débiles, sacudidas, otros ejes y giros lentos, regreso de la muñeca (con y sin pausa), espera mínima, reinicios del sensor, umbral exacto de 0,80 y el recorrido completo hasta la ventana de confirmación.
- La consola del emulador **solo actualiza el giroscopio una vez por segundo**, así que no puede imitar un giro rápido. Con el sensor real del emulador solo se ha comprobado el rechazo de un giro lento. Los giros rápidos se prueban en depuración reproduciendo una serie sintética por el mismo reconocedor:
  ```powershell
  adb shell am start -n com.smartcontrol.watch/.MainActivity -f 0x20000000 --es replay_gesture out   # in, out_hold, return, weak, shake, slow, cross
  ```
- Limitaciones conocidas de las pruebas: las series sintéticas tienen forma de escalón, sin ruido; un giro de ida y vuelta muy rápido (ciclo de ~200 ms) puede rechazarse como sacudida según dónde caigan las muestras; y hace falta un pico de unos 4,5 rad/s para reconocerlo de forma fiable.
- **Pendiente**: validar en un reloj real los umbrales, el tiempo de reasentamiento de 3 s, el comportamiento con la pantalla apagada y el sentido "hacia fuera / hacia dentro" (muñeca izquierda y derecha, posición de la corona), medir la tasa de aciertos (≥ 90 %, RNF-02) y de falsos positivos con movimientos cotidianos (RNF-03), y decidir si se añade aprendizaje automático (ADR-03). Para calibrar: `adb shell setprop log.tag.SmartControlSensor VERBOSE` registra cada muestra del giroscopio (solo en depuración).

**IA.** Con conexión, cada movimiento que segmentan las reglas se recorta (`recording/WindowCutter.kt`, 200 ms previos incluidos) y se envía a Windows como `SENSOR_WINDOW`; allí decide el modelo de IA o, si aún no hay modelo entrenado, las reglas. La ventana de confirmación dice quién decidió: «Hecho · Diapositiva siguiente · IA 98 %» o «… · reglas».

**Conexión.** El reloj y el PC tienen que estar en la **misma red Wi-Fi**. Si el reloj solo tiene conexión por el teléfono (Bluetooth), la pantalla de Inicio dice «Sin Wi-Fi». En el emulador hay que conectar su Wi-Fi virtual una vez: `adb shell cmd wifi connect-network AndroidWifi open`; el PC se encuentra en `10.0.2.2`.

**Simulación (solo para pruebas sin Windows).** Sin conexión, los comandos se rechazan («Windows no está conectado»). En compilaciones de **depuración** se puede activar una simulación al abrir la app (en release se ignora):

```powershell
adb shell am start -S -n com.smartcontrol.watch/.MainActivity --ez simulation true
```

Versiones: AGP 9.4.1, Kotlin 2.2.10, Gradle 9.6.0, compileSdk/targetSdk 37, minSdk 30 (Wear OS 3+), Wear Compose 1.5.0.

### Ejecutar en Android Studio

1. *File → Open…* y selecciona la carpeta `watch-app` (no la raíz del repositorio).
2. Espera a que termine *Gradle Sync* (la primera vez descarga las dependencias de Wear Compose y `aapt2`).
3. *Tools → Device Manager → + → Create Virtual Device… → Wear OS →* **Wear OS Large Round** *→* elige una imagen de sistema Wear OS x86_64 y descárgala → *Finish*.
4. En la barra superior, elige la configuración `app` y el emulador Wear OS, y pulsa **Run ▶**.
5. En el reloj: desliza hacia arriba o abajo (o gira la corona) para elegir un modo y haz los gestos. En el emulador, usa el comando `replay_gesture` de arriba. Para ver el rechazo, abre la app con el comando `adb` de arriba.

La interfaz no muestra identificadores internos (`MEDIA_PAUSE`, números de secuencia, códigos de rechazo). Esos detalles van al registro de diagnóstico, solo en compilaciones de depuración: `adb logcat -s SmartControl`.

Pruebas unitarias: `.\gradlew.bat :app:testDebugUnitTest` (o clic derecho en `ControlStateTest` → *Run*).
