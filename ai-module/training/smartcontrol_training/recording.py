"""Lectura y validación de grabaciones sensor-data/v1 (docs/recording-format.md).

Las reglas de cada serie son las mismas que SensorWindow en Windows, para que una ventana
aceptada aquí también sea aceptada por la inferencia en C#.
"""
from __future__ import annotations

import hashlib
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np

FORMAT = "sensor-data/v1"
DATA_SOURCES = ("physical-watch", "synthetic")
RECORDERS = ("watch-debug-recorder/1", "synthetic-generator/1")
CONSENT_VERSIONS = ("consent/v1",)
LABELS = ("TWIST_OUT", "TWIST_IN", "NO_GESTURE")
MODES = ("PRESENTATION", "MULTIMEDIA")
POSTURES = ("sitting", "standing", "walking")
ACTIVITIES = ("walking", "typing", "drinking", "door", "talking", "shake", "slow_turn", "ambiguous", "other")
DETECTOR_EVENTS = ("TWIST_OUT", "TWIST_IN", "UNCLEAR")

MIN_SAMPLES = 8
MAX_SAMPLES = 256
MAX_DURATION_MS = 2000
MAX_GAP_MS = 100
MAX_GYROSCOPE_ABS = 35.0
MAX_ACCELEROMETER_ABS = 160.0

HEADER_FIELDS = {"format", "dataSource", "recorder", "consentVersion", "sessionId", "participantId",
                 "wrist", "crown", "samplingHz", "createdDate"}
WINDOW_FIELDS = {"windowId", "label", "mode", "posture", "activity", "detectorEvent", "gyroscope", "accelerometer"}

_ID_PATTERN = re.compile(r"\A[A-Za-z0-9_-]{1,32}\Z")
_DATE_PATTERN = re.compile(r"\A\d{4}-\d{2}-\d{2}\Z")


class RecordingError(ValueError):
    """Grabación inválida. `code` es una constante; el mensaje indica la línea."""

    def __init__(self, code: str, message: str):
        super().__init__(f"{code}: {message}")
        self.code = code


@dataclass(frozen=True)
class Session:
    data_source: str
    recorder: str
    consent_version: str | None
    session_id: str
    participant_id: str
    wrist: str
    crown: str
    sampling_hz: int
    created_date: str


@dataclass(frozen=True)
class Window:
    window_id: int
    label: str
    mode: str | None
    posture: str | None
    activity: str | None
    detector_event: str | None
    gyroscope: np.ndarray  # (n, 4): t_ms, x, y, z
    accelerometer: np.ndarray | None


@dataclass(frozen=True)
class Recording:
    path: Path
    sha256: str
    session: Session
    windows: tuple[Window, ...]


@dataclass(frozen=True)
class Dataset:
    data_source: str
    recordings: tuple[Recording, ...]
    sha256: str

    @property
    def windows(self) -> list[tuple[Recording, Window]]:
        return [(r, w) for r in self.recordings for w in r.windows]

    @property
    def participants(self) -> list[str]:
        return sorted({r.session.participant_id for r in self.recordings})


def _no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise RecordingError("DUPLICATE_FIELD", f"campo repetido: {key}")
        result[key] = value
    return result


def _no_constants(name):
    raise RecordingError("NON_FINITE_VALUE", f"valor no permitido: {name}")


def _parse_line(text: str, line: int) -> dict:
    try:
        value = json.loads(text, object_pairs_hook=_no_duplicates, parse_constant=_no_constants)
    except json.JSONDecodeError as error:
        raise RecordingError("MALFORMED_JSON", f"línea {line}: {error.msg}") from None
    if not isinstance(value, dict):
        raise RecordingError("MALFORMED_JSON", f"línea {line}: no es un objeto")
    return value


def _fields(obj: dict, expected: set[str], line: int) -> None:
    missing = expected - obj.keys()
    unknown = obj.keys() - expected
    if missing:
        raise RecordingError("MISSING_FIELD", f"línea {line}: faltan {sorted(missing)}")
    if unknown:
        raise RecordingError("UNKNOWN_FIELD", f"línea {line}: campos desconocidos {sorted(unknown)}")


