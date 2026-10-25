# PoC 2 — Receptor local de mensajes de prueba

> **Prototipo temporal en Python** (solo biblioteca estándar), separado del código de producción. La aplicación definitiva de Windows seguirá siendo C#/.NET, pero no se puede compilar todavía porque falta el .NET SDK (ver `docs/phase2-environment-audit.md`).

## Qué hace

- Escucha **solo en `127.0.0.1`**: el constructor rechaza cualquier otra dirección. Usa un puerto efímero asignado por el sistema.
- Recibe mensajes JSON delimitados por salto de línea, de 4096 bytes como máximo. Si una trama es mayor o no termina en salto de línea, responde con un rechazo y cierra la conexión.
- Valida cada mensaje con `poc/command-validator`.
- **No ejecuta nada real.** Cuando un mensaje es válido, entrega el enum al ejecutor simulado y responde `VALIDATED_NOT_EXECUTED`, un estado propio de la PoC que no pertenece al protocolo.
- El registro guarda solo `messageId`, `status` y `reasonCode`. Nunca guarda el token ni el contenido del mensaje.
- Se detiene con `shutdown()` + `server_close()` + `join()`.

No abre puertos a la red, no modifica el firewall y no se comunica con otros dispositivos.

## Ejecución

```powershell
# Pruebas (8)
python -B -m unittest discover -s poc/local-receiver -v

# Demostración: inicia, envía 6 mensajes de ejemplo, mide 200 idas y vueltas en loopback y se cierra
python -B poc/local-receiver/receiver.py --demo
```

## Advertencia sobre la latencia

La demostración mide el tiempo de ida y vuelta en **loopback (`127.0.0.1`)**. Ese dato solo muestra que la validación y el registro en Python añaden poco tiempo. **No** sirve como medición de Bluetooth, de Wi-Fi ni de la latencia de extremo a extremo de RNF-01.

## Reutilización prevista

El emulador de Android expone la interfaz loopback del equipo anfitrión en `10.0.2.2` (documentación pública de Android, sin verificar aquí porque no hay AVD). Con una imagen de Wear OS instalada, un cliente en el emulador podría hablar con este receptor sin abrir puertos en la LAN.
