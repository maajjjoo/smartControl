"""Pruebas de la PoC 1 (validador de comandos y lista blanca).

Ejecución (desde la raíz del repositorio):
    python -B -m unittest discover -s poc/command-validator -v
"""

import copy
import json
import os
import pathlib
import subprocess
import unittest
from unittest import mock

from simulated_executor import SimulatedExecutor
from validator import (
    AllowedAction, CommandValidator, ControlMode, MAX_MESSAGE_BYTES,
    STATUS_ACCEPTED, STATUS_INVALID, STATUS_LOW_CONFIDENCE, STATUS_NOT_ALLOWED,
)

NOW_MS = 1792633800125
DEVICE = "wearos-watch-01"
# Valor ficticio solo para pruebas; no es una credencial real.
TOKEN = "poc-test-token-not-a-secret"

BASE = {
    "protocolVersion": "1.0-draft",
    "messageId": "550e8400-e29b-41d4-a716-446655440000",
    "timestampUtc": NOW_MS,
    "deviceId": DEVICE,
    "sessionToken": TOKEN,
    "messageType": "COMMAND_REQUEST",
    "payload": {
        "activeMode": "PRESENTATION",
        "gestureId": "SWIPE_RIGHT",
        "confidence": 0.94,
        "requestedAction": "SLIDE_NEXT",
    },
}

_counter = 0


def make(payload=None, **header):
    """Crea un mensaje válido con un messageId único, aplicando cambios."""
    global _counter
    _counter += 1
    msg = copy.deepcopy(BASE)
    msg["messageId"] = f"00000000-0000-4000-8000-{_counter:012x}"
    msg.update(header)
    if payload:
        msg["payload"].update(payload)
    return msg


def raw(msg):
    return json.dumps(msg).encode("utf-8")


class ValidatorTestBase(unittest.TestCase):
    def setUp(self):
        self.clock = mock.Mock(return_value=NOW_MS)
        self.v = CommandValidator({DEVICE: TOKEN}, clock_ms=self.clock)

    def assertResult(self, data, status, reason=None):
        result = self.v.validate(data if isinstance(data, (bytes, str)) else raw(data))
        self.assertEqual(result.status, status, result)
        if reason:
            self.assertEqual(result.reason_code, reason, result)
        if status != STATUS_ACCEPTED:
            self.assertIsNone(result.action)
            self.assertFalse(result.accepted)
        return result


class PositiveCases(ValidatorTestBase):
    def test_four_allowed_actions_in_their_mode(self):
        cases = [
            ("PRESENTATION", "SLIDE_NEXT", AllowedAction.SLIDE_NEXT),
            ("PRESENTATION", "SLIDE_PREVIOUS", AllowedAction.SLIDE_PREVIOUS),
            ("MULTIMEDIA", "MEDIA_PLAY", AllowedAction.MEDIA_PLAY),
            ("MULTIMEDIA", "MEDIA_PAUSE", AllowedAction.MEDIA_PAUSE),
        ]
        for mode, action, expected in cases:
            with self.subTest(action=action):
                r = self.assertResult(make({"activeMode": mode, "requestedAction": action}),
                                      STATUS_ACCEPTED, "OK")
                self.assertIs(r.action, expected)
                self.assertTrue(r.accepted)

    def test_protocol_example_is_accepted(self):
        self.assertResult(BASE, STATUS_ACCEPTED)

    def test_confidence_bounds_and_threshold_inclusive(self):
        self.assertResult(make({"confidence": 0.80}), STATUS_ACCEPTED)
        self.assertResult(make({"confidence": 1}), STATUS_ACCEPTED)
        self.assertResult(make({"confidence": 1.0}), STATUS_ACCEPTED)

    def test_accepts_str_input(self):
        self.assertResult(json.dumps(make()), STATUS_ACCEPTED)

    def test_timestamp_within_window(self):
        self.assertResult(make(timestampUtc=NOW_MS - 5000), STATUS_ACCEPTED)
        self.assertResult(make(timestampUtc=NOW_MS + 2000), STATUS_ACCEPTED)


