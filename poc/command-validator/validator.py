"""PoC 1 de SmartControl: validador de mensajes COMMAND_REQUEST.

Prototipo TEMPORAL en Python (solo biblioteca estándar). No es la tecnología
definitiva de windows-app, que seguirá siendo C#/.NET. Su objetivo es comprobar
las reglas de docs/communication-protocol.md (sección 4) y
docs/security-allowlist.md con casos positivos y negativos reproducibles.

Este módulo NO ejecuta acciones. Solo devuelve un resultado de validación con un
valor enumerado cerrado (AllowedAction) cuando el mensaje es aceptable.
"""

from __future__ import annotations

import hmac
import json
import math
import re
import time
from collections import OrderedDict
from dataclasses import dataclass
from enum import Enum
from typing import Callable, Mapping, Optional


class ControlMode(Enum):
    PRESENTATION = "PRESENTATION"
    MULTIMEDIA = "MULTIMEDIA"


class AllowedAction(Enum):
    SLIDE_NEXT = "SLIDE_NEXT"
    SLIDE_PREVIOUS = "SLIDE_PREVIOUS"
    MEDIA_PLAY = "MEDIA_PLAY"
    MEDIA_PAUSE = "MEDIA_PAUSE"


# Lista blanca cerrada: cada acción pertenece a un único modo.
ALLOWLIST: Mapping[AllowedAction, ControlMode] = {
    AllowedAction.SLIDE_NEXT: ControlMode.PRESENTATION,
    AllowedAction.SLIDE_PREVIOUS: ControlMode.PRESENTATION,
    AllowedAction.MEDIA_PLAY: ControlMode.MULTIMEDIA,
    AllowedAction.MEDIA_PAUSE: ControlMode.MULTIMEDIA,
}

# Estados tomados de communication-protocol.md, sección 3.2.
STATUS_ACCEPTED = "ACCEPTED"  # Interno de la PoC: validado, pendiente de ejecutor.
STATUS_LOW_CONFIDENCE = "REJECTED_LOW_CONFIDENCE"
STATUS_NOT_ALLOWED = "REJECTED_NOT_ALLOWED"
STATUS_INVALID = "REJECTED_INVALID_MESSAGE"

SUPPORTED_VERSIONS = frozenset({"1.0-draft"})
HEADER_FIELDS = frozenset(
    {"protocolVersion", "messageId", "timestampUtc", "deviceId",
     "sessionToken", "messageType", "payload"}
)
PAYLOAD_FIELDS = frozenset({"activeMode", "gestureId", "confidence", "requestedAction"})

# Parámetros de la PoC. Son valores de ensayo, no decisiones definitivas.
MAX_MESSAGE_BYTES = 4096
DEFAULT_CONFIDENCE_THRESHOLD = 0.80
DEFAULT_MAX_AGE_MS = 5000
DEFAULT_MAX_FUTURE_SKEW_MS = 2000
DEFAULT_REPLAY_CACHE_SIZE = 1024

_UUID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
_DEVICE_ID_RE = re.compile(r"^[A-Za-z0-9._-]{1,64}$")
_GESTURE_ID_RE = re.compile(r"^[A-Z][A-Z0-9_]{0,63}$")
_MAX_TOKEN_LEN = 256


@dataclass(frozen=True)
class ValidationResult:
    status: str
    reason_code: str
    action: Optional[AllowedAction] = None
    message_id: Optional[str] = None

    @property
    def accepted(self) -> bool:
        return self.status == STATUS_ACCEPTED and self.action is not None


class _InvalidJson(ValueError):
    pass


def _reject_constant(name: str):
    # json.loads acepta NaN, Infinity y -Infinity por defecto; aquí se rechazan.
    raise _InvalidJson(f"constante no permitida: {name}")


def _no_duplicate_keys(pairs):
    # json.loads conserva la última clave repetida en silencio; aquí se rechaza.
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise _InvalidJson(f"clave duplicada: {key}")
        obj[key] = value
    return obj


def _is_int(value) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_number(value) -> bool:
    return (isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value))


