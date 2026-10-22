# SmartControl

SmartControl es un sistema de control inalámbrico que permite controlar funciones específicas de un computador con **Windows (64 bits)** mediante gestos realizados con la mano y la muñeca mientras se utiliza un smartwatch con **Wear OS**.

El sistema emplea los sensores de movimiento del reloj para capturar gestos predefinidos y un mecanismo de **inteligencia artificial ejecutado localmente** (sin depender de Internet ni de servicios en la nube) para interpretar los movimientos y ejecutar las acciones permitidas según el modo de control activo.

---

## 1. Alcance del MVP

### Funcionalidades incluidas
- Aplicación para smartwatch compatible con **Wear OS**.
- Captura de datos de movimiento mediante los sensores disponibles en el reloj.
- Reconocimiento de un conjunto acotado de gestos predefinidos con umbral de confianza configurable.
- Interpretación contextual de gestos mediante procesamiento local según el modo activo:
  - **Modo Presentación:** avanzar diapositiva y retroceder diapositiva.
  - **Modo Multimedia:** reproducir contenido multimedia y pausar contenido multimedia.
- Comunicación inalámbrica local entre el smartwatch y Windows (canal definitivo sujeto a prueba de concepto entre Bluetooth y Wi-Fi).
- Visualización en el reloj del modo activo, del estado de conexión y de las acciones reconocidas o rechazadas.
- Validación estricta mediante lista blanca de comandos permitidos en Windows.
- Detección de pérdida de conexión y recuperación sin requerir el reinicio completo del sistema.
- Pruebas funcionales, de seguridad, recuperación y rendimiento.

### Funcionalidades fuera del alcance inicial
- Control de cámara del celular y compatibilidad completa con Android/iPhone.
- Entrenamiento automático de gestos personalizados por usuario.
- Control de dispositivos adicionales.
- Sincronización mediante cuentas o almacenamiento en la nube.
- Ejecución de comandos arbitrarios en el sistema operativo Windows.

---

## 2. Tecnologías Previstas

| Componente | Tecnología prevista | Observaciones |
|---|---|---|
| **Aplicación del Smartwatch** | **Kotlin** y **Wear OS** (Android Studio / Wear OS SDK) | Captura de sensores, interfaz de control para pantalla reducida y envío de mensajes. |
| **Aplicación de Windows** | **C#** y **.NET** (Windows 64 bits) | Recepción inalámbrica, validación contra lista blanca y ejecución segura de acciones permitidas. |
| **Módulo de Inteligencia Artificial** | **IA Local** (modelo local o enfoque híbrido de reglas y aprendizaje automático) | Procesamiento 100 % local. La ubicación definitiva del modelo (reloj o computador) está pendiente de evaluación técnica en la Fase 2. |
| **Control de versiones** | **Git** | Estructura modular con validación previa a cada commit. |

---

## 3. Estructura del Repositorio

El repositorio separa los módulos de captura, reconocimiento, protocolo y ejecución ([RF-18](docs/SRS.md), [RNF-12](docs/SRS.md)):

```text
smartControl/
├── docs/                           # Documentación técnica y de requisitos
│   ├── SRS.md                      # Documento de Requisitos de Software (v1.0)
│   ├── communication-protocol.md   # Análisis Bluetooth vs. Wi-Fi, formato previsto y validaciones
│   ├── security-allowlist.md       # Lista blanca explícita de acciones permitidas en Windows
│   ├── architecture-decisions.md   # Registro de decisiones de arquitectura (ADR)
│   └── environment-status.md       # Diagnóstico del entorno de desarrollo local
├── watch-app/                      # Aplicación para smartwatch (Kotlin / Wear OS)
├── windows-app/                    # Aplicación complementaria para Windows (C# / .NET)
├── ai-module/                      # Módulo de reconocimiento local de gestos
├── protocol/                       # Esquemas y definiciones compartidas de mensajes
└── poc/                            # Espacio para pruebas de concepto (Fase 2)
```

---

## 4. Fases del Proyecto

1. **Fase 1 — Preparación y análisis técnico:**
   - Revisión del entorno de desarrollo (Android Studio, Wear OS SDK, .NET, Git).
   - Creación de la estructura modular del repositorio.
   - Documentación de requisitos ([SRS](docs/SRS.md)), protocolo previsto ([communication-protocol.md](docs/communication-protocol.md)), lista de acciones permitidas ([security-allowlist.md](docs/security-allowlist.md)) y decisiones de arquitectura ([architecture-decisions.md](docs/architecture-decisions.md)).
2. **Fase 2 — Prueba de concepto (PoC):**
   - Captura experimental de datos de sensores en Wear OS.
   - Pruebas iniciales de reconocimiento de gestos predefinidos.
   - Comparativa experimental entre **Bluetooth** y **Wi-Fi**.
   - Evaluación de viabilidad y latencia para definir la ubicación del modelo de IA local (Smartwatch vs. Windows).
3. **Fase 3 — Desarrollo del MVP:**
   - Implementación de la interfaz y lógica en Wear OS (Kotlin).
   - Implementación de la aplicación receptora en Windows (C# / .NET).
   - Integración del canal de comunicación validado y del ejecutor seguro de acciones.
   - Integración del reconocimiento contextual con umbral de confianza.
4. **Fase 4 — Validación:**
   - Pruebas funcionales y de integración.
   - Medición de latencia ($\le 1\text{ s}$ en $\ge 95\,\%$ de pruebas), precisión ($\ge 90\,\%$) y falsos positivos por separado.
   - Verificación de seguridad (lista blanca) y recuperación ante desconexiones.
5. **Fase 5 — Entrega:**
   - Documentación de instalación y configuración.
   - Empaquetado de versión ejecutable para pruebas.
   - Informe de resultados y limitaciones conocidas.