class PlayAndPauseAreDistinct(ValidatorTestBase):
    def test_play_and_pause_map_to_different_actions(self):
        play = self.assertResult(make({"activeMode": "MULTIMEDIA", "requestedAction": "MEDIA_PLAY"}),
                                 STATUS_ACCEPTED)
        pause = self.assertResult(make({"activeMode": "MULTIMEDIA", "requestedAction": "MEDIA_PAUSE"}),
                                  STATUS_ACCEPTED)
        self.assertIsNot(play.action, pause.action)
        ex = SimulatedExecutor()
        self.assertNotEqual(ex.execute(play.action), ex.execute(pause.action))

    def test_toggle_is_not_allowed(self):
        for action in ("MEDIA_PLAY_PAUSE", "VK_MEDIA_PLAY_PAUSE", "MEDIA_TOGGLE"):
            with self.subTest(action=action):
                self.assertResult(make({"activeMode": "MULTIMEDIA", "requestedAction": action}),
                                  STATUS_NOT_ALLOWED, "ACTION_NOT_IN_ALLOWLIST")


class AllowlistAndMode(ValidatorTestBase):
    def test_unknown_or_altered_actions_rejected(self):
        for action in ("", "slide_next", "Slide_Next", " SLIDE_NEXT", "SLIDE_NEXT ",
                       "SLIDE_NEXT\n", "SLIDE_NEXT\u0000", "SHUTDOWN", "ALT_F4",
                       "SlideNext", "SLIDE_NEXT;MEDIA_PLAY", "\uff33LIDE_NEXT"):
            with self.subTest(action=repr(action)):
                self.assertResult(make({"requestedAction": action}),
                                  STATUS_NOT_ALLOWED, "ACTION_NOT_IN_ALLOWLIST")

    def test_mode_action_mismatch_rejected(self):
        cases = [("MULTIMEDIA", "SLIDE_NEXT"), ("MULTIMEDIA", "SLIDE_PREVIOUS"),
                 ("PRESENTATION", "MEDIA_PLAY"), ("PRESENTATION", "MEDIA_PAUSE")]
        for mode, action in cases:
            with self.subTest(mode=mode, action=action):
                self.assertResult(make({"activeMode": mode, "requestedAction": action}),
                                  STATUS_NOT_ALLOWED, "MODE_ACTION_MISMATCH")

    def test_unknown_mode_rejected(self):
        for mode in ("", "presentation", "GAMING", "PRESENTATION "):
            with self.subTest(mode=mode):
                self.assertResult(make({"activeMode": mode}), STATUS_INVALID, "UNKNOWN_MODE")

    def test_receiver_mode_mismatch_rejected(self):
        self.v.receiver_mode = ControlMode.MULTIMEDIA
        self.assertResult(make(), STATUS_NOT_ALLOWED, "RECEIVER_MODE_MISMATCH")
        self.v.receiver_mode = ControlMode.PRESENTATION
        self.assertResult(make(), STATUS_ACCEPTED)


class StructureAndTypes(ValidatorTestBase):
    def test_missing_header_fields(self):
        for field in ("protocolVersion", "messageId", "timestampUtc", "deviceId",
                      "sessionToken", "messageType", "payload"):
            with self.subTest(field=field):
                msg = make()
                del msg[field]
                self.assertResult(msg, STATUS_INVALID, "MISSING_HEADER_FIELD")

    def test_missing_payload_fields(self):
        for field in ("activeMode", "gestureId", "confidence", "requestedAction"):
            with self.subTest(field=field):
                msg = make()
                del msg["payload"][field]
                self.assertResult(msg, STATUS_INVALID, "MISSING_PAYLOAD_FIELD")

    def test_unknown_fields_rejected(self):
        msg = make()
        msg["command"] = "cmd.exe /c calc"
        self.assertResult(msg, STATUS_INVALID, "UNKNOWN_HEADER_FIELD")
        msg = make()
        msg["payload"]["shell"] = "powershell -c Stop-Computer"
        self.assertResult(msg, STATUS_INVALID, "UNKNOWN_PAYLOAD_FIELD")

    def test_wrong_types(self):
        cases = [
            (make(messageId=123), "INVALID_MESSAGE_ID"),
            (make(messageId="not-a-uuid"), "INVALID_MESSAGE_ID"),
            (make(messageId="550E8400-E29B-41D4-A716-446655440000"), "INVALID_MESSAGE_ID"),
            (make(timestampUtc="1792633800125"), "INVALID_TIMESTAMP"),
            (make(timestampUtc=1792633800125.5), "INVALID_TIMESTAMP"),
            (make(timestampUtc=True), "INVALID_TIMESTAMP"),
            (make(timestampUtc=-1), "INVALID_TIMESTAMP"),
            (make(deviceId=None), "INVALID_DEVICE_ID"),
            (make(deviceId="watch 01; rm -rf"), "INVALID_DEVICE_ID"),
            (make(sessionToken=""), "INVALID_SESSION_TOKEN"),
            (make(sessionToken=["x"]), "INVALID_SESSION_TOKEN"),
            (make(payload=None, messageType="PING"), "UNSUPPORTED_MESSAGE_TYPE"),
            (make(messageType="command_request"), "UNSUPPORTED_MESSAGE_TYPE"),
            (make(payload=None) | {"payload": "SLIDE_NEXT"}, "INVALID_PAYLOAD"),
            (make(payload=None) | {"payload": []}, "INVALID_PAYLOAD"),
            (make({"requestedAction": ["SLIDE_NEXT"]}), "INVALID_FIELD_TYPE"),
            (make({"activeMode": 1}), "INVALID_FIELD_TYPE"),
            (make({"gestureId": None}), "INVALID_FIELD_TYPE"),
            (make({"gestureId": "swipe right"}), "INVALID_GESTURE_ID"),
            (make({"confidence": "0.94"}), "INVALID_CONFIDENCE"),
            (make({"confidence": True}), "INVALID_CONFIDENCE"),
            (make({"confidence": None}), "INVALID_CONFIDENCE"),
            (make({"confidence": -0.01}), "INVALID_CONFIDENCE"),
            (make({"confidence": 1.01}), "INVALID_CONFIDENCE"),
        ]
        for msg, reason in cases:
            with self.subTest(reason=reason, msg=msg):
                self.assertResult(msg, STATUS_INVALID, reason)

    def test_unsupported_version(self):
        for version in ("1.0", "2.0-draft", 1.0, None):
            with self.subTest(version=version):
                self.assertResult(make(protocolVersion=version), STATUS_INVALID, "UNSUPPORTED_VERSION")


