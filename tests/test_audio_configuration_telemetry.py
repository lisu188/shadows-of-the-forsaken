"""Synthetic passive-observer evidence regressions; no Unity or OS audio calls."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_telemetry_tool", ROOT / "tools/run_standalone_audio.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)
START, END = "2026-10-16T14:00:00Z", "2026-10-16T14:00:05Z"


def stamp(seconds):
    return "2026-10-16T14:00:%010.7fZ" % seconds


def evidence(output, path, events=True):
    def config(seconds):
        return dict(readUtc=stamp(seconds), readClockTicks=round(seconds * 1000), readThreadId=4,
                    sampleRate=48000, dspBufferSize=1024, numRealVoices=32, numVirtualVoices=512,
                    speakerMode="Stereo", frame=10, sceneHandle=1, scenePath="Assets/Synthetic.unity", sceneLoaded=True)
    names = ["prepared", "prime-and-baseline", "playing", "signal-captured", "stopped-silence",
             "silence-captured", "cleanup", "observer-closing"]
    phases = [dict(token=i, name=name, utc=stamp(1 + i * .4), clockTicks=1000 + i * 400, frame=10)
              for i, name in enumerate(names)]
    retained = [dict(sequence=1, callbackUtc=stamp(1.05), callbackClockTicks=1050, callbackThreadId=7,
                     callbackPhaseToken=0, deviceWasChanged=True, drainedPhaseToken=2,
                     configuration=config(1.9), observedDelaySeconds=.85)] if events else []
    return dict(schemaVersion=1, profile="audio-mixer-passive-configuration", pid=42,
                caseFullName=audio.AUDIO_CASE, outputDirectory=str(output), evidencePath=str(path),
                probeSessionId="a" * 32, mainThreadId=4, sceneHandle=1, scenePath="Assets/Synthetic.unity",
                sceneName="Synthetic", sceneLoaded=True, beganUtc=stamp(1), closedUtc=stamp(4),
                closed=True, ownedObserverDestroyed=True, subscribed=False, acceptingCallbacks=False,
                counterOverflow=False, phaseLimitReached=False, errors=[], queueCapacity=128, recordCapacity=256,
                stopwatchFrequency=1000, initial=config(1.1), final=config(3.99), phases=phases,
                retained=retained, callbackCount=len(retained), queueDrops=0, recordDrops=0, pendingCallbacks=0)


class AudioConfigurationTelemetryTests(unittest.TestCase):
    def check(self, value, output, path):
        return audio.validate_audio_configuration(value, 42, output, path, START, END)

    def test_zero_actual_events_is_valid_and_no_recovery_or_audibility_is_claimed(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            result = self.check(evidence(output, path, events=False), output, path)
            self.assertEqual(result["callback_count"], 0)
            self.assertIn("No device recovery", result["scope"])

    def test_callback_before_initial_read_is_valid_when_later_main_drain_is_measured(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            value = evidence(output, path)
            self.assertLess(value["retained"][0]["callbackClockTicks"], value["initial"]["readClockTicks"])
            self.assertEqual(self.check(value, output, path)["callback_count"], 1)
            value["retained"][0]["deviceWasChanged"] = False
            self.assertEqual(self.check(value, output, path)["callback_count"], 1)

    def test_process_case_scene_and_lease_paths_cannot_be_rebound(self):
        mutations = [dict(pid=43), dict(pid=True), dict(caseFullName="Other"), dict(probeSessionId="bad"),
                     dict(outputDirectory="relative"), dict(evidencePath="relative"), dict(sceneLoaded=False),
                     dict(sceneHandle=True), dict(sceneName=""), dict(mainThreadId=False), dict(schemaVersion=2), dict(schemaVersion=True)]
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            for change in mutations:
                with self.subTest(change=change), self.assertRaises(ValueError):
                    self.check({**evidence(output, path), **change}, output, path)

    def test_lifecycle_losses_overflow_or_subscription_leaks_fail_even_with_pcm_pass(self):
        changes = [dict(closed=False), dict(ownedObserverDestroyed=False), dict(subscribed=True),
                   dict(acceptingCallbacks=True), dict(counterOverflow=True), dict(phaseLimitReached=True),
                   dict(errors=["failure"]), dict(queueCapacity=129), dict(recordCapacity=257),
                   dict(callbackCount=2), dict(queueDrops=1, callbackCount=2),
                   dict(recordDrops=1, callbackCount=2), dict(pendingCallbacks=1, callbackCount=2)]
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            for change in changes:
                with self.subTest(change=change), self.assertRaises(ValueError):
                    self.check({**evidence(output, path), **change}, output, path)

    def test_drain_thread_configuration_and_actual_delay_are_checked(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            changes = [dict(sequence=2), dict(deviceWasChanged=1), dict(callbackThreadId=0),
                       dict(callbackPhaseToken=8), dict(drainedPhaseToken=-1), dict(callbackClockTicks=2000),
                       dict(observedDelaySeconds=.1), dict(observedDelaySeconds=float("nan")),
                       dict(callbackUtc=stamp(.9))]
            for change in changes:
                value = evidence(output, path); value["retained"][0].update(change)
                with self.subTest(change=change), self.assertRaises(ValueError):
                    self.check(value, output, path)
            for change in (dict(readThreadId=7), dict(sceneHandle=2), dict(sceneLoaded=False), dict(sampleRate=0),
                           dict(speakerMode="unknown"), dict(dspBufferSize=True), dict(readUtc=stamp(5))):
                value = evidence(output, path); value["retained"][0]["configuration"].update(change)
                with self.subTest(change=change), self.assertRaises(ValueError):
                    self.check(value, output, path)
            # These forged reads previously passed the general lease bounds.
            # Keep callback-before-initial valid; only the later main drain
            # must follow initial read and its sampled main-thread phase.
            for change in (dict(readUtc=stamp(1.06)), dict(readClockTicks=1060),
                           dict(readUtc=stamp(1.7)), dict(readClockTicks=1700)):
                value = evidence(output, path); item = value["retained"][0]
                item["configuration"].update(change)
                item["observedDelaySeconds"] = (item["configuration"]["readClockTicks"] - item["callbackClockTicks"]) / 1000
                with self.subTest(forged_earlier_drain=change), self.assertRaisesRegex(ValueError, "Drain configuration predates"):
                    self.check(value, output, path)

    def test_initial_final_utc_and_capture_phases_have_exact_bounded_order(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "evidence.json"
            for change in ("missing", "duplicate", "order", "clock", "frame", "late-final", "offset", "initial-worker"):
                value = evidence(output, path)
                if change == "missing": value["phases"].pop()
                elif change == "duplicate": value["phases"].append(copy.deepcopy(value["phases"][-1]))
                elif change == "order": value["phases"][2]["name"] = "cleanup"
                elif change == "clock": value["phases"][2]["clockTicks"] = 1
                elif change == "frame": value["phases"][2]["frame"] = 1
                elif change == "late-final": value["final"]["readUtc"] = stamp(4.1)
                elif change == "offset": value["beganUtc"] = "2026-10-16T16:00:01+02:00"
                else: value["initial"]["readThreadId"] = 7
                with self.subTest(change=change), self.assertRaises(ValueError): self.check(value, output, path)

    def test_same_mixer_hash_and_file_are_required_by_separate_telemetry_validator(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp).resolve(); path = output / "audio/evidence.json"; path.parent.mkdir()
            path.write_text(json.dumps(dict(audioConfigurationTelemetry=evidence(output, path))), encoding="utf-8")
            mixer = dict(path="audio/evidence.json", sha256=hashlib.sha256(path.read_bytes()).hexdigest())
            result = audio.validate_audio_configuration_output(output, 42, mixer, START, END)
            self.assertEqual(result["sha256"], mixer["sha256"])
            path.write_text(path.read_text() + " ", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "changed between validators"):
                audio.validate_audio_configuration_output(output, 42, mixer, START, END)

    def test_timestamp_flag_is_only_addition_to_actual_launch_arguments(self):
        base = ["Synthetic.exe", "-parentHWND", "10", "-force-d3d11", "-logFile", "synthetic.log"]
        with mock.patch.object(audio.native, "player_command", return_value=base) as original:
            command = audio.audio_player_command(Path("Synthetic.exe"), Path("Output"), 10)
        original.assert_called_once_with(Path("Synthetic.exe"), Path("Output"), "legacy", 10, "embedded")
        self.assertEqual(command, [*base, "-timestamps"])
        self.assertEqual(base[-1], "synthetic.log")
        with mock.patch.object(audio.native, "player_command", return_value=[*base, "-timestamps"]):
            with self.assertRaises(ValueError): audio.audio_player_command(Path("Synthetic.exe"), Path("Output"), 10)
        for command, expected in ((base, False), ([*base, "-timestamps"], True),
                                  ([*base, "-timestamps", "-timestamps"], False), (None, False)):
            record = dict(errors=[], command=command, audio_configuration_telemetry_verified=True)
            audio.qualify(record)
            self.assertIs(record["qualification_checks"]["audio_configuration_telemetry_verified"], expected)


if __name__ == "__main__":
    unittest.main()