class CommandValidator:
    def __init__(
        self,
        paired_devices: Mapping[str, str],
        *,
        confidence_threshold: float = DEFAULT_CONFIDENCE_THRESHOLD,
        max_age_ms: int = DEFAULT_MAX_AGE_MS,
        max_future_skew_ms: int = DEFAULT_MAX_FUTURE_SKEW_MS,
        replay_cache_size: int = DEFAULT_REPLAY_CACHE_SIZE,
        clock_ms: Callable[[], int] = lambda: int(time.time() * 1000),
        receiver_mode: Optional[ControlMode] = None,
    ):
        if not 0.0 <= confidence_threshold <= 1.0:
            raise ValueError("confidence_threshold fuera de [0, 1]")
        self._paired = dict(paired_devices)
        self._threshold = confidence_threshold
        self._max_age_ms = max_age_ms
        self._max_future_skew_ms = max_future_skew_ms
        self._clock_ms = clock_ms
        self._seen: OrderedDict[str, int] = OrderedDict()
        self._seen_max = replay_cache_size
        self.receiver_mode = receiver_mode

    def validate(self, raw) -> ValidationResult:
        """Valida un mensaje recibido como bytes o str. Nunca lanza excepciones."""
        try:
            return self._validate(raw)
        except Exception:  # Cualquier fallo inesperado termina en rechazo.
            return ValidationResult(STATUS_INVALID, "INTERNAL_VALIDATION_ERROR")

    def _validate(self, raw) -> ValidationResult:
        # 1. Tamaño y sintaxis.
        if isinstance(raw, str):
            raw = raw.encode("utf-8")
        if not isinstance(raw, (bytes, bytearray)):
            return ValidationResult(STATUS_INVALID, "NOT_BYTES")
        if len(raw) == 0:
            return ValidationResult(STATUS_INVALID, "EMPTY_MESSAGE")
        if len(raw) > MAX_MESSAGE_BYTES:
            return ValidationResult(STATUS_INVALID, "MESSAGE_TOO_LARGE")
        try:
            text = bytes(raw).decode("utf-8", errors="strict")
            msg = json.loads(text, parse_constant=_reject_constant,
                             object_pairs_hook=_no_duplicate_keys)
        except (UnicodeDecodeError, ValueError, RecursionError):
            return ValidationResult(STATUS_INVALID, "MALFORMED_JSON")

        # 1b. Estructura y tipos de la cabecera.
        if not isinstance(msg, dict):
            return ValidationResult(STATUS_INVALID, "NOT_AN_OBJECT")
        keys = set(msg)
        if HEADER_FIELDS - keys:
            return ValidationResult(STATUS_INVALID, "MISSING_HEADER_FIELD")
        if keys - HEADER_FIELDS:
            return ValidationResult(STATUS_INVALID, "UNKNOWN_HEADER_FIELD")

        message_id = msg["messageId"]
        if not isinstance(message_id, str) or not _UUID_RE.match(message_id):
            return ValidationResult(STATUS_INVALID, "INVALID_MESSAGE_ID")
        if not _is_int(msg["timestampUtc"]) or msg["timestampUtc"] < 0:
            return ValidationResult(STATUS_INVALID, "INVALID_TIMESTAMP", message_id=message_id)
        if not isinstance(msg["deviceId"], str) or not _DEVICE_ID_RE.match(msg["deviceId"]):
            return ValidationResult(STATUS_INVALID, "INVALID_DEVICE_ID", message_id=message_id)
        token = msg["sessionToken"]
        if not isinstance(token, str) or not 0 < len(token) <= _MAX_TOKEN_LEN:
            return ValidationResult(STATUS_INVALID, "INVALID_SESSION_TOKEN", message_id=message_id)
        if msg["messageType"] != "COMMAND_REQUEST":
            return ValidationResult(STATUS_INVALID, "UNSUPPORTED_MESSAGE_TYPE", message_id=message_id)

        payload = msg["payload"]
        if not isinstance(payload, dict):
            return ValidationResult(STATUS_INVALID, "INVALID_PAYLOAD", message_id=message_id)
        pkeys = set(payload)
        if PAYLOAD_FIELDS - pkeys:
            return ValidationResult(STATUS_INVALID, "MISSING_PAYLOAD_FIELD", message_id=message_id)
        if pkeys - PAYLOAD_FIELDS:
            return ValidationResult(STATUS_INVALID, "UNKNOWN_PAYLOAD_FIELD", message_id=message_id)
        for field in ("activeMode", "gestureId", "requestedAction"):
            if not isinstance(payload[field], str):
                return ValidationResult(STATUS_INVALID, "INVALID_FIELD_TYPE", message_id=message_id)
        if not _GESTURE_ID_RE.match(payload["gestureId"]):
            return ValidationResult(STATUS_INVALID, "INVALID_GESTURE_ID", message_id=message_id)
        confidence = payload["confidence"]
        if not _is_number(confidence) or not 0.0 <= confidence <= 1.0:
            return ValidationResult(STATUS_INVALID, "INVALID_CONFIDENCE", message_id=message_id)

        # 2. Versión.
        if msg["protocolVersion"] not in SUPPORTED_VERSIONS:
            return ValidationResult(STATUS_INVALID, "UNSUPPORTED_VERSION", message_id=message_id)

        # 3. Vinculación del dispositivo (comparación en tiempo constante).
        expected = self._paired.get(msg["deviceId"])
        if expected is None or not hmac.compare_digest(
                expected.encode("utf-8"), token.encode("utf-8")):
            return ValidationResult(STATUS_INVALID, "UNAUTHORIZED_DEVICE", message_id=message_id)

        # 4. Frescura y repetición.
        now = self._clock_ms()
        age = now - msg["timestampUtc"]
        if age > self._max_age_ms:
            return ValidationResult(STATUS_INVALID, "STALE_MESSAGE", message_id=message_id)
        if -age > self._max_future_skew_ms:
            return ValidationResult(STATUS_INVALID, "FUTURE_TIMESTAMP", message_id=message_id)
        if message_id in self._seen:
            return ValidationResult(STATUS_INVALID, "REPLAYED_MESSAGE", message_id=message_id)
        if not self._remember(message_id, now):
            return ValidationResult(STATUS_INVALID, "REPLAY_CACHE_FULL", message_id=message_id)

        # 5. Umbral de confianza.
        if confidence < self._threshold:
            return ValidationResult(STATUS_LOW_CONFIDENCE, "BELOW_THRESHOLD", message_id=message_id)

        # 6. Modo y lista blanca (coincidencia exacta, sensible a mayúsculas).
        mode = _enum_by_value(ControlMode, payload["activeMode"])
        if mode is None:
            return ValidationResult(STATUS_INVALID, "UNKNOWN_MODE", message_id=message_id)
        action = _enum_by_value(AllowedAction, payload["requestedAction"])
        if action is None:
            return ValidationResult(STATUS_NOT_ALLOWED, "ACTION_NOT_IN_ALLOWLIST", message_id=message_id)
        if ALLOWLIST[action] is not mode:
            return ValidationResult(STATUS_NOT_ALLOWED, "MODE_ACTION_MISMATCH", message_id=message_id)
        if self.receiver_mode is not None and self.receiver_mode is not mode:
            return ValidationResult(STATUS_NOT_ALLOWED, "RECEIVER_MODE_MISMATCH", message_id=message_id)

        return ValidationResult(STATUS_ACCEPTED, "OK", action=action, message_id=message_id)

    def _remember(self, message_id: str, now: int) -> bool:
        # Los identificadores más antiguos que la ventana de frescura ya no pueden
        # repetirse con éxito (se rechazarían como STALE), así que se descartan.
        # Si la caché está llena de identificadores vigentes, se rechaza el mensaje
        # en lugar de olvidar uno que todavía podría repetirse.
        window = self._max_age_ms + self._max_future_skew_ms
        while self._seen:
            _, seen_at = next(iter(self._seen.items()))
            if now - seen_at > window:
                self._seen.popitem(last=False)
            else:
                break
        if len(self._seen) >= self._seen_max:
            return False
        self._seen[message_id] = now
        return True


def _enum_by_value(enum_cls, value: str):
    for member in enum_cls:
        if member.value == value:
            return member
    return None
