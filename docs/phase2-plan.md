# Plan técnico — Fase 2 (pruebas de concepto)

**Fecha:** 2026-10-24
**Base:** decisiones pendientes ADR-01 a ADR-04 de [architecture-decisions.md](architecture-decisions.md), sección 6 del [SRS](SRS.md) y [communication-protocol.md](communication-protocol.md).

Cada PoC es pequeña e independiente. Las hipótesis **no** son decisiones. El resultado de cada una se recoge en el README de su carpeta en `poc/` y en la columna «Estado» de esta tabla.

| PoC | Decisión que alimenta | Estado |
|---|---|---|
| 1. Validación de comandos y lista blanca | ADR-04, RF-15, RNF-04, RNF-06 | **Ejecutada (aprobada)** |
| 2. Recepción local en Windows | ADR-04, RF-06, RNF-14 | **Ejecutada (aprobada, solo loopback)** |
| 3. Bluetooth frente a Wi-Fi local | ADR-02 | Análisis documental; **bloqueada para medición física** |
| 4. Emulador de Wear OS y sensores | ADR-01, ADR-03 | **Ejecutada (aprobada, con limitaciones)** |
| 5. Dónde ejecutar el reconocimiento | ADR-01 | Análisis documental; falta evidencia medida |
| 6. Reconocimiento inicial con datos de sensores | ADR-03, RNF-02, RNF-03 | **No ejecutada**: no hay datos legítimos |

---

## PoC 1 — Validación de comandos y lista blanca de seguridad

- **Objetivo:** comprobar que las reglas del protocolo §4 y de la lista blanca §3 rechazan todo mensaje que no sea un comando permitido, coherente con su modo y bien formado.
- **Hipótesis:** un validador por etapas, sin dependencias y basado en enums cerrados, rechaza el 100 % de los casos negativos definidos sin aceptar ninguno indebido.
- **Recursos:** Python 3.12 (stdlib), `unittest`. Prototipo temporal en `poc/command-validator/`.
- **Procedimiento:** implementar el validador; escribir casos positivos (las 4 acciones) y negativos (estructura, tipos, modo, lista blanca, repetición, frescura, tamaño, JSON malformado, cargas tipo shell); ejecutar las pruebas; aplicar mutaciones al validador para comprobar que las pruebas detectan regresiones.
- **Datos medidos:** pruebas aprobadas y fallidas, mutaciones detectadas.
- **Criterio de aceptación:** todas las pruebas pasan; ninguna prueba negativa produce `AllowedAction`; cada mutación de una regla de seguridad hace fallar al menos una prueba.
- **Limitaciones:** es Python, no C#. El portado a .NET debe repetir las pruebas.
- **Riesgos:** divergencia entre esta PoC y la implementación en C# (por ejemplo, en el manejo de NaN o de claves duplicadas, que cambia según el parser).
- **Resultado real:** ver [poc/command-validator/README.md](../poc/command-validator/README.md).

## PoC 2 — Recepción local de mensajes de prueba en Windows

- **Objetivo:** comprobar el ciclo iniciar → recibir → validar → responder → registrar → cerrar sin comunicación inalámbrica y sin ejecutar acciones.
- **Hipótesis:** un receptor TCP con mensajes JSON por línea, ligado solo a loopback, cumple ese ciclo, se cierra limpio y no deja procesos ni puertos abiertos.
- **Recursos:** Python 3.12 (stdlib). `poc/local-receiver/`.
- **Procedimiento:** pruebas de integración con clientes reales sobre `127.0.0.1`: mensajes válidos e inválidos, repetición, trama sobredimensionada, 5 clientes concurrentes, ausencia del token en los registros y cierre limpio. Demostración con 200 idas y vueltas.
- **Datos medidos:** pruebas aprobadas; RTT en loopback (mediana, p95, máximo).
- **Criterio de aceptación:** todas las pruebas pasan; el puerto queda libre después de `stop()`; no quedan procesos.
- **Limitaciones:** el RTT en loopback **no** mide ningún enlace inalámbrico. No hay C# porque falta el .NET SDK.
- **Riesgos:** confundir el RTT en loopback con la latencia de RNF-01. Se mitiga con advertencias explícitas.
- **Resultado real:** ver [poc/local-receiver/README.md](../poc/local-receiver/README.md).

## PoC 3 — Evaluación preliminar de Bluetooth frente a Wi-Fi local

