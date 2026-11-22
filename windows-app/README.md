# Aplicación Complementaria para Windows (`windows-app`)

Componente receptor para **Windows (64 bits)** previsto en **C# y .NET**, encargado de:
- Mantener el canal de comunicación inalámbrica con el smartwatch ([RF-06](../docs/SRS.md), [RF-17](../docs/SRS.md)).
- Validar la autenticidad, estructura e integridad de los mensajes recibidos ([RNF-04](../docs/SRS.md), [RNF-07](../docs/SRS.md), [communication-protocol.md](../docs/communication-protocol.md)).
- Comprobar que cada comando pertenezca estrictamente a la lista blanca de acciones permitidas ([RF-15](../docs/SRS.md), [RNF-06](../docs/SRS.md), [security-allowlist.md](../docs/security-allowlist.md)).
- Ejecutar las acciones explícitas permitidas de manera desacoplada de la capa de red ([RF-07](../docs/SRS.md), [RF-08](../docs/SRS.md), [RF-18](../docs/SRS.md)):
  - **Modo Presentación (`PRESENTATION`):** Avanzar diapositiva (`SLIDE_NEXT`), retroceder diapositiva (`SLIDE_PREVIOUS`).
  - **Modo Multimedia (`MULTIMEDIA`):** Reproducir (`MEDIA_PLAY`), pausar (`MEDIA_PAUSE`), siguiente canción (`MEDIA_NEXT_TRACK`) y canción anterior (`MEDIA_PREVIOUS_TRACK`).

## Estado actual (etapa 2: acciones reales en Windows)

Aplicación de escritorio **WinForms sobre .NET 8**. Las acciones aceptadas **se ejecutan de verdad** (`src/SmartControl.Desktop/Execution/`):

| Acción | Qué hace Windows |
|---|---|
| `SLIDE_NEXT` / `SLIDE_PREVIOUS` | Pulsa **→** / **←** en la ventana que esté al frente (PowerPoint, Google Slides, Canva, PDF a pantalla completa). La presentación debe estar al frente. |
| `MEDIA_PLAY` / `MEDIA_PAUSE` | Pide **reproducir** o **pausar** al reproductor que tiene el control multimedia de Windows (Spotify, YouTube en el navegador, Reproductor multimedia…). Son dos órdenes distintas: pausar nunca reanuda. |
| `MEDIA_NEXT_TRACK` / `MEDIA_PREVIOUS_TRACK` | Pide **siguiente** o **anterior canción** al mismo reproductor. |

Si no hay ningún reproductor abierto, el reproductor rechaza la orden o Windows bloquea la tecla, la respuesta es `EXECUTION_ERROR` con el motivo (`NO_MEDIA_SESSION`, `MEDIA_COMMAND_REFUSED`, `INPUT_BLOCKED`). Las teclas son fijas en el código: desde la red solo llega la acción validada. Limitación conocida de Windows: una ventana abierta como administrador no recibe las teclas de una aplicación normal, y Windows no lo indica.

| Proyecto | Contenido |
|---|---|
| `src/SmartControl.Core` | Lista blanca (`CommandAllowlist`), validador (`CommandValidator`), receptor TCP solo en `127.0.0.1` (`LocalCommandReceiver`), ejecutor simulado (solo para pruebas), registro de eventos y reconocimiento local (`Recognition/`). No depende de la interfaz. |
| `src/SmartControl.Desktop` | Ventana: estado del receptor, comandos recibidos con su resultado y panel de comandos de prueba. Ejecutor real (`Execution/`). |
| `tests/SmartControl.Core.Tests` | Pruebas del validador, del receptor (con clientes reales en loopback), del registro y del reconocimiento local. Sin paquetes NuGet. |
| `tests/SmartControl.Desktop.Tests` | Pruebas de la ventana sin mostrarla: texto y avisos para los lectores de pantalla, colores con contraste alto y ejecutor real (sin pulsar teclas ni tocar la música: se usan versiones falsas, y el control multimedia real solo se lee). Sin paquetes NuGet. |

Comportamiento:
- La aplicación escucha en la **red local** (TCP 47800) y responde a la búsqueda del reloj (UDP 47801), así el reloj la encuentra solo. Recibe JSON delimitado por salto de línea (máx. 4096 bytes), con las reglas de [communication-protocol.md §4](../docs/communication-protocol.md). La primera vez, Windows pregunta en el **Firewall**: hay que permitir el acceso en redes privadas.
- **Vinculación:** cuando un reloj nuevo se conecta, aparece «Un reloj quiere conectarse» con un código de 4 cifras, el mismo que muestra el reloj; se pulsa **Vincular**. Los relojes vinculados se guardan cifrados (DPAPI, solo para la cuenta de Windows actual) en `%LOCALAPPDATA%\SmartControl\pairing.dat` y se pueden olvidar desde el botón «N relojes vinculados». Sin vincular, ningún dispositivo de la red puede enviar comandos. Detalle del protocolo en [communication-protocol.md §3.4](../docs/communication-protocol.md).
- El panel de prueba envía mensajes reales por loopback y **las acciones se ejecutan**: «Reproducir» y «Pausar» actúan sobre la música; las flechas llegan a la ventana al frente, que al pulsar el botón es la propia SmartControl. Los mensajes van firmados con un token aleatorio que se genera en cada arranque y solo existe en memoria. **Son comandos explícitos, no gestos reconocidos**, y una prueba en loopback no demuestra la comunicación con el reloj.
- La interfaz solo muestra valores validados o generados por la aplicación, nunca texto recibido.
- El registro está en `%LOCALAPPDATA%\SmartControl\logs\smartcontrol-AAAAMMDD.log` (un archivo por día, se borran los de más de 7 días). Guarda la fecha, el estado, el motivo, la acción validada y el `messageId` validado; nunca el token ni el mensaje.

