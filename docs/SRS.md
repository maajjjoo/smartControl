# SmartControl
## Documento de requisitos de software (SRS)

**Versión:** 1.0  
**Estado:** Requisitos iniciales consolidados  
**Plataforma inicial:** Smartwatch con Wear OS y computador con Windows  
**Enfoque:** Control de dispositivos mediante gestos e inteligencia artificial local.

---

## 1. Descripción general

### 1.1. Propósito

SmartControl será una aplicación que permitirá controlar determinadas funciones de un computador mediante gestos realizados con la mano y la muñeca mientras se utiliza un smartwatch compatible.

El sistema utilizará los sensores de movimiento del reloj para reconocer gestos predefinidos y un mecanismo de inteligencia artificial ejecutado localmente para interpretar los movimientos y determinar las acciones correspondientes, de acuerdo con el modo de control activo.

La primera versión estará enfocada en controlar presentaciones y reproducción multimedia en Windows. La arquitectura deberá permitir incorporar posteriormente nuevas funciones y compatibilidad con dispositivos móviles.

### 1.2. Objetivo general

Desarrollar un sistema de control inalámbrico que integre una aplicación para smartwatch, un programa complementario para Windows y un mecanismo de reconocimiento inteligente de gestos, ofreciendo una interacción natural, segura y confiable.

### 1.3. Objetivos específicos

- Reconocer gestos predefinidos mediante los sensores de movimiento del smartwatch.
- Interpretar los gestos de acuerdo con el contexto y el modo de control seleccionado.
- Transmitir comandos al computador mediante un mecanismo inalámbrico.
- Ejecutar acciones compatibles en Windows, principalmente para presentaciones y reproducción multimedia.
- Incorporar inteligencia artificial local para reducir la dependencia de servicios externos.
- Mantener una arquitectura modular que permita ampliar las funcionalidades del sistema.

## 2. Alcance

### 2.1. Funcionalidades incluidas en el MVP

La primera versión incluirá:

- Aplicación para smartwatch compatible con Wear OS.
- Captura de datos de movimiento mediante sensores disponibles.
- Reconocimiento de un conjunto limitado de gestos predefinidos.
- Interpretación contextual de los gestos mediante procesamiento local.
- Comunicación inalámbrica entre el reloj y Windows.
- Control de presentaciones.
- Control de reproducción multimedia.
- Visualización del estado de conexión y de las acciones reconocidas.
- Gestión de errores y recuperación de la conexión.
- Pruebas funcionales y de rendimiento.

### 2.2. Funcionalidades fuera del alcance inicial

Las siguientes funcionalidades se reservarán para versiones posteriores:

- Control de cámara del celular.
- Compatibilidad completa con iPhone y Android.
- Aprendizaje automático de gestos personalizados por cada usuario.
- Control de dispositivos adicionales.
- Sincronización mediante cuentas y almacenamiento en la nube.
- Ejecución de comandos arbitrarios en el sistema operativo.

## 3. Requerimientos funcionales

Los requerimientos funcionales describen los servicios y comportamientos que deberá ofrecer el sistema.

