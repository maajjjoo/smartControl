# Guía de consentimiento para la captura de datos de movimiento

**Versión:** `consent/v1` (es el valor de `consentVersion` en las grabaciones, ver [recording-format.md](recording-format.md)).
**Alcance:** grabaciones con un reloj físico para entrenar y evaluar el reconocimiento de gestos de SmartControl (RNF-08). No aplica a datos sintéticos.

> Esta guía es una base práctica para un proyecto académico. Si la institución exige revisión ética o un formulario propio, prevalece lo que indique la institución.

## 1. Antes de grabar

1. Explicar el propósito: enseñar a un modelo a reconocer dos giros de muñeca y a distinguirlos de movimientos cotidianos.
2. Explicar qué se graba y qué no (secciones 2 y 3).
3. Dejar claro que participar es **voluntario**, que se puede parar en cualquier momento y que se pueden borrar los datos después, sin dar explicaciones.
4. Pedir el consentimiento **por escrito** (papel o documento firmado) y guardarlo **fuera** del repositorio y separado de las grabaciones.
5. Asignar un identificador anónimo (`P01`, `P02`, …). La tabla que une ese identificador con la persona se guarda solo junto a los consentimientos, nunca en las grabaciones ni en Git.
6. No grabar a menores de edad.

## 2. Qué se graba

- Lecturas del giroscopio (y, si se activa, del acelerómetro) durante los tramos de movimiento.
- Las etiquetas del formato: gesto, modo, postura, actividad, muñeca, posición de la corona.
- El identificador anónimo de participante y de sesión, y la fecha (sin hora).

## 3. Qué no se graba

Nombre, edad, ubicación, audio, vídeo, fotos, identificadores del reloj o de la cuenta, ritmo cardíaco ni ningún otro sensor. Tampoco notas libres que puedan identificar a la persona.

## 4. Durante la sesión

- Sesiones cortas (unos 15 minutos), sentados y con espacio libre alrededor; la correa ajustada.
- Gestos y movimientos cotidianos a ritmo natural, sin forzar la muñeca. Parar si hay molestias.
- No grabar mientras se conduce o se hace una actividad peligrosa.

## 5. Almacenamiento y uso

- Las grabaciones se guardan **solo en local** en `ai-module/data/` (ignorado por Git). No se suben a ningún servicio en la nube.
- Solo se usan para entrenar y evaluar el reconocimiento de SmartControl.
- Se conservan mientras dure el proyecto y después se borran, salvo que la persona acepte expresamente otra cosa.

## 6. Retirada y borrado

Si una persona lo pide, se borran todas sus sesiones (archivos con su `participantId`) y se vuelve a entrenar cualquier modelo que las usara. El borrado se anota junto a los consentimientos, sin datos de movimiento.

## 7. Texto breve para leer a la persona participante

> «Vamos a grabar cómo se mueve tu muñeca con el reloj mientras haces unos gestos y algunos movimientos normales. Solo se guardan datos de movimiento con un código, no tu nombre ni nada que te identifique, y se quedan en este ordenador. Sirven para entrenar el reconocimiento de gestos del proyecto. Puedes parar cuando quieras y pedir que borremos tus datos en cualquier momento. ¿Estás de acuerdo en participar?»
