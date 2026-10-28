"""Características gyro-v1 con los mismos valores calculados a mano que las pruebas de C#.
Las series son SINTÉTICAS: comprueban el cálculo, no movimientos reales."""
import math
import unittest

import numpy as np

from smartcontrol_training.features import COUNT, NAMES, extract


def x_series(*values):
    return np.array([[i * 20, v, 0.0, 0.0] for i, v in enumerate(values)])


def named(values):
    return dict(zip(NAMES, values))


class FeatureTests(unittest.TestCase):
    def test_names_match_csharp_order(self):
        self.assertEqual(31, COUNT)
        self.assertEqual(31, len(set(NAMES)))
        self.assertEqual("gyro_x_mean", NAMES[0])
        self.assertEqual("gyro_y_mean", NAMES[9])
        self.assertEqual("gyro_z_energy", NAMES[26])
        self.assertEqual("x_dominance", NAMES[30])

    def test_hand_computed_values(self):
        f = named(extract(x_series(0, 1, 2, 3, 3, 2, 1, 0)))
        expected = {
            "gyro_x_mean": 1.5, "gyro_x_std": math.sqrt(1.25), "gyro_x_min": 0.0, "gyro_x_max": 3.0,
            "gyro_x_peak_abs": 3.0, "gyro_x_peak_position": 60 / 140, "gyro_x_integral": 0.24,
            "gyro_x_zero_crossings": 0.0, "gyro_x_energy": 3.5, "gyro_magnitude_max": 3.0,
            "gyro_magnitude_mean": 1.5, "duration_s": 0.14, "x_dominance": 1.0,
        }
        for name, value in expected.items():
            self.assertAlmostEqual(value, f[name], places=12, msg=name)
        for axis in "yz":
            for name in ("mean", "std", "min", "max", "peak_abs", "peak_position", "integral", "zero_crossings", "energy"):
                self.assertEqual(0.0, f[f"gyro_{axis}_{name}"])

    def test_zero_crossings_with_dead_band(self):
        self.assertEqual(4.0, named(extract(x_series(0.5, -0.5, 0.1, 0.5, -0.1, -0.5, 0.5, 0.5)))["gyro_x_zero_crossings"])
        self.assertEqual(0.0, named(extract(x_series(0.1, -0.1, 0.15, -0.19, 0.1, -0.1, 0.05, -0.05)))["gyro_x_zero_crossings"])

    def test_uneven_steps(self):
        times = [0, 10, 30, 60, 70, 90, 120, 130]
        f = named(extract(np.array([[t, 2.0, -1.0, 0.0] for t in times])))
        self.assertAlmostEqual(0.26, f["gyro_x_integral"], places=12)
        self.assertAlmostEqual(-0.13, f["gyro_y_integral"], places=12)
        self.assertAlmostEqual(0.13, f["duration_s"], places=12)

    def test_magnitude_and_dominance(self):
        f = named(extract(np.array([[i * 20, 3.0, 4.0, 0.0] for i in range(8)])))
        self.assertAlmostEqual(5.0, f["gyro_magnitude_max"], places=12)
        self.assertAlmostEqual(3 / 7, f["x_dominance"], places=12)
        still = named(extract(x_series(*[0] * 8)))
        self.assertEqual(0.0, still["x_dominance"])
        self.assertEqual(0.0, still["gyro_x_peak_position"])
        self.assertTrue(all(math.isfinite(v) for v in still.values()))


if __name__ == "__main__":
    unittest.main()