def _choice(value, allowed, code: str, line: int, nullable: bool = False):
    if value is None and nullable:
        return None
    if not isinstance(value, str) or value not in allowed:
        raise RecordingError(code, f"línea {line}: valor no admitido")
    return value


def _parse_header(obj: dict) -> Session:
    _fields(obj, HEADER_FIELDS, 1)
    if obj["format"] != FORMAT:
        raise RecordingError("UNSUPPORTED_FORMAT", "línea 1: formato desconocido")
    source = _choice(obj["dataSource"], DATA_SOURCES, "INVALID_DATA_SOURCE", 1)
    recorder = _choice(obj["recorder"], RECORDERS, "INVALID_RECORDER", 1)
    consent = obj["consentVersion"]
    if source == "physical-watch":
        _choice(consent, CONSENT_VERSIONS, "MISSING_CONSENT", 1)
        if recorder != "watch-debug-recorder/1":
            raise RecordingError("INVALID_RECORDER", "línea 1: un dato real solo lo graba el reloj")
    elif consent is not None:
        raise RecordingError("INVALID_CONSENT", "línea 1: los datos sintéticos no llevan consentimiento")
    for name in ("sessionId", "participantId"):
        if not isinstance(obj[name], str) or not _ID_PATTERN.match(obj[name]):
            raise RecordingError("INVALID_ID", f"línea 1: {name} no válido")
    wrist = _choice(obj["wrist"], ("left", "right"), "INVALID_WRIST", 1)
    crown = _choice(obj["crown"], ("hand", "elbow"), "INVALID_CROWN", 1)
    hz = obj["samplingHz"]
    if not isinstance(hz, int) or isinstance(hz, bool) or not 10 <= hz <= 200:
        raise RecordingError("INVALID_SAMPLING", "línea 1: samplingHz fuera de 10..200")
    if not isinstance(obj["createdDate"], str) or not _DATE_PATTERN.match(obj["createdDate"]):
        raise RecordingError("INVALID_DATE", "línea 1: createdDate debe ser AAAA-MM-DD")
    return Session(source, recorder, consent, obj["sessionId"], obj["participantId"], wrist, crown, hz, obj["createdDate"])


def parse_series(value, max_abs: float, line: int, name: str) -> np.ndarray:
    """Serie [[tOffsetMs, x, y, z], ...] con las reglas de SensorWindow."""
    if not isinstance(value, list):
        raise RecordingError("INVALID_SERIES", f"línea {line}: {name} no es una lista")
    if len(value) < MIN_SAMPLES:
        raise RecordingError("TOO_FEW_SAMPLES", f"línea {line}: {name}")
    if len(value) > MAX_SAMPLES:
        raise RecordingError("TOO_MANY_SAMPLES", f"línea {line}: {name}")
    rows = []
    for item in value:
        if not isinstance(item, list) or len(item) != 4:
            raise RecordingError("INVALID_SERIES", f"línea {line}: {name} debe tener [t, x, y, z]")
        t, *xyz = item
        if not isinstance(t, int) or isinstance(t, bool):
            raise RecordingError("INVALID_SERIES", f"línea {line}: {name} con tiempo no entero")
        for v in xyz:
            if isinstance(v, bool) or not isinstance(v, (int, float)):
                raise RecordingError("INVALID_SERIES", f"línea {line}: {name} con valor no numérico")
            if not math.isfinite(v):
                raise RecordingError("NON_FINITE_VALUE", f"línea {line}: {name}")
            if abs(v) > max_abs:
                raise RecordingError("VALUE_OUT_OF_RANGE", f"línea {line}: {name}")
        rows.append([t, *xyz])
    series = np.asarray(rows, dtype=np.float64)
    if series[0, 0] != 0:
        raise RecordingError("FIRST_OFFSET_NOT_ZERO", f"línea {line}: {name}")
    gaps = np.diff(series[:, 0])
    if np.any(gaps <= 0):
        raise RecordingError("NON_INCREASING_TIME", f"línea {line}: {name}")
    if np.any(gaps > MAX_GAP_MS):
        raise RecordingError("GAP_TOO_LARGE", f"línea {line}: {name}")
    if series[-1, 0] > MAX_DURATION_MS:
        raise RecordingError("WINDOW_TOO_LONG", f"línea {line}: {name}")
    return series


