# Estado del Entorno de Desarrollo — Fase 1

Fecha de verificación: 2026-10-21  
Sistema Operativo: Microsoft Windows NT 10.0.26200.0 (64-bit) — Cumple [RNF-11](SRS.md).

---

## 1. Herramientas Instaladas y Verificadas

| Herramienta | Estado | Versión / Ruta | Uso en SmartControl |
|---|---|---|---|
| **Git** | Disponible | `2.53.0.windows.2` (`C:\Program Files\Git\cmd\git.exe`) | Control de versiones del repositorio modular (Sección 9). |
| **Java (JDK)** | Disponible | OpenJDK `21.0.10 LTS` Temurin (`C:\Program Files\Eclipse Adoptium\jdk-21.0.10.7-hotspot`) | Compilación de Gradle / Kotlin para Wear OS (`watch-app`). |
| **Android Studio** | Instalado | `C:\Program Files\Android\Android Studio` | IDE para desarrollo y depuración de `watch-app`. |
| **Android SDK** | Instalado | `C:\Users\majos\AppData\Local\Android\Sdk` | Contiene `platform-tools` (`adb`), `build-tools` (`36.0.0`), `emulator` y `platforms` (`android-37.0`). |
| **Python** | Disponible | `3.12.10` (`C:\Users\majos\AppData\Local\Programs\Python\Python312\python.exe`) | Herramienta auxiliar opcional para análisis de datos o experimentación local (la aplicación `windows-app` está prevista en **C# / .NET**). |
| **Node.js / npm** | Disponible | Instalado vía NVM (`C:\Users\majos\AppData\Local\Author Software\nvm\.nodejs`) | Disponible como herramienta auxiliar si se requiere. |
| **.NET (Host)** | Host instalado (SDK pendiente) | `C:\Program Files\dotnet\dotnet.exe` (sin SDK instalado aún) | Requerirá instalar el SDK de .NET al iniciar la compilación en C# de `windows-app`. |

---

## 2. Observaciones sobre Wear OS y Android SDK

1. **`adb` y `emulator`:** Se encuentran instalados dentro de `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` y `%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe` (no están agregados globalmente al `PATH` de la terminal actual, pero pueden invocarse por ruta completa o agregarse al `PATH`).
2. **Imagen de sistema de Wear OS:** Actualmente no existe la carpeta `system-images` en el Android SDK local.
   - Si las pruebas se realizarán con un **reloj físico Wear OS**, basta con activar *Opciones de desarrollador* y *Depuración por ADB (Wi-Fi o USB)* en el reloj.
   - Si se utilizará el **emulador de Wear OS**, se deberá descargar una imagen de sistema de Wear OS (por ejemplo, *Wear OS 4 / API 33 o 34*) desde el *SDK Manager / Virtual Device Manager* de Android Studio.
