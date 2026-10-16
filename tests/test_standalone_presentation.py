"""Portable native-presentation artifact/qualification regressions; no engine launch."""
import copy
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parents[1]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / (name + ".py"))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


evidence = load("presentation_evidence")
runner = load("run_standalone_presentation")
START, END = "2026-10-14T10:00:00Z", "2026-10-14T10:01:00Z"


def vec(values):
    return dict(zip(("x", "y", "z", "w"), values))


def pose(index=0):
    room, objectives = ("Courtyard", "None") if index < 3 else ("FirstEncounter", "FirstEnemyDefeated") if index == 3 else ("Puzzle", evidence.OBJECTIVES)
    return dict(frame=1 + index * 10, seconds=index + .1, phase="recorded lower-room phase", input="-:",
                room=room, objectives=objectives, position=vec((0, .02, -88 if index == 0 else index)),
                cameraPosition=vec((0, 4, -91)), cameraRotation=vec((0, 0, 0, 1)), yaw=0,
                effectiveRadius=evidence.RADIUS, aspect=16 / 9, hasSafePose=True, cameraEnabled=True, nearbyColliders=[])


def exploration():
    return dict(completed=True, observedFrames=101, unsafeFrames=0, blockedWarnings=0,
                elapsedObservedSeconds=9.0, episodes=[], warnings=[],
                viewport=dict(requestedAspect=16 / 9, observedAspect=16 / 9,
                              requestedRadius=evidence.RADIUS, observedRadius=evidence.RADIUS),
                samples=[pose(0), pose(8)],
                inputs=[dict(frame=1, seconds=0, keys=["W", "A", "D", "E", "Space"], attack=True),
                        dict(frame=81, seconds=8.5, keys=[], attack=False)],
                captures=[dict(name=name, file=name + ".png", width=960, height=540,
                               status="rendered from sampled LateUpdate pose without snap; synthetic test fixture",
                               pose=pose(i)) for i, name in enumerate(evidence.CAPTURES)])


def hud(pose_name):
    positions = evidence.POSES[pose_name]
    regions = []
    for i, name in enumerate(evidence.REGIONS):
        corners = [vec((.1 + .3 * i, .2, .301)) for _ in range(4)]
        regions.append(dict(name=name, viewportBounds=dict(x=.1 + .3 * i, y=.2, width=.1, height=.02),
                            inFrontOfCamera=True, insideViewport=True, changedPixels=64, textChangedPixels=64,
                            worldCorners=copy.deepcopy(corners), viewportCorners=corners))
    return dict(pose=pose_name, scene="Assets/Scenes/ForsakenCastle.unity", renderMode="ScreenSpaceCamera",
                width=1280, height=720, healthText="Health  200 / 200", frozenActorPartsUnchanged=True,
                frozenActorCount=4, frozenActorPartCount=236, textOriginallyEnabled=[True, True],
                visibleFrame=10, hiddenFrame=14, textHiddenFrame=18, nearClip=.3, planeDistance=.301,
                aspect=16 / 9, fieldOfView=75, minimumChangedPixels=32, worldPixelsWithHudDisabled=1280 * 720,
                playerPosition=vec(positions[0]), cameraPosition=vec(positions[1]),
                cameraRotation=vec(positions[2]), recordedCameraRotation=vec(positions[2]),
                assignedCameraRotation=vec(positions[2]), regions=regions)


def png(width, height, patches=False):
    rows = [bytearray([20, 20, 20] * width) for _ in range(height)]
    if patches:
        for i in range(3):
            for y in range(146, 150):
                for x in range(130 + 384 * i, 146 + 384 * i):
                    rows[height - 1 - y][3 * x:3 * x + 3] = bytes((100, 100, 100))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(b"".join(b"\0" + row for row in rows))) + chunk(b"IEND", b""))


def event_rows():
    rows = [dict(kind="run-started", utc=START)]
    for case, begin, finish in zip(sorted(evidence.EXPECTED), (1, 21, 31), (20, 30, 40)):
        for kind, second in (("test-started", begin), ("test-finished", finish)):
            row = dict(kind=kind, full_name=case, is_suite=False, utc=f"2026-10-14T10:00:{second:02}Z")
            if kind == "test-finished":
                row.update(result="Passed", passed=1, failed=0, skipped=0, inconclusive=0)
            rows.append(row)
    return rows + [dict(kind="run-finished", utc=END, result="Passed", passed=3, failed=0, skipped=0, inconclusive=0)]