- **Objetivo:** decidir qué transporte probar primero y qué medir.
- **Hipótesis:** Wi-Fi local (TCP) es el primer candidato por su facilidad de prueba con el emulador. BLE GATT es el candidato para el reloj físico sin red común. Ninguna se confirma sin hardware.
- **Recursos:** documentación pública. Para medir: un reloj Wear OS físico, el adaptador Bluetooth del PC y una red local controlada.
- **Procedimiento:** (a) análisis documental (hecho, en [communication-protocol.md §1](communication-protocol.md)); (b) con emulador: cliente → `10.0.2.2` → receptor de la PoC 2; (c) con reloj físico: medir RTT, pérdidas, reconexión y batería en ambos transportes.
- **Datos que se medirán:** RTT de extremo a extremo (n ≥ 200 por condición), tasa de pérdida, tiempo de reconexión tras cortar el enlace, consumo de batería por hora con el enlace activo e inactivo.
- **Criterio de aceptación:** p95 del transporte de comando suficientemente bajo para que el total cumpla RNF-01 (≤ 1 s en el 95 % de los casos, sumando la captura y la inferencia); reconexión sin reiniciar (RF-17).
- **Limitaciones:** no hay reloj; el emulador no pone el adaptador Bluetooth del PC a disposición de la aplicación.
- **Riesgos:** políticas de Wear OS que apagan el Wi-Fi; dispositivos que no permiten emparejar con un PC o anunciar por BLE.

## PoC 4 — Emulador de Wear OS y sensores

- **Objetivo:** confirmar que se puede iniciar un AVD de Wear OS y qué sensores simulados expone.
- **Hipótesis:** el emulador expone un acelerómetro y un giroscopio virtuales que se pueden manipular desde *Extended controls → Virtual sensors* o con la consola (`adb emu sensor ...`).
- **Recursos:** imagen Wear OS 6.0 (API 36, x86_64) y AVD `Wear_OS_Large_Round_API_36`, instalados el 2026-10-25 con autorización. Aceleración WHPX.
- **Procedimiento:** tras autorizar la descarga, crear el AVD, iniciarlo, ejecutar `adb shell dumpsys sensorservice` y `adb emu sensor status`, y modificar los valores para observar su lectura.
- **Datos medidos:** lista de sensores, frecuencias declaradas, posibilidad de inyectar valores.
- **Criterio de aceptación:** el AVD arranca y aparecen el acelerómetro y el giroscopio.
- **Limitaciones:** los datos simulados **no** son movimientos reales de muñeca. Sirven para probar el flujo, no la precisión.
- **Riesgos:** confundir la inyección manual de valores con datos de entrenamiento.
- **Resultado real:** el AVD arranca y expone acelerómetro y giroscopio (Goldfish), entregados a ~50 Hz con `SENSOR_DELAY_GAME`. Los valores se pueden inyectar con `adb emu sensor set gyroscope x:y:z`, pero el sensor **solo cambia aproximadamente una vez por segundo**, así que no se puede imitar un giro rápido de muñeca. Por eso los giros rápidos se prueban reproduciendo series sintéticas por el reconocedor (solo en depuración; ver [watch-app/README.md](../watch-app/README.md)). Estos datos no sirven para entrenar ni para medir la precisión.

## PoC 5 — Dónde ejecutar el reconocimiento

- **Objetivo:** reunir los criterios y la evidencia necesarios para ADR-01.
- **Hipótesis:** para la primera iteración conviene la opción híbrida (C): el reloj detecta la activación con una regla ligera y Windows clasifica. Cuando haya mediciones, puede convenir mover la clasificación al reloj.
- **Recursos:** análisis en [phase2-ai-evaluation.md](phase2-ai-evaluation.md). Para medir: reloj físico.
- **Datos que se medirán:** tiempo de inferencia en el reloj, tamaño de la ventana transmitida, consumo de batería, latencia total.
- **Criterio de aceptación:** la alternativa elegida cumple RNF-01 y RNF-10 con datos medidos.
- **Limitaciones y riesgos:** sin reloj, todo es estimación.

## PoC 6 — Reconocimiento inicial con datos de sensores

- **Objetivo:** primera estimación de precisión y de falsos positivos.
- **Condición previa:** existen datos reales etiquetados. **Hoy no existen**, y el emulador no puede generarlos de forma legítima.
- **Procedimiento previsto:** ver el protocolo de captura en [phase2-ai-evaluation.md §5](phase2-ai-evaluation.md).
- **Criterio de aceptación:** se informan la precisión (objetivo RNF-02 ≥ 90 %) y los falsos positivos (RNF-03) **por separado**, con validación por participante.
- **Resultado real:** no ejecutada.
