"""Pruebas de la PoC 2 (receptor local en 127.0.0.1).

Ejecución (desde la raíz del repositorio):
    python -B -m unittest discover -s poc/local-receiver -v
"""

import json
import logging
import socket
import threading
import time
import unittest
import uuid

from receiver import LOOPBACK, STATUS_SIMULATED, LocalReceiver
from validator import MAX_MESSAGE_BYTES, CommandValidator

DEVICE = "wearos-watch-01"
# Valor ficticio solo para pruebas; no es una credencial real.
TOKEN = "poc-test-token-not-a-secret"


def command(action="SLIDE_NEXT", mode="PRESENTATION", confidence=0.94, token=TOKEN):
    return json.dumps({
        "protocolVersion": "1.0-draft", "messageId": str(uuid.uuid4()),
        "timestampUtc": int(time.time() * 1000), "deviceId": DEVICE,
        "sessionToken": token, "messageType": "COMMAND_REQUEST",
        "payload": {"activeMode": mode, "gestureId": "SWIPE_RIGHT",
                    "confidence": confidence, "requestedAction": action},
    }).encode("utf-8")


class ReceiverTests(unittest.TestCase):
    def setUp(self):
        self.rx = LocalReceiver(CommandValidator({DEVICE: TOKEN})).start()
        self.addCleanup(self._stop)

    def _stop(self):
        if self.rx.running:
            self.rx.stop()

    def connect(self):
        s = socket.create_connection((LOOPBACK, self.rx.port), timeout=5)
        self.addCleanup(s.close)
        return s, s.makefile("rb")

    def roundtrip(self, s, f, data):
        s.sendall(data + b"\n")
        line = f.readline()
        self.assertTrue(line, "el receptor no respondió")
        return json.loads(line)

    def test_binds_only_to_loopback(self):
        self.assertEqual(self.rx.server_address[0], "127.0.0.1")
        for host in ("0.0.0.0", "", "::", "192.168.1.10", "localhost"):
            with self.subTest(host=host):
                with self.assertRaises(ValueError):
                    LocalReceiver(CommandValidator({}), host=host)

    def test_accepts_valid_and_rejects_invalid_on_same_connection(self):
        s, f = self.connect()
        cases = [
            (command(), STATUS_SIMULATED, "SLIDE_NEXT"),
            (command("MEDIA_PLAY", "MULTIMEDIA"), STATUS_SIMULATED, "MEDIA_PLAY"),
            (command("MEDIA_PAUSE", "MULTIMEDIA"), STATUS_SIMULATED, "MEDIA_PAUSE"),
            (command("MEDIA_PLAY"), "REJECTED_NOT_ALLOWED", None),
            (command("cmd.exe /c calc"), "REJECTED_NOT_ALLOWED", None),
            (command(confidence=0.3), "REJECTED_LOW_CONFIDENCE", None),
            (command(token="wrong"), "REJECTED_INVALID_MESSAGE", None),
            (b"{not json", "REJECTED_INVALID_MESSAGE", None),
            (b"", "REJECTED_INVALID_MESSAGE", None),
        ]
        for data, status, action in cases:
            with self.subTest(status=status, data=data[:60]):
                resp = self.roundtrip(s, f, data)
                self.assertEqual(resp["messageType"], "COMMAND_RESPONSE")
                self.assertEqual(resp["payload"]["status"], status)
                self.assertEqual(resp["payload"]["validatedAction"], action)
                self.assertIsNone(resp["payload"]["executedAction"])
        self.assertEqual([a.value for a in self.rx.executor.history],
                         ["SLIDE_NEXT", "MEDIA_PLAY", "MEDIA_PAUSE"])

    def test_correlation_id_matches_request(self):
        s, f = self.connect()
        data = command()
        resp = self.roundtrip(s, f, data)
        self.assertEqual(resp["correlationId"], json.loads(data)["messageId"])

    def test_replay_over_network_rejected(self):
        s, f = self.connect()
        data = command()
        self.assertEqual(self.roundtrip(s, f, data)["payload"]["status"], STATUS_SIMULATED)
        resp = self.roundtrip(s, f, data)
        self.assertEqual(resp["payload"]["reasonCode"], "REPLAYED_MESSAGE")
        self.assertEqual(len(self.rx.executor.history), 1)

    def test_oversized_frame_rejected_and_connection_closed(self):
        s, f = self.connect()
        s.sendall(b"{" + b" " * (MAX_MESSAGE_BYTES + 10) + b"}\n")
        resp = json.loads(f.readline())
        self.assertEqual(resp["payload"]["reasonCode"], "MESSAGE_TOO_LARGE_OR_UNTERMINATED")
        self.assertEqual(f.readline(), b"")  # el receptor cerró la conexión
        self.assertEqual(self.rx.executor.history, [])

    def test_logs_do_not_contain_token_or_raw_message(self):
        records = []
        handler = logging.Handler()
        handler.emit = lambda r: records.append(r.getMessage())
        logger = logging.getLogger("smartcontrol.poc.receiver")
        logger.addHandler(handler)
        logger.setLevel(logging.INFO)
        self.addCleanup(logger.removeHandler, handler)
        s, f = self.connect()
        self.roundtrip(s, f, command())
        self.roundtrip(s, f, command("cmd.exe /c calc"))
        self.assertEqual(len(records), 2)
        for text in records:
            self.assertNotIn(TOKEN, text)
            self.assertNotIn("cmd.exe", text)

    def test_concurrent_clients(self):
        errors = []

        def client():
            try:
                with socket.create_connection((LOOPBACK, self.rx.port), timeout=5) as s:
                    f = s.makefile("rb")
                    for _ in range(10):
                        s.sendall(command() + b"\n")
                        if json.loads(f.readline())["payload"]["status"] != STATUS_SIMULATED:
                            errors.append("estado inesperado")
            except Exception as exc:  # pragma: no cover - solo diagnóstico
                errors.append(repr(exc))

        threads = [threading.Thread(target=client) for _ in range(5)]
        for t in threads:
            t.start()
        for t in threads:
            t.join(timeout=10)
        self.assertEqual(errors, [])
        self.assertEqual(len(self.rx.executor.history), 50)

    def test_clean_shutdown_releases_port(self):
        port = self.rx.port
        self.rx.stop()
        self.assertFalse(self.rx.running)
        with self.assertRaises(OSError):
            socket.create_connection((LOOPBACK, port), timeout=1).close()
        names = [t.name for t in threading.enumerate()]
        self.assertNotIn("poc-receiver", names)


if __name__ == "__main__":
    unittest.main()
