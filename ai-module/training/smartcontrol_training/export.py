"""Exporta un modelo entrenado al formato smartcontrol-gesture-model/v1 (ADR-06) con su resumen SHA-256.

El archivo resultante lo carga GestureModelLoader en Windows. Los modelos entrenados con datos
sintéticos se marcan como "synthetic-fixture" y Windows solo los admite en las pruebas.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import numpy as np

from smartcontrol_training import features
from smartcontrol_training.model import CLASSES, TrainedModel

SCHEMA_VERSION = "smartcontrol-gesture-model/v1"
MINIMUM_THRESHOLD = 0.80


def _vector(values: np.ndarray) -> list[float]:
    values = [float(v) for v in np.asarray(values).ravel()]
    if not all(math.isfinite(v) for v in values):
        raise ValueError("el modelo contiene valores no finitos")
    return values


def to_json(model: TrainedModel, data_source: str, participants: int, windows: int,
            dataset_sha256: str | None, model_version: str, threshold: float = MINIMUM_THRESHOLD) -> dict:
    """`data_source` es el de las grabaciones: "physical-watch" o "synthetic"."""
    if data_source == "physical-watch":
        source = "physical-watch"
        if participants <= 0 or windows <= 0 or not dataset_sha256:
            raise ValueError("un modelo de datos reales debe declarar participantes, ventanas y resumen del conjunto")
    elif data_source == "synthetic":
        source = "synthetic-fixture"
    else:
        raise ValueError(f"procedencia desconocida: {data_source}")
    if not MINIMUM_THRESHOLD <= threshold <= 1.0:
        raise ValueError("el umbral debe estar entre 0.80 y 1")
    if not 0 < len(model_version) <= 64:
        raise ValueError("versión del modelo vacía o demasiado larga")
    if len(model.mean) != features.COUNT or len(model.std) != features.COUNT:
        raise ValueError("el modelo no corresponde a gyro-v1")

    return {
        "schemaVersion": SCHEMA_VERSION,
        "modelVersion": model_version,
        "featureSet": features.FEATURE_SET_ID,
        "classes": list(CLASSES),
        "dataSource": source,
        "training": {"participants": participants, "windows": windows, "datasetSha256": dataset_sha256},
        "decisionThreshold": threshold,
        "normalization": {"mean": _vector(model.mean), "std": _vector(model.std)},
        "layers": [
            {"weights": [_vector(row) for row in layer.weights], "bias": _vector(layer.bias), "activation": layer.activation}
            for layer in model.layers
        ],
    }


def write_model(path: str | Path, model_json: dict) -> Path:
    """Escribe el modelo y "modelo.json.sha256" (mismo formato que GestureModelLoader.WriteChecksum)."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    data = (json.dumps(model_json, allow_nan=False, separators=(",", ":")) + "\n").encode("utf-8")
    path.write_bytes(data)
    digest = hashlib.sha256(data).hexdigest()
    Path(str(path) + ".sha256").write_bytes(f"{digest}  {path.name}\n".encode("ascii"))
    return path


def verify_checksum(path: str | Path) -> bool:
    path = Path(path)
    expected = Path(str(path) + ".sha256").read_text(encoding="ascii").split()[0].lower()
    return hashlib.sha256(path.read_bytes()).hexdigest() == expected