| Código | Descripción | Prioridad |
|---|---|---|
| RF-01 | El sistema deberá permitir iniciar y cerrar la aplicación desde el smartwatch. | Alta |
| RF-02 | El sistema deberá capturar los datos de movimiento disponibles mediante los sensores compatibles del reloj. | Alta |
| RF-03 | El sistema deberá reconocer los gestos predefinidos incluidos en el MVP. | Alta |
| RF-04 | El sistema deberá interpretar los gestos según el modo de control activo. | Alta |
| RF-05 | El sistema deberá permitir seleccionar entre el modo de presentaciones y el modo multimedia. | Alta |
| RF-06 | El sistema deberá transmitir los comandos reconocidos al programa complementario de Windows mediante la conexión configurada. | Alta |
| RF-07 | El sistema deberá permitir avanzar y retroceder diapositivas mediante gestos configurados. | Alta |
| RF-08 | El sistema deberá permitir reproducir y pausar contenido multimedia mediante gestos configurados. | Alta |
| RF-09 | El sistema deberá mostrar en el reloj el estado de conexión con el computador. | Alta |
| RF-10 | El sistema deberá informar al usuario cuando un gesto no sea reconocido o una acción no pueda ejecutarse. | Alta |
| RF-11 | El sistema deberá rechazar gestos ambiguos o con una confianza inferior al umbral definido. | Alta |
| RF-12 | El sistema deberá procesar localmente los datos necesarios para reconocer e interpretar los gestos. | Alta |
| RF-13 | El sistema deberá permitir configurar los parámetros básicos de reconocimiento que se definan para el MVP. | Media |
| RF-14 | El sistema deberá permitir modificar la asociación entre los gestos disponibles y las acciones compatibles, cuando esté habilitado en la configuración. | Media |
| RF-15 | El sistema deberá comprobar que los comandos recibidos en Windows pertenecen al conjunto de acciones permitidas. | Alta |
| RF-16 | El sistema deberá detectar la pérdida de conexión y notificarla al usuario. | Alta |
| RF-17 | El sistema deberá permitir restablecer la conexión sin requerir obligatoriamente el reinicio completo de las aplicaciones. | Alta |
| RF-18 | El sistema deberá mantener separados los módulos de captura de sensores, reconocimiento, comunicación y ejecución de acciones. | Alta |
| RF-19 | El sistema deberá permitir incorporar nuevos gestos y modos de control sin rediseñar los componentes principales. | Media |
| RF-20 | El sistema deberá permitir integrar en versiones futuras funciones de control para dispositivos móviles. | Futura |

### 3.1. Requerimientos específicos de inteligencia artificial

| Código | Descripción | Prioridad |
|---|---|---|
| RF-IA-01 | El sistema deberá utilizar un mecanismo de reconocimiento basado en características de movimiento para identificar los gestos predefinidos. | Alta |
| RF-IA-02 | El sistema deberá realizar el reconocimiento mediante un modelo local o un mecanismo híbrido de reglas y aprendizaje automático ejecutado en el dispositivo. | Alta |
| RF-IA-03 | El sistema deberá asociar cada gesto reconocido con una acción permitida según el modo activo. | Alta |
| RF-IA-04 | El sistema deberá utilizar un umbral de confianza configurable para evitar acciones derivadas de reconocimientos inciertos. | Alta |
| RF-IA-05 | El sistema deberá permitir sustituir o actualizar el modelo de reconocimiento sin modificar innecesariamente los demás módulos. | Media |
| RF-IA-06 | El sistema deberá proporcionar una respuesta visual cuando se reconozca un gesto o se rechace una interpretación. | Alta |

**Condición de diseño:** las funciones principales de reconocimiento e interpretación no deberán depender de una conexión a Internet ni de un servicio de IA en la nube. La ubicación definitiva del modelo —reloj o computador— se decidirá mediante una evaluación técnica previa.

## 4. Requerimientos no funcionales

Estos requerimientos establecen las condiciones de calidad, seguridad y rendimiento que deberá cumplir SmartControl.

