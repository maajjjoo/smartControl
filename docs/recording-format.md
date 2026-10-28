# Formato de grabación de datos de sensores — `sensor-data/v1`

**Uso:** grabar tramos de movimiento etiquetados para entrenar y evaluar el modelo de reconocimiento (ver [phase2-ai-evaluation.md §5](phase2-ai-evaluation.md)). Antes de grabar a una persona hay que seguir la [guía de consentimiento](data-consent.md).

## 1. Archivo

- Una **sesión** por archivo, en **JSON Lines** (UTF-8, un objeto JSON por línea, sin líneas vacías intermedias). Extensión `.jsonl`.
- La **primera línea** es la cabecera de la sesión; cada línea siguiente es **una ventana**.
- Los campos son exactamente los indicados: se rechazan campos desconocidos, repetidos o ausentes.
- Las grabaciones reales se guardan en `ai-module/data/`, que **Git ignora**: nunca se suben al repositorio.

## 2. Cabecera

```json
{"format":"sensor-data/v1","dataSource":"physical-watch","recorder":"watch-debug-recorder/1","consentVersion":"consent/v1","sessionId":"S01","participantId":"P01","wrist":"left","crown":"hand","samplingHz":50,"createdDate":"2026-10-28"}
```

| Campo | Valores |
|---|---|
| `format` | `sensor-data/v1` |
| `dataSource` | `physical-watch` (reloj físico real) o `synthetic` (generado por programa o grabado en el emulador). **Nunca se mezclan** en un mismo conjunto de entrenamiento o evaluación |
| `recorder` | Herramienta que escribió el archivo: `watch-debug-recorder/1` o `synthetic-generator/1` |
| `consentVersion` | Versión del consentimiento firmado (`consent/v1`). Obligatorio con `physical-watch`; `null` con `synthetic` |
| `sessionId`, `participantId` | Identificadores **anónimos**: 1 a 32 caracteres `A-Z a-z 0-9 _ -`. La correspondencia con personas reales se guarda fuera del repositorio y fuera de estos archivos |
| `wrist` | `left` o `right` |
| `crown` | Hacia dónde apunta la corona: `hand` (hacia la mano) o `elbow` (hacia el codo) |
| `samplingHz` | Frecuencia nominal pedida al sensor (entero entre 10 y 200) |
| `createdDate` | Solo la fecha (`AAAA-MM-DD`), sin hora |

No se graban nombres, ubicación, audio, identificadores del dispositivo ni la hora exacta.

## 3. Ventana

```json
{"windowId":1,"label":"TWIST_OUT","mode":"PRESENTATION","posture":"sitting","activity":null,"detectorEvent":"TWIST_OUT","gyroscope":[[0,0.01,0.0,-0.02],[20,0.4,0.1,0.0]],"accelerometer":null}
```

| Campo | Valores |
|---|---|
| `windowId` | Entero ≥ 1, creciente dentro de la sesión |
| `label` | Etiqueta verdadera: `TWIST_OUT`, `TWIST_IN` o `NO_GESTURE` |
| `mode` | `PRESENTATION`, `MULTIMEDIA` o `null` (el modo no influye en la etiqueta del movimiento) |
| `posture` | `sitting`, `standing`, `walking` o `null` |
| `activity` | Solo con `NO_GESTURE`: `walking`, `typing`, `drinking`, `door`, `talking`, `shake`, `slow_turn`, `ambiguous` u `other`. `null` en los gestos |
| `detectorEvent` | Lo que dijo el reconocedor por reglas del reloj al segmentar esta ventana: `TWIST_OUT`, `TWIST_IN`, `UNCLEAR` o `null` (si no hubo detector). Permite comparar el modelo con las reglas sobre los mismos datos |
| `gyroscope` | Lista de `[tOffsetMs, x, y, z]`, en rad/s |
| `accelerometer` | Igual que `gyroscope` en m/s², o `null` (opcional en v1) |

Reglas de cada serie (las mismas que `SensorWindow` en Windows, [phase2-ai-evaluation.md §6.2](phase2-ai-evaluation.md)): entre 8 y 256 muestras; el primer desfase es 0 y siempre crece; hueco máximo de 100 ms; duración máxima de 2000 ms; valores finitos; giroscopio con |valor| ≤ 35 rad/s y acelerómetro con |valor| ≤ 160 m/s².

## 4. Versionado

Cualquier cambio incompatible (campos, unidades, reglas) exige `sensor-data/v2`. Los lectores rechazan versiones que no conocen.
