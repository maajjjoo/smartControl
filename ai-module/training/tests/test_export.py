"""Exportación al formato v1 con modelos SINTÉTICOS."""
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

from smartcontrol_training import export, model, parity
from tests.test_model import synthetic_blobs


class ExportTests(unittest.TestCase):
    def setUp(self):
        x, y = synthetic_blobs()
        self.model = model.train_logistic(x, y, epochs=50)

    def test_synthetic_data_becomes_synthetic_fixture(self):
        data = export.to_json(self.model, "synthetic", 0, 180, None, "prueba-1")
        self.assertEqual("synthetic-fixture", data["dataSource"])
        self.assertEqual(["TWIST_OUT", "TWIST_IN", "NO_GESTURE"], data["classes"])
        self.assertEqual("gyro-v1", data["featureSet"])
        self.assertEqual(0.80, data["decisionThreshold"])
        self.assertEqual(3, len(data["layers"][0]["weights"]))

    def test_physical_watch_requires_dataset_metadata(self):
        with self.assertRaises(ValueError):
            export.to_json(self.model, "physical-watch", 0, 10, "a" * 64, "v1")
        with self.assertRaises(ValueError):
            export.to_json(self.model, "physical-watch", 3, 10, None, "v1")
        with self.assertRaises(ValueError):
            export.to_json(self.model, "emulator", 0, 10, None, "v1")

    def test_threshold_never_below_minimum(self):
        with self.assertRaises(ValueError):
            export.to_json(self.model, "synthetic", 0, 1, None, "v1", threshold=0.79)
        self.assertEqual(0.9, export.to_json(self.model, "synthetic", 0, 1, None, "v1", threshold=0.9)["decisionThreshold"])

    def test_rejects_non_finite_weights(self):
        broken = model.TrainedModel(self.model.mean, self.model.std,
                                    (model.Layer(self.model.layers[0].weights * np.nan, self.model.layers[0].bias, "softmax"),),
                                    "logistic")
        with self.assertRaises(ValueError):
            export.to_json(broken, "synthetic", 0, 1, None, "v1")

    def test_writes_model_and_checksum(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = export.write_model(Path(tmp) / "out" / "model.json", export.to_json(self.model, "synthetic", 0, 1, None, "v1"))
            self.assertTrue(export.verify_checksum(path))
            sidecar = Path(str(path) + ".sha256").read_text(encoding="ascii")
            self.assertTrue(sidecar.endswith("  model.json\n"))
            path.write_bytes(path.read_bytes().replace(b'"v1"', b'"v2"'))
            self.assertFalse(export.verify_checksum(path))

    def test_model_parity_fixture_is_consistent(self):
        # Se recalcula con el modelo guardado (no se reentrena): el resultado no depende del equipo.
        self.assertTrue(export.verify_checksum(parity.MODEL_FIXTURE))
        stored = json.loads(parity.MODEL_FIXTURE.read_text(encoding="utf-8"))
        self.assertEqual("synthetic-fixture", stored["dataSource"])
        layers = tuple(model.Layer(np.array(l["weights"]), np.array(l["bias"]), l["activation"]) for l in stored["layers"])
        loaded = model.TrainedModel(np.array(stored["normalization"]["mean"]), np.array(stored["normalization"]["std"]), layers, "mlp")
        cases = json.loads(parity.FIXTURE.read_text(encoding="utf-8"))["cases"]
        expected = json.loads(parity.EXPECTED_FIXTURE.read_text(encoding="utf-8"))
        self.assertEqual("synthetic", expected["dataSource"])
        probabilities = model.predict_proba(loaded, np.array([c["features"] for c in cases]))
        np.testing.assert_allclose(probabilities, np.array(expected["probabilities"]), rtol=0, atol=1e-12)


if __name__ == "__main__":
    unittest.main()