| Código | Categoría | Requerimiento y criterio de aceptación |
|---|---|---|
| RNF-01 | Rendimiento | El tiempo entre el final del gesto y la respuesta observable del sistema deberá ser de 1 segundo o menos en al menos el 95 % de las pruebas realizadas en condiciones normales. |
| RNF-02 | Precisión | El reconocimiento deberá evaluarse con un conjunto de pruebas etiquetadas. El objetivo inicial será alcanzar al menos un 90 % de reconocimientos correctos en los gestos definidos. |
| RNF-03 | Falsos positivos | El sistema deberá medir la frecuencia con la que ejecuta acciones ante movimientos que no corresponden a gestos válidos. Se establecerá un límite de aceptación antes de la validación final del MVP. |
| RNF-04 | Confiabilidad | El sistema no deberá ejecutar acciones cuando no exista conexión válida, el comando esté incompleto o el gesto sea rechazado. |
| RNF-05 | Disponibilidad local | Las funciones principales deberán seguir operando sin conexión a Internet, siempre que el reloj y el computador mantengan la comunicación necesaria. |
| RNF-06 | Seguridad | Windows solo deberá ejecutar comandos incluidos en una lista explícita de acciones permitidas. |
| RNF-07 | Seguridad de comunicación | La comunicación deberá incorporar mecanismos adecuados de autenticación o vinculación de dispositivos y validación de mensajes según el protocolo seleccionado. |
| RNF-08 | Privacidad | Los datos de movimiento deberán procesarse localmente y no se enviarán a servicios externos como parte del funcionamiento normal del MVP. |
| RNF-09 | Usabilidad | La interfaz deberá mostrar de forma clara el modo activo, el estado de conexión y la respuesta al gesto, teniendo en cuenta el tamaño reducido de la pantalla del reloj. |
| RNF-10 | Eficiencia energética | El reconocimiento deberá utilizar únicamente los sensores y la frecuencia de muestreo necesarios, evitando mantener procesos de captura activos cuando no se requieran. |
| RNF-11 | Compatibilidad | La primera versión deberá funcionar en un dispositivo Wear OS compatible y en Windows de 64 bits, según las versiones que se establezcan durante la configuración del entorno. |
| RNF-12 | Mantenibilidad | El código deberá estar organizado en módulos con responsabilidades definidas y dependencias controladas. |
| RNF-13 | Extensibilidad | La incorporación de un nuevo gesto o modo de control deberá realizarse sin reescribir los módulos de comunicación ni los componentes no relacionados. |
| RNF-14 | Testabilidad | Los módulos de reconocimiento, comunicación y ejecución deberán poder probarse de manera independiente. |
| RNF-15 | Recuperación | Después de una interrupción de comunicación, el sistema deberá permitir recuperar la conexión sin reiniciar todo el sistema. |
| RNF-16 | Interoperabilidad | La arquitectura deberá permitir incorporar protocolos o adaptadores de comunicación alternativos si se amplía la compatibilidad con otros dispositivos. |

Los objetivos numéricos de rendimiento y reconocimiento son criterios iniciales propuestos. Deberán validarse mediante pruebas con el hardware seleccionado y ajustarse si la evidencia experimental lo requiere. La precisión del reconocimiento y la frecuencia de falsos positivos deberán medirse por separado.

## 5. Arquitectura inicial

SmartControl se organizará en tres componentes principales:

### 5.1. Aplicación del smartwatch

Responsabilidades:

- Mostrar los modos y controles disponibles.
- Capturar los datos de movimiento.
- Ejecutar o solicitar el reconocimiento de gestos.
- Mostrar los resultados y el estado de conexión.
- Enviar los comandos permitidos al computador.

### 5.2. Aplicación de Windows

Responsabilidades:

- Establecer y mantener la comunicación con el reloj.
- Validar los comandos recibidos.
- Ejecutar las acciones permitidas en el computador.
- Informar errores de ejecución y cambios de conexión.
- Mantener separados los mecanismos de comunicación y las acciones del sistema operativo.

### 5.3. Módulo de inteligencia artificial

Responsabilidades:

- Procesar las características de movimiento necesarias.
- Reconocer los gestos predefinidos.
- Entregar el resultado y su nivel de confianza.
- Relacionar el gesto con el modo activo.
- Rechazar interpretaciones inciertas.

El módulo podrá ejecutarse en el reloj o en Windows. No se creará automáticamente un backend remoto: la arquitectura se decidirá según los resultados de las pruebas de viabilidad del procesamiento local.

## 6. Decisiones técnicas pendientes

