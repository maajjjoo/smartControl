# Protocolo de Comunicación (`protocol`)

Especificación y esquemas compartidos entre el smartwatch (`watch-app`), el módulo de IA (`ai-module`) y la aplicación de Windows (`windows-app`).

## Documentación de Referencia
- [Protocolo de Comunicación Inalámbrica (Borrador)](../docs/communication-protocol.md)
- [Lista Blanca de Acciones Permitidas (Allowlist)](../docs/security-allowlist.md)

## Objetivos de Diseño
- **Validación estricta ([RF-15](../docs/SRS.md), [RNF-04](../docs/SRS.md)):** Ningún comando incompleto o fuera del catálogo permitido puede ejecutarse.
- **Seguridad ([RNF-06](../docs/SRS.md), [RNF-07](../docs/SRS.md)):** Prohibición de ejecución de comandos arbitrarios del sistema operativo; uso de códigos de acción cerrados y token de sesión/vinculación.
- **Extensibilidad ([RF-19](../docs/SRS.md), [RNF-13](../docs/SRS.md), [RNF-16](../docs/SRS.md)):** Independencia del canal físico de transporte (Wi-Fi LAN o Bluetooth).
