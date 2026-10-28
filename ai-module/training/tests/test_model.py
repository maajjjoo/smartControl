"""Entrenamiento con datos SINTÉTICOS (nubes de puntos separables). Comprueban que los algoritmos
funcionan; no dicen nada de la precisión con movimientos reales."""
import unittest

import numpy as np

from smartcontrol_training.model import (
    CLASSES, Layer, TrainedModel, cross_entropy, normalization, predict_proba, train_logistic, train_mlp,
)


def synthetic_blobs(seed=1, per_class=60, features=31):
    rng = np.random.default_rng(seed)
    centers = rng.normal(0.0, 3.0, size=(len(CLASSES), features))
    x = np.concatenate([rng.normal(c, 0.5, size=(per_class, features)) for c in centers])
    y = np.repeat(np.arange(len(CLASSES)), per_class)
    return x, y


class ModelTests(unittest.TestCase):
    def test_normalization_handles_constant_features(self):
        x = np.array([[1.0, 5.0], [3.0, 5.0]])
        mean, std = normalization(x)
        np.testing.assert_allclose(mean, [2.0, 5.0])
        np.testing.assert_allclose(std, [1.0, 1.0])  # la segunda es constante: desviación 1

    def test_forward_matches_softmax_formula(self):
        layer = Layer(np.array([[1.0, 0.0], [0.0, 1.0], [0.0, 0.0]]), np.array([0.0, 0.0, 1.0]), "softmax")
        model = TrainedModel(np.zeros(2), np.ones(2), (layer,), "logistic")
        logits = np.array([2.0, -1.0, 1.0])
        expected = np.exp(logits) / np.exp(logits).sum()
        np.testing.assert_allclose(predict_proba(model, [2.0, -1.0])[0], expected, rtol=0, atol=1e-15)

    def test_logistic_learns_separable_synthetic_data(self):
        x, y = synthetic_blobs()
        untrained = train_logistic(x, y, epochs=1)
        model = train_logistic(x, y)
        self.assertLess(cross_entropy(model, x, y), cross_entropy(untrained, x, y))
        self.assertGreaterEqual(np.mean(predict_proba(model, x).argmax(axis=1) == y), 0.99)
        self.assertEqual((3, 31), model.layers[0].weights.shape)

    def test_mlp_learns_separable_synthetic_data(self):
        x, y = synthetic_blobs(seed=2)
        model = train_mlp(x, y, hidden=8, epochs=800)
        self.assertEqual(["relu", "softmax"], [layer.activation for layer in model.layers])
        self.assertGreaterEqual(np.mean(predict_proba(model, x).argmax(axis=1) == y), 0.99)
        np.testing.assert_allclose(predict_proba(model, x).sum(axis=1), 1.0, atol=1e-12)

    def test_training_is_deterministic(self):
        x, y = synthetic_blobs()
        a = train_mlp(x, y, hidden=4, epochs=50, seed=7)
        b = train_mlp(x, y, hidden=4, epochs=50, seed=7)
        for la, lb in zip(a.layers, b.layers):
            np.testing.assert_array_equal(la.weights, lb.weights)

    def test_rejects_invalid_input(self):
        x, y = synthetic_blobs()
        with self.assertRaises(ValueError):
            train_logistic(x, y[:-1])
        with self.assertRaises(ValueError):
            train_logistic(x, np.full(len(y), 3))
        bad = x.copy()
        bad[0, 0] = np.nan
        with self.assertRaises(ValueError):
            train_logistic(bad, y)


if __name__ == "__main__":
    unittest.main()