Las siguientes decisiones deberán resolverse antes de consolidar la implementación.

| Decisión | Alternativas | Criterio de selección |
|---|---|---|
| Ubicación del modelo de IA | Smartwatch o Windows | Latencia, recursos disponibles, consumo de batería, precisión y facilidad de actualización. |
| Comunicación inalámbrica | Bluetooth o Wi-Fi | Latencia, estabilidad, seguridad, consumo, configuración y compatibilidad. |
| Reconocimiento | Modelo de aprendizaje automático o combinación de reglas y modelo | Precisión, complejidad, recursos y resultados de las pruebas. |
| Protocolo de comandos | Protocolo de mensajes estructurados por definir | Validación, simplicidad, seguridad y posibilidad de ampliación. |
| Tecnología de Windows | Alternativas compatibles con Windows de 64 bits | Integración con el sistema operativo, mantenibilidad y facilidad de empaquetado. |

La selección del protocolo de comunicación deberá estar respaldada por una prueba de concepto antes de fijarse como decisión definitiva.

## 7. Criterios de aceptación del MVP

El MVP se considerará funcional cuando se compruebe lo siguiente:

1. La aplicación se ejecuta en un smartwatch compatible con Wear OS.
2. Los sensores capturan los movimientos necesarios para los gestos definidos.
3. Los gestos predefinidos se reconocen con los objetivos de precisión establecidos.
4. El sistema distingue el modo de presentaciones del modo multimedia.
5. Los comandos llegan correctamente a Windows y producen las acciones esperadas.
6. Los gestos ambiguos no generan acciones no deseadas.
7. La interfaz muestra los cambios de conexión y los errores relevantes.
8. Las funciones principales siguen disponibles sin Internet.
9. Las pruebas de rendimiento, seguridad y recuperación quedan documentadas.
10. Los componentes principales pueden probarse de forma independiente.

## 8. Plan de trabajo inicial

El desarrollo se organizará por etapas verificables:

**Fase 1. Preparación y análisis técnico**
- Configurar Android Studio y el SDK de Wear OS.
- Revisar el entorno de desarrollo disponible.
- Crear la estructura inicial del repositorio.
- Documentar las decisiones de arquitectura.

**Fase 2. Prueba de concepto**
- Capturar datos de sensores.
- Probar el reconocimiento de gestos predefinidos.
- Comparar Bluetooth y Wi-Fi.
- Evaluar las alternativas para ejecutar la IA localmente.

**Fase 3. Desarrollo del MVP**
- Implementar la interfaz del reloj.
- Implementar la aplicación de Windows.
- Integrar la comunicación y la ejecución de acciones.
- Implementar el reconocimiento contextual.

**Fase 4. Validación**
- Ejecutar pruebas funcionales y de integración.
- Medir rendimiento, precisión y falsos positivos.
- Verificar recuperación de conexión y seguridad.
- Corregir los problemas detectados.

**Fase 5. Entrega**
- Documentar la instalación y configuración.
- Preparar una versión ejecutable para las pruebas.
- Registrar los resultados y las limitaciones conocidas.

## 9. Gestión del código y validación

El proyecto se mantendrá en un repositorio Git con una estructura que permita desarrollar los componentes de forma independiente.

Cada etapa implementada deberá validarse antes de crear su commit. Los mensajes de commit deberán describir claramente los cambios realizados. No se realizarán envíos al repositorio remoto sin autorización expresa.

## 10. Consideraciones finales

Los requerimientos de este documento constituyen la base para diseñar e implementar SmartControl. Las decisiones técnicas pendientes deberán resolverse mediante pruebas de concepto, sin asumir compatibilidades que todavía no se hayan comprobado.

La prioridad será desarrollar una primera versión funcional, segura y medible para controlar Windows desde un smartwatch compatible. La integración con celulares, la personalización avanzada de gestos y los modos adicionales se incorporarán después de validar el MVP.

**Fin del documento.**
