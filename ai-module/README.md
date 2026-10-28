# Módulo de Inteligencia Artificial Local (`ai-module`)

Componente independiente encargado del procesamiento y reconocimiento de gestos ([RF-12](../docs/SRS.md), [RF-18](../docs/SRS.md)):
- Preprocesamiento y extracción de características de series temporales de movimiento ([RF-IA-01](../docs/SRS.md)).
- Clasificación local mediante modelo ligero o enfoque híbrido (reglas + aprendizaje automático) sin conexión a Internet ([RF-IA-02](../docs/SRS.md), [RNF-05](../docs/SRS.md), [RNF-08](../docs/SRS.md)).
- Evaluación de nivel de confianza y rechazo de gestos ambiguos bajo el umbral configurado ([RF-11](../docs/SRS.md), [RF-IA-04](../docs/SRS.md)).
- Traducción contextual del gesto reconocido a la acción permitida según el modo activo ([RF-04](../docs/SRS.md), [RF-IA-03](../docs/SRS.md)).
- Conjunto de pruebas etiquetadas y scripts de evaluación de precisión (`>= 90 %`) y tasa de falsos positivos ([RNF-02](../docs/SRS.md), [RNF-03](../docs/SRS.md)).

**Estado actual.** Todavía **no hay ningún modelo entrenado**. El reloj tiene un reconocedor por reglas (no es IA) que, en la arquitectura aprobada (ADR-01 C), pasará a segmentar el movimiento: [`watch-app/app/src/main/java/com/smartcontrol/watch/gesture/`](../watch-app/app/src/main/java/com/smartcontrol/watch/gesture/). La base de la inferencia en Windows ya existe en C# (ventana, características, formato y carga del modelo, clasificador, decisión, selección por modo e integración con el validador): [`windows-app/src/SmartControl.Core/Recognition/`](../windows-app/src/SmartControl.Core/Recognition/). Solo se ha probado con datos y modelos sintéticos. Lo que queda bloqueado hasta tener grabaciones de un reloj físico está en [phase2-ai-evaluation.md §11](../docs/phase2-ai-evaluation.md).
