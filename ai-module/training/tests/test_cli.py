"""Recorrido completo con datos SINTÉTICOS en una carpeta temporal: generar, evaluar y entrenar."""
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from smartcontrol_training import evaluate, export, synthetic
from smartcontrol_training.__main__ import main
from smartcontrol_training.recording import load_recording


def run(*argv):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = main(list(argv))
    return code, out.getvalue(), err.getvalue()


class CliTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)
        self.data = self.dir / "synthetic"
        synthetic.generate(self.data, participants=3, per_class=8, seed=1)

    def tearDown(self):
        self._tmp.cleanup()

    def test_generated_sessions_are_valid_and_synthetic(self):
        recordings = [load_recording(p) for p in sorted(self.data.glob("*.jsonl"))]
        self.assertEqual(3, len(recordings))
        for recording in recordings:
            self.assertEqual("synthetic", recording.session.data_source)
            self.assertIsNone(recording.session.consent_version)
            self.assertTrue(all(w.detector_event is None for w in recording.windows))

    def test_refuses_to_write_synthetic_into_real_data_folder(self):
        with self.assertRaises(ValueError):
            synthetic.generate(synthetic.REAL_DATA_DIR / "x")
        code, _, err = run("synthetic", "--out", str(synthetic.REAL_DATA_DIR))
        self.assertEqual(2, code)
        self.assertIn("ai-module/data", err)

    def test_synthetic_needs_explicit_permission(self):
        code, _, err = run("evaluate", "--data", str(self.data))
        self.assertEqual(2, code)
        self.assertIn("SYNTHETIC_NOT_ALLOWED", err)

    def test_evaluate_marks_synthetic_report(self):
        report_path = self.dir / "out" / "report.json"
        code, out, err = run("evaluate", "--data", str(self.data), "--allow-synthetic", "--report", str(report_path))
        self.assertEqual(0, code)
        self.assertIn(evaluate.SYNTHETIC_WARNING, err)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        self.assertEqual("synthetic", report["dataSource"])
        self.assertEqual(evaluate.SYNTHETIC_WARNING, report["warning"])
        self.assertEqual("leave-one-participant-out", report["validation"])
        self.assertIsNone(report["rules"], "sin detectorEvent no hay comparación con las reglas")
        self.assertEqual(3 * 3 * 8, report["model"]["windows"])

    def test_train_writes_synthetic_fixture_model(self):
        model_path = self.dir / "out" / "model.json"
        code, out, _ = run("train", "--data", str(self.data), "--allow-synthetic", "--kind", "mlp",
                           "--version", "syn-1", "--out", str(model_path))
        self.assertEqual(0, code)
        self.assertTrue(export.verify_checksum(model_path))
        stored = json.loads(model_path.read_text(encoding="utf-8"))
        self.assertEqual("synthetic-fixture", stored["dataSource"])
        self.assertEqual(3 * 3 * 8, stored["training"]["windows"])
        self.assertEqual(2, len(stored["layers"]))


if __name__ == "__main__":
    unittest.main()
