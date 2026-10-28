"""Evaluación con salidas escritas a mano y con datos SINTÉTICOS."""
import unittest

import numpy as np

from smartcontrol_training import evaluate
from smartcontrol_training.model import CLASSES
from tests.test_model import synthetic_blobs


class DecideTests(unittest.TestCase):
    def test_same_rules_as_csharp(self):
        rows = np.array([
            [0.90, 0.05, 0.05],      # gesto claro
            [0.80, 0.10, 0.10],      # umbral exacto: se actúa
            [0.7999, 0.1001, 0.10],  # por debajo: no se actúa
            [0.45, 0.10, 0.45],      # empate con NO_GESTURE: no se actúa
            [0.02, 0.97, 0.01],
            [0.01, 0.01, 0.98],
        ])
        self.assertEqual(["TWIST_OUT", "TWIST_OUT", "NONE", "NONE", "TWIST_IN", "NONE"], evaluate.decide(rows))
        self.assertEqual(["NONE"], evaluate.decide(np.array([[0.90, 0.05, 0.05]]), threshold=0.95))
        self.assertEqual(["NONE"], evaluate.decide(np.array([[0.70, 0.20, 0.10]]), threshold=0.5), "nunca por debajo de 0.80")


class MetricsTests(unittest.TestCase):
    def test_hand_computed_metrics(self):
        labels = ["TWIST_OUT", "TWIST_OUT", "TWIST_IN", "TWIST_IN", "NO_GESTURE", "NO_GESTURE", "NO_GESTURE", "NO_GESTURE"]
        outcomes = ["TWIST_OUT", "NONE", "TWIST_IN", "TWIST_OUT", "NONE", "NONE", "NONE", "TWIST_IN"]
        m = evaluate.metrics(labels, outcomes)
        self.assertEqual([[1, 0, 1], [1, 1, 0], [0, 1, 3]], m["confusion_matrix"]["counts"])
        self.assertAlmostEqual(5 / 8, m["accuracy"])
        self.assertAlmostEqual(0.5, m["per_class"]["TWIST_OUT"]["precision"])  # 1 de 2 predichos
        self.assertAlmostEqual(0.5, m["per_class"]["TWIST_OUT"]["recall"])
        self.assertAlmostEqual(0.75, m["per_class"]["NO_GESTURE"]["recall"])
        self.assertAlmostEqual(1 / 4, m["rejection_rate"])       # 1 gesto de 4 sin acción
        self.assertAlmostEqual(1 / 4, m["false_positive_rate"])  # 1 negativo de 4 con acción

    def test_rules_outcomes_from_detector(self):
        self.assertEqual(["TWIST_OUT", "NONE", "TWIST_IN", "NONE"],
                         evaluate.rules_outcomes(["TWIST_OUT", "UNCLEAR", "TWIST_IN", None]))


class LeaveOneOutTests(unittest.TestCase):
    def test_never_trains_with_held_out_participant(self):
        x, y = synthetic_blobs(per_class=10)
        participants = np.array(["P01", "P02", "P03"] * 10)
        samples = evaluate.Samples(x, y, participants, [None] * len(y))
        seen = []

        def trainer(train_x, train_y):
            seen.append({tuple(row) for row in train_x})
            from smartcontrol_training.model import train_logistic
            return train_logistic(train_x, train_y, epochs=20)

        outcomes = evaluate.leave_one_participant_out(samples, trainer)
        self.assertEqual(3, len(seen))
        for held_out, train_rows in zip(["P01", "P02", "P03"], seen):
            held_rows = {tuple(row) for row in x[participants == held_out]}
            self.assertFalse(held_rows & train_rows, f"fuga de {held_out}")
        self.assertEqual(len(y), len(outcomes))
        self.assertTrue(all(o in evaluate.OUTCOMES for o in outcomes))

    def test_requires_two_participants(self):
        x, y = synthetic_blobs(per_class=5)
        samples = evaluate.Samples(x, y, np.array(["P01"] * len(y)), [None] * len(y))
        with self.assertRaises(ValueError):
            evaluate.leave_one_participant_out(samples, lambda a, b: None)

    def test_class_names(self):
        self.assertEqual(("TWIST_OUT", "TWIST_IN", "NO_GESTURE"), CLASSES)


if __name__ == "__main__":
    unittest.main()
