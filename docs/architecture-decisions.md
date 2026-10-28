# Registro de Decisiones de Arquitectura (ADR) — SmartControl

Este documento registra la arquitectura modular inicial (Sección 5 del [SRS](SRS.md)) y el marco de evaluación para las decisiones técnicas pendientes (Sección 6 del [SRS](SRS.md)).

---

## 1. Separación Modular de Componentes (Adoptado — RF-18, RNF-12, RNF-14)

El sistema se divide estrictamente en cuatro capas desacopladas para permitir pruebas unitarias e integración independiente:

1. **Captura de Sensores (`watch-app/sensors`):** Muestreo de acelerómetro y giroscopio (`Sensor.TYPE_ACCELEROMETER`, `Sensor.TYPE_GYROSCOPE` / `TYPE_LINEAR_ACCELERATION`) con control activo de ciclo de vida para eficiencia energética (RNF-10).
2. **Reconocimiento e Interpretación IA (`ai-module`):** Extracción de características en ventanas temporales, clasificación del gesto con nivel de confianza (`confidence`), filtro por umbral configurable (RF-11, RF-IA-04) y mapeo contextual según el modo activo (`PRESENTATION` o `MULTIMEDIA`).
3. **Comunicación y Protocolo (`protocol` + adaptadores de red):** Transporte inalámbrico local autenticado/vinculado con reconexión automática (RF-16, RF-17, RNF-07, RNF-16).
4. **Ejecución de Acciones en Windows (`windows-app/executor`):** Validador de lista blanca estricta (RF-15, RNF-06) y despachador de eventos del sistema operativo (teclas virtuales de presentación y controles multimedia).

---

## 2. Decisiones Técnicas Pendientes de Validación en Fase 2 (PoC)

De acuerdo con la Sección 6 del SRS, las siguientes decisiones no se asumen a priori sino que se resolverán mediante las pruebas de concepto en `poc/`:

### ADR-01: Ubicación del Modelo de IA (Smartwatch vs. Windows)
- **Opción A (Ejecución en Smartwatch - Edge):** El reloj procesa la ventana de sensores y solo transmite el comando reconocido (menor tráfico de red, mayor carga de CPU/batería en el reloj).
- **Opción B (Ejecución en Windows - Host Local):** El reloj transmite tramas de sensores durante la ventana activa y Windows ejecuta el clasificador (mínimo consumo en reloj, mayor uso de ancho de banda local).
- **Opción C (Híbrido):** Detección de activación de movimiento (umbral de energía/regla ligera) en el reloj y clasificación fina en el reloj o en Windows.
- **Criterio de decisión en Fase 2:** Latencia total `< 1000 ms` (RNF-01), precisión `>= 90 %` (RNF-02) y consumo de batería (RNF-10).
- **Decisión (aprobada el 2026-10-28, provisional): Opción C.** El reloj captura los sensores y **segmenta** el movimiento con su detector por reglas; Windows ejecuta la **inferencia del modelo** en local. Es provisional: se revisará con mediciones de latencia y batería en un reloj físico. La frase de RF-IA-02 «ejecutado en el dispositivo» se interpreta como «ejecutado en local» (sin nube), de acuerdo con la condición de diseño del SRS §3.1.

### ADR-02: Canal de Comunicación Inalámbrica (Wi-Fi LAN/WebSockets/UDP vs. Bluetooth RFCOMM/BLE)
- **Opción A (Wi-Fi Local — WebSocket / TCP / UDP en LAN):** Alta velocidad, fácil depuración desde emulador Wear OS y reloj físico en la misma red local, soporte nativo multiplataforma.
- **Opción B (Bluetooth Clásico RFCOMM / BLE GATT):** No requiere red Wi-Fi común entre el reloj y el PC, menor consumo energético en reposo, pero mayor complejidad de emparejamiento con el stack Bluetooth de Windows y limitaciones en el emulador de Android Studio.
- **Criterio de decisión en Fase 2:** Latencia, estabilidad de reconexión (RF-17) y facilidad de vinculación segura (RNF-07). Gracias a la interfaz de transporte desacoplada (RNF-16), el sistema podrá soportar adaptadores intercambiables.
- **Estado:** sigue pendiente. La integración del transporte queda fuera de la primera etapa del módulo de IA hasta resolver el protocolo y las pruebas con un reloj físico.