class MalformedInput(ValidatorTestBase):
    def test_malformed_json(self):
        good = raw(make())
        cases = [b"{", b"null", b"[]", b'"SLIDE_NEXT"', b"SLIDE_NEXT", good[:-1],
                 b"\xff\xfe\x00", good + b"garbage", b"{\"a\":1}{\"b\":2}"]
        for data in cases:
            with self.subTest(data=data[:40]):
                r = self.v.validate(data)
                self.assertEqual(r.status, STATUS_INVALID)
                self.assertIsNone(r.action)

    def test_empty_and_non_bytes(self):
        self.assertResult(b"", STATUS_INVALID, "EMPTY_MESSAGE")
        for value in (None, 42, {"a": 1}, ["x"]):
            with self.subTest(value=value):
                r = self.v.validate(value)
                self.assertEqual(r.status, STATUS_INVALID)
                self.assertEqual(r.reason_code, "NOT_BYTES")

    def test_nan_and_infinity_rejected(self):
        for const in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(const=const):
                text = raw(make()).decode().replace('"confidence": 0.94', f'"confidence": {const}')
                self.assertIn(const, text)
                self.assertResult(text, STATUS_INVALID, "MALFORMED_JSON")

    def test_duplicate_keys_rejected(self):
        # Sin esta defensa, la segunda clave sobrescribiría a la primera en silencio.
        text = raw(make()).decode().replace(
            '"requestedAction": "SLIDE_NEXT"',
            '"requestedAction": "SLIDE_NEXT", "requestedAction": "MEDIA_PLAY"')
        self.assertResult(text, STATUS_INVALID, "MALFORMED_JSON")

    def test_size_limit(self):
        msg = make({"gestureId": "G" * 60})
        msg["payload"]["gestureId"] = "SWIPE_RIGHT"
        padded = raw(msg).decode()
        padded = padded[:-1] + " " * (MAX_MESSAGE_BYTES - len(padded)) + "}"
        self.assertEqual(len(padded.encode()), MAX_MESSAGE_BYTES)
        self.assertResult(padded, STATUS_ACCEPTED)
        too_big = padded[:-1] + " }"
        self.assertResult(too_big, STATUS_INVALID, "MESSAGE_TOO_LARGE")

    def test_deep_nesting_does_not_crash(self):
        data = b"[" * 100000 + b"]" * 100000
        self.assertResult(data, STATUS_INVALID)


