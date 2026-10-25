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
