import copy
import importlib.util
import json
import re
from pathlib import Path
import tempfile
import unittest
from unittest import mock

TOOL = Path(__file__).resolve().parents[1] / "tools/audio_run_lifecycle_evidence.py"
spec = importlib.util.spec_from_file_location("audio_run_lifecycle_evidence", TOOL)
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)


def stamp(seconds):
    return "2026-10-16T10:00:%02d.%07dZ" % (int(seconds), round((seconds % 1) * 10_000_000))


def fixture(output, callback=False):
    names = ["observer-began", "run-started", "exit-attached", "test-started", "test-finished", "test-started", "test-finished", "run-finished", "quit-intent", "audio-closed", "wants-to-quit", "application-quit", "destroyed"]
    rows = []
    event_tokens = {1: ("suite", "synthetic suite", True, ""), 3: ("main", e.CASES[0], False, ""), 4: ("main", e.CASES[0], False, "Passed"),
                    5: ("secret", e.CASES[1], False, ""), 6: ("secret", e.CASES[1], False, "Passed"), 7: ("suite", "synthetic suite", True, "Passed")}
    events = []
    for index, kind in enumerate(names):
        original_id, name, suite, result = event_tokens.get(index, ("", "", False, ""))
        row = dict(schema_version=1, profile=e.PROFILE, pid=123, run_id="a" * 32, sequence=index, kind=kind, utc=stamp(index),
                   clock_ticks=1000 + index * 10, thread_id=1, phase_token=index, frame=index if index <= 9 else -1,
                   exit_entity_id=999 if index >= 2 else 0, original_id=original_id, full_name=name, is_suite=suite, result=result, quit_allowed=kind == "wants-to-quit")
        rows.append(row)
        if index in event_tokens:
            events.append(dict(kind=kind, id=original_id, full_name=name, is_suite=suite, result=result, utc=stamp(index + .1)))

    def configuration(seconds):
        return dict(readUtc=stamp(seconds), readClockTicks=1000 + round(seconds * 10), readThreadId=1, sampleRate=48000,
                    dspBufferSize=1024, numRealVoices=32, numVirtualVoices=512, speakerMode="Stereo", frame=int(seconds),
                    sceneHandle=777, scenePath="", sceneLoaded=True)

    retained = []
    if callback:
        retained = [dict(sequence=1, callbackUtc=stamp(4.2), callbackClockTicks=1042, callbackThreadId=2, callbackPhaseToken=4,
                         deviceWasChanged=True, drainedPhaseToken=5, configuration=configuration(5.5), observedDelaySeconds=1.3)]
    pre = dict(schema_version=1, profile=e.PROFILE, pid=123, output_directory=str(output), run_id="a" * 32, main_thread_id=1,
               stopwatch_frequency=10, exit_entity_id=999, is_editor=False, initial=configuration(.5), final=configuration(8.5),
               callback_count=len(retained), queue_drops=0, record_drops=0, pending_callbacks=0, counter_overflow=False,
               accepting_callbacks=False, subscribed=False, subscribe_attempts=1, unsubscribe_attempts=1, retained=retained, errors=[], queue_capacity=128, record_capacity=256)
    terminal = dict(schema_version=1, profile=e.PROFILE, pid=123, output_directory=str(output), run_id="a" * 32, main_thread_id=1,
                    stopwatch_frequency=10, exit_entity_id=999, prequit_observed=True, audio_closed=True, subscribed=False, wants_subscribed=False,
                    exit_attach_count=1, exit_detach_count=1, wants_subscribe_count=1, wants_detach_count=1, wants_to_quit_count=1,
                    application_quit_count=1, on_destroy_count=1, errors=[])
    return rows, pre, terminal, events


def encode(values):
    rows, pre, terminal, events = copy.deepcopy(values)
    encode_json = lambda value: json.dumps(value, separators=(",", ":")).encode() + b"\n"
    lines = [encode_json(row) for row in rows]
    close = next(i for i, row in enumerate(rows) if row["kind"] == "audio-closed")
    prefix, journal = b"".join(lines[:close + 1]), b"".join(lines)
    pre.update(journal_sha256=e.digest(prefix), journal_bytes=len(prefix), journal_records=close + 1)
    pre_raw = encode_json(pre)
    terminal.update(prequit_sha256=e.digest(pre_raw), journal_sha256=e.digest(journal), journal_bytes=len(journal), journal_records=len(rows))
    return dict(zip(e.FILES, (journal, pre_raw, encode_json(terminal)))), b"".join(encode_json(row) for row in events)


class AudioRunLifecycleEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)

    def validate(self, values):
        raw, events = encode(values)
        return e.validate(raw, 123, self.output, stamp(0), stamp(15), events)

    def rejected(self, change, callback=False):
        values = fixture(self.output, callback)
        change(*values)
        with self.assertRaises((AssertionError, ValueError, KeyError, TypeError)):
            self.validate(values)

    def test_zero_actual_configuration_callbacks_is_valid(self):
        result = self.validate(fixture(self.output))
        self.assertEqual(result["callback_count"], 0)
        self.assertEqual(result["marker_count"], 13)

    def test_post_case_worker_callback_and_later_main_thread_read_are_retained(self):
        result = self.validate(fixture(self.output, True))
        self.assertEqual(result["callback_count"], 1)

    def test_callback_can_precede_initial_read_after_subscription(self):
        values = fixture(self.output, True)
        callback = values[1]["retained"][0]
        callback.update(callbackUtc=stamp(.1), callbackClockTicks=1001, callbackPhaseToken=0, observedDelaySeconds=5.4)
        self.validate(values)

    def test_callback_lock_admission_order_can_invert_timestamps_and_phases(self):
        values = fixture(self.output, True)
        later = values[1]["retained"][0]
        later.update(callbackUtc=stamp(5.1), callbackClockTicks=1051, callbackPhaseToken=5, observedDelaySeconds=.4)
        earlier = copy.deepcopy(later)
        earlier.update(sequence=2, callbackUtc=stamp(4.2), callbackClockTicks=1042, callbackPhaseToken=4, observedDelaySeconds=1.3)
        values[1]["retained"].append(earlier)
        values[1]["callback_count"] = 2
        self.validate(values)

    def test_sampled_phase_can_advance_after_callback_timestamp_capture(self):
        values = fixture(self.output, True)
        values[1]["retained"][0].update(callbackPhaseToken=5)
        # Callback UTC4.2 precedes phase5 UTC5 because the captures are not atomic.
        self.validate(values)

    def test_support_run_started_can_precede_companion_begin(self):
        values = fixture(self.output)
        values[3][0]["utc"] = stamp(0)
        self.validate(values)

    def test_late_exit_discovery_after_run_finished_is_valid(self):
        values = fixture(self.output)
        rows = values[0]
        attached = rows.pop(2)
        rows.insert(7, attached)
        for index, row in enumerate(rows):
            row.update(sequence=index, phase_token=index, utc=stamp(index), clock_ticks=1000 + index * 10, frame=index if index <= 9 else -1,
                       exit_entity_id=999 if index >= 7 else 0)
        values[1]["initial"]["readUtc"] = stamp(.5)
        for own, original in zip([row for row in rows if row["kind"] in ("run-started", "test-started", "test-finished", "run-finished")], values[3]):
            original["utc"] = own["utc"]
        self.validate(values)

    def test_forged_process_run_output_or_exit_identity_is_rejected(self):
        for field, value in (("pid", 124), ("run_id", "b" * 32), ("output_directory", str(self.output.parent)), ("exit_entity_id", 998)):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: pre.update({field: value}))

    def test_non_boolean_or_denied_quit_decision_is_rejected(self):
        for value in (False, 1, "true", None):
            with self.subTest(value=value):
                self.rejected(lambda rows, pre, terminal, events: rows[10].update(quit_allowed=value))

    def test_missing_duplicate_or_wrong_terminal_markers_are_rejected(self):
        for index, kind in ((12, "application-quit"), (11, "destroyed"), (10, "audio-closed")):
            with self.subTest(index=index):
                self.rejected(lambda rows, pre, terminal, events: rows[index].update(kind=kind))

    def test_incomplete_cleanup_error_or_early_destroy_is_rejected(self):
        for field, value in (("prequit_observed", False), ("audio_closed", False), ("subscribed", True), ("wants_subscribed", True),
                             ("application_quit_count", 0), ("on_destroy_count", 0), ("exit_detach_count", 0), ("errors", ["I/O failure"])):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: terminal.update({field: value}))

    def test_callback_loss_pending_overflow_and_unsubscribe_fault_fail(self):
        for field, value in (("queue_drops", 1), ("record_drops", 1), ("pending_callbacks", 1), ("counter_overflow", True),
                             ("subscribed", True), ("unsubscribe_attempts", 2), ("errors", ["detach failed"])):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: pre.update({field: value}))

    def test_foreign_or_missing_original_utf_token_fails(self):
        for field, value in (("original_id", "other"), ("full_name", e.CASES[1]), ("is_suite", True), ("result", "Skipped")):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: rows[3].update({field: value}))

    def test_callback_timestamp_thread_delay_and_scene_forgery_fail(self):
        for field, value in (("callbackUtc", stamp(10)), ("callbackClockTicks", 1100), ("callbackThreadId", 0),
                             ("callbackPhaseToken", 10), ("drainedPhaseToken", 9), ("observedDelaySeconds", .1), ("deviceWasChanged", 1)):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: pre["retained"][0].update({field: value}), True)
        for field, value in (("readThreadId", 2), ("readUtc", stamp(13)), ("readClockTicks", 1090), ("sceneHandle", 778)):
            with self.subTest(configuration=field):
                self.rejected(lambda rows, pre, terminal, events: pre["retained"][0]["configuration"].update({field: value}), True)

    def test_final_configuration_after_quit_and_unavailable_metadata_fail(self):
        for field, value in (("readUtc", stamp(10)), ("readClockTicks", 1100), ("sampleRate", 0), ("sceneLoaded", False), ("frame", 20)):
            with self.subTest(field=field):
                self.rejected(lambda rows, pre, terminal, events: pre["final"].update({field: value}))

    def test_teardown_unity_frame_or_marker_off_main_thread_fail(self):
        self.rejected(lambda rows, pre, terminal, events: rows[10].update(frame=123))
        self.rejected(lambda rows, pre, terminal, events: rows[11].update(thread_id=2))

    def test_duplicate_json_and_truncated_hashes_are_rejected(self):
        raw, events = encode(fixture(self.output))
        raw[e.FILES[1]] = raw[e.FILES[1]].replace(b'"pid":123', b'"pid":123,"pid":123')
        with self.assertRaises(AssertionError):
            e.validate(raw, 123, self.output, stamp(0), stamp(15), events)
        raw, events = encode(fixture(self.output))
        raw[e.FILES[0]] = raw[e.FILES[0]][:-1]
        with self.assertRaises(AssertionError):
            e.validate(raw, 123, self.output, stamp(0), stamp(15), events)

    def test_closed_output_files_are_hash_bound(self):
        raw, events = encode(fixture(self.output))
        for name, body in raw.items():
            (self.output / name).write_bytes(body)
        result = e.validate_output(self.output, 123, stamp(0), stamp(15), events_raw=events)
        self.assertEqual(result["files"][e.FILES[0]]["sha256"], e.digest(raw[e.FILES[0]]))
        (self.output / e.FILES[1]).write_bytes(b"{}")
        with self.assertRaises(AssertionError):
            e.validate_output(self.output, 123, stamp(0), stamp(15), events_raw=events)

    def test_symlink_path_guard_rejects_before_read_without_native_symlink_calls(self):
        raw, events = encode(fixture(self.output))
        for name, body in raw.items():
            (self.output / name).write_bytes(body)
        with mock.patch.object(Path, "is_symlink", return_value=True):
            with self.assertRaisesRegex(AssertionError, "Linked lifecycle evidence"):
                e.validate_output(self.output, 123, stamp(0), stamp(15), events_raw=events)

    def test_pre_initial_and_pre_drain_phase_configuration_forgery_is_rejected(self):
        self.rejected(lambda rows, pre, terminal, events: pre["retained"][0]["configuration"].update(readUtc=stamp(.4), readClockTicks=1004), True)
        self.rejected(lambda rows, pre, terminal, events: pre["retained"][0]["configuration"].update(readUtc=stamp(4.5), readClockTicks=1045), True)

    def test_actual_callback_pair_order_and_booleans_are_strict(self):
        self.rejected(lambda rows, pre, terminal, events: events[2].update(utc=stamp(2)))
        self.rejected(lambda rows, pre, terminal, events: events[1].update(is_suite=0))

    def test_optional_event_fallback_rejects_oversize_and_link_before_read(self):
        raw, events = encode(fixture(self.output))
        for name, body in raw.items():
            (self.output / name).write_bytes(body)
        events_path = self.output / "events.jsonl"
        events_path.write_bytes(b"x" * (e.LIMIT + 1))
        original_open = Path.open
        opened = []

        def observe_open(path, *args, **kwargs):
            opened.append(path)
            return original_open(path, *args, **kwargs)

        with mock.patch.object(Path, "open", autospec=True, side_effect=observe_open):
            with self.assertRaisesRegex(AssertionError, "oversized regular"):
                e.validate_output(self.output, 123, stamp(0), stamp(15))
        self.assertNotIn(events_path, opened)
        events_path.write_bytes(events)
        opened.clear()
        with mock.patch.object(Path, "is_symlink", autospec=True, side_effect=lambda path: path == events_path):
            with mock.patch.object(Path, "open", autospec=True, side_effect=observe_open):
                with self.assertRaisesRegex(AssertionError, "Linked lifecycle"):
                    e.validate_output(self.output, 123, stamp(0), stamp(15))
        self.assertNotIn(events_path, opened)

    def test_growing_file_actual_read_is_capped(self):
        target = self.output / "bounded.json"
        target.write_bytes(b"{}")
        stream = mock.MagicMock()
        stream.__enter__.return_value.read.return_value = b"x" * (e.LIMIT + 1)
        with mock.patch.object(Path, "open", return_value=stream):
            with self.assertRaisesRegex(AssertionError, "grew beyond"):
                e.read_closed(target)
        stream.__enter__.return_value.read.assert_called_once_with(e.LIMIT + 1)