### Reconocimiento local de gestos (`src/SmartControl.Core/Recognition`)

IA de reconocimiento en Windows (ADR-01, ADR-03, ADR-06 en [architecture-decisions.md](../docs/architecture-decisions.md); detalle en [phase2-ai-evaluation.md](../docs/phase2-ai-evaluation.md)). Está **conectada al flujo real** desde el 2026-11-21: el reloj envía la ventana de cada movimiento (`SENSOR_WINDOW`, [communication-protocol.md §3.3](../docs/communication-protocol.md)) y `GestureRecognizer` decide con el modelo; la acción pasa por el validador de siempre.

- **Gemini (ADR-07):** copiar `.env.example` como `.env` (en la raíz del repositorio o en `%LOCALAPPDATA%\SmartControl\`) y poner `GEMINI_API_KEY`. Al abrir la app, la ventana muestra «IA activa · Gemini (gemini-3.5-flash-lite)». Solo se envían a Gemini los números del giroscopio; si no responde en 2,5 s o no hay internet, deciden las reglas. El `.env` lo ignora Git.
- **Modelo local (opcional, tiene prioridad):** copiar `model.json` y `model.json.sha256` (los genera `ai-module/training`) en `%LOCALAPPDATA%\SmartControl\model\`. La ventana muestra «IA activa · modelo …».
- **Sin IA** (ni modelo ni clave): deciden las reglas del reloj y la ventana dice «IA sin configurar». En la actividad, cada gesto dice «IA 93 %» o «reglas del reloj». El registro anota `gemini_configured`, `gemini_missing_key`, `model_loaded` o `model_rejected`, y en cada gesto `by=AI engine="Gemini (…)"` o `by=RULES`; nunca la clave.
- **Todavía no hay ningún modelo entrenado con datos reales.** Probado de punta a punta con el emulador y un modelo sintético de demostración (solo en una ventana de prueba): la IA reconoció un giro con 98 % y descartó una sacudida.

| Componente | Función |
|---|---|
| `SensorWindow` | Ventana de giroscopio (acelerómetro opcional) ya segmentada por el reloj; valida número de muestras, tiempo, huecos, duración y rangos. |
| `MovementFeatures` | 31 características deterministas `gyro-v1`, solo del giroscopio. |
| `GestureModel` / `GestureModelLoader` | Formato JSON del modelo con procedencia de los datos (`physical-watch` o `synthetic-fixture`) y su resumen SHA-256. Rechaza modelos alterados, incoherentes o con umbral menor que 0,80; los sintéticos solo en pruebas. |
| `DenseClassifier` | Inferencia en C# puro (regresión logística o MLP pequeño), sin paquetes NuGet. |
| `GestureDecision` | Umbral de confianza y clase `NO_GESTURE`: ante la duda, no actuar. |
| `ContextualActionSelector` | Modo activo (contexto de confianza) + gesto → acción candidata, solo dentro de la lista blanca del modo. |
| `RecognitionPipeline` | Une todo y envía la candidata al `CommandValidator` existente, sin modificarlo. No ejecuta acciones: devuelve, como mucho, una `AllowedAction` validada. |

Las pruebas usan ventanas y modelos **sintéticos** escritos a mano (`tests/SmartControl.Core.Tests/ModelFixtures.cs`): comprueban el código, no la precisión con movimientos reales. La latencia medida en este equipo está en [phase2-ai-evaluation.md §8.1](../docs/phase2-ai-evaluation.md).

### Compilar y probar

Con el **.NET 8 SDK** instalado (forma oficial):

```powershell
dotnet run --project windows-app/tests/SmartControl.Core.Tests
dotnet run --project windows-app/tests/SmartControl.Desktop.Tests
dotnet run --project windows-app/src/SmartControl.Desktop
```

Sin el SDK (provisional): `tools/build-nosdk.ps1` compila con el compilador C# de Visual Studio Build Tools y los ensamblados del runtime .NET 8 ya instalados. No descarga nada. La salida queda en `windows-app/bin/nosdk`.

```powershell
.\windows-app\tools\build-nosdk.ps1 -Test   # compila y ejecuta las pruebas
.\windows-app\tools\build-nosdk.ps1 -Run    # compila y abre la aplicación
```

Con esta vía la aplicación arranca mediante `dotnet SmartControl.dll`, así que también aparece una ventana de consola.
