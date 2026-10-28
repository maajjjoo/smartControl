"""Genera fixtures/feature-parity.json: ventanas SINTÉTICAS y sus características calculadas en Python.

La prueba de C# (FeatureParityTests) calcula las mismas características y las compara, para
garantizar que el modelo entrenado aquí recibe en Windows exactamente las mismas entradas.

    python -m smartcontrol_training.parity --write fixtures/feature-parity.json
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from smartcontrol_training import features

SEED = 20261028
FIXTURE = Path(__file__).resolve().parent.parent / "fixtures" / "feature-parity.json"


def _random_window(rng: np.random.Generator) -> list[list[float]]:
    count = int(rng.integers(8, 121))
    steps = rng.integers(5, 41, size=count - 1)
    while steps.sum() > 2000:
        steps = steps // 2 + 1
    times = np.concatenate([[0], np.cumsum(steps)])
    values = rng.normal(0.0, 2.0, size=(count, 3))
    if rng.random() < 0.5:
        # Giro sintético en X en mitad de la ventana.
        center, width = count // 2, max(2, count // 6)
        values[center - width:center + width, 0] += rng.choice([-1.0, 1.0]) * rng.uniform(3.0, 8.0)
    values = np.clip(np.round(values, 6), -30.0, 30.0)
    return [[int(t), float(x), float(y), float(z)] for t, (x, y, z) in zip(times, values)]


def build(count: int = 40) -> dict:
    rng = np.random.default_rng(SEED)
    windows = [_random_window(rng) for _ in range(count)]
    # Casos fijos: reposo total y un único valor distinto de cero.
    windows.append([[i * 20, 0.0, 0.0, 0.0] for i in range(8)])
    windows.append([[i * 20, 0.0, 0.0, 0.0] for i in range(7)] + [[140, 0.0, -0.5, 0.0]])
    return {
        "purpose": "Paridad de características gyro-v1 entre Python y C#",
        "dataSource": "synthetic",
        "featureSet": features.FEATURE_SET_ID,
        "seed": SEED,
        "names": list(features.NAMES),
        "cases": [{"gyroscope": w, "features": features.extract(np.array(w)).tolist()} for w in windows],
    }


MODEL_FIXTURE = FIXTURE.parent / "model-parity" / "model.json"
EXPECTED_FIXTURE = FIXTURE.parent / "model-parity" / "expected.json"


def build_model_fixture(feature_fixture: dict) -> tuple[dict, dict]:
    """MLP SINTÉTICO pequeño y sus probabilidades esperadas sobre las ventanas de paridad.

    Las etiquetas son inventadas (signo de la integral de X); solo sirve para comprobar que C#
    carga el modelo exportado y obtiene las mismas probabilidades.
    """
    from smartcontrol_training import export, model

    x = np.array([case["features"] for case in feature_fixture["cases"]])
    integral = x[:, features.NAMES.index("gyro_x_integral")]
    labels = np.where(integral > 0.05, 0, np.where(integral < -0.05, 1, 2))
    trained = model.train_mlp(x, labels, hidden=4, epochs=300, seed=SEED)
    model_json = export.to_json(trained, "synthetic", participants=0, windows=len(x),
                                dataset_sha256=None, model_version="parity-fixture-1")
    probabilities = model.predict_proba(trained, x)
    expected = {
        "purpose": "Paridad de inferencia entre Python y C# con un modelo sintético",
        "dataSource": "synthetic",
        "classes": list(model.CLASSES),
        "probabilities": probabilities.tolist(),
    }
    return model_json, expected


def main() -> None:
    from smartcontrol_training import export

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--write", type=Path, default=FIXTURE, help="ruta del fixture que se escribe")
    args = parser.parse_args()
    args.write.parent.mkdir(parents=True, exist_ok=True)
    feature_fixture = build()
    # write_bytes para escribir LF en cualquier sistema (el fixture se compara byte a byte en Git).
    args.write.write_bytes((json.dumps(feature_fixture, indent=1) + "\n").encode("utf-8"))
    print(f"escrito {args.write}")

    model_json, expected = build_model_fixture(feature_fixture)
    export.write_model(MODEL_FIXTURE, model_json)
    EXPECTED_FIXTURE.write_bytes((json.dumps(expected, indent=1) + "\n").encode("utf-8"))
    print(f"escrito {MODEL_FIXTURE} y {EXPECTED_FIXTURE}")


if __name__ == "__main__":
    main()