class CallbackAssemblyRegistrationTests(unittest.TestCase):
    def test_observer_namespace_escapes_the_unchanged_route_isolation_predicate(self):
        root = TOOL.parents[1]
        source = (root / "Assets/Tests/PlayMode/AudioRunLifecycleObserver.cs").read_text(encoding="utf-8-sig")
        observer = source.index("public sealed class AudioRunLifecycleObserver : MonoBehaviour")
        namespaces = list(re.finditer(r"\bnamespace\s+([A-Za-z0-9_.]+)", source[:observer]))
        self.assertTrue(namespaces)
        self.assertEqual(namespaces[-1][1], "ForsakenPassiveAudioLifecycleTestSupport")
        self.assertFalse(namespaces[-1][1].startswith("ShadowsOfTheForsaken"))
        fixture = (root / "Assets/Tests/PlayMode/FullCastleRouteTests.cs").read_text(encoding="utf-8-sig")
        self.assertIn('item.GetType().Namespace?.StartsWith("ShadowsOfTheForsaken", StringComparison.Ordinal) == true', fixture)

    def test_one_callback_attribute_per_actual_assembly_boundary(self):
        root = TOOL.parents[1]
        assets = root / "Assets"
        registrations = {}
        for source in (assets / "Tests").rglob("*.cs"):
            declarations = re.findall(r"^\s*\[assembly:\s*TestRunCallback\(typeof\(([^)]+)\)\)\]", source.read_text(encoding="utf-8-sig"), re.M)
            if not declarations:
                continue
            parent = source.parent
            while parent != assets:
                assemblies = list(parent.glob("*.asmdef"))
                if assemblies:
                    self.assertEqual(len(assemblies), 1)
                    assembly = json.loads(assemblies[0].read_text(encoding="utf-8-sig"))["name"]
                    break
                parent = parent.parent
            else:
                self.fail("Callback declaration has no assembly definition: " + str(source))
            registrations.setdefault(assembly, []).extend(declarations)
        self.assertEqual(registrations["Shadows.PlayMode.Tests"], ["ShadowsOfTheForsaken.Tests.PlayMode.StandaloneMainTestCallbacks"])
        self.assertEqual(registrations["Shadows.StandaloneRenderedRoutes.PlayerSupport"], ["ForsakenStandaloneRenderedRouteSupport.StandaloneRenderedRoutePlayerEvidence"])
        self.assertEqual(registrations["Shadows.StandaloneAudio.PlayerSupport"], ["ForsakenStandaloneAudioTestSupport.StandaloneAudioPlayerEvidence"])
        self.assertEqual(registrations["Shadows.StandalonePresentation.PlayerSupport"], ["ForsakenStandalonePresentationTestSupport.StandalonePresentationPlayerEvidence"])
        self.assertTrue(all(len(values) == 1 for values in registrations.values()))


if __name__ == "__main__":
    unittest.main()
