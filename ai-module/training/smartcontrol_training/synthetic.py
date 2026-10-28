"""Generador de grabaciones SINTÉTICAS (dataSource "synthetic") para probar el código.

No imitan fielmente movimientos reales y nunca deben usarse como datos de entrenamiento de un
modelo de verdad ni para medir la precisión. No se pueden escribir en ai-module/data/, la carpeta
de las grabaciones reales. detectorEvent queda en null porque ningún reconocedor las segmentó.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

REAL_DATA_DIR = (Path(__file__).resolve().parent.parent.parent / "data").resolve()
_NEGATIVES = ("walking", "shake", "slow_turn", "typing")


def _twist(rng, sign: float, scale: float) -> np.ndarray:
    count = int(rng.integers(30, 50))
    values = rng.normal(0.0, 0.15, size=(count, 3))
    center = int(rng.integers(10, count - 10))
    width = int(rng.integers(3, 6))
    peak = sign * scale * rng.uniform(4.0, 7.0)
    values[center - width:center + width, 0] += peak * np.hanning(2 * width)
    return values


def _negative(rng, activity: str, scale: float) -> np.ndarray:
    count = int(rng.integers(30, 60))
    t = np.arange(count) * 0.02
    values = rng.normal(0.0, 0.15, size=(count, 3))
    if activity == "walking":
        values[:, 1] += scale * 1.5 * np.sin(2 * np.pi * 1.8 * t)
        values[:, 2] += scale * 1.0 * np.cos(2 * np.pi * 1.8 * t)
    elif activity == "shake":
        values[:, 0] += scale * 5.0 * np.sin(2 * np.pi * 5.0 * t)
    elif activity == "slow_turn":
        values[:, 0] += scale * 1.2
    else:  # typing: pequeños golpes
        values += rng.normal(0.0, 0.5, size=values.shape)
    return values


def _window(window_id: int, label: str, activity, values: np.ndarray) -> dict:
    values = np.clip(np.round(values, 5), -34.0, 34.0)
    return {
        "windowId": window_id, "label": label, "mode": None, "posture": "sitting",
        "activity": activity, "detectorEvent": None,
        "gyroscope": [[i * 20, *map(float, row)] for i, row in enumerate(values)],
        "accelerometer": None,
    }


def generate(directory: str | Path, participants: int = 3, per_class: int = 20, seed: int = 0) -> list[Path]:
    directory = Path(directory).resolve()
    if directory == REAL_DATA_DIR or REAL_DATA_DIR in directory.parents:
        raise ValueError("los datos sintéticos no se pueden guardar en ai-module/data/ (solo datos reales)")
    directory.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(seed)
    paths = []
    for p in range(1, participants + 1):
        scale = rng.uniform(0.8, 1.2)  # variación sintética entre "personas"
        windows, window_id = [], 0
        for label in ("TWIST_OUT", "TWIST_IN", "NO_GESTURE"):
            for _ in range(per_class):
                window_id += 1
                if label == "NO_GESTURE":
                    activity = _NEGATIVES[int(rng.integers(len(_NEGATIVES)))]
                    windows.append(_window(window_id, label, activity, _negative(rng, activity, scale)))
                else:
                    windows.append(_window(window_id, label, None, _twist(rng, 1.0 if label == "TWIST_OUT" else -1.0, scale)))
        header = {
            "format": "sensor-data/v1", "dataSource": "synthetic", "recorder": "synthetic-generator/1",
            "consentVersion": None, "sessionId": f"SYN{p:02d}", "participantId": f"SYN-P{p:02d}",
            "wrist": "left", "crown": "hand", "samplingHz": 50, "createdDate": "2026-10-28",
        }
        path = directory / f"synthetic-P{p:02d}.jsonl"
        lines = [json.dumps(header)] + [json.dumps(w) for w in windows]
        path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
        paths.append(path)
    return paths
