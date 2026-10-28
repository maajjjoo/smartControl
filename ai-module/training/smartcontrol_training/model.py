"""Entrenamiento con numpy de los modelos admitidos por smartcontrol-gesture-model/v1.

- Regresión logística multinomial (sin capas ocultas).
- MLP con una capa oculta relu.

La pasada hacia delante (`predict_proba`) hace lo mismo que DenseClassifier en C#: normalizar
con media y desviación, capas densas y softmax estable. Todo es determinista con la semilla.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

CLASSES = ("TWIST_OUT", "TWIST_IN", "NO_GESTURE")


@dataclass(frozen=True)
class Layer:
    weights: np.ndarray  # (salidas, entradas)
    bias: np.ndarray     # (salidas,)
    activation: str      # "relu", "tanh" o "softmax"


@dataclass(frozen=True)
class TrainedModel:
    mean: np.ndarray
    std: np.ndarray
    layers: tuple[Layer, ...]
    kind: str  # "logistic" o "mlp"


def normalization(features: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Media y desviación por característica. Una característica constante usa desviación 1 (C# exige > 0)."""
    mean = features.mean(axis=0)
    std = features.std(axis=0)
    std = np.where(std > 1e-12, std, 1.0)
    return mean, std


def _softmax(logits: np.ndarray) -> np.ndarray:
    shifted = logits - logits.max(axis=1, keepdims=True)
    exp = np.exp(shifted)
    return exp / exp.sum(axis=1, keepdims=True)


def _forward(layers, values: np.ndarray) -> np.ndarray:
    for layer in layers:
        values = values @ layer.weights.T + layer.bias
        if layer.activation == "relu":
            values = np.maximum(values, 0.0)
        elif layer.activation == "tanh":
            values = np.tanh(values)
        elif layer.activation == "softmax":
            values = _softmax(values)
        else:
            raise ValueError(f"activación desconocida: {layer.activation}")
    return values


def predict_proba(model: TrainedModel, features: np.ndarray) -> np.ndarray:
    """Probabilidades (n, 3) en el orden de CLASSES."""
    features = np.atleast_2d(np.asarray(features, dtype=np.float64))
    return _forward(model.layers, (features - model.mean) / model.std)


def _one_hot(labels: np.ndarray) -> np.ndarray:
    return np.eye(len(CLASSES))[labels]


def _check(features: np.ndarray, labels: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    features = np.asarray(features, dtype=np.float64)
    labels = np.asarray(labels, dtype=np.int64)
    if features.ndim != 2 or len(features) != len(labels) or len(labels) == 0:
        raise ValueError("características y etiquetas no encajan")
    if not np.all(np.isfinite(features)):
        raise ValueError("características no finitas")
    if labels.min() < 0 or labels.max() >= len(CLASSES):
        raise ValueError("etiqueta fuera de rango")
    return features, labels


class _Adam:
    def __init__(self, params, rate: float):
        self.params, self.rate = params, rate
        self.m = [np.zeros_like(p) for p in params]
        self.v = [np.zeros_like(p) for p in params]
        self.t = 0

    def step(self, grads):
        self.t += 1
        for p, g, m, v in zip(self.params, grads, self.m, self.v):
            m *= 0.9
            m += 0.1 * g
            v *= 0.999
            v += 0.001 * g * g
            p -= self.rate * (m / (1 - 0.9 ** self.t)) / (np.sqrt(v / (1 - 0.999 ** self.t)) + 1e-8)


def train_logistic(features, labels, l2: float = 1e-3, epochs: int = 2000, rate: float = 0.05, seed: int = 0) -> TrainedModel:
    features, labels = _check(features, labels)
    mean, std = normalization(features)
    x = (features - mean) / std
    target = _one_hot(labels)
    rng = np.random.default_rng(seed)
    w = rng.normal(0.0, 0.01, size=(len(CLASSES), x.shape[1]))
    b = np.zeros(len(CLASSES))
    adam = _Adam([w, b], rate)
    for _ in range(epochs):
        error = (_softmax(x @ w.T + b) - target) / len(x)
        adam.step([error.T @ x + l2 * w, error.sum(axis=0)])
    return TrainedModel(mean, std, (Layer(w.copy(), b.copy(), "softmax"),), "logistic")


def train_mlp(features, labels, hidden: int = 16, l2: float = 1e-3, epochs: int = 3000, rate: float = 0.01, seed: int = 0) -> TrainedModel:
    features, labels = _check(features, labels)
    mean, std = normalization(features)
    x = (features - mean) / std
    target = _one_hot(labels)
    rng = np.random.default_rng(seed)
    w1 = rng.normal(0.0, np.sqrt(2.0 / x.shape[1]), size=(hidden, x.shape[1]))
    b1 = np.zeros(hidden)
    w2 = rng.normal(0.0, np.sqrt(2.0 / hidden), size=(len(CLASSES), hidden))
    b2 = np.zeros(len(CLASSES))
    adam = _Adam([w1, b1, w2, b2], rate)
    for _ in range(epochs):
        pre = x @ w1.T + b1
        h = np.maximum(pre, 0.0)
        error = (_softmax(h @ w2.T + b2) - target) / len(x)
        back = (error @ w2) * (pre > 0)
        adam.step([back.T @ x + l2 * w1, back.sum(axis=0), error.T @ h + l2 * w2, error.sum(axis=0)])
    layers = (Layer(w1.copy(), b1.copy(), "relu"), Layer(w2.copy(), b2.copy(), "softmax"))
    return TrainedModel(mean, std, layers, "mlp")


def cross_entropy(model: TrainedModel, features, labels) -> float:
    features, labels = _check(features, labels)
    probabilities = predict_proba(model, features)
    return float(-np.mean(np.log(np.clip(probabilities[np.arange(len(labels)), labels], 1e-15, 1.0))))
