"""Utilidades de prueba. Todo lo que se escribe aquí es SINTÉTICO y va a carpetas temporales."""
from __future__ import annotations

import json
from pathlib import Path


def synthetic_header(**changes) -> dict:
    header = {
        "format": "sensor-data/v1", "dataSource": "synthetic", "recorder": "synthetic-generator/1",
        "consentVersion": None, "sessionId": "S01", "participantId": "P01", "wrist": "left",
        "crown": "hand", "samplingHz": 50, "createdDate": "2026-10-28",
    }
    header.update(changes)
    return header


def series(values, step_ms: int = 20) -> list:
    """Serie sintética con el valor dado en X y 0 en Y y Z."""
    return [[i * step_ms, float(v), 0.0, 0.0] for i, v in enumerate(values)]


def window(window_id: int = 1, label: str = "TWIST_OUT", **changes) -> dict:
    item = {
        "windowId": window_id, "label": label, "mode": "PRESENTATION", "posture": "sitting",
        "activity": None, "detectorEvent": None, "gyroscope": series([0, 1, 2, 3, 3, 2, 1, 0]),
        "accelerometer": None,
    }
    item.update(changes)
    return item


def write_jsonl(path: Path, header: dict, windows: list[dict]) -> Path:
    lines = [json.dumps(header)] + [json.dumps(w) for w in windows]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path