def _parse_window(obj: dict, line: int, previous_id: int) -> Window:
    _fields(obj, WINDOW_FIELDS, line)
    window_id = obj["windowId"]
    if not isinstance(window_id, int) or isinstance(window_id, bool) or window_id <= previous_id:
        raise RecordingError("INVALID_WINDOW_ID", f"línea {line}: windowId debe crecer")
    label = _choice(obj["label"], LABELS, "INVALID_LABEL", line)
    mode = _choice(obj["mode"], MODES, "INVALID_MODE", line, nullable=True)
    posture = _choice(obj["posture"], POSTURES, "INVALID_POSTURE", line, nullable=True)
    activity = _choice(obj["activity"], ACTIVITIES, "INVALID_ACTIVITY", line, nullable=True)
    if activity is not None and label != "NO_GESTURE":
        raise RecordingError("INVALID_ACTIVITY", f"línea {line}: activity solo con NO_GESTURE")
    detector = _choice(obj["detectorEvent"], DETECTOR_EVENTS, "INVALID_DETECTOR_EVENT", line, nullable=True)
    gyroscope = parse_series(obj["gyroscope"], MAX_GYROSCOPE_ABS, line, "gyroscope")
    accelerometer = None
    if obj["accelerometer"] is not None:
        accelerometer = parse_series(obj["accelerometer"], MAX_ACCELEROMETER_ABS, line, "accelerometer")
    return Window(window_id, label, mode, posture, activity, detector, gyroscope, accelerometer)


def load_recording(path: str | Path) -> Recording:
    path = Path(path)
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise RecordingError("INVALID_ENCODING", f"{path.name}: no es UTF-8") from None
    lines = text.splitlines()
    while lines and lines[-1] == "":
        lines.pop()
    if not lines:
        raise RecordingError("EMPTY_FILE", path.name)
    if any(line.strip() == "" for line in lines):
        raise RecordingError("MALFORMED_JSON", f"{path.name}: línea vacía intermedia")
    session = _parse_header(_parse_line(lines[0], 1))
    windows = []
    previous_id = 0
    for number, line in enumerate(lines[1:], start=2):
        window = _parse_window(_parse_line(line, number), number, previous_id)
        windows.append(window)
        previous_id = window.window_id
    if not windows:
        raise RecordingError("NO_WINDOWS", path.name)
    return Recording(path, hashlib.sha256(raw).hexdigest(), session, tuple(windows))


def load_dataset(paths, allow_synthetic: bool = False) -> Dataset:
    """Carga varias sesiones. Nunca mezcla datos reales y sintéticos; los sintéticos solo con permiso explícito."""
    recordings = [load_recording(p) for p in sorted(Path(p) for p in paths)]
    if not recordings:
        raise RecordingError("NO_RECORDINGS", "no hay archivos")
    sources = {r.session.data_source for r in recordings}
    if len(sources) != 1:
        raise RecordingError("MIXED_DATA_SOURCES", "no se pueden mezclar grabaciones reales y sintéticas")
    source = sources.pop()
    if source == "synthetic" and not allow_synthetic:
        raise RecordingError("SYNTHETIC_NOT_ALLOWED", "datos sintéticos: usar solo para probar el código")
    # Resumen del conjunto independiente del orden y de las rutas: SHA-256 de los resúmenes ordenados.
    digest = hashlib.sha256("\n".join(sorted(r.sha256 for r in recordings)).encode("ascii")).hexdigest()
    return Dataset(source, tuple(recordings), digest)
