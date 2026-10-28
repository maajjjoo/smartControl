"""Evaluación (docs/phase2-ai-evaluation.md §8).

- Validación dejando fuera a un participante cada vez: nunca hay datos de la misma persona en
  entrenamiento y prueba.
- La decisión es la misma que GestureDecision en C#: gana la clase más probable (en empate,
  NO_GESTURE) y solo se actúa si supera el umbral. "NONE" significa que no se actúa.
- El reconocedor por reglas del reloj se evalúa con el campo detectorEvent de las mismas ventanas.

Con datos sintéticos el informe lleva un aviso: no es precisión real.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from smartcontrol_training import features
from smartcontrol_training.model import CLASSES, predict_proba
from smartcontrol_training.recording import Dataset

OUTCOMES = ("TWIST_OUT", "TWIST_IN", "NONE")
SYNTHETIC_WARNING = "DATOS SINTÉTICOS: estas cifras solo comprueban el código; no son precisión real."


@dataclass(frozen=True)
class Samples:
    features: np.ndarray
    labels: np.ndarray        # índice en CLASSES
    participants: np.ndarray  # identificador anónimo por ventana
    detector: list            # detectorEvent de cada ventana (o None)


def samples_from(dataset: Dataset) -> Samples:
    rows, labels, groups, detector = [], [], [], []
    for recording, window in dataset.windows:
        rows.append(features.extract(window.gyroscope))
        labels.append(CLASSES.index(window.label))
        groups.append(recording.session.participant_id)
        detector.append(window.detector_event)
    return Samples(np.array(rows), np.array(labels), np.array(groups), detector)


def decide(probabilities: np.ndarray, threshold: float = 0.80) -> list[str]:
    """Una salida de OUTCOMES por fila, con las mismas reglas que GestureDecision.Decide."""
    threshold = max(threshold, 0.80)
    outcomes = []
    for p_out, p_in, p_none in np.atleast_2d(probabilities):
        # Como en C#: TWIST_IN solo gana a TWIST_OUT si es estrictamente mayor.
        best, confidence = ("TWIST_OUT", p_out) if p_out >= p_in else ("TWIST_IN", p_in)
        if p_none >= confidence or not confidence >= threshold:
            outcomes.append("NONE")
        else:
            outcomes.append(best)
    return outcomes


def rules_outcomes(detector_events: list) -> list[str]:
    """Salida del reconocedor por reglas: solo actúa con TWIST_OUT o TWIST_IN."""
    return [event if event in ("TWIST_OUT", "TWIST_IN") else "NONE" for event in detector_events]


def metrics(labels: list[str], outcomes: list[str]) -> dict:
    """Métricas por clase sobre etiquetas verdaderas (CLASSES) y salidas (OUTCOMES)."""
    if len(labels) != len(outcomes) or not labels:
        raise ValueError("etiquetas y salidas no encajan")
    truth = ["NONE" if label == "NO_GESTURE" else label for label in labels]
    matrix = [[sum(1 for t, o in zip(truth, outcomes) if t == row and o == col) for col in OUTCOMES] for row in OUTCOMES]
    per_class = {}
    for i, name in enumerate(OUTCOMES):
        predicted = sum(matrix[r][i] for r in range(3))
        actual = sum(matrix[i])
        per_class[CLASSES[i]] = {
            "precision": matrix[i][i] / predicted if predicted else None,
            "recall": matrix[i][i] / actual if actual else None,
            "support": actual,
        }
    gestures = [o for t, o in zip(truth, outcomes) if t != "NONE"]
    negatives = [o for t, o in zip(truth, outcomes) if t == "NONE"]
    return {
        "windows": len(labels),
        "accuracy": sum(1 for t, o in zip(truth, outcomes) if t == o) / len(labels),
        "per_class": per_class,
        "confusion_matrix": {"rows_true": list(OUTCOMES), "cols_outcome": list(OUTCOMES), "counts": matrix},
        # Gestos reales en los que no se actuó.
        "rejection_rate": gestures.count("NONE") / len(gestures) if gestures else None,
        # Ventanas sin gesto en las que se habría actuado (por ventana; por hora requiere grabación continua).
        "false_positive_rate": sum(1 for o in negatives if o != "NONE") / len(negatives) if negatives else None,
    }


def leave_one_participant_out(samples: Samples, trainer, threshold: float = 0.80) -> list[str]:
    """Salida para cada ventana con un modelo entrenado sin su participante. `trainer(x, y)` devuelve un modelo."""
    participants = sorted(set(samples.participants.tolist()))
    if len(participants) < 2:
        raise ValueError("hacen falta al menos 2 participantes para validar sin fugas")
    outcomes = [None] * len(samples.labels)
    for held_out in participants:
        test = samples.participants == held_out
        model = trainer(samples.features[~test], samples.labels[~test])
        for index, outcome in zip(np.flatnonzero(test), decide(predict_proba(model, samples.features[test]), threshold)):
            outcomes[index] = outcome
    return outcomes


def report(dataset: Dataset, trainer, threshold: float = 0.80) -> dict:
    samples = samples_from(dataset)
    labels = [CLASSES[i] for i in samples.labels]
    model_metrics = metrics(labels, leave_one_participant_out(samples, trainer, threshold))
    with_detector = [i for i, event in enumerate(samples.detector) if event is not None]
    rules = None
    if with_detector:
        rules = metrics([labels[i] for i in with_detector], rules_outcomes([samples.detector[i] for i in with_detector]))
        rules["coverage"] = len(with_detector) / len(labels)
    result = {
        "dataSource": dataset.data_source,
        "datasetSha256": dataset.sha256,
        "participants": len(dataset.participants),
        "threshold": max(threshold, 0.80),
        "validation": "leave-one-participant-out",
        "model": model_metrics,
        "rules": rules,
    }
    if dataset.data_source != "physical-watch":
        result["warning"] = SYNTHETIC_WARNING
    return result
