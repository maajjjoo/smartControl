# Módulo de Inteligencia Artificial Local (`ai-module`)

Componente independiente encargado del procesamiento y reconocimiento de gestos ([RF-12](../docs/SRS.md), [RF-18](../docs/SRS.md)):
- Preprocesamiento y extracción de características de series temporales de movimiento ([RF-IA-01](../docs/SRS.md)).
- Clasificación local mediante modelo ligero o enfoque híbrido (reglas + aprendizaje automático) sin conexión a Internet ([RF-IA-02](../docs/SRS.md), [RNF-05](../docs/SRS.md), [RNF-08](../docs/SRS.md)).
- Evaluación de nivel de confianza y rechazo de gestos ambiguos bajo el umbral configurado ([RF-11](../docs/SRS.md), [RF-IA-04](../docs/SRS.md)).
- Traducción contextual del gesto reconocido a la acción permitida según el modo activo ([RF-04](../docs/SRS.md), [RF-IA-03](../docs/SRS.md)).
- Conjunto de pruebas etiquetadas y scripts de evaluación de precisión (`>= 90 %`) y tasa de falsos positivos ([RNF-02](../docs/SRS.md), [RNF-03](../docs/SRS.md)).

**Estado actual.** Todavía no hay modelo de aprendizaje automático. La primera etapa (reglas sobre el giroscopio: detección de giros de muñeca, umbral de confianza de 0,80 y rechazo de movimientos ambiguos) está en el reloj: [`watch-app/app/src/main/java/com/smartcontrol/watch/gesture/`](../watch-app/app/src/main/java/com/smartcontrol/watch/gesture/). Los datos para entrenar y evaluar un modelo deben grabarse en un reloj real (ver el registro de muestras en [watch-app/README.md](../watch-app/README.md)).
