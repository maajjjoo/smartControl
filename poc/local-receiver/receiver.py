"""PoC 2 de SmartControl: receptor local de mensajes de prueba.

Prototipo TEMPORAL en Python (solo biblioteca estándar). La aplicación definitiva
de Windows seguirá siendo C#/.NET. Este receptor:

- Escucha EXCLUSIVAMENTE en 127.0.0.1 (no acepta otra dirección) y en un puerto
  efímero asignado por el sistema, salvo que se indique otro.
- Recibe mensajes JSON delimitados por salto de línea (máx. 4096 bytes).
- Valida cada mensaje con poc/command-validator.
- NO ejecuta acciones reales: entrega el enum validado al ejecutor simulado.
- Registra únicamente messageId, estado y motivo (nunca el token ni el mensaje).

Uso de demostración (inicia, envía mensajes de ejemplo y se cierra):
    python -B poc/local-receiver/receiver.py --demo
"""

from __future__ import annotations

import json
import logging
import socket
import socketserver
import statistics
import sys
import threading
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "command-validator"))

from simulated_executor import SimulatedExecutor  # noqa: E402
from validator import (  # noqa: E402
    MAX_MESSAGE_BYTES, STATUS_INVALID, CommandValidator, ValidationResult,
)

LOOPBACK = "127.0.0.1"
CONNECTION_TIMEOUT_S = 5.0
# Estado propio de la PoC: el mensaje fue validado y entregado al ejecutor
# simulado, pero no se realizó ninguna acción real. No forma parte del protocolo.
STATUS_SIMULATED = "VALIDATED_NOT_EXECUTED"

log = logging.getLogger("smartcontrol.poc.receiver")


def build_response(result, effect: str | None) -> dict:
    return {
        "protocolVersion": "1.0-draft",
        "messageId": str(uuid.uuid4()),
        "correlationId": result.message_id,
        "timestampUtc": int(time.time() * 1000),
        "messageType": "COMMAND_RESPONSE",
        "payload": {
            "status": STATUS_SIMULATED if result.accepted else result.status,
            "executedAction": None,
            "validatedAction": result.action.value if result.action else None,
            "reasonCode": result.reason_code,
            "message": effect or "Comando rechazado; no se ejecutó ninguna acción",
        },
    }


class _Handler(socketserver.StreamRequestHandler):
    timeout = CONNECTION_TIMEOUT_S

    def handle(self):
        server: LocalReceiver = self.server  # type: ignore[assignment]
        while True:
            try:
                line = self.rfile.readline(MAX_MESSAGE_BYTES + 2)
            except (socket.timeout, ConnectionError, OSError):
                return
            if not line:
                return
            if not line.endswith(b"\n"):
                # Trama demasiado grande o sin delimitador: no se puede resincronizar.
                result = ValidationResult(STATUS_INVALID, "MESSAGE_TOO_LARGE_OR_UNTERMINATED")
                server.record(result)
                self._send(build_response(result, None))
                return
            body = line.rstrip(b"\r\n")
            result = server.validator.validate(body)
            effect = None
            if result.accepted:
                effect = server.executor.execute(result.action)
            server.record(result)
            self._send(build_response(result, effect))

    def _send(self, response: dict) -> None:
        try:
            self.wfile.write(json.dumps(response).encode("utf-8") + b"\n")
            self.wfile.flush()
        except (ConnectionError, OSError):
            pass


class LocalReceiver(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = False

    def __init__(self, validator: CommandValidator, host: str = LOOPBACK, port: int = 0):
        if host != LOOPBACK:
            raise ValueError("la PoC solo puede escuchar en 127.0.0.1")
        self.validator = validator
        self.executor = SimulatedExecutor()
        self.events: list[tuple[str | None, str, str]] = []
        self._events_lock = threading.Lock()
        self._thread: threading.Thread | None = None
        super().__init__((host, port), _Handler)

    @property
    def port(self) -> int:
        return self.server_address[1]

    def record(self, result) -> None:
        with self._events_lock:
            self.events.append((result.message_id, result.status, result.reason_code))
        log.info("messageId=%s status=%s reason=%s",
                 result.message_id, result.status, result.reason_code)

    def start(self) -> "LocalReceiver":
        self._thread = threading.Thread(target=self.serve_forever, name="poc-receiver",
                                        kwargs={"poll_interval": 0.05}, daemon=True)
        self._thread.start()
        return self

    def stop(self) -> None:
        self.shutdown()
        self.server_close()
        if self._thread is not None:
            self._thread.join(timeout=5)

    @property
    def running(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def __enter__(self):
        return self.start()

    def __exit__(self, *exc):
        self.stop()


def _demo() -> int:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    device, token = "wearos-watch-01", "poc-demo-token-not-a-secret"

    def msg(action, mode, confidence=0.94):
        return {
            "protocolVersion": "1.0-draft", "messageId": str(uuid.uuid4()),
            "timestampUtc": int(time.time() * 1000), "deviceId": device,
            "sessionToken": token, "messageType": "COMMAND_REQUEST",
            "payload": {"activeMode": mode, "gestureId": "DEMO_GESTURE",
                        "confidence": confidence, "requestedAction": action},
        }

    samples = [
        json.dumps(msg("SLIDE_NEXT", "PRESENTATION")).encode(),
        json.dumps(msg("MEDIA_PAUSE", "MULTIMEDIA")).encode(),
        json.dumps(msg("MEDIA_PLAY", "PRESENTATION")).encode(),
        json.dumps(msg("SLIDE_NEXT", "PRESENTATION", 0.4)).encode(),
        json.dumps(msg("cmd.exe /c calc", "PRESENTATION")).encode(),
        b"{not json",
    ]
    with LocalReceiver(CommandValidator({device: token})) as rx:
        print(f"Receptor escuchando en {LOOPBACK}:{rx.port}")
        with socket.create_connection((LOOPBACK, rx.port), timeout=5) as s:
            f = s.makefile("rb")
            for data in samples:
                s.sendall(data + b"\n")
                p = json.loads(f.readline())["payload"]
                print(f"  -> status={p['status']} reason={p['reasonCode']} "
                      f"validatedAction={p['validatedAction']}")
            # Ida y vuelta en loopback. NO representa Bluetooth ni Wi-Fi.
            log.setLevel(logging.WARNING)
            rtts = []
            for _ in range(200):
                data = json.dumps(msg("SLIDE_NEXT", "PRESENTATION")).encode() + b"\n"
                t0 = time.perf_counter()
                s.sendall(data)
                f.readline()
                rtts.append((time.perf_counter() - t0) * 1000)
        rtts.sort()
        print(f"Loopback 127.0.0.1, n=200: mediana={statistics.median(rtts):.3f} ms, "
              f"p95={rtts[int(0.95 * len(rtts)) - 1]:.3f} ms, max={rtts[-1]:.3f} ms")
        print(f"Acciones simuladas registradas: {len(rx.executor.history)}")
    print(f"Receptor detenido: {not rx.running}")
    return 0


if __name__ == "__main__":
    if "--demo" in sys.argv:
        sys.exit(_demo())
    print(__doc__)
