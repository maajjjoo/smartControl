# Aplicación de Smartwatch (`watch-app`)

Componente para **Wear OS** previsto en **Kotlin**, encargado de:
- Mostrar el modo activo (`PRESENTATION` / `MULTIMEDIA`) y el estado de conexión con Windows ([RF-05](../docs/SRS.md), [RF-09](../docs/SRS.md), [RNF-09](../docs/SRS.md)).
- Capturar señales de sensores de movimiento (acelerómetro y giroscopio) de forma eficiente ([RF-02](../docs/SRS.md), [RNF-10](../docs/SRS.md)).
- Ejecutar o solicitar el reconocimiento local de gestos y mostrar retroalimentación visual al usuario ([RF-10](../docs/SRS.md), [RF-IA-06](../docs/SRS.md)).
- Transmitir comandos validados al computador y gestionar la reconexión ([RF-06](../docs/SRS.md), [RF-16](../docs/SRS.md), [RF-17](../docs/SRS.md)).

## Estado actual (etapa 2: control por gestos de muñeca)

Proyecto Android de **Wear OS** en Kotlin con **Compose for Wear OS (Material 3)**, interfaz en **modo claro**. La interacción principal son **gestos de muñeca** detectados con el giroscopio del reloj. Aún **no** hay conexión con Windows (ni Bluetooth ni red): los comandos aceptados se simulan dentro del reloj y nada se envía.

| Archivo | Contenido |
|---|---|
| `gesture/WristGestureDetector.kt` | Reconocedor **por reglas** (primera etapa del enfoque híbrido de ADR-03, sin aprendizaje automático). Kotlin puro. |
| `gesture/GyroscopeGestureSource.kt` | Lee el giroscopio a ~50 Hz solo con la app en primer plano y alimenta el reconocedor. |
| `gesture/SyntheticGestures.kt` | Series sintéticas de cada movimiento, para pruebas unitarias y para el emulador. |
| `ControlState.kt` | Modos, lista blanca, validación, simulación y traducción gesto → comando según el modo. Kotlin puro. |
| `CommandLog.kt` | Registro de diagnóstico solo en depuración (`adb logcat -s SmartControl`). |
| `ui/SmartControlApp.kt` | Pantallas verticales (Inicio, Presentación, Multimedia, Ayuda) y ventana de confirmación. |
| `ui/Theme.kt` / `ui/SmartIcons.kt` | Único archivo de colores / iconos propios de trazo redondeado. |

### Gestos

| Gesto | Presentación | Multimedia |
|---|---|---|
| **Giro rápido de la muñeca hacia fuera** | Siguiente diapositiva | Reproducir |
| **Giro rápido de la muñeca hacia dentro** | Diapositiva anterior | Pausar |

Reglas del reconocedor (eje X del giroscopio, que se supone alineado con el antebrazo):
- Empieza cuando la velocidad angular supera 2,5 rad/s y termina cuando baja de 1,25 rad/s; debe durar como máximo 600 ms.
- Confianza = intensidad del pico (respecto a 5 rad/s) × dominancia del eje X. Es una puntuación heurística, no una probabilidad calibrada. Se actúa solo si es **≥ 0,80**: un único valor (`MIN_GESTURE_CONFIDENCE`, en `gesture/WristGestureDetector.kt`) que usan el reconocedor y la validación del estado.
- Se rechazan las sacudidas de ida y vuelta, los movimientos en otros ejes y los giros lentos.
- **Regreso a reposo.** Tras cada gesto (reconocido o no) el reconocedor queda desarmado y no acepta otro hasta que: (1) pasan 700 ms; (2) la muñeca está quieta (|ω| < 0,8 rad/s) durante 200 ms; y (3) ha vuelto a ±20° de la orientación en la que empezó el gesto, o lleva 3 s quieta en la nueva posición (que pasa a ser la neutra). Así, el regreso de la muñeca, incluso tras una pausa girada, no cuenta como el gesto contrario. Si la pausa supera 3 s, el regreso sí puede contar: ese tiempo debe calibrarse en un reloj real.
- **Reinicios del sensor.** Al volver a registrar el giroscopio (onResume) se descarta un gesto a medias, pero se conservan la espera y el estado desarmado. Las muestras con marca de tiempo anterior a la última procesada se ignoran.
- Los gestos solo cuentan en las pantallas de modo, nunca en Inicio ni en Ayuda.
- Un gesto reconocido pasa por **la misma validación** que antes (lista blanca del modo y conexión).
- **El sentido no está validado.** "Hacia fuera" = velocidad angular positiva en X es una convención. El mismo movimiento anatómico da el signo contrario según la muñeca (izquierda o derecha) y la posición de la corona, y la app no tiene en cuenta el ajuste de Wear OS que gira la pantalla 180°. No se ajustará sin datos de un reloj real.
