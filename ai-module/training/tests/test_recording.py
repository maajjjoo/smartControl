"""Lectura de grabaciones sensor-data/v1 con archivos SINTÉTICOS en carpetas temporales."""
import tempfile
import unittest
from pathlib import Path

from smartcontrol_training.recording import RecordingError, load_dataset, load_recording
from tests.helpers import series, synthetic_header, window, write_jsonl


class RecordingTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def write(self, name="s.jsonl", header=None, windows=None) -> Path:
        return write_jsonl(self.dir / name, header or synthetic_header(), windows if windows is not None else [window()])

    def code(self, path) -> str:
        with self.assertRaises(RecordingError) as raised:
            load_recording(path)
        return raised.exception.code

    def test_loads_valid_synthetic_session(self):
        recording = load_recording(self.write(windows=[window(1), window(2, "NO_GESTURE", activity="walking")]))
        self.assertEqual("synthetic", recording.session.data_source)
        self.assertEqual(2, len(recording.windows))
        self.assertEqual((8, 4), recording.windows[0].gyroscope.shape)
        self.assertEqual(64, len(recording.sha256))

    def test_physical_watch_requires_consent_and_watch_recorder(self):
        real = synthetic_header(dataSource="physical-watch", recorder="watch-debug-recorder/1")
        self.assertEqual("MISSING_CONSENT", self.code(self.write(header=real)))
        real["consentVersion"] = "consent/v1"
        self.assertEqual("physical-watch", load_recording(self.write(header=real)).session.data_source)
        self.assertEqual("INVALID_RECORDER", self.code(self.write(header=dict(real, recorder="synthetic-generator/1"))))
        self.assertEqual("INVALID_CONSENT", self.code(self.write(header=synthetic_header(consentVersion="consent/v1"))))

    def test_strict_header(self):
        self.assertEqual("UNSUPPORTED_FORMAT", self.code(self.write(header=synthetic_header(format="sensor-data/v2"))))
        self.assertEqual("UNKNOWN_FIELD", self.code(self.write(header=synthetic_header(name="Ana"))))
        self.assertEqual("INVALID_ID", self.code(self.write(header=synthetic_header(participantId="Ana López"))))
        self.assertEqual("INVALID_DATE", self.code(self.write(header=synthetic_header(createdDate="2026-10-28T10:00"))))
        self.assertEqual("INVALID_SAMPLING", self.code(self.write(header=synthetic_header(samplingHz=500))))
        self.assertEqual("INVALID_CROWN", self.code(self.write(header=synthetic_header(crown="left"))))
        header = synthetic_header()
        del header["wrist"]
        self.assertEqual("MISSING_FIELD", self.code(self.write(header=header)))

    def test_rejects_malformed_json_duplicates_and_nan(self):
        path = self.dir / "bad.jsonl"
        path.write_text('{"format": "sensor-data/v1", "format": "x"}\n', encoding="utf-8")
        self.assertEqual("DUPLICATE_FIELD", self.code(path))
        path.write_text("{no es json\n", encoding="utf-8")
        self.assertEqual("MALFORMED_JSON", self.code(path))
        good = self.write().read_text(encoding="utf-8")
        self.assertIn("[60, 3.0,", good)
        path.write_text(good.replace("[60, 3.0,", "[60, NaN,"), encoding="utf-8")
        self.assertEqual("NON_FINITE_VALUE", self.code(path))
        path.write_text(good.replace("[60, 3.0,", "[60, Infinity,"), encoding="utf-8")
        self.assertEqual("NON_FINITE_VALUE", self.code(path))
        path.write_text(good.replace("\n", "\n\n", 1), encoding="utf-8")
        self.assertEqual("MALFORMED_JSON", self.code(path))

    def test_series_rules_match_sensor_window(self):
        self.assertEqual("TOO_FEW_SAMPLES", self.code(self.write(windows=[window(gyroscope=series([0] * 7))])))
        self.assertEqual("TOO_MANY_SAMPLES", self.code(self.write(windows=[window(gyroscope=series([0] * 257, 5))])))
        self.assertEqual("WINDOW_TOO_LONG", self.code(self.write(windows=[window(gyroscope=series([0] * 102))])))
        load_recording(self.write(windows=[window(gyroscope=series([0] * 101))]))  # 2000 ms exactos
        shifted = series([0] * 8)
        shifted[0][0] = 5
        self.assertEqual("FIRST_OFFSET_NOT_ZERO", self.code(self.write(windows=[window(gyroscope=shifted)])))
        repeated = series([0] * 8)
        repeated[3][0] = repeated[2][0]
        self.assertEqual("NON_INCREASING_TIME", self.code(self.write(windows=[window(gyroscope=repeated)])))
        gap = series([0] * 8)
        gap[7][0] = gap[6][0] + 101
        self.assertEqual("GAP_TOO_LARGE", self.code(self.write(windows=[window(gyroscope=gap)])))
        self.assertEqual("VALUE_OUT_OF_RANGE", self.code(self.write(windows=[window(gyroscope=series([35.001] * 8))])))
        load_recording(self.write(windows=[window(gyroscope=series([-35.0] * 8))]))
        accel = [[i * 20, 0.0, 0.0, 161.0] for i in range(8)]
        self.assertEqual("VALUE_OUT_OF_RANGE", self.code(self.write(windows=[window(accelerometer=accel)])))

    def test_window_fields(self):
        self.assertEqual("INVALID_LABEL", self.code(self.write(windows=[window(label="SLIDE_NEXT")])))
        self.assertEqual("INVALID_ACTIVITY", self.code(self.write(windows=[window(activity="walking")])))
        self.assertEqual("INVALID_WINDOW_ID", self.code(self.write(windows=[window(2), window(2)])))
        self.assertEqual("INVALID_DETECTOR_EVENT", self.code(self.write(windows=[window(detectorEvent="SLIDE_NEXT")])))
        self.assertEqual("NO_WINDOWS", self.code(self.write(windows=[])))

    def test_dataset_never_mixes_sources(self):
        synthetic = self.write("a.jsonl")
        with self.assertRaises(RecordingError) as raised:
            load_dataset([synthetic])
        self.assertEqual("SYNTHETIC_NOT_ALLOWED", raised.exception.code)

        real = synthetic_header(dataSource="physical-watch", recorder="watch-debug-recorder/1", consentVersion="consent/v1")
        mixed = [synthetic, self.write("b.jsonl", header=real)]
        with self.assertRaises(RecordingError) as raised:
            load_dataset(mixed, allow_synthetic=True)
        self.assertEqual("MIXED_DATA_SOURCES", raised.exception.code)

    def test_dataset_hash_does_not_depend_on_order(self):
        a = self.write("a.jsonl", header=synthetic_header(participantId="P01"))
        b = self.write("b.jsonl", header=synthetic_header(participantId="P02"))
        first = load_dataset([a, b], allow_synthetic=True)
        second = load_dataset([b, a], allow_synthetic=True)
        self.assertEqual(first.sha256, second.sha256)
        self.assertEqual(["P01", "P02"], first.participants)
        self.assertEqual("synthetic", first.data_source)


if __name__ == "__main__":
    unittest.main()
