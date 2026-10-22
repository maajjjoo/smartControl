# Aplicación Complementaria para Windows (`windows-app`)

Componente receptor para **Windows (64 bits)** previsto en **C# y .NET**, encargado de:
- Mantener el canal de comunicación inalámbrica con el smartwatch ([RF-06](../docs/SRS.md), [RF-17](../docs/SRS.md)).
- Validar la autenticidad, estructura e integridad de los mensajes recibidos ([RNF-04](../docs/SRS.md), [RNF-07](../docs/SRS.md), [communication-protocol.md](../docs/communication-protocol.md)).
- Comprobar que cada comando pertenezca estrictamente a la lista blanca de acciones permitidas ([RF-15](../docs/SRS.md), [RNF-06](../docs/SRS.md), [security-allowlist.md](../docs/security-allowlist.md)).
- Ejecutar las acciones explícitas permitidas de manera desacoplada de la capa de red ([RF-07](../docs/SRS.md), [RF-08](../docs/SRS.md), [RF-18](../docs/SRS.md)):
  - **Modo Presentación (`PRESENTATION`):** Avanzar diapositiva (`SLIDE_NEXT`), retroceder diapositiva (`SLIDE_PREVIOUS`).
  - **Modo Multimedia (`MULTIMEDIA`):** Reproducir multimedia (`MEDIA_PLAY`), pausar multimedia (`MEDIA_PAUSE`).
