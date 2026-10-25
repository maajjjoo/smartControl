# Aplicación Complementaria para Windows (`windows-app`)

Componente receptor para **Windows (64 bits)** previsto en **C# y .NET**, encargado de:
- Mantener el canal de comunicación inalámbrica con el smartwatch ([RF-06](../docs/SRS.md), [RF-17](../docs/SRS.md)).
- Validar la autenticidad, estructura e integridad de los mensajes recibidos ([RNF-04](../docs/SRS.md), [RNF-07](../docs/SRS.md), [communication-protocol.md](../docs/communication-protocol.md)).
- Comprobar que cada comando pertenezca estrictamente a la lista blanca de acciones permitidas ([RF-15](../docs/SRS.md), [RNF-06](../docs/SRS.md), [security-allowlist.md](../docs/security-allowlist.md)).
- Ejecutar las acciones explícitas permitidas de manera desacoplada de la capa de red ([RF-07](../docs/SRS.md), [RF-08](../docs/SRS.md), [RF-18](../docs/SRS.md)):
  - **Modo Presentación (`PRESENTATION`):** Avanzar diapositiva (`SLIDE_NEXT`), retroceder diapositiva (`SLIDE_PREVIOUS`).
  - **Modo Multimedia (`MULTIMEDIA`):** Reproducir multimedia (`MEDIA_PLAY`), pausar multimedia (`MEDIA_PAUSE`).

## Estado actual (etapa 1: aplicación base con acciones simuladas)

Aplicación de escritorio **WinForms sobre .NET 8**. Las acciones **se simulan**: no se envían teclas ni se controla ninguna aplicación.

| Proyecto | Contenido |
|---|---|
| `src/SmartControl.Core` | Lista blanca (`CommandAllowlist`), validador (`CommandValidator`), receptor TCP solo en `127.0.0.1` (`LocalCommandReceiver`), ejecutor simulado y registro de eventos. No depende de la interfaz. |
| `src/SmartControl.Desktop` | Ventana: estado del receptor, comandos recibidos con su resultado y panel de comandos de prueba. |
| `tests/SmartControl.Core.Tests` | Pruebas del validador, del receptor (con clientes reales en loopback) y del registro. Sin paquetes NuGet. |

Comportamiento:
- El receptor escucha en `127.0.0.1:47800` y recibe JSON delimitado por salto de línea (máx. 4096 bytes), con las reglas de [communication-protocol.md §4](../docs/communication-protocol.md).
- El panel de prueba envía mensajes reales por loopback, firmados con un token aleatorio que se genera en cada arranque y solo existe en memoria. **Son comandos explícitos, no gestos reconocidos**, y una prueba en loopback no demuestra la comunicación con el reloj.
- La interfaz solo muestra valores validados o generados por la aplicación, nunca texto recibido.
- El registro está en `%LOCALAPPDATA%\SmartControl\logs\smartcontrol-AAAAMMDD.log` (un archivo por día, se borran los de más de 7 días). Guarda la fecha, el estado, el motivo, la acción validada y el `messageId` validado; nunca el token ni el mensaje.

### Compilar y probar

Con el **.NET 8 SDK** instalado (forma oficial):

```powershell
dotnet run --project windows-app/tests/SmartControl.Core.Tests
dotnet run --project windows-app/src/SmartControl.Desktop
```

Sin el SDK (provisional): `tools/build-nosdk.ps1` compila con el compilador C# de Visual Studio Build Tools y los ensamblados del runtime .NET 8 ya instalados. No descarga nada. La salida queda en `windows-app/bin/nosdk`.

```powershell
.\windows-app\tools\build-nosdk.ps1 -Test   # compila y ejecuta las pruebas
.\windows-app\tools\build-nosdk.ps1 -Run    # compila y abre la aplicación
```

Con esta vía la aplicación arranca mediante `dotnet SmartControl.dll`, así que también aparece una ventana de consola.
