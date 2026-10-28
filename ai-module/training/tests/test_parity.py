"""El fixture de paridad debe estar al día con el cálculo de Python (si cambia, regenerarlo)."""
import json
import unittest

from smartcontrol_training import parity
from smartcontrol_training.recording import MAX_GYROSCOPE_ABS, parse_series


class ParityFixtureTests(unittest.TestCase):
    def test_fixture_is_up_to_date(self):
        stored = json.loads(parity.FIXTURE.read_text(encoding="utf-8"))
        self.assertEqual(parity.build(), stored, "regenerar con: python -m smartcontrol_training.parity")

    def test_fixture_windows_are_valid_and_marked_synthetic(self):
        stored = json.loads(parity.FIXTURE.read_text(encoding="utf-8"))
        self.assertEqual("synthetic", stored["dataSource"])
        self.assertGreaterEqual(len(stored["cases"]), 40)
        for case in stored["cases"]:
            parse_series(case["gyroscope"], MAX_GYROSCOPE_ABS, 0, "gyroscope")


if __name__ == "__main__":
    unittest.main()
