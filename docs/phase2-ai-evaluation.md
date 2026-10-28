# Evaluación del módulo de IA — Fase 2

**Estado:** dirección aprobada el 2026-10-28 (ver ADR-01, ADR-03, ADR-05 y ADR-06 en [architecture-decisions.md](architecture-decisions.md)). **Todavía no hay ningún modelo entrenado**: no existen datos reales de un reloj físico.

## 1. Objetivo y alcance

SmartControl debe reconocer gestos de muñeca con **aprendizaje automático ejecutado en local en Windows**, sin servicios en la nube (SRS §3.1, RNF-05, RNF-08). La IA:

- **sí** clasifica un tramo de movimiento en `TWIST_OUT`, `TWIST_IN` o `NO_GESTURE`, con probabilidades;
- **no** elige libremente la acción del computador: la acción candidata sale de una tabla fija por modo, restringida a la lista blanca, y el validador existente (`CommandValidator`, sin cambios) la vuelve a validar;
- **no** decide el modo: el modo es contexto de confianza que aporta la aplicación.

No es un chatbot ni usa modelos de lenguaje.

## 2. Situación de partida

- **Reloj:** `WristGestureDetector` reconoce dos giros con **reglas fijas** (umbrales, dominancia del eje, espera y vuelta a reposo). No es IA. Solo usa el giroscopio.
- **Windows:** recibe `COMMAND_REQUEST` por TCP solo en loopback, los valida y los entrega a un ejecutor **simulado**.
- **Transporte reloj–Windows:** no existe (ADR-02 pendiente). El primer trabajo de IA no depende de él.

## 3. Reparto de responsabilidades

| Parte | Responsabilidad | Tipo |
|---|---|---|
| Reloj (reglas) | Captura y **segmentación**: detectar inicio y fin del movimiento, espera mínima, vuelta a reposo | Determinista (ya existe) |
| Windows (modelo) | Clasificar el tramo segmentado: `TWIST_OUT`, `TWIST_IN` o `NO_GESTURE` | **Aprendizaje automático** |
| Windows (selector) | Modo activo (contexto de confianza) + etiqueta → acción candidata **solo** entre las del modo | Determinista |
| Windows (validador) | Autorización, lista blanca, umbral de confianza, modo, rechazo seguro | Determinista (existente, sin cambios) |

La clase `NO_GESTURE` es la aportación principal del modelo frente a las reglas: distinguir un giro intencionado de movimientos cotidianos (RNF-03).

## 4. Alternativas comparadas

Entorno comprobado: .NET 8 solo como runtime (sin SDK ni caché de NuGet), compilación con `windows-app/tools/build-nosdk.ps1`, Python 3.12 con numpy (sin scikit-learn, ONNX ni PyTorch).