class SessionFreshnessReplay(ValidatorTestBase):
    def test_unpaired_device_or_wrong_token(self):
        self.assertResult(make(deviceId="other-watch"), STATUS_INVALID, "UNAUTHORIZED_DEVICE")
        self.assertResult(make(sessionToken="wrong"), STATUS_INVALID, "UNAUTHORIZED_DEVICE")
        self.assertResult(make(sessionToken=TOKEN + "x"), STATUS_INVALID, "UNAUTHORIZED_DEVICE")

    def test_stale_and_future_messages(self):
        self.assertResult(make(timestampUtc=NOW_MS - 5001), STATUS_INVALID, "STALE_MESSAGE")
        self.assertResult(make(timestampUtc=0), STATUS_INVALID, "STALE_MESSAGE")
        self.assertResult(make(timestampUtc=NOW_MS + 2001), STATUS_INVALID, "FUTURE_TIMESTAMP")

    def test_replay_rejected(self):
        msg = make()
        self.assertResult(msg, STATUS_ACCEPTED)
        self.assertResult(msg, STATUS_INVALID, "REPLAYED_MESSAGE")

    def test_replay_of_rejected_low_confidence_message_still_blocked(self):
        msg = make({"confidence": 0.5})
        self.assertResult(msg, STATUS_LOW_CONFIDENCE)
        msg["payload"]["confidence"] = 0.99
        self.assertResult(msg, STATUS_INVALID, "REPLAYED_MESSAGE")

    def test_replay_cache_fails_closed_when_full(self):
        v = CommandValidator({DEVICE: TOKEN}, clock_ms=self.clock, replay_cache_size=2)
        self.assertTrue(v.validate(raw(make())).accepted)
        self.assertTrue(v.validate(raw(make())).accepted)
        r = v.validate(raw(make()))
        self.assertEqual(r.reason_code, "REPLAY_CACHE_FULL")
        # Cuando las entradas caducan, la caché vuelve a admitir mensajes.
        self.clock.return_value = NOW_MS + 7001
        self.assertTrue(v.validate(raw(make(timestampUtc=NOW_MS + 7001))).accepted)

    def test_low_confidence(self):
        self.assertResult(make({"confidence": 0.79}), STATUS_LOW_CONFIDENCE, "BELOW_THRESHOLD")
        self.assertResult(make({"confidence": 0}), STATUS_LOW_CONFIDENCE, "BELOW_THRESHOLD")

    def test_invalid_threshold_config(self):
        with self.assertRaises(ValueError):
            CommandValidator({}, confidence_threshold=1.5)


class NoArbitraryExecution(ValidatorTestBase):
    PAYLOADS = [
        "cmd.exe /c calc", "powershell -Command Stop-Computer", "calc.exe",
        "C:\\Windows\\System32\\cmd.exe", "$(whoami)", "`whoami`", "SLIDE_NEXT && calc",
        "SLIDE_NEXT | calc", "../../../Windows/System32/cmd.exe", "ALT+F4",
        "CTRL+ALT+DEL", "WIN+R", "{{7*7}}", "%COMSPEC%",
    ]

    def test_shell_like_payloads_never_accepted(self):
        def boom(*a, **k):
            raise AssertionError("se intentó iniciar un proceso")

        with mock.patch.object(subprocess, "Popen", boom), \
                mock.patch.object(os, "system", boom), \
                mock.patch.object(os, "startfile", boom, create=True):
            for p in self.PAYLOADS:
                for field in ("requestedAction", "activeMode", "gestureId"):
                    with self.subTest(field=field, payload=p):
                        r = self.v.validate(raw(make({field: p})))
                        self.assertFalse(r.accepted)
                        self.assertIsNone(r.action)

    def test_executor_rejects_strings(self):
        ex = SimulatedExecutor()
        for value in ("SLIDE_NEXT", "cmd.exe", None, 0):
            with self.subTest(value=value):
                with self.assertRaises(TypeError):
                    ex.execute(value)
        self.assertEqual(ex.history, [])

    def test_poc_sources_do_not_import_process_or_native_apis(self):
        forbidden = ("subprocess", "os.system", "os.startfile", "os.popen", "ctypes",
                     "win32api", "pyautogui", "eval(", "exec(")
        here = pathlib.Path(__file__).parent
        for name in ("validator.py", "simulated_executor.py"):
            source = (here / name).read_text(encoding="utf-8")
            for token in forbidden:
                with self.subTest(file=name, token=token):
                    self.assertNotIn(token, source)

    def test_validator_never_raises(self):
        weird = [b"\x00" * 10, "\ud800".encode("utf-8", "surrogatepass"), b"1e999999",
                 b'{"payload": {"confidence": 1e999}}', bytearray(b"{}")]
        for data in weird:
            with self.subTest(data=data[:20]):
                r = self.v.validate(data)
                self.assertEqual(r.status, STATUS_INVALID)


if __name__ == "__main__":
    unittest.main()
