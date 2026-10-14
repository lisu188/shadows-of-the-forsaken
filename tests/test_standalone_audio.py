"""Native mixer qualification regressions; synthetic evidence, no player launch."""
import copy
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("standalone_audio", ROOT / "tools/run_standalone_audio.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)
CASE = "ShadowsOfTheForsaken.Tests.PlayMode.AudioMixerPlaybackTests.QuietClipTraversesMixerAndReturnsToSilence"
START = "2026-10-14T10:00:00Z"
END = "2026-10-14T10:00:05Z"


def xml_result(tag="test-run"):
    root = ET.Element(tag, result="Passed", total="1", passed="1", failed="0", skipped="0",
                      inconclusive="0", duration="0.25")
    suite = ET.SubElement(root, "test-suite", result="Passed")
    ET.SubElement(suite, "test-case", fullname=CASE, result="Passed")
    return root


def encoded(root):
    return ET.tostring(root, encoding="utf-8")


def events():
    common = dict(full_name=CASE, is_suite=False, utc="2026-10-14T10:00:01Z")
    return [dict(kind="run-started", utc=START), dict(common, kind="test-started"),
            dict(common, kind="test-finished", result="Passed", passed=1, failed=0, skipped=0, inconclusive=0),
            dict(kind="run-finished", utc=END, result="Passed", passed=1, failed=0, skipped=0, inconclusive=0)]


def event_bytes(rows):
    return ("\n".join(json.dumps(row) for row in rows) + "\n").encode()


def runtime(output):
    return dict(schema_version=1, test_profile="audio-mixer", expected_cases=[CASE], is_editor=False,
                batch_mode=False, unity_version="6000.6.3f1", platform="WindowsPlayer", graphics_api="Direct3D11",
                pid=42, output_directory=str(output), expected_case_count=1,
                test_input_source="Test-only generated quiet clip", hardware=dict(gpu="Synthetic GPU"))


def build_report(player):
    return dict(schema_version=1, result="Succeeded", test_profile="audio-mixer", expected_cases=[CASE],
                target="StandaloneWindows64", errors=0, bytes=123456, duration_seconds=12.5,
                include_test_assemblies=True, auto_run_player=False, connect_to_host=False, development=True,
                output_path=str(player), scenes=["Assets/InitTestScene7f595842-c402-4b75-853b-5d27405d0fc3.unity",
                                                "Assets/Scenes/ForsakenCastle.unity"])


def completion(results, observed):
    return dict(schema_version=1, test_profile="audio-mixer", expected_cases=[CASE], total=1,
                passed=1, failed=0, skipped=0, inconclusive=0, case_names=[CASE], exit_code=0,
                results_sha256=results["sha256"], events_sha256=observed["sha256"])


def qualified_record():
    return dict(errors=[], results_verified=True, runtime_verified=True, events_verified=True,
                completion_verified=True, integrity_before_launch=dict(normal_integrity=True),
                child_integrity=dict(normal_integrity=True), closure=dict(mode="self_exit", exit_code=0, forced=False),
                cleanup_intervened=False, foreground_or_visible_window_violation=False,
                hidden_parent=dict(destroyed=True), power_request=dict(restored=True), identity_unchanged=True,
                parent_relation_verified=True, callback_failure_present=False, mixer_verified=True,
                logs_scanned=True, native_fatal_markers=[], audio_failure_markers=[], crash_reports=[],
                graphics_threading_observations=[dict(mode="kGfxThreadingModeClientWorkerJobs")])


def mixer_evidence():
    # Complete retained windows, independent of Unity or ignored local evidence.
    pcm = [.002 * math.sin(2 * math.pi * 440 * i / 48000) for i in range(1024)]
    peak = max(abs(value) for value in pcm)
    baseline = dict(stage="prime-and-baseline", realtime=1.0, dspTime=1.0, sourcePlaying=False,
                    sourcePeak=0.0, masterPeak=0.0)
    signal = dict(stage="playing", realtime=1.05, dspTime=1.05, sourcePlaying=True,
                  sourcePeak=peak, masterPeak=peak)
    stopped = dict(stage="stopped-silence", realtime=1.22, dspTime=1.22, sourcePlaying=False,
                   sourcePeak=0.0, masterPeak=0.0)
    return dict(unityVersion="6000.6.3f1", platform="WindowsPlayer", isEditor=False,
                playingObserved=True, baselineVerified=True, nonzeroMixerVerified=True, stoppedSilenceVerified=True,
                signalChecksPassed=True, sourcesRestored=True, listenersRestored=True, globalSettingsRestored=True,
                ownedObjectsDestroyed=True, cleanupErrors=[], stage="signal-checks-complete",
                recordedUtc="2026-10-14T10:00:02Z", outputSampleRate=48000, speakerMode="Stereo",
                dspBufferFrames=1024, dspBuffers=4, borrowedSources=0, borrowedListeners=0,
                originalListenerPaused=False, originalListenerVolume=1.0, clipFrames=24000,
                clipSeconds=.5, clipPeak=.003, polls=3, playDspTime=1.0, stopDspTime=1.1,
                tap=dict(callbacks=7, nonzeroSamples=4054, invalidSamples=0, peak=peak),
                signal=signal, stopped=stopped, sourceWindow=pcm, masterWindow=pcm.copy(),
                samples=[baseline, signal.copy(), stopped.copy()])


def write_mixer(output, value=None):
    bundle = output / "audio" / ("audio-mixer-" + "a" * 32)
    bundle.mkdir(parents=True)
    path = bundle / "evidence.json"
    path.write_text(json.dumps(mixer_evidence() if value is None else value), encoding="utf-8")
    return path


class StandaloneAudioTests(unittest.TestCase):
    def test_singleton_catalog_accepts_both_real_nunit_root_shapes(self):
        self.assertEqual(audio.EXPECTED, {CASE})
        for tag in ("test-run", "test-suite"):
            with self.subTest(tag=tag):
                result = audio.validate_results(encoded(xml_result(tag)))
                self.assertEqual(result["case_names"], [CASE])
                self.assertEqual(result["counts"]["total"], 1)

    def test_green_aggregate_cannot_hide_route_catalog_foreign_duplicate_or_missing_case(self):
        for change in ("ten-routes", "foreign", "duplicate", "missing"):
            root = xml_result()
            suite = root.find("test-suite")
            if change == "ten-routes":
                suite.clear()
                root.set("total", "10")
                root.set("passed", "10")
                for i in range(10):
                    ET.SubElement(suite, "test-case", fullname=audio.PREFIX + "FullCastleRouteTests.Route" + str(i),
                                  result="Passed")
            elif change == "foreign":
                next(root.iter("test-case")).set("fullname", "Other.AudioCase")
            elif change == "duplicate":
                ET.SubElement(suite, "test-case", fullname=CASE, result="Passed")
            else:
                suite.remove(next(root.iter("test-case")))
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_results(encoded(root))

    def test_green_root_cannot_hide_failed_case_suite_skip_or_inconclusive(self):
        for change in ("case", "suite", "root", "skipped", "inconclusive"):
            root = xml_result()
            if change in ("skipped", "inconclusive"):
                root.set(change, "1")
            else:
                node = next(root.iter("test-case")) if change == "case" else root.find("test-suite") if change == "suite" else root
                node.set("result", "Failed")
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_results(encoded(root))

    def test_untrusted_xml_and_nonfinite_duration_do_not_qualify(self):
        with self.assertRaises(ValueError):
            audio.validate_results(b'<!DOCTYPE x [<!ENTITY x SYSTEM "file:///secret">]>' + encoded(xml_result()))
        for value in ("NaN", "Infinity", "-1"):
            root = xml_result()
            root.set("duration", value)
            with self.subTest(value=value), self.assertRaises(ValueError):
                audio.validate_results(encoded(root))

    def test_runtime_binds_profile_case_actual_process_platform_and_output(self):
        output = Path("audio-attempt").absolute()
        valid = runtime(output)
        audio.validate_runtime(valid, 42, output)
        for change in (dict(test_profile="routes"), dict(expected_cases=[]), dict(expected_cases=[CASE, CASE]),
                       dict(expected_cases=["Other.Case"]), dict(expected_case_count=10), dict(is_editor=True),
                       dict(batch_mode=True), dict(pid=43), dict(unity_version="other"), dict(platform="LinuxPlayer"),
                       dict(graphics_api="Null"), dict(hardware={}), dict(output_directory=str(output / "other")),
                       dict(test_input_source="Fixture-controlled InputSystem events")):
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_runtime({**valid, **change}, 42, output)

    def test_callback_stream_requires_exact_once_ordered_pass_and_counts(self):
        self.assertEqual(audio.validate_events(event_bytes(events()))["case_names"], [CASE])
        for change in ("missing", "duplicate", "foreign", "order", "boundary", "failed", "skipped", "suite-failed"):
            rows = events()
            if change == "missing":
                rows.pop(2)
            elif change == "duplicate":
                rows.insert(3, rows[2].copy())
            elif change == "foreign":
                rows[1]["full_name"] = "Other.Case"
            elif change == "order":
                rows[1], rows[2] = rows[2], rows[1]
            elif change == "boundary":
                rows.insert(1, rows[0].copy())
            elif change == "failed":
                rows[2]["result"] = "Failed"
            elif change == "skipped":
                rows[-1]["skipped"] = 1
            else:
                rows.insert(3, dict(kind="test-finished", utc=END, is_suite=True, result="Failed"))
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_events(event_bytes(rows))

    def test_callback_stream_rejects_duplicate_json_keys_and_unbounded_input(self):
        with self.assertRaises(ValueError):
            audio.validate_events(b'{"kind":"run-started","kind":"run-finished"}\n')
        with self.assertRaises(ValueError):
            audio.validate_events(b" " * (8 * 1024 * 1024 + 1))

    def test_completion_binds_singleton_profile_and_actual_raw_xml_and_events(self):
        results = audio.validate_results(encoded(xml_result()))
        observed = audio.validate_events(event_bytes(events()))
        valid = completion(results, observed)
        audio.validate_completion(valid, results, observed)
        for change in (dict(test_profile="routes"), dict(expected_cases=[]), dict(expected_cases=[CASE, CASE]),
                       dict(total=10, passed=10), dict(case_names=["Other.Case"]), dict(case_names=[CASE, CASE]),
                       dict(exit_code=1), dict(results_sha256="0" * 64), dict(events_sha256="0" * 64),
                       dict(skipped=1), dict(failed=1), dict(inconclusive=1), dict(passed=True)):
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_completion({**valid, **change}, results, observed)

    def test_build_receipt_is_final_disconnected_and_bound_to_audio_profile_and_output(self):
        player = Path("AudioTestPlayer.exe").absolute()
        valid = build_report(player)
        audio.validate_build_report(valid, player)
        for change in (dict(test_profile="routes"), dict(expected_cases=[]), dict(expected_cases=[CASE] * 10),
                       dict(result="Failed"), dict(errors=1), dict(errors=False), dict(target="StandaloneWindows"),
                       dict(include_test_assemblies=False), dict(auto_run_player=True), dict(connect_to_host=True),
                       dict(development=False), dict(bytes=0), dict(duration_seconds=0),
                       dict(duration_seconds=float("nan")), dict(duration_seconds=float("inf")),
                       dict(output_path=str(player.parent / "Other.exe")), dict(scenes=[]),
                       dict(scenes=list(reversed(valid["scenes"]))),
                       dict(scenes=valid["scenes"] + ["Assets/Scenes/ForsakenRuntime.unity"])):
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_build_report({**valid, **change}, player)
        missing = valid.copy()
        del missing["connect_to_host"]
        with self.assertRaises(ValueError):
            audio.validate_build_report(missing, player)

    def test_child_environment_removes_route_and_observer_hooks_without_mutating_parent(self):
        inherited = {name: "stale" for name in ("SHADOWS_ROUTE_PLAYER_OUTPUT", "SHADOWS_ROUTE_PLAYER_BUILD",
                     "SHADOWS_AUDIO_PLAYER_BUILD", "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR",
                     "SHADOWS_CAPTURE_DIR", "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PROBE_OUTPUT")}
        inherited["OTHER"] = "preserved"
        before = inherited.copy()
        output = Path("audio-attempt").absolute()
        child = audio.player_environment(output, inherited)
        self.assertEqual(inherited, before)
        self.assertEqual(child["OTHER"], "preserved")
        self.assertEqual(child["SHADOWS_AUDIO_PLAYER_OUTPUT"], str(output))
        self.assertEqual(child["SHADOWS_AUDIO_PROBE_OUTPUT"], str(output / "audio"))
        for name in set(inherited) - {"OTHER", "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PROBE_OUTPUT"}:
            self.assertNotIn(name, child)

    def test_all_fifteen_qualification_gates_have_an_independent_failure(self):
        valid = qualified_record()
        self.assertTrue(audio.qualify(valid))
        self.assertEqual(len(valid["qualification_checks"]), 15)
        mutations = {
            "exact_results_verified": dict(results_verified=False),
            "runtime_callback_verified": dict(completion_verified=False),
            "normal_integrity": dict(child_integrity=dict(normal_integrity=False)),
            "self_exit_zero": dict(closure=dict(mode="self_exit", exit_code=1, forced=False)),
            "no_intervention": dict(cleanup_intervened=True),
            "hidden_guard": dict(foreground_or_visible_window_violation=True),
            "owned_host_destroyed": dict(hidden_parent=dict(destroyed=False)),
            "actual_owned_parent_verified": dict(parent_relation_verified=False),
            "power_restored": dict(power_request=dict(restored=False)),
            "identity_unchanged": dict(identity_unchanged=False),
            "logs_reviewed": dict(logs_scanned=False),
            "observed_normal_renderer": dict(graphics_threading_observations=[]),
            "no_fatal_audio_or_dump": dict(audio_failure_markers=[dict(text="FMOD output failed")]),
            "no_callback_failure": dict(callback_failure_present=True),
            "mixer_verified": dict(mixer_verified=False),
        }
        self.assertEqual(set(mutations), set(valid["qualification_checks"]))
        for gate, change in mutations.items():
            record = qualified_record()
            record.update(change)
            with self.subTest(gate=gate):
                self.assertFalse(audio.qualify(record))
                self.assertFalse(record["qualification_checks"][gate])

    def test_native_crash_dump_callback_or_launcher_failure_cannot_be_hidden_by_mixer_pass(self):
        for change in (dict(native_fatal_markers=[dict(text="Native Crash Reporting")]),
                       dict(crash_reports=[dict(path="crash.dmp")]), dict(runtime_verified=False),
                       dict(events_verified=False), dict(integrity_before_launch={}), dict(errors=["probe failure"])):
            record = qualified_record()
            record.update(change)
            with self.subTest(change=change):
                self.assertFalse(audio.qualify(record))

    def test_owned_zero_self_exit_destroys_host_without_close_or_fallback(self):
        process, parent = mock.Mock(), mock.Mock()
        process.pid, process.poll.return_value = 42, 0
        record = qualified_record()
        with mock.patch.object(audio.native, "cleanup_owned_resources") as fallback:
            audio.finish_process(process, mock.Mock(), None, record, parent)
        fallback.assert_not_called()
        parent.close.assert_called_once_with(process)
        self.assertEqual(record["closure"]["pid"], 42)
        self.assertEqual(record["closure"]["wm_close"], [])
        self.assertTrue(audio.qualify(record))

    def test_live_missing_nonzero_or_forced_process_never_qualifies(self):
        for change in ("live", "missing", "nonzero", "close", "forced", "host-error"):
            record = qualified_record()
            process, parent = mock.Mock(), mock.Mock()
            process.pid, process.poll.return_value = 42, None if change == "live" else 1 if change == "nonzero" else 0
            if change == "missing":
                process = None
            if change == "host-error":
                parent.close.side_effect = RuntimeError("host remains")
            with mock.patch.object(audio.native, "cleanup_owned_resources"):
                audio.finish_process(process, mock.Mock(), None, record, parent)
            if change == "close":
                record["closure"]["mode"] = "WM_CLOSE"
            elif change == "forced":
                record["closure"]["forced"] = True
            with self.subTest(change=change):
                self.assertFalse(audio.qualify(record))

    def test_actual_parent_requires_owned_pid_child_style_class_and_getparent_match(self):
        user = mock.Mock()
        user.GetForegroundWindow.return_value = 0
        valid = dict(window=100, owner_pid=42, style=0x40000000, **{"class": "UnityWndClass"})
        for change in ({}, dict(owner_pid=99), dict(style=0), {"class": "Other"}, dict(actual_parent=901)):
            row = {**valid, **change}
            user.GetParent.return_value = row.pop("actual_parent", 900)
            with self.subTest(change=change), \
                 mock.patch.object(audio.native, "own_windows", return_value=[100]), \
                 mock.patch.object(audio.native, "describe_windows", return_value=[row]):
                self.assertEqual(audio.observe_parent(user, None, 42, 900)["matching_parent_observed"], not change)

    def test_player_tree_rejects_changed_added_or_removed_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            player = root / "AudioTest.exe"
            player.write_bytes(b"synthetic executable")
            managed = root / "AudioTest_Data/Managed"
            managed.mkdir(parents=True)
            for name in ("Shadows.PlayMode.Tests.dll", "Shadows.StandaloneAudio.PlayerSupport.dll"):
                (managed / name).write_bytes(name.encode())
            before = audio.snapshot_player(player)
            self.assertTrue(audio.compare_identity(before, audio.snapshot_player(player)))
            for change in ("changed", "added", "removed"):
                if change == "changed":
                    player.write_bytes(b"modified executable")
                elif change == "added":
                    (root / "new.bin").write_bytes(b"new")
                else:
                    (managed / "Shadows.StandaloneAudio.PlayerSupport.dll").unlink()
                with self.subTest(change=change), self.assertRaises(ValueError):
                    audio.compare_identity(before, audio.native.manifest.file_tree(root))
                if change == "changed":
                    player.write_bytes(b"synthetic executable")
                elif change == "added":
                    (root / "new.bin").unlink()

    def test_player_requires_both_fixture_and_profile_callback_assemblies(self):
        for missing in ("Shadows.PlayMode.Tests.dll", "Shadows.StandaloneAudio.PlayerSupport.dll"):
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                player = root / "AudioTest.exe"
                player.write_bytes(b"synthetic executable")
                managed = root / "AudioTest_Data/Managed"
                managed.mkdir(parents=True)
                for name in ("Shadows.PlayMode.Tests.dll", "Shadows.StandaloneAudio.PlayerSupport.dll"):
                    if name != missing:
                        (managed / name).write_bytes(name.encode())
                with self.subTest(missing=missing), self.assertRaises(ValueError):
                    audio.snapshot_player(player)

    def test_complete_pcm_windows_recompute_peak_rms_and_nonzero_count(self):
        value = mixer_evidence()
        result = audio.validate_mixer(value)
        for key in ("sourceWindow", "masterWindow"):
            actual = value[key]
            self.assertEqual(result["windows"][key]["samples"], 1024)
            self.assertEqual(result["windows"][key]["peak"], max(abs(item) for item in actual))
            self.assertAlmostEqual(result["windows"][key]["rms"], math.sqrt(sum(item ** 2 for item in actual) / 1024))
            self.assertEqual(result["windows"][key]["nonzero_samples"], sum(item != 0 for item in actual))
        self.assertGreater(result["signal_after_play_dsp_seconds"], .02)
        self.assertIn("physical output is unobserved", result["scope"])

    def test_nonzero_booleans_cannot_replace_finite_retained_source_and_master_pcm(self):
        for key in ("sourceWindow", "masterWindow"):
            for change in ("zero", "nan", "inf", "boolean", "short", "peak-mismatch"):
                value = mixer_evidence()
                if change == "zero":
                    value[key] = [0] * 1024
                elif change in ("nan", "inf", "boolean"):
                    value[key][1] = float("nan") if change == "nan" else float("inf") if change == "inf" else True
                elif change == "short":
                    value[key].pop()
                else:
                    value["signal"]["sourcePeak" if key == "sourceWindow" else "masterPeak"] *= 2
                with self.subTest(key=key, change=change), self.assertRaises(ValueError):
                    audio.validate_mixer(value)

    def test_finite_json_pcm_cannot_qualify_an_overflowed_rms(self):
        for key, peak_name in (("sourceWindow", "sourcePeak"), ("masterWindow", "masterPeak")):
            value = mixer_evidence()
            value[key] = [1e308] * 1024
            value["signal"][peak_name] = 1e308
            with self.subTest(window=key), self.assertRaisesRegex(ValueError, "RMS"):
                audio.validate_mixer(value)

    def test_stale_dsp_or_backwards_sample_clocks_cannot_prove_play_then_silence(self):
        for change in ("play", "stop-before-signal", "unflushed-stop", "real-backwards", "history-dsp", "history-real"):
            value = mixer_evidence()
            if change == "play":
                value["signal"]["dspTime"] = value["playDspTime"]
            elif change == "stop-before-signal":
                value["stopDspTime"] = value["signal"]["dspTime"] - .01
            elif change == "unflushed-stop":
                value["stopped"]["dspTime"] = value["stopDspTime"]
            elif change == "real-backwards":
                value["stopped"]["realtime"] = value["signal"]["realtime"] - .01
            else:
                value["samples"][1]["dspTime" if change == "history-dsp" else "realtime"] = .5
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_mixer(value)

    def test_playing_state_and_master_return_to_silence_are_required(self):
        for phase, key, changed in (("signal", "sourcePlaying", False), ("signal", "stage", "stopped-silence"),
                                    ("stopped", "sourcePlaying", True), ("stopped", "masterPeak", .001)):
            value = mixer_evidence()
            value[phase][key] = changed
            with self.subTest(phase=phase, key=key), self.assertRaises(ValueError):
                audio.validate_mixer(value)

    def test_audio_thread_tap_requires_real_callbacks_nonzero_and_no_invalid_samples(self):
        for change in (dict(callbacks=0), dict(nonzeroSamples=0), dict(invalidSamples=1),
                       dict(invalidSamples=False), dict(peak=0), dict(peak=float("nan"))):
            value = mixer_evidence()
            value["tap"].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_mixer(value)

    def test_every_signal_or_restoration_flag_and_cleanup_error_is_fatal(self):
        flags = ("playingObserved", "baselineVerified", "nonzeroMixerVerified", "stoppedSilenceVerified",
                 "signalChecksPassed", "sourcesRestored", "listenersRestored", "globalSettingsRestored", "ownedObjectsDestroyed")
        for change in [*({flag: False} for flag in flags), dict(cleanupErrors=["restore failed"]), dict(stage="playing")]:
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_mixer({**mixer_evidence(), **change})

    def test_mixer_pin_configuration_quiet_clip_and_sample_history_are_bounded(self):
        for change in (dict(isEditor=True), dict(unityVersion="other"), dict(platform="LinuxPlayer"),
                       dict(outputSampleRate=0), dict(dspBufferFrames=True), dict(dspBuffers=0), dict(speakerMode=""),
                       dict(clipPeak=.3), dict(clipFrames=24001), dict(clipSeconds=1), dict(polls=2),
                       dict(samples=[]), dict(samples=[mixer_evidence()["signal"]] * 513),
                       dict(originalListenerVolume=2), dict(originalListenerPaused=0), dict(borrowedSources=-1)):
            with self.subTest(change=change), self.assertRaises(ValueError):
                audio.validate_mixer({**mixer_evidence(), **change})

    def test_evidence_bundle_binds_exact_file_hash_and_current_run_timestamp(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve()
            path = write_mixer(output)
            result = audio.validate_mixer_output(output, START, END)
            self.assertEqual(result["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(result["path"], path.relative_to(output).as_posix())
            for stamp in ("2026-10-14T09:59:59Z", "2026-10-14T10:00:06Z",
                          "2026-10-14T10:00:02", "2026-10-14T10:00:02+02:00"):
                value = mixer_evidence()
                value["recordedUtc"] = stamp
                path.write_text(json.dumps(value), encoding="utf-8")
                with self.subTest(stamp=stamp), self.assertRaises(ValueError):
                    audio.validate_mixer_output(output, START, END)

    def test_missing_duplicate_malformed_or_oversized_bundle_is_rejected(self):
        for change in ("missing", "duplicate", "malformed-name", "oversized"):
            with tempfile.TemporaryDirectory() as temp:
                output = Path(temp).resolve()
                path = write_mixer(output)
                if change == "missing":
                    path.unlink()
                elif change == "duplicate":
                    (output / "audio" / ("audio-mixer-" + "b" * 32)).mkdir()
                elif change == "malformed-name":
                    path.parent.rename(path.parent.with_name("foreign-probe"))
                else:
                    path.write_bytes(b" " * (512 * 1024 + 1))
                with self.subTest(change=change), self.assertRaises((ValueError, FileNotFoundError)):
                    audio.validate_mixer_output(output, START, END)

    def test_escaped_audio_directory_or_evidence_file_is_rejected_without_symlink_privileges(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve()
            path = write_mixer(output)
            original = Path.resolve
            for escaped in (output / "audio", path):
                def resolve(candidate, *args, **kwargs):
                    return output.parent / "outside" if candidate == escaped else original(candidate, *args, **kwargs)
                with self.subTest(escaped=escaped), mock.patch.object(Path, "resolve", resolve), self.assertRaises(ValueError):
                    audio.validate_mixer_output(output, START, END)

    def test_timeout_disk_reserve_and_actual_evidence_growth_are_bounded(self):
        self.assertIsNone(audio.stop_reason(119, 120, audio.RESERVE, audio.GROWTH_LIMIT))
        self.assertEqual(audio.stop_reason(120, 120, audio.RESERVE, 0), "timeout")
        self.assertEqual(audio.stop_reason(1, 120, audio.RESERVE - 1, 0), "disk reserve")
        self.assertEqual(audio.stop_reason(1, 120, audio.RESERVE, audio.GROWTH_LIMIT + 1), "evidence size limit")


if __name__ == "__main__":
    unittest.main()
