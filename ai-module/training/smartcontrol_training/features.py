"""Características gyro-v1 (docs/phase2-ai-evaluation.md §6.3).

Debe dar los mismos valores que MovementFeatures en C# (windows-app/src/SmartControl.Core/
Recognition/MovementFeatures.cs): el modelo se entrena aquí y se ejecuta allí. La paridad se
comprueba con fixtures/feature-parity.json desde las pruebas de ambos lados.
"""
from __future__ import annotations

import numpy as np

FEATURE_SET_ID = "gyro-v1"
ZERO_CROSSING_DEAD_BAND = 0.2

_PER_AXIS = ("mean", "std", "min", "max", "peak_abs", "peak_position", "integral", "zero_crossings", "energy")
NAMES = tuple(
    [f"gyro_{axis}_{name}" for axis in "xyz" for name in _PER_AXIS]
    + ["gyro_magnitude_max", "gyro_magnitude_mean", "duration_s", "x_dominance"]
)
COUNT = len(NAMES)


def _zero_crossings(values: np.ndarray) -> int:
    significant = values[np.abs(values) >= ZERO_CROSSING_DEAD_BAND]
    if significant.size < 2:
        return 0
    signs = np.sign(significant)
    return int(np.count_nonzero(signs[1:] != signs[:-1]))


def extract(gyroscope: np.ndarray) -> np.ndarray:
    """`gyroscope` con forma (n, 4): t_ms, x, y, z. La serie ya debe cumplir las reglas de la ventana."""
    gyroscope = np.asarray(gyroscope, dtype=np.float64)
    t = gyroscope[:, 0]
    dt = np.diff(t) / 1000.0
    duration_ms = t[-1]
    features = []
    abs_integrals = []
    for axis in range(1, 4):
        v = gyroscope[:, axis]
        mean = v.mean()
        energy = np.mean(v * v)
        std = np.sqrt(max(0.0, energy - mean * mean))
        peak_index = int(np.argmax(np.abs(v)))  # primer instante del pico, como en C#
        integral = float(np.sum((v[1:] + v[:-1]) / 2.0 * dt))
        abs_integrals.append(float(np.sum((np.abs(v[1:]) + np.abs(v[:-1])) / 2.0 * dt)))
        features += [mean, std, v.min(), v.max(), abs(v[peak_index]), t[peak_index] / duration_ms,
                     integral, _zero_crossings(v), energy]
    magnitude = np.sqrt(np.sum(gyroscope[:, 1:] ** 2, axis=1))
    total = sum(abs_integrals)
    features += [magnitude.max(), magnitude.mean(), duration_ms / 1000.0,
                 abs_integrals[0] / total if total > 0 else 0.0]
    return np.asarray(features, dtype=np.float64)