### ADR-03: Enfoque del Reconocimiento de Gestos (Reglas + ML Híbrido vs. Modelo ML Puro)
- **Alternativas:** Clasificador híbrido (filtrado por energía + características estadísticas/DTW/Random Forest/MLP ligero o TFLite/ONNX).
- **Criterio de decisión en Fase 2:** Tasa de aciertos `>= 90 %` (RNF-02) y control de falsos positivos ante movimientos cotidianos (RNF-03).
- **Decisión (aprobada el 2026-10-28):** enfoque híbrido. El detector por reglas del reloj se usa para **segmentar** (no para clasificar en el sistema final); un **modelo entrenado** clasifica cada tramo en `TWIST_OUT`, `TWIST_IN` o `NO_GESTURE`. La familia concreta (regresión logística o MLP pequeño) se elegirá con datos reales de un reloj físico, comparándola con el reconocedor por reglas sobre los mismos datos. La primera versión usa solo el giroscopio; el acelerómetro queda como entrada opcional por evaluar (ver [phase2-ai-evaluation.md §6.1](phase2-ai-evaluation.md)).
- **Selección contextual:** el modo activo es contexto de confianza que aporta la aplicación. La acción candidata se obtiene de una tabla fija (modo, gesto) restringida a la lista blanca del modo; el modelo nunca produce nombres de acción ni comandos.

### ADR-04: Protocolo de Mensajes Estructurados
- **Estado:** Borrador en evaluación para la Fase 2 (ver [communication-protocol.md](communication-protocol.md)). No se fija como definitivo sin prueba de concepto.
- **Seguridad (RNF-06, RNF-07):** Lista cerrada (allowlist) de acciones permitidas en el receptor de Windows (ver [security-allowlist.md](security-allowlist.md)):
  - Modo Presentación (`PRESENTATION`): `SLIDE_NEXT`, `SLIDE_PREVIOUS`
  - Modo Multimedia (`MULTIMEDIA`): `MEDIA_PLAY`, `MEDIA_PAUSE`

### ADR-05: Stack Tecnológico Previsto por Componente
- **Aplicación del Smartwatch (`watch-app`):** **Kotlin** y **Wear OS** (Android Studio / Wear OS SDK).
- **Aplicación Complementaria de Windows (`windows-app`):** **C#** y **.NET** para Windows de 64 bits.
- **Módulo de IA (`ai-module`):** Procesamiento 100 % local (ubicación definitiva entre reloj y Windows sujeta a evaluación técnica en la Fase 2).
- **Actualización (2026-10-28):** la inferencia se implementa en **C# sin dependencias externas** dentro de `windows-app` (`SmartControl.Core.Recognition`), compatible con `windows-app/tools/build-nosdk.ps1`. **Python con numpy** solo se usará como herramienta auxiliar de entrenamiento y evaluación en `ai-module/training/`; no es una dependencia en ejecución.

### ADR-06: Modelo de reconocimiento, datos y validación (aprobado el 2026-10-28)
- **Formato:** archivo JSON con versión del esquema, versión del modelo, conjunto de características, clases cerradas (`TWIST_OUT`, `TWIST_IN`, `NO_GESTURE`), normalización, capas densas, umbral y procedencia de los datos (`physical-watch` o `synthetic-fixture`), acompañado de su resumen SHA-256 (detalle en [phase2-ai-evaluation.md §7](phase2-ai-evaluation.md)).
- **Fallo seguro:** sin un modelo válido (ausente, malformado, con dimensiones incoherentes, valores no finitos o resumen incorrecto), todos los gestos se rechazan. Los modelos `synthetic-fixture` solo se admiten en pruebas.
- **Validación:** la acción candidata se envía como `COMMAND_REQUEST` al `CommandValidator` existente, que no se modifica ni se omite. Solo un `AllowedAction` aceptado puede llegar al ejecutor, que sigue siendo simulado.
- **Datos:** los datos sintéticos solo se usan como fixtures de prueba y nunca para medir la precisión. Los datos reales se capturan con consentimiento, sin información personal ajena al movimiento y solo en local (RNF-08).