def encoded_events(rows=None):
    return ("\n".join(json.dumps(row) for row in (event_rows() if rows is None else rows)) + "\n").encode()


def write_output(output):
    intervals = evidence.case_intervals(encoded_events(), evidence.utc(START), evidence.utc(END))

    def save(path, data, case):
        path.write_bytes(data)
        low, high = intervals[case]
        stamp = (low + (high - low) / 2).timestamp()
        os.utime(path, (stamp, stamp))

    root = output / "captures"
    root.mkdir()
    absent, shown, explored = png(1280, 720), png(1280, 720, True), png(960, 540)
    for i, (pose_name, case) in enumerate(evidence.HUD_CASES.items()):
        bundle = root / ("primary-hud-" + str(i) * 32)
        bundle.mkdir()
        save(bundle / (pose_name + "-evidence.json"), json.dumps(hud(pose_name)).encode(), case)
        for suffix in ("hud-visible", "hud-disabled", "hud-text-disabled"):
            save(bundle / (pose_name + "-" + suffix + ".png"), shown if suffix == "hud-visible" else absent, case)
    bundle = root / ("camera-exploration-" + "e" * 32)
    bundle.mkdir()
    save(bundle / "evidence.json", json.dumps(exploration()).encode(), evidence.EXPLORATION_CASE)
    for name in evidence.CAPTURES:
        save(bundle / (name + ".png"), explored, evidence.EXPLORATION_CASE)


def results_xml():
    root = ET.Element("test-run", result="Passed", total="3", passed="3", failed="0", skipped="0", inconclusive="0", duration="10.0")
    suite = ET.SubElement(root, "test-suite", result="Passed")
    for name in sorted(evidence.EXPECTED):
        ET.SubElement(suite, "test-case", fullname=name, result="Passed")
    return root


def qualified_record():
    return dict(errors=[], results_verified=True, runtime_verified=True, events_verified=True, completion_verified=True,
                integrity_before_launch=dict(normal_integrity=True), child_integrity=dict(normal_integrity=True),
                closure=dict(mode="self_exit", exit_code=0, forced=False), cleanup_intervened=False,
                foreground_or_visible_window_violation=False, hidden_parent=dict(destroyed=True), power_request=dict(restored=True),
                identity_unchanged=True, parent_relation_verified=True, callback_failure_present=False, presentation_verified=True,
                logs_scanned=True, native_fatal_markers=[], audio_failure_markers=[], crash_reports=[],
                graphics_threading_observations=[dict(mode="kGfxThreadingModeClientWorkerJobs")])


class PresentationEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.absent = evidence.decode_png(png(1280, 720), (1280, 720))
        cls.visible = evidence.decode_png(png(1280, 720, True), (1280, 720))
        cls.images = {"hud-visible": cls.visible, "hud-disabled": cls.absent, "hud-text-disabled": cls.absent}

    def test_actual_artifacts_are_independently_decoded_recounted_and_hash_bound(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp).resolve()
            write_output(output)
            result = evidence.validate_presentation_output(output, START, END, encoded_events())
            self.assertEqual(result["file_count"], 18)
            self.assertTrue(result["case_timestamps_bound"])
            self.assertEqual(result["exploration"]["final_objectives"], evidence.OBJECTIVES)
            self.assertEqual(result["exploration"]["checkpoint_names"], list(evidence.CAPTURES))
            for item in result["HUD"]:
                self.assertEqual([x["textChangedPixels"] for x in item["regions"]], [64] * 3)
            for row in result["files"]:
                raw = (output / row["path"]).read_bytes()
                self.assertEqual(row["sha256"], hashlib.sha256(raw).hexdigest())

    def test_forged_pixel_counts_missing_text_wrong_pose_and_clipping_are_rejected(self):
        changes = [lambda d: d["regions"][0].update(textChangedPixels=65),
                   lambda d: d.update(regions=d["regions"][:-1]), lambda d: d.update(planeDistance=.2),
                   lambda d: d.update(textOriginallyEnabled=[1, 1]), lambda d: d.update(frozenActorPartsUnchanged=False),
                   lambda d: d["cameraPosition"].update(x=0), lambda d: d["regions"][0].update(insideViewport=False),
                   lambda d: d.update(worldPixelsWithHudDisabled=0), lambda d: d["regions"][0]["viewportBounds"].update(x=-1)]
        for change in changes:
            value = hud(next(iter(evidence.HUD_CASES)))
            change(value)
            with self.subTest(value=value), self.assertRaises(ValueError):
                evidence.validate_hud(value, self.images)
        with self.assertRaises(ValueError):
            evidence.validate_hud(hud(next(iter(evidence.HUD_CASES))), dict(self.images, **{"hud-text-disabled": self.visible}))

    def test_png_crc_dimension_truncation_trailing_data_and_inflation_are_rejected(self):
        raw = png(1280, 720)
        mutations = [b"not-png", raw[:-1], raw + b"junk", raw[:30] + bytes([raw[30] ^ 1]) + raw[31:], png(960, 540)]
        for malformed in mutations:
            with self.subTest(size=len(malformed)), self.assertRaises(ValueError):
                evidence.decode_png(malformed, (1280, 720))
        def chunk(kind, data):
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
        bomb = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)) +
                chunk(b"IDAT", zlib.compress(b"\0" * 1000000)) + chunk(b"IEND", b""))
        with self.assertRaises(ValueError):
            evidence.decode_png(bomb, (1, 1))

    def test_unsafe_incomplete_wrong_route_unreleased_input_and_bad_clocks_cannot_qualify(self):
        mutations = [lambda d: d.update(completed=False), lambda d: d.update(unsafeFrames=1),
                     lambda d: d.update(blockedWarnings=1), lambda d: d.update(episodes=[{}]),
                     lambda d: d["samples"][-1].update(cameraEnabled=False),
                     lambda d: d["samples"][-1].update(objectives="None"),
                     lambda d: d["samples"][-1].update(seconds=-1), lambda d: d["inputs"][-1].update(keys=["W"]),
                     lambda d: d["inputs"][0].update(attack=False), lambda d: d["captures"].pop(),
                     lambda d: d["captures"][0].update(file="../outside.png"),
                     lambda d: d["viewport"].update(observedRadius=.1), lambda d: d.update(observedFrames=102.5),
                     lambda d: d["samples"][0]["position"].update(z=0),
                     lambda d: d["captures"][4]["pose"].update(room="Courtyard")]
        evidence.validate_exploration(exploration())
        for mutation in mutations:
            value = exploration()
            mutation(value)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                evidence.validate_exploration(value)

    def test_missing_extra_stale_and_other_case_artifacts_are_rejected(self):
        for mutation in ("missing", "extra", "stale", "foreign-case-time", "duplicate-bundle", "duration"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as tmp:
                output = Path(tmp).resolve()
                write_output(output)
                path = next((output / "captures").glob("primary-hud-*/*-evidence.json"))
                if mutation == "missing":
                    path.unlink()
                elif mutation == "extra":
                    (path.parent / "unqualified.json").write_text("{}")
                elif mutation in ("stale", "foreign-case-time"):
                    stamp = evidence.utc(START).timestamp() - 1 if mutation == "stale" else evidence.utc(END).timestamp() - 1
                    os.utime(path, (stamp, stamp))
                elif mutation == "duplicate-bundle":
                    (output / "captures" / ("primary-hud-" + "f" * 32)).mkdir()
                else:
                    path = next((output / "captures").glob("camera-exploration-*/evidence.json"))
                    before = path.stat().st_mtime
                    value = exploration()
                    value["elapsedObservedSeconds"] = 200
                    path.write_text(json.dumps(value))
                    os.utime(path, (before, before))
                with self.assertRaises((ValueError, OSError)):
                    evidence.validate_presentation_output(output, START, END, encoded_events())

    def test_confined_bundle_rejects_a_symlink(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp).resolve()
            write_output(output)
            bundle = next((output / "captures").glob("primary-hud-*"))
            path = next(bundle.glob("*-evidence.json"))
            actual = output / "outside.json"
            actual.write_bytes(path.read_bytes())
            path.unlink()
            try:
                path.symlink_to(actual)
            except OSError as error:
                self.skipTest("Host cannot create symlinks: " + str(error))
            with self.assertRaises(ValueError):
                evidence.validate_presentation_output(output, START, END, encoded_events())

    def test_case_time_binding_rejects_missing_foreign_failed_duplicate_and_reversed_events(self):
        for mutation in ("missing", "foreign", "failed", "duplicate", "reversed"):
            rows = event_rows()
            if mutation == "missing":
                rows.pop(2)
            elif mutation == "foreign":
                rows[1]["full_name"] = "Other.Case"
            elif mutation == "failed":
                rows[2]["result"] = "Failed"
            elif mutation == "duplicate":
                rows.insert(2, rows[1].copy())
            else:
                rows[2]["utc"] = START
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                evidence.case_intervals(encoded_events(rows), evidence.utc(START), evidence.utc(END))


class PresentationRunnerTests(unittest.TestCase):
    def test_fixed_three_filter_and_nunit_cases_reject_green_foreign_or_missing_catalog(self):
        self.assertEqual(runner.EXPECTED, evidence.EXPECTED)
        self.assertTrue(all(re.fullmatch(runner.FILTER, name) for name in evidence.EXPECTED))
        self.assertIsNone(re.fullmatch(runner.FILTER, runner.PREFIX + "AudioMixerPlaybackTests.QuietClipTraversesMixerAndReturnsToSilence"))
        valid = runner.validate_results(ET.tostring(results_xml()))
        self.assertEqual(valid["counts"]["total"], 3)
        for mutation in ("missing", "foreign", "duplicate", "failed", "skipped"):
            root = results_xml()
            suite, cases = root.find("test-suite"), list(root.iter("test-case"))
            if mutation == "missing":
                suite.remove(cases[0])
            elif mutation == "foreign":
                cases[0].set("fullname", "Other.Case")
            elif mutation == "duplicate":
                cases[1].set("fullname", cases[0].get("fullname"))
            elif mutation == "failed":
                cases[0].set("result", "Failed")
            else:
                root.set("skipped", "1")
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                runner.validate_results(ET.tostring(root))

    def test_profile_runtime_and_completion_require_exact_cases_and_raw_hashes(self):
        output = Path("presentation-attempt").absolute()
        runtime = dict(schema_version=1, test_profile="presentation", expected_cases=sorted(evidence.EXPECTED),
                       is_editor=False, batch_mode=False, unity_version="6000.6.3f1", platform="WindowsPlayer",
                       graphics_api="Direct3D11", pid=42, output_directory=str(output), expected_case_count=3,
                       test_input_source="Test-only input events and fixed HUD presentation poses", hardware=dict(gpu="Synthetic"))
        runner.validate_runtime(runtime, 42, output)
        for change in (dict(test_profile="audio-mixer"), dict(expected_case_count=1), dict(expected_cases=[]),
                       dict(is_editor=True), dict(batch_mode=True), dict(pid=41), dict(graphics_api="Null")):
            with self.subTest(change=change), self.assertRaises(ValueError):
                runner.validate_runtime({**runtime, **change}, 42, output)
        results, events = runner.validate_results(ET.tostring(results_xml())), runner.validate_events(encoded_events())
        completion = dict(schema_version=1, test_profile="presentation", expected_cases=sorted(evidence.EXPECTED),
                          total=3, passed=3, failed=0, skipped=0, inconclusive=0, case_names=sorted(evidence.EXPECTED),
                          exit_code=0, results_sha256=results["sha256"], events_sha256=events["sha256"])
        runner.validate_completion(completion, results, events)
        for change in (dict(test_profile="routes"), dict(total=10), dict(case_names=[]), dict(exit_code=1),
                       dict(results_sha256="0" * 64), dict(events_sha256="0" * 64)):
            with self.subTest(change=change), self.assertRaises(ValueError):
                runner.validate_completion({**completion, **change}, results, events)

    def test_build_receipt_requires_scoped_filter_final_success_and_no_autorun(self):
        player = Path("Presentation.exe").absolute()
        valid = dict(schema_version=1, result="Succeeded", test_profile="presentation", expected_cases=sorted(evidence.EXPECTED),
                     test_filter=runner.FILTER, target="StandaloneWindows64", errors=0, bytes=100, duration_seconds=1.0,
                     include_test_assemblies=True, auto_run_player=False, connect_to_host=False, development=True,
                     output_path=str(player), scenes=["Assets/InitTestScene7f595842-c402-4b75-853b-5d27405d0fc3.unity", "Assets/Scenes/ForsakenCastle.unity"])
        runner.validate_build_report(valid, player)
        for change in (dict(test_profile="audio-mixer"), dict(test_filter=".*"), dict(expected_cases=[]),
                       dict(bytes=0), dict(duration_seconds=float("nan")), dict(errors=1), dict(auto_run_player=True),
                       dict(connect_to_host=True), dict(include_test_assemblies=False), dict(scenes=[])):
            with self.subTest(change=change), self.assertRaises(ValueError):
                runner.validate_build_report({**valid, **change}, player)

    def test_environment_is_process_only_and_clears_all_other_probe_outputs(self):
        names = ("SHADOWS_ROUTE_PLAYER_OUTPUT", "SHADOWS_ROUTE_PLAYER_BUILD", "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PLAYER_BUILD",
                 "SHADOWS_AUDIO_PROBE_OUTPUT", "SHADOWS_PRESENTATION_PLAYER_BUILD", "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR",
                 "SHADOWS_CAPTURE_DIR", "SHADOWS_PRESENTATION_PLAYER_OUTPUT")
        inherited = dict.fromkeys(names, "stale")
        saved = inherited.copy()
        output = Path("presentation-attempt").absolute()
        child = runner.player_environment(output, inherited)
        self.assertEqual(inherited, saved)
        self.assertEqual(child, dict(SHADOWS_PRESENTATION_PLAYER_OUTPUT=str(output), SHADOWS_CAPTURE_DIR=str(output / "captures")))

    def test_each_native_qualification_gate_independently_rejects_false_success(self):
        valid = qualified_record()
        self.assertTrue(runner.qualify(valid))
        changes = dict(exact_results_verified=dict(results_verified=False), runtime_callback_verified=dict(completion_verified=False),
                       normal_integrity=dict(child_integrity={}), self_exit_zero=dict(closure=dict(mode="self_exit", exit_code=1, forced=False)),
                       no_intervention=dict(cleanup_intervened=True), hidden_guard=dict(foreground_or_visible_window_violation=True),
                       owned_host_destroyed=dict(hidden_parent=dict(destroyed=False)), actual_owned_parent_verified=dict(parent_relation_verified=False),
                       power_restored=dict(power_request=dict(restored=False)), identity_unchanged=dict(identity_unchanged=False),
                       logs_reviewed=dict(logs_scanned=False), observed_normal_renderer=dict(graphics_threading_observations=[]),
                       no_fatal_audio_or_dump=dict(crash_reports=[dict(path="crash.dmp")]), no_callback_failure=dict(callback_failure_present=True),
                       presentation_verified=dict(presentation_verified=False))
        self.assertEqual(set(changes), set(valid["qualification_checks"]))
        for gate, change in changes.items():
            record = qualified_record()
            record.update(change)
            with self.subTest(gate=gate):
                self.assertFalse(runner.qualify(record))
                self.assertFalse(record["qualification_checks"][gate])

    def test_fmod_native_fatal_or_forced_cleanup_fail_despite_green_images_and_exit_zero(self):
        for change in (dict(audio_failure_markers=[dict(text="FMOD failed")]), dict(native_fatal_markers=[dict(text="Native Crash Reporting")]),
                       dict(closure=dict(mode="self_exit", exit_code=0, forced=True)), dict(errors=["cleanup failed"])):
            value = qualified_record()
            value.update(change)
            with self.subTest(change=change):
                self.assertFalse(runner.qualify(value))


if __name__ == "__main__":
    unittest.main()
