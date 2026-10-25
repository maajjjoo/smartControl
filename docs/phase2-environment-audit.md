# Auditoría del entorno — Fase 2

**Fecha:** 2026-10-24
**Método:** solo comandos de diagnóstico no destructivos. No se instaló, descargó ni configuró nada.

Leyenda de estado:

- ✅ **Confirmado**: comprobado en esta sesión.
- 📄 **Informado, no verificado**: viene de la Fase 1 o de documentación, pero no se volvió a comprobar.
- ❌ **No disponible**: se buscó y no existe.
- 🔒 **Pendiente de autorización**: necesita instalar, descargar o cambiar el sistema.
- ⛔ **No comprobable**: el entorno actual no permite comprobarlo (por ejemplo, falta hardware).

---

## 1. Repositorio

| Elemento | Estado | Evidencia |
|---|---|---|
| Rama actual | ✅ `master` | `git status` / `git branch -a` (es la única rama local) |
| HEAD | ✅ `f543e251bf2688e74197beddacb37f7427009659` | `git rev-parse HEAD`. Coincide con el commit de referencia de la Fase 1 |
| Estado inicial | ✅ Limpio | `nothing to commit, working tree clean`. No hay stashes |
| Archivos versionados | ✅ 12 | `.gitignore`, `README.md`, 5 documentos en `docs/` y 5 README de módulos |
| Código, soluciones o proyectos no documentados | ✅ Ninguno | Listado recursivo, excluyendo `.git` |
| Rama `main` | ❌ No existe localmente | La herramienta indica que `main` es la rama principal, pero `git branch -a` solo muestra `master` y no hay remotos configurados. Solo se registra el dato; no se cambió nada |

**Observación:** el contenido de `poc/README.md` es una guía de *todos* los directorios del repositorio, no una descripción de `poc/`. Es un detalle de la Fase 1; no se modificó.

## 2. Herramientas

