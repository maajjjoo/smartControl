# Entrenamiento y evaluación (`ai-module/training`)

Herramienta **auxiliar** para entrenar y evaluar el modelo de reconocimiento de gestos (ADR-05). No forma parte de la aplicación: la inferencia en ejecución está en C# ([`windows-app/src/SmartControl.Core/Recognition`](../../windows-app/src/SmartControl.Core/Recognition/)).

> **Todavía no hay datos de un reloj físico ni ningún modelo entrenado con ellos.** Todo lo que se ha ejecutado hasta ahora usa datos sintéticos y solo comprueba el código.

## Requisitos

Python 3.12 y **numpy** (ya instalados). Sin otros paquetes. Los comandos se ejecutan desde esta carpeta.

## Datos

| Carpeta | Contenido | En Git |
|---|---|---|
| `../data/` | Grabaciones **reales** `sensor-data/v1` ([formato](../../docs/recording-format.md)), con [consentimiento](../../docs/data-consent.md) | **No** (ignorada) |
| `../out/` | Modelos e informes generados | **No** (ignorada) |
| `fixtures/` | Fixtures **sintéticos** de paridad con C# | Sí, sin conversión de finales de línea |

Reglas de separación:
- Un conjunto de datos nunca mezcla grabaciones `physical-watch` y `synthetic`.
- Las grabaciones sintéticas solo se aceptan con `--allow-synthetic`, y entonces el informe y la consola llevan el aviso *«DATOS SINTÉTICOS: … no son precisión real»*.
- El generador sintético se niega a escribir en `../data/`.
- Un modelo entrenado con datos sintéticos se exporta como `synthetic-fixture`, y Windows solo lo admite en pruebas.

## Comandos

```powershell
# Evaluar (validación dejando fuera a un participante cada vez; requiere al menos 2)
python -m smartcontrol_training evaluate --data ..\data --kind logistic --report ..\out\report.json

# Entrenar con todos los datos y exportar el modelo y su .sha256
python -m smartcontrol_training train --data ..\data --kind mlp --version 2026-11-01 --out ..\out\model.json

# Solo para probar el código: generar y usar datos sintéticos fuera de ../data
python -m smartcontrol_training synthetic --out $env:TEMP\smartcontrol-sintetico
python -m smartcontrol_training evaluate --data $env:TEMP\smartcontrol-sintetico --allow-synthetic
```

El informe incluye, para el modelo y para el reconocedor por reglas del reloj (a partir del campo `detectorEvent` de cada ventana): exactitud, precisión y exhaustividad por clase, matriz de confusión, tasa de rechazo de gestos y tasa de falsos positivos por ventana negativa. La decisión es la misma que en C#: umbral mínimo 0,80 y, en caso de empate, no actuar.

## Paridad con C#

Las características `gyro-v1` y la inferencia deben dar lo mismo en Python y en C#. `python -m smartcontrol_training.parity` regenera los fixtures, y las pruebas de ambos lados los comparan:
- `fixtures/feature-parity.json`: ventanas sintéticas y sus características (prueba de C# `FeatureParityTests`).
- `fixtures/model-parity/`: un MLP sintético exportado y sus probabilidades esperadas (prueba de C# `ModelParityTests`, que lo carga con `GestureModelLoader`).

## Pruebas

```powershell
python -m unittest discover -s tests -t .
```

## Lo que falta (requiere un reloj físico)

Grabar datos reales, entrenar y elegir el modelo, evaluarlo de verdad y compararlo con las reglas, validar el sentido de los giros y decidir sobre el acelerómetro. Detalle en [phase2-ai-evaluation.md §11](../../docs/phase2-ai-evaluation.md). La tasa de falsos positivos **por hora** necesitará además grabaciones continuas, que `sensor-data/v1` no recoge.