| Opción | Dependencias en ejecución | Coste de inferencia | Datos necesarios | Valoración |
|---|---|---|---|---|
| **Red densa en C# puro** (regresión logística = 0 capas ocultas; MLP pequeño = 1 capa) | Ninguna | Microsegundos, < 1 MB | Cientos de ejemplos | **Elegida** como formato. La arquitectura concreta se decide con datos reales |
| Árboles potenciados | Ninguna (inferencia en C#), pero el entrenamiento necesita scikit-learn/LightGBM | Microsegundos | Cientos | Alternativa si la red densa no basta |
| DTW + k vecinos | Ninguna | Milisegundos | Muy pocos por persona | Confianza poco calibrada |
| Red convolucional 1D con ONNX Runtime | Paquete NuGet nativo; PyTorch para entrenar | Milisegundos, decenas de MB | Miles | Desproporcionada ahora; requiere descargas |
| ML.NET | Paquetes NuGet y SDK | Bajo | Cientos | Incompatible con el entorno actual |
| Servicio en la nube | — | — | — | Excluido por el SRS |

Python con numpy solo se usará para entrenar y evaluar, fuera de la aplicación (ADR-05).

## 5. Protocolo de captura de datos (pendiente: requiere un reloj físico)

Ningún dato del emulador ni ninguna serie sintética sirve como dato de entrenamiento o de evaluación.

- **Formato:** versionado (`sensor-data/v1`), una ventana por registro: desfases en milisegundos, giroscopio (x, y, z en rad/s) y, opcionalmente, acelerómetro (x, y, z en m/s²).
- **Etiquetas:** `TWIST_OUT`, `TWIST_IN`, `NO_GESTURE`; además, modo activo, muñeca (izquierda/derecha), posición de la corona, postura (sentado/de pie/caminando) y un identificador anónimo de participante y de sesión.
- **Negativos:** movimientos cotidianos (caminar, escribir, beber, abrir una puerta, gesticular al hablar), sacudidas, giros lentos y gestos ambiguos o incompletos.
- **Consentimiento y privacidad:** consentimiento informado; solo datos de movimiento y las etiquetas anteriores, sin nombre, ubicación, audio ni identificadores del dispositivo; almacenamiento solo local (RNF-08); derecho a borrar los datos de una persona.
- **Separación:** entrenamiento y prueba **por participante** (ningún participante en ambos conjuntos) para evitar fugas.

## 6. Entrada del modelo (versión 1)

### 6.1. ¿Hace falta el acelerómetro?

**No para la primera versión.** El gesto es un giro alrededor del antebrazo, que mide el giroscopio, y las reglas actuales funcionan solo con él. El acelerómetro aportaría contexto (postura del brazo) y podría ayudar a separar gestos de la marcha, pero no hay datos que lo demuestren; además gastaría batería (RNF-10) y duplicaría el tamaño de la ventana, que ya supera el límite actual de 4096 bytes. Por eso:

- la ventana admite el acelerómetro como **opcional** (para grabarlo en la captura y compararlo después);
- el conjunto de características `gyro-v1` **no lo usa**.

### 6.2. Ventana de sensores

| Regla | Valor |
|---|---|
| Muestras de giroscopio | entre 8 y 256 |
| Tiempo | el primer desfase es 0; estrictamente creciente; hueco máximo entre muestras 100 ms; duración máxima 2000 ms |
| Valores | números finitos; giroscopio con |valor| ≤ 35 rad/s por eje |
| Acelerómetro (opcional) | mismas reglas de tiempo y número de muestras; |valor| ≤ 160 m/s² por eje |

### 6.3. Características `gyro-v1` (31 valores)

Para cada eje del giroscopio (x, y, z), en este orden: media, desviación típica (poblacional), mínimo, máximo, pico absoluto, posición relativa del pico (instante del pico / duración), integral (ángulo girado, regla del trapecio, rad), cruces por cero (cambios de signo entre valores con |valor| ≥ 0,2 rad/s) y energía (media de los cuadrados): 27 valores.

Después: velocidad angular total máxima, velocidad angular total media, duración (s) y dominancia del eje X (∫|x| / (∫|x| + ∫|y| + ∫|z|)): 4 valores.

## 7. Formato del modelo (ADR-06)

JSON con: versión del esquema, versión del modelo, conjunto de características, clases (cerradas: `TWIST_OUT`, `TWIST_IN`, `NO_GESTURE`), normalización de las características (media y desviación), capas densas con su activación, umbral de decisión y **procedencia de los datos** (`physical-watch` o `synthetic-fixture`). Junto al modelo, su resumen SHA-256. Sin modelo válido, todos los gestos se rechazan. Un modelo `synthetic-fixture` solo se admite en las pruebas.

## 8. Evaluación y criterios de aceptación

- Validación dejando fuera a un participante cada vez.
- Por clase: precisión, exhaustividad y matriz de confusión; tasa de rechazo; falsos positivos por hora de movimiento cotidiano (RNF-03, con un límite fijado antes de medir); calibración de la confianza.
- **Comparación con el reconocedor de reglas** sobre los mismos datos: el modelo solo se adopta si lo mejora.
- Objetivos: ≥ 90 % de reconocimientos correctos (RNF-02); latencia de extremo a extremo ≤ 1 s en el 95 % de los casos (RNF-01). La inferencia en Windows se medirá aparte, con su configuración documentada.

### 8.1. Latencia medida en Windows (2026-10-28)

Prueba `latencia: características + inferencia y recorrido completo` (`windows-app/tests/SmartControl.Core.Tests/RecognitionLatencyTests.cs`), ejecutada con `windows-app/tools/build-nosdk.ps1 -Test`.

- **Equipo:** Intel Core i9-13900H (20 procesadores lógicos), Windows 11 (10.0.26200), .NET 8.0.25, compilación optimizada.
- **Entrada:** ventana **sintética** de 50 muestras (1 s a 50 Hz) y el modelo **sintético** de una capa de las pruebas. 1000 repeticiones de calentamiento y 10 000 medidas.
- **Resultado (tres ejecuciones):**

| Tramo | p50 | p95 | Máximo |
|---|---|---|---|
| Características + inferencia | 15–15,2 µs | 20,3–21,3 µs | 0,26–1,0 ms |
| Recorrido completo con el validador existente | 43,1–43,5 µs | 69,5–79,7 µs | 1,9–3,2 ms |

Los máximos son picos aislados del sistema (planificación, recolección de memoria). **No es la latencia de RNF-01**: no incluye la captura en el reloj, la segmentación ni el transporte, que aún no existe. Un MLP con una capa oculta añade pocas operaciones; se medirá de nuevo con el modelo definitivo.

## 9. Datos sintéticos frente a datos reales

Las pruebas usan series y modelos sintéticos escritos a mano para comprobar el código (formato, cálculos, rechazos). **No miden la precisión** y siempre se identifican como `synthetic-fixture`. No se afirmará ninguna precisión real hasta entrenar y evaluar con datos representativos de un reloj físico.

## 10. Estado

| Parte | Estado |
|---|---|
| Decisiones de arquitectura | Aprobadas (ADR-01, ADR-03, ADR-05, ADR-06) |
| Ventana de sensores y características `gyro-v1` (C#) | **Hecho**, con pruebas |
| Formato, carga con SHA-256 e inferencia del modelo (C#) | **Hecho**, con pruebas y modelos sintéticos |
| Decisión (umbral, `NO_GESTURE`) y selección contextual | **Hecho**, con pruebas |
| Integración con el `CommandValidator` existente | **Hecho** (sin modificar el validador); sin conectar al receptor ni a la interfaz |
| Medición de latencia en Windows | **Hecho** con datos sintéticos (§8.1) |
| Modelo entrenado | **No existe** |
| Captura de datos y entrenamiento | **Bloqueado**: requiere un reloj físico (etapa 3) |
| Transporte y mensaje `SENSOR_WINDOW` | Fuera de esta etapa; depende de ADR-02 (etapa 4) |

## 11. Pasos bloqueados hasta tener grabaciones de un reloj físico

Se pueden preparar sin el reloj (y se marcarán como sintéticos o sin datos), pero **no se pueden completar**:

1. **Grabar datos etiquetados** (§5): gestos, negativos, consentimiento y participantes reales.
2. **Entrenar** cualquier modelo con `dataSource: physical-watch`. El cargador exige que ese modelo declare participantes, número de ventanas y el resumen SHA-256 del conjunto de datos.
3. **Elegir la arquitectura** (regresión logística o MLP) comparándolas con datos reales.
4. **Evaluar**: precisión y exhaustividad por clase, matriz de confusión, tasa de rechazo, falsos positivos por hora y calibración (§8).
5. **Comparar con el reconocedor por reglas** del reloj sobre los mismos datos.
6. **Validar el sentido de los giros** según la muñeca y la posición de la corona antes de cambiar ninguna asociación.
7. **Decidir si el acelerómetro aporta** (comparar `gyro-v1` con un conjunto que lo use).
8. **Medir la latencia de extremo a extremo** (RNF-01), que necesita además el transporte (ADR-02).

Hasta entonces no se afirmará ninguna precisión real, y ningún modelo sintético se instalará fuera de las pruebas.
