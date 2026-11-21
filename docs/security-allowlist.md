# Lista Blanca de Acciones Permitidas (Security Allowlist)

**Estado:** Especificación inicial de seguridad para el MVP  
**Requisitos relacionados:** RF-04, RF-05, RF-07, RF-08, RF-15, RNF-04, RNF-06, RNF-07 y Sección 2.2 del [SRS](SRS.md).

---

## 1. Principio de Seguridad

De acuerdo con **RF-15**, **RNF-06** y la **Sección 2.2** del SRS:
- La aplicación complementaria de Windows (**C# / .NET**) **únicamente** podrá ejecutar acciones que estén declaradas de forma explícita en esta lista blanca (*allowlist*).
- **Queda estrictamente prohibida** la recepción o ejecución de órdenes arbitrarias del sistema operativo, cadenas de consola (`cmd.exe`, `powershell.exe`), rutas de archivos ejecutables, scripts o códigos de teclado arbitrarios enviados desde el exterior.
- El smartwatch solicita una **acción semántica cerrada** (mediante un identificador enumerado) y el módulo ejecutor de Windows traduce internamente dicho identificador al evento nativo predefinido.

---

## 2. Catálogo Inicial de Acciones Permitidas (MVP)

En la versión inicial del MVP solo se autorizan las siguientes **cuatro (4) acciones explícitas**, agrupadas según el modo de control activo:

| Código de Acción (`requestedAction`) | Modo Requerido (`activeMode`) | Descripción Funcional | Requisito SRS | Acción Interna en Windows (C# / .NET) |
|---|---|---|---|---|
| `SLIDE_NEXT` | `PRESENTATION` | Avanzar a la siguiente diapositiva | RF-07 | Envío de evento de tecla virtual predefinido para avanzar diapositiva (p. ej. `VK_RIGHT` / `VK_NEXT`) |
| `SLIDE_PREVIOUS` | `PRESENTATION` | Retroceder a la diapositiva anterior | RF-07 | Envío de evento de tecla virtual predefinido para retroceder diapositiva (p. ej. `VK_LEFT` / `VK_PRIOR`) |
| `MEDIA_PLAY` | `MULTIMEDIA` | Iniciar o reanudar la reproducción multimedia | RF-08 | Ejecución de acción explícita de reproducción (distinta de pausa). Alternativa preferida sujeta a validación de viabilidad: operación de reproducción sobre `GlobalSystemMediaTransportControlsSession` |
| `MEDIA_PAUSE` | `MULTIMEDIA` | Pausar la reproducción multimedia activa | RF-08 | Ejecución de acción explícita de pausa (distinta de reproducción). Alternativa preferida sujeta a validación de viabilidad: operación de pausa sobre `GlobalSystemMediaTransportControlsSession` |

> **Nota sobre `MEDIA_PLAY` y `MEDIA_PAUSE`:** Ambas acciones deben ejecutar comportamientos explícitos y diferenciados (no un simple alternador o *toggle* ciego como `VK_MEDIA_PLAY_PAUSE`, el cual no distingue el estado actual). Se contempla como alternativa preferida el uso de las operaciones específicas de reproducción y pausa expuestas por `GlobalSystemMediaTransportControlsSession` en Windows, sujeto a comprobar su viabilidad técnica durante las pruebas correspondientes.

> **Implementación (2026-11-20):** `windows-app/src/SmartControl.Desktop/Execution/`. Las diapositivas usan `SendInput` con `VK_RIGHT` / `VK_LEFT` fijos en el código. La música usa `TryPlayAsync` / `TryPauseAsync` de `GlobalSystemMediaTransportControlsSession`, llamadas por COM sin paquetes externos. Comprobado en este equipo: la sesión multimedia se lee (era Spotify). Pendiente de comprobar con un reproductor y una presentación reales.

---

## 3. Reglas de Validación de la Lista Blanca

El validador de seguridad en Windows (`ActionAllowlistValidator`) aplicará las siguientes reglas de forma obligatoria antes de invocar al ejecutor del sistema operativo:

1. **Coincidencia exacta de identificador:**
   - El campo `requestedAction` debe coincidir exactamente (sensible a mayúsculas/minúsculas) con uno de los cuatro valores enumerados: `SLIDE_NEXT`, `SLIDE_PREVIOUS`, `MEDIA_PLAY` o `MEDIA_PAUSE`.
   - Cualquier otro valor (vacío, nulo, desconocido o con caracteres adicionales) será rechazado inmediatamente con `REJECTED_NOT_ALLOWED`.

2. **Coherencia estricta con el Modo Activo (RF-04, RF-05):**
   - Si `activeMode` es `PRESENTATION`, solo se permiten `SLIDE_NEXT` y `SLIDE_PREVIOUS`.
   - Si `activeMode` es `MULTIMEDIA`, solo se permiten `MEDIA_PLAY` y `MEDIA_PAUSE`.
   - Si un mensaje solicita `SLIDE_NEXT` mientras el modo activo en el mensaje o en el receptor es `MULTIMEDIA` (o viceversa), la acción será **rechazada** sin ejecutarse.

3. **Aislamiento del Ejecutor (RF-18, RNF-06):**
   - El módulo ejecutor de Windows no recibirá nunca cadenas de texto libres provenientes de la red; recibirá únicamente un tipo enumerado interno de C# (`AllowedAction`) una vez superada la validación:
     ```csharp
     public enum ControlMode
     {
         Presentation,
         Multimedia
     }

     public enum AllowedAction
     {
         SlideNext,
         SlidePrevious,
         MediaPlay,
         MediaPause
     }
     ```
   - El mapeo hacia la API nativa de Windows estará codificado de forma estática dentro del módulo de ejecución y no podrá parametrizarse desde los mensajes entrantes.

---

## 4. Restricciones Explícitas (Lo que NUNCA debe permitirse)

Bajo ninguna circunstancia el protocolo o la aplicación de Windows permitirán:
- Invocar `System.Diagnostics.Process.Start` con argumentos provenientes de un mensaje recibido.
- Ejecutar comandos de shell (`cmd`, `powershell`, `wscript`, `bash`).
- Enviar combinaciones de teclas arbitrarias (como `Alt+F4`, `Ctrl+Alt+Del`, `Win+R`) o texto libre mediante simulación de teclado.
- Modificar archivos del sistema, el registro de Windows o configuraciones de red a partir de comandos del reloj.

---

## 5. Procedimiento para Ampliar la Lista Blanca en el Futuro (RF-19, RNF-13)

Cualquier acción adicional en versiones posteriores al MVP deberá:
1. Documentarse formalmente en este archivo (`docs/security-allowlist.md`) con su modo asociado y su requisito correspondiente.
2. Incorporarse como un valor explícito en la enumeración cerrada `AllowedAction` del receptor en C#.
3. Contar con su prueba unitaria de aceptación y de rechazo fuera de modo antes de habilitarse.
