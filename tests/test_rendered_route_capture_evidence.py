"""Pure portable capture tamper regressions; never launch an engine/player."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest import mock
import zlib


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("rendered_route_capture_evidence",
                                           ROOT / "tools/rendered_route_capture_evidence.py")
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


def png(width=960, height=540):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    # Deliberately uniform: structural decoding is not a human-quality test.
    rows = (b"\0" + bytes((32, 48, 64)) * width) * height
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def trace(secret=False):
    states = [([], False), (["W"], False), (["A"], False), (["D"], False),
              (["E"], False), ([], True), ([], False)]
    return {"seconds": 120.0, "secret": secret, "health": 197,
            "progression": evidence.expected_progression(secret),
            "inputs": [{"time": (0 if i < 2 else i * 20.01), "keys": keys, "attack": attack}
                       for i, (keys, attack) in enumerate(states)]}


def framing(test, objective):
    names = ["head", "torso", "right attack arm"]
    if objective != "MinibossDefeated":
        names.append("left attack arm")
    return dict(scope=evidence.FRAMING_SCOPE, encounter=evidence.ENCOUNTERS[objective], test=test,
                geometryFailure="", playerPosition=dict(x=0, y=.02, z=15),
                enemyPosition=dict(x=0, y=.05, z=16.6), cameraPosition=dict(x=-1.4, y=3.4, z=12.7),
                cameraRotation=dict(x=0, y=0, z=0, w=1), cameraDistance=3, cameraHeight=4,
                cameraPivotHeight=1, lookHeightOffset=1.5, shoulderOffset=-1.8, shoulderAimFraction=.5,
                fieldOfView=75, aspect=16 / 9, measurementMilliseconds=17.1, columns=48, rows=64,
                sampledRays=3072, playerMeshes=37, enemyMeshes=43,
                regions=[dict(name=name, silhouetteSamples=100, visibleSamples=75,
                              playerBlockedSamples=20, outsideViewSamples=5, visibleFraction=.75) for name in names])


class RenderedRouteCaptureEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name)
        self.captures = self.output / "captures"
        self.captures.mkdir()
        image = png()
        for kind, test in evidence.CASES.items():
            names = evidence.expected_files(kind)
            for name in names["png"]:
                (self.captures / name).write_bytes(image)
            for name, objective in zip(names["framing"], evidence.ENCOUNTERS):
                self.write(name, framing(test, objective))
            self.write(names["trace"], trace(kind == "secret"))
        self.main = evidence.expected_files("main")

    def write(self, name, value):
        (self.captures / name).write_text(json.dumps(value), encoding="utf-8")

    def test_complete_exact_sets_are_decoded_and_sha_bound_without_quality_claim(self):
        result = evidence.validate_captures(self.output)
        self.assertEqual((result["file_count"], result["png_count"], result["framing_count"], result["trace_count"]),
                         (31, 23, 6, 2))
        self.assertEqual([case["png_count"] for case in result["cases"]], [11, 12])
        self.assertEqual(len({entry["path"] for entry in result["files"]}), 31)
        for entry in result["files"]:
            raw = (self.output / entry["path"]).read_bytes()
            self.assertEqual(entry["sha256"], hashlib.sha256(raw).hexdigest())
            self.assertEqual(entry["bytes"], len(raw))
        self.assertEqual(sum(entry.get("PNG", {}).get("decoded", False) for entry in result["files"]), 23)
        self.assertIn("No ordinary rendering", result["limits"])

    def test_tied_inputs_and_slightly_post_completion_final_release_are_valid(self):
        value = trace()
        self.assertEqual(value["inputs"][0]["time"], value["inputs"][1]["time"])
        self.assertGreater(value["inputs"][-1]["time"], value["seconds"])
        self.assertTrue(evidence.validate_trace(value, False)["final_input_released"])

    def test_missing_or_extra_or_wrong_case_artifact_is_rejected(self):
        name = self.main["png"][0]
        original = (self.captures / name).read_bytes()
        (self.captures / name).unlink()
        with self.assertRaisesRegex(ValueError, "Missing"):
            evidence.validate_captures(self.output)
        (self.captures / name).write_bytes(original)
        (self.captures / "unexpected.png").write_bytes(original)
        with self.assertRaisesRegex(ValueError, "Unexpected"):
            evidence.validate_captures(self.output)
        (self.captures / "unexpected.png").unlink()
        (self.captures / name).rename(self.captures / name.replace("CastleMain", "castleMain"))
        with self.assertRaisesRegex(ValueError, "Unexpected"):
            evidence.validate_captures(self.output)

    def test_directory_in_place_of_expected_file_is_rejected(self):
        path = self.captures / self.main["png"][0]
        path.unlink()
        path.mkdir()
        with self.assertRaisesRegex(ValueError, "Nonregular"):
            evidence.validate_captures(self.output)

    def test_linked_root_is_rejected_without_opening_any_artifact(self):
        with mock.patch.object(Path, "is_symlink", return_value=True), \
                mock.patch.object(Path, "open", side_effect=AssertionError("must not open linked evidence")):
            with self.assertRaisesRegex(ValueError, "linked/escaped"):
                evidence.validate_captures(self.output)

    def test_png_crc_dimensions_and_trailing_payload_tampering_are_rejected(self):
        path = self.captures / self.main["png"][0]
        good = path.read_bytes()
        corrupt = bytearray(good)
        corrupt[-1] ^= 1
        for raw in (bytes(corrupt), png(959, 540), good + b"trailing"):
            with self.subTest(size=len(raw)):
                path.write_bytes(raw)
                with self.assertRaises(ValueError):
                    evidence.validate_captures(self.output)

    def test_file_and_total_byte_bounds_precede_png_decode(self):
        for constant in ("MAX_FILE_BYTES", "MAX_TOTAL_BYTES"):
            with self.subTest(constant=constant), mock.patch.object(evidence, constant, 1), \
                    mock.patch.object(evidence, "decode_png", side_effect=AssertionError("oversize must fail first")):
                with self.assertRaisesRegex(ValueError, "excessive|size exceeds"):
                    evidence.validate_captures(self.output)

    def test_changed_read_identity_is_rejected(self):
        real_fstat = evidence.os.fstat
        with mock.patch.object(evidence.os, "fstat") as observed:
            actual = None
            def changed(handle):
                nonlocal actual
                actual = real_fstat(handle)
                return mock.Mock(st_dev=actual.st_dev, st_ino=actual.st_ino + 1, st_size=actual.st_size,
                                 st_mtime_ns=actual.st_mtime_ns, st_ctime_ns=actual.st_ctime_ns)
            observed.side_effect = changed
            with self.assertRaisesRegex(ValueError, "changed before read"):
                evidence.validate_captures(self.output)

    def test_duplicate_and_nonfinite_json_are_rejected(self):
        for raw in (b'{"seconds":1,"seconds":2}', b'{"time":NaN}', b'{"time":Infinity}'):
            with self.subTest(raw=raw), self.assertRaisesRegex(ValueError, "Duplicate|Nonfinite"):
                evidence.parse_json(raw)

    def test_trace_missing_foreign_or_optional_progression_is_rejected(self):
        for transform in (lambda x: x["progression"].pop(),
                          lambda x: x["progression"].reverse(),
                          lambda x: x.update(progression=evidence.expected_progression(True)),
                          lambda x: x.update(secret=True)):
            value = trace()
            transform(value)
            with self.assertRaises(ValueError):
                evidence.validate_trace(value, False)

    def test_secret_return_cannot_skip_library_or_final_fight(self):
        for index in (13, 14, 17):
            value = trace(True)
            value["progression"].pop(index)
            with self.assertRaisesRegex(ValueError, "progression"):
                evidence.validate_trace(value, True)

    def test_trace_input_types_keys_order_controls_and_release_are_checked(self):
        mutations = [lambda x: x["inputs"][2].update(time=-1),
                     lambda x: x["inputs"][3].update(time=.1),
                     lambda x: x["inputs"][2].update(time=True),
                     lambda x: x["inputs"][2].update(attack=1),
                     lambda x: x["inputs"][2].update(keys=["R"]),
                     lambda x: x["inputs"][2].update(keys=["A", "A"]),
                     lambda x: x["inputs"][-1].update(keys=["W"]),
                     lambda x: x["inputs"].pop(4),
                     lambda x: x["inputs"].insert(2, copy.deepcopy(x["inputs"][1]))]
        for mutate in mutations:
            value = trace()
            mutate(value)
            with self.subTest(mutate=mutate), self.assertRaises(ValueError):
                evidence.validate_trace(value, False)

    def test_trace_counters_nonfinite_and_bounds_are_checked(self):
        for key, wrong in (("health", 0), ("health", 201), ("health", True), ("seconds", float("nan")),
                           ("seconds", 0), ("seconds", True), ("seconds", 10 ** 1000)):
            value = trace()
            value[key] = wrong
            with self.subTest(key=key, wrong_type=type(wrong)), self.assertRaises(ValueError):
                evidence.validate_trace(value, False)
        with mock.patch.object(evidence, "MAX_INPUTS", 2), self.assertRaisesRegex(ValueError, "excessive"):
            evidence.validate_trace(trace(), False)

    def test_framing_identity_geometry_and_nested_schema_are_checked(self):
        test = evidence.CASES["main"]
        objective = "FirstEnemyDefeated"
        mutations = [lambda x: x.update(test=evidence.CASES["secret"]),
                     lambda x: x.update(encounter="Corrupted Guard"),
                     lambda x: x.update(scope="general visual quality"),
                     lambda x: x.update(geometryFailure="near-plane failure"),
                     lambda x: x["playerPosition"].update(extra=0),
                     lambda x: x["cameraRotation"].update(w=2),
                     lambda x: x.update(sampledRays=True),
                     lambda x: x.update(columns=47),
                     lambda x: x.update(aspect=4 / 3),
                     lambda x: x["regions"].pop(),
                     lambda x: x["regions"][0].update(name="torso")]
        for mutate in mutations:
            value = framing(test, objective)
            mutate(value)
            with self.subTest(mutate=mutate), self.assertRaises(ValueError):
                evidence.validate_framing(value, test, objective)

    def test_framing_sample_floor_fraction_and_accounting_are_checked(self):
        test = evidence.CASES["main"]
        objective = "MinibossDefeated"
        mutations = [dict(silhouetteSamples=11, visibleSamples=11, playerBlockedSamples=0,
                          outsideViewSamples=0, visibleFraction=1),
                     dict(visibleFraction=.49), dict(visibleFraction=.8), dict(visibleSamples=76),
                     dict(silhouetteSamples=True), dict(outsideViewSamples=-1)]
        for wrong in mutations:
            value = framing(test, objective)
            value["regions"][0].update(wrong)
            with self.subTest(wrong=wrong), self.assertRaises(ValueError):
                evidence.validate_framing(value, test, objective)


if __name__ == "__main__":
    unittest.main()