| Herramienta | Estado | Versión / ruta real | Comando |
|---|---|---|---|
| Sistema operativo | ✅ | Windows 11 Pro 10.0.26200, 64 bits | Entorno de la sesión |
| Git | ✅ | 2.53.0.windows.2, `C:\Program Files\Git\cmd\git.exe` | `git --version` |
| Java | ✅ | OpenJDK 21.0.10 LTS (Temurin), `C:\Program Files\Eclipse Adoptium\jdk-21.0.10.7-hotspot`. `JAVA_HOME` apunta ahí | `java -version` |
| Python | ✅ | 3.12.10, `C:\Users\majos\AppData\Local\Programs\Python\Python312\python.exe`. También está el lanzador `py.exe` | `python --version` |
| Node.js | ✅ (no se usa) | `...\nvm\.nodejs\node.exe` | `Get-Command node` |
| .NET host | ✅ | `C:\Program Files\dotnet\dotnet.exe` | `Get-Command dotnet` |
| .NET runtimes | ✅ | `Microsoft.NETCore.App 8.0.25` y `Microsoft.WindowsDesktop.App 8.0.25` | `dotnet --list-runtimes` |
| **.NET SDK** | ❌ / 🔒 | `dotnet --list-sdks` no devuelve nada. **No se puede compilar C#** | `dotnet --list-sdks` |
| Android Studio | ✅ | `C:\Program Files\Android\Android Studio`, versión `AI-261.26222.65.2614.16379836`. Incluye su propio JBR y el plugin de Kotlin | `product-info.json` |
| Android SDK | ✅ | `C:\Users\majos\AppData\Local\Android\Sdk`. `ANDROID_HOME` y `ANDROID_SDK_ROOT` **no** están definidas | Listado de carpetas |
| SDK Platforms | ✅ | `android-37.0` | `platforms\` |
| Build-tools | ✅ | `36.0.0` | `build-tools\` |
| platform-tools (adb) | ✅ | adb 1.0.41 / 37.0.1-15733141. No está en `PATH`; se invoca por ruta completa | `adb version` (no inicia el servidor) |
| Android Emulator | ✅ | 37.1.11.0 (build 15917651). No está en `PATH` | `emulator -version` |
| Aceleración del emulador | ✅ | `WHPX(10.0.26200) is installed and usable` | `emulator -accel-check` |
| SDK Command-line tools (`sdkmanager`, `avdmanager`) | ❌ | No existe `cmdline-tools\` | Listado de carpetas |
| **Imágenes de sistema Wear OS** | ❌ / 🔒 | No existe la carpeta `system-images\`, así que tampoco hay imágenes de teléfono | `Test-Path system-images` → `False` |
| **AVD configurado** | ❌ | `emulator -list-avds` no devuelve nada y `%USERPROFILE%\.android\avd` no existe | `emulator -list-avds` |
| Gradle | ✅ (en caché) | Distribución `gradle-9.6.0` descomprimida en `%USERPROFILE%\.gradle\wrapper\dists\`. No hay `gradle` en `PATH` | Listado de carpetas |
| Kotlin (`kotlinc`) | ❌ en `PATH` | Solo existe dentro de Android Studio (plugin) | `Get-Command kotlinc` |
| Android Gradle Plugin / dependencias de Wear OS | ⛔ / 🔒 | No hay ningún proyecto. Un proyecto nuevo descargaría AGP y las bibliotecas AndroidX desde Google Maven | No aplica |

## 3. Hardware de comunicación

| Elemento | Estado | Evidencia |
|---|---|---|
| Adaptador Bluetooth | ✅ | `MediaTek Bluetooth Adapter`, estado `OK`. Servicio `bthserv` en ejecución |
| Versión de Bluetooth y soporte de BLE periférico/GATT server | ⛔ | No se consultaron propiedades avanzadas del controlador. Hay que comprobarlo con una PoC de BLE |
| Adaptador Wi-Fi | ✅ | `MediaTek Wi-Fi 6E MT7902`, `Up`. No se consultó el SSID |
| Adaptador virtual | ✅ | `vEthernet (WSL (Hyper-V firewall))` |
| Smartwatch Wear OS | ⛔ | La usuaria todavía no tiene uno |

## 4. Procesos al terminar

Al finalizar las pruebas se comprobó que no quedaban procesos `python`, `adb`, `emulator` ni `qemu*` en ejecución, ni puertos en escucha de las PoC (`Get-Process`, `Get-NetTCPConnection -State Listen`).

## 5. Diferencias respecto a `environment-status.md` (Fase 1)

| Dato de la Fase 1 | Resultado ahora |
|---|---|
| .NET host instalado, sin SDK | ✅ Sigue igual. Se añade el detalle de los runtimes 8.0.25 |
| No existe `system-images` | ✅ Sigue igual |
| adb y emulator fuera del `PATH` | ✅ Sigue igual |
| Versiones de Git, Java y Python | ✅ Coinciden |
| No figuraban | Se añaden: emulador 37.1.11.0, WHPX usable, Gradle 9.6.0 en caché, ausencia de `cmdline-tools` y de AVD, adaptadores Bluetooth y Wi-Fi |

## 6. Componentes que requieren autorización

| Componente | Para qué | Cómo instalarlo más adelante |
|---|---|---|
| Imagen de sistema Wear OS (x86_64) | PoC 4 y pruebas en emulador | Android Studio → *Device Manager* → *Create Virtual Device* → categoría *Wear OS* → elegir la imagen (se descarga). También desde *SDK Manager* → *SDK Platforms* → *Show Package Details* |
| AVD de Wear OS | PoC 4 | Se crea en el mismo asistente |
| .NET SDK (LTS vigente) | Compilar `windows-app` y portar la PoC 1 a C# | Instalador oficial de Microsoft o `winget`. Elegir la versión LTS vigente cuando se autorice |
| SDK Command-line tools (opcional) | Automatizar `sdkmanager`/`avdmanager` por terminal | *SDK Manager* → *SDK Tools* |
| Dependencias de Gradle (AGP, AndroidX, Wear) | Compilar una PoC Kotlin para Wear OS | Se descargan solas al sincronizar un proyecto en Android Studio |
| Regla de firewall de entrada | Solo para pruebas Wi-Fi reloj físico → PC | Exige autorización explícita. No hace falta para emulador → `10.0.2.2` |
