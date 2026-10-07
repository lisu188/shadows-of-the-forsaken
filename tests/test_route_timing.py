"""Synthetic observer logs test the timing reader, never engine/human acceptance."""
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from contextlib import redirect_stderr

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import report_route_timing as timing


class RouteTimingTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.session = "0b274da0-0986-4e35-8631-2be2a359196a"
        self.events = self.make_events()
        histogram = [0] * 2049
        histogram[39] = 15000
        self.stats = dict(observedFrames=15501, measuredFrames=15000, measuredSeconds=150,
                          warmupObservedEligibleSeconds=5, hasSamples=True, histogram=histogram,
                          histogramBucketMilliseconds=.25, histogramFiniteBuckets=2048,
                          warmupRequiredEligibleSeconds=5, averageMilliseconds=10, maximumMilliseconds=10,
                          excluded=[dict(reason=reason, frames=500 if reason == "Warmup" else 1 if reason == "Transition" else 0,
                                         milliseconds=5000 if reason == "Warmup" else 0)
                                    for reason in sorted(timing.EXCLUSIONS)])
        self.hardware = dict(unityVersion="6000.6.3f1", isEditor=False, processor="Synthetic CPU",
                             graphicsDevice="Synthetic GPU", platform="WindowsPlayer")
        self.performance = [dict(utc=self.stamp(155), reason="completion", sessionId=self.session,
                                 frameIntervals=copy.deepcopy(self.stats), hardware=self.hardware,
                                 screenWidth=1280, screenHeight=720, fullScreenMode="Windowed",
                                 qualityLevel=1, vSyncCount=1, targetFrameRate=-1),
                            dict(utc=self.stamp(156), reason="normal-exit", sessionId=self.session,
                                 frameIntervals=copy.deepcopy(self.stats), hardware=self.hardware,
                                 screenWidth=1280, screenHeight=720, fullScreenMode="Windowed",
                                 qualityLevel=1, vSyncCount=1, targetFrameRate=-1)]
        self.status = dict(utc=self.stamp(156), sequence=200, sessionId=self.session, phase="Completed",
                           room="Exit", objectiveFlags=79, elapsedSeconds=155, player=dict(health=200),
                           performanceSummariesWritten=2, performanceSummaryLimitReached=False,
                           hardware=self.hardware, performance=dict(measuredFrames=15000))
        archive = self.write("source.zip", b"Synthetic input bytes; no Unity build was executed.")
        receipt = self.write("BUILD-INFO.json", dict(source_commit="a" * 40, source_archive_sha256=archive["sha256"],
                                                    result="Succeeded", unity="6000.6.3f1"))
        self.metadata = dict(schema_version=1, run_id="synthetic-basic", session_id=self.session,
                             revision="a" * 40, input_origin="human", route_familiarity="first_attempt",
                             route="basic", interruptions_observed=False, build_receipt=receipt,
                             source_archive=archive, files={})

    def stamp(self, seconds):
        return (datetime(2026, 1, 1, tzinfo=timezone.utc) + timedelta(seconds=seconds)).isoformat()

    def make_events(self):
        events, room, flags = [], "Courtyard", 0

        def add(seconds, kind, destination=None, objective="None", phase="Running"):
            nonlocal room, flags
            previous = room
            if destination:
                room = destination
            if objective != "None":
                flags |= timing.OBJECTIVES[objective]
            events.append(dict(utc=self.stamp(seconds), kind=kind, sequence=len(events) * 5 + 1,
                               phase=phase, sessionId=self.session, beforeSessionId=self.session,
                               room=room, beforeRoom=previous, objective=objective, objectiveFlags=flags,
                               elapsedSeconds=seconds, change="RoomEntered" if destination else
                               "ObjectiveCompleted" if objective != "None" else ""))
        add(0, "started")
        add(20, "progression", "FirstEncounter")
        add(35, "progression", objective="FirstEnemyDefeated")
        add(40, "progression", "Puzzle")
        add(55, "progression", objective="MainPuzzleSolved")
        add(58, "progression", "FirstEncounter")
        add(65, "progression", "ThroneRoom")
        add(90, "progression", objective="MinibossDefeated")
        add(95, "progression", "Library")
        add(110, "progression", objective="LibraryOpened")
        add(115, "progression", "Catacombs")
        add(125, "progression", "FinalArena")
        add(150, "progression", objective="FinalEnemyDefeated")
        # Actual listener ordering: session completion precedes Exit's
        # progression callback, and already observes the updated snapshot.
        add(155, "session", phase="Completed")
        events[-1].update(room="Exit", beforeRoom="Exit")
        add(155, "progression", "Exit", phase="Completed")
        add(155, "normal-exit", phase="Completed")
        events[-1]["utc"] = self.stamp(156)
        return events

    def write(self, name, value):
        raw = value if isinstance(value, bytes) else json.dumps(value).encode()
        (self.root / name).write_bytes(raw)
        return dict(path=name, sha256=hashlib.sha256(raw).hexdigest())

    def save(self, name="run.json"):
        self.metadata["files"] = {
            "events": self.write("events.jsonl", ("\n".join(json.dumps(row) for row in self.events) + "\n").encode()),
            "performance": self.write("performance.jsonl", ("\n".join(json.dumps(row) for row in self.performance) + "\n").encode()),
            "status": self.write("status.json", self.status),
        }
        self.write(name, self.metadata)
        return self.root / name

    def reject(self, message):
        result = timing.report([self.save()])
        self.assertEqual("rejected", result["status"])
        self.assertFalse(result["acceptance_passed"])
        self.assertIn(message, result["runs"][0]["reason"])

    def add_initialization_prefix(self):
        initial = "20000000-0000-0000-0000-000000000000"
        started = copy.deepcopy(self.events[0])
        started.update(phase="Resetting", sessionId=initial, beforeSessionId=initial)
        reset = copy.deepcopy(started)
        reset.update(kind="progression", change="SessionReset", sessionId=self.session)
        self.events[0]["kind"] = "session"
        self.events[:0] = [started, reset]
        for index, event in enumerate(self.events):
            event["sequence"] = index * 5 + 1
        reset_summary = copy.deepcopy(self.performance[0])
        reset_summary.update(utc=self.stamp(0), reason="reset", sessionId=initial)
        reset_summary["frameIntervals"].update(observedFrames=0, measuredFrames=0, measuredSeconds=0,
                                               warmupObservedEligibleSeconds=0, hasSamples=False,
                                               histogram=[0] * 2049, averageMilliseconds=0, maximumMilliseconds=0)
        for item in reset_summary["frameIntervals"]["excluded"]:
            item.update(frames=0, milliseconds=0)
        self.performance.insert(0, reset_summary)
        self.status["performanceSummariesWritten"] = 3

    def test_complete_trace_reports_only_supported_intervals_and_never_acceptance(self):
        path = self.save()
        before = {p.name: p.read_bytes() for p in self.root.iterdir()}
        result = timing.report([path])
        self.assertEqual("review_input", result["status"])
        run = result["runs"][0]
        self.assertEqual(155, run["seconds"])
        self.assertEqual(20, run["stage_totals_seconds"]["calm_introduction"])
        self.assertEqual(65, run["stage_totals_seconds"]["encounter_room_before_objective"])
        self.assertEqual(30, run["stage_totals_seconds"]["mechanism_room_before_objective"])
        self.assertEqual(40, run["stage_totals_seconds"]["other_traversal_or_exploration"])
        self.assertEqual(1, result["distinct_complete_runs"]["human_standalone"]["basic"])
        self.assertFalse(result["acceptance_passed"], "Even synthetic 155 seconds cannot certify DOCX pacing")
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.root.iterdir()})

    def test_secret_trace_is_distinguished_from_basic(self):
        self.metadata["route"] = "secret"
        # Insert optional milestones in the observed catacomb interval.
        additions = []
        for seconds, change, room, previous, objective, flags in [
            (116, "ObjectiveCompleted", "Catacombs", "Catacombs", "SecretLeverPulled", 31),
            (117, "RoomEntered", "BonusRoom", "Catacombs", "None", 31),
            (121, "ObjectiveCompleted", "BonusRoom", "BonusRoom", "BonusDiscovered", 63),
            (124, "RoomEntered", "Catacombs", "BonusRoom", "None", 63),
        ]:
            event = copy.deepcopy(self.events[10])
            event.update(utc=self.stamp(seconds), elapsedSeconds=seconds, change=change, room=room,
                         beforeRoom=previous, objective=objective, objectiveFlags=flags)
            additions.append(event)
        self.events[11:11] = additions
        for index, event in enumerate(self.events):
            event["sequence"] = index * 5 + 1
            if index >= 15:
                event["objectiveFlags"] |= 48
        self.status["objectiveFlags"] = 127
        result = timing.report([self.save()])
        self.assertEqual("review_input", result["status"], result)
        self.assertEqual(7, result["runs"][0]["stage_totals_seconds"]["optional_room"])
        self.metadata["route"] = "basic"
        self.reject("optional objectives")

    def test_exact_zero_time_start_initialization_has_no_lost_or_invented_segment(self):
        self.add_initialization_prefix()
        before = copy.deepcopy(self.events)
        result = timing.report([self.save()])
        self.assertEqual("review_input", result["status"], result)
        run = result["runs"][0]
        self.assertTrue(run["initialization_prefix"]["observed"])
        self.assertEqual(self.session, run["session_id"])
        self.assertEqual(155, run["seconds"])
        self.assertEqual(20, run["stage_totals_seconds"]["calm_introduction"])
        self.assertEqual(before, self.events)
        self.assertFalse(result["acceptance_passed"])

    def test_initialization_prefix_rejects_play_time_and_frame_observations(self):
        self.add_initialization_prefix()
        self.events[0]["elapsedSeconds"] = .1
        self.reject("elapsed gameplay")
        self.events[0]["elapsedSeconds"] = 0
        self.performance[0]["frameIntervals"]["observedFrames"] = 1
        self.reject("frame observations")

    def test_zero_time_reset_after_running_is_not_initialization(self):
        self.add_initialization_prefix()
        self.events[0]["phase"] = "Running"
        self.reject("stitched")

    def test_reset_after_initialized_gameplay_is_still_rejected(self):
        self.add_initialization_prefix()
        self.events[7]["change"] = "SessionReset"
        self.reject("Reset or unknown")

    def test_automation_and_editor_never_count_as_standalone_human(self):
        for origin, editor, group in [("automation", False, "automation"), ("human", True, "human_editor")]:
            with self.subTest(origin=origin, editor=editor):
                self.metadata["input_origin"] = origin
                self.hardware["isEditor"] = editor
                result = timing.report([self.save()])
                self.assertEqual(1, result["distinct_complete_runs"][group]["basic"])
                self.assertFalse(result["runs"][0]["human_review_candidate"])
                self.assertFalse(result["acceptance_passed"])

    def test_requires_origin_familiarity_revision_and_uninterrupted_declaration(self):
        for key in ("input_origin", "route_familiarity", "revision", "interruptions_observed"):
            with self.subTest(key=key):
                previous = self.metadata.pop(key)
                self.assertEqual("rejected", timing.report([self.save()])["status"])
                self.metadata[key] = previous

    def test_rejects_hash_changes_and_wrong_source_revision(self):
        path = self.save()
        (self.root / "events.jsonl").write_text("{}\n")
        self.assertIn("SHA-256 mismatch", timing.report([path])["runs"][0]["reason"])
        self.metadata["revision"] = "b" * 40
        self.reject("declared revision")

    def test_rejects_missing_completion_death_and_observer_stop(self):
        original = copy.deepcopy(self.events)
        for edit, expected in [
            (lambda: self.events.pop(13), "Missing completion"),
            (lambda: self.events[6].update(phase="Defeated"), "Defeated"),
            (lambda: self.events[-1].update(kind="observer-disabled"), "normal exit"),
            (lambda: self.events[5].update(change="SessionReset"), "Reset"),
        ]:
            self.events = copy.deepcopy(original)
            edit()
            self.reject(expected)

    def test_rejects_truncated_trace_and_missing_performance(self):
        self.events = self.events[:8]
        self.reject("normal exit")
        self.events = self.make_events()
        self.performance = []
        self.reject("JSONL record")

    def test_rejects_stitching_duplicate_events_and_missing_objectives(self):
        original = copy.deepcopy(self.events)
        for edit, message in [
            (lambda: self.events[4].update(sessionId="20000000-0000-0000-0000-000000000000"), "stitched"),
            (lambda: self.events.insert(4, copy.deepcopy(self.events[3])), "duplicate"),
            (lambda: self.events.pop(2), "prerequisites"),
            (lambda: self.events[6].update(beforeRoom="Courtyard"), "continuity"),
        ]:
            self.events = copy.deepcopy(original)
            edit()
            self.reject(message)

    def test_rejects_pause_focus_loss_and_no_frame_gap(self):
        original = copy.deepcopy(self.performance)
        for reason in ("Paused", "Unfocused", "Invalid", "Transition"):
            self.performance = copy.deepcopy(original)
            excluded = self.performance[0]["frameIntervals"]["excluded"]
            next(item for item in excluded if item["reason"] == reason)["frames"] += 1
            self.performance[0]["frameIntervals"]["observedFrames"] += 1
            self.reject("interruption" if reason == "Transition" else "disqualify")
        self.performance = original
        for event in self.events[3:]:
            event["utc"] = self.stamp(event["elapsedSeconds"] + 10)
        self.reject("Wall/session clock gap")

    def test_rejects_bad_histogram_and_foreign_performance(self):
        self.performance[0]["frameIntervals"]["histogram"][0] += 1
        self.reject("histogram count")
        self.performance[0]["frameIntervals"]["histogram"][0] -= 1
        self.performance[1]["sessionId"] = "20000000-0000-0000-0000-000000000000"
        self.reject("another session")

    def test_rejects_partial_performance_coverage_and_stale_final_status(self):
        for row in self.performance:
            stats = row["frameIntervals"]
            stats.update(measuredSeconds=5, measuredFrames=500, observedFrames=1001)
            stats["histogram"][39] = 500
        self.reject("Performance coverage")
        for row in self.performance:
            row["frameIntervals"] = copy.deepcopy(self.stats)
        self.status["sequence"] = 1
        self.reject("predates")

    def test_rejects_duration_changes_hidden_behind_a_valid_histogram_count(self):
        self.performance[0]["frameIntervals"]["measuredSeconds"] = 5
        self.reject("Frame duration disagrees")

    def test_rejects_early_completion_and_nonfinite_event_time(self):
        self.events.insert(12, self.events.pop(13))
        for index, event in enumerate(self.events):
            event["sequence"] = index * 5 + 1
        self.reject("completion")
        self.events = self.make_events()
        self.events[4]["elapsedSeconds"] = float("inf")
        self.reject("Nonfinite")

    def test_rejects_duplicate_runs_in_collection(self):
        path = self.save()
        result = timing.report([path, path])
        self.assertEqual("rejected", result["status"])
        self.assertTrue(result["collection_rejections"])
        self.assertEqual(0, result["distinct_complete_runs"]["human_standalone"]["basic"])

    def test_rejects_mixed_source_collection(self):
        first = timing.report_run(self.save())
        second = copy.deepcopy(first)
        second.update(run_id="another", session_id="20000000-0000-0000-0000-000000000000", revision="b" * 40)
        second["provenance"]["files"]["events"]["sha256"] = "c" * 64
        from unittest.mock import patch
        with patch.object(timing, "report_run", side_effect=[first, second]):
            result = timing.report(["first", "second"])
        self.assertEqual("rejected", result["status"])
        self.assertIn("Mixed revisions", result["collection_rejections"][0])

    def test_paths_are_contained_and_output_never_overwrites_evidence(self):
        path = self.save()
        before = (self.root / "events.jsonl").read_bytes()
        with redirect_stderr(io.StringIO()):
            self.assertEqual(1, timing.main([str(path), "--output", str(self.root / "events.jsonl")]))
        self.assertEqual(before, (self.root / "events.jsonl").read_bytes())
        self.metadata["source_archive"]["path"] = "../outside.zip"
        self.reject("escapes")

    def test_duplicate_json_keys_nonfinite_and_size_limits_are_rejected(self):
        for raw in (b'{"schema_version":1,"schema_version":1}', b'{"seconds":NaN}'):
            with self.subTest(raw=raw):
                self.write("bad.json", raw)
                self.assertEqual("rejected", timing.report([self.root / "bad.json"])["status"])
        path = self.save()
        from unittest.mock import patch
        with patch.object(timing, "MAX_JSON", 8):
            self.assertIn("size limit", timing.report([path])["runs"][0]["reason"])


if __name__ == "__main__":
    unittest.main()
