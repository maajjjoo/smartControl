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


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--write", type=Path, default=FIXTURE, help="ruta del fixture que se escribe")
    args = parser.parse_args()
    args.write.parent.mkdir(parents=True, exist_ok=True)
    args.write.write_text(json.dumps(build(), indent=1) + "\n", encoding="utf-8")
    print(f"escrito {args.write}")


if __name__ == "__main__":
    main()
