"""Run the separate instrumented native audio mixer fixture hidden, with exact results.

This is test-only native source/DSP/master processing, not physical audibility or game audio acceptance.
The child must finish its own tests and exit; runner intervention never qualifies.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def load_tool(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


native = load_tool("native_player_validation")
unity = load_tool("unity_validation")
PREFIX = "ShadowsOfTheForsaken.Tests.PlayMode."
AUDIO_CASE = PREFIX + "AudioMixerPlaybackTests.QuietClipTraversesMixerAndReturnsToSilence"
EXPECTED = frozenset({AUDIO_CASE})
if AUDIO_CASE.removeprefix(PREFIX) not in unity.EXPECTED_CASES["playmode"]:
    raise ValueError("The pinned Editor catalog must include the exact audio mixer fixture")
RESERVE = 32 * 2**30
GROWTH_LIMIT = 5 * 2**30
require = native.require


def exact_cases(names):
    require(type(names) is list and all(type(name) is str for name in names), "Case names must be a JSON/string list")
    require(len(names) == len(EXPECTED) and len(set(names)) == len(names) and set(names) == EXPECTED,
            "Expected exactly the one native audio mixer case")


def successful_counts(value, total=1):
    for key, expected in (("passed", total), ("failed", 0), ("skipped", 0), ("inconclusive", 0)):
        require(type(value.get(key)) is int and value[key] == expected,
                "Invalid successful result counter: " + key)


def validate_results(raw):
    require(len(raw) <= 10_000_000, "NUnit XML exceeds the 10 MB limit")
    require(b"<!DOCTYPE" not in raw.upper() and b"<!ENTITY" not in raw.upper(), "DTD/entities refused")
    root = ET.fromstring(raw)
    require(root.tag in ("test-run", "test-suite"), "Unexpected NUnit result root")
    require(root.get("result") == "Passed", "NUnit root did not pass")
    counts = {key: int(root.attrib[key]) for key in ("total", "passed", "failed", "skipped", "inconclusive")}
    require(counts["total"] == len(EXPECTED), "Unexpected NUnit total")
    successful_counts(counts)
    cases = list(root.iter("test-case"))
    names = [case.get("fullname") for case in cases]
    exact_cases(names)
    require(all(node.get("result") == "Passed" for node in [*cases, *root.iter("test-suite")]),
            "A case or suite did not pass")
    duration = float(root.get("duration", "nan"))
    require(math.isfinite(duration) and duration >= 0, "Invalid NUnit duration")
    return {"root": root.tag, "counts": counts, "case_names": sorted(names),
            "duration_seconds": duration, "sha256": hashlib.sha256(raw).hexdigest()}


def validate_runtime(value, pid, output):
    require(value.get("schema_version") == 1, "Unknown runtime evidence schema")
    require(value.get("is_editor") is False and value.get("batch_mode") is False,
            "Expected a non-editor, non-batch embedded player")
    require(value.get("unity_version") == "6000.6.3f1" and value.get("platform") == "WindowsPlayer",
            "Runtime pin/platform differs")
    require(value.get("graphics_api") == "Direct3D11", "Expected observed Direct3D11")
    require(value.get("pid") == pid, "Runtime evidence belongs to another process")
    require(Path(value.get("output_directory", "")).absolute() == output, "Runtime output directory differs")
    require(type(value.get("expected_case_count")) is int and value["expected_case_count"] == 1,
            "Runtime selected case count differs")
    require(value.get("test_input_source") == "Test-only generated quiet clip", "Input origin differs")
    require(value.get("test_profile") == "audio-mixer", "Unexpected runtime profile")
    exact_cases(value.get("expected_cases", []))
    require(isinstance(value.get("hardware"), dict) and bool(value["hardware"]), "Missing hardware metadata")
    return value


def validate_events(raw):
    require(len(raw) <= 8 * 1024 * 1024, "Callback events exceed the bounded input limit")
    events = [json.loads(line, object_pairs_hook=native.manifest.unique_json)
              for line in raw.decode("utf-8-sig").splitlines() if line.strip()]
    require(2 <= len(events) <= 128, "Missing or excessive callback events")
    require(events[0].get("kind") == "run-started" and events[-1].get("kind") == "run-finished",
            "Callback run start/finish proof missing")
    require(sum(event.get("kind") == "run-started" for event in events) == 1 and
            sum(event.get("kind") == "run-finished" for event in events) == 1, "Duplicate callback run boundary")
    starts, finishes = {}, {}
    for index, event in enumerate(events):
        require(event.get("kind") in ("run-started", "test-started", "test-finished", "run-finished"),
                "Unknown callback event")
        stamp = dt.datetime.fromisoformat(event["utc"].replace("Z", "+00:00"))
        require(stamp.tzinfo is not None, "Callback UTC timestamp lacks offset")
        if event["kind"] in ("test-started", "test-finished"):
            require(type(event.get("is_suite")) is bool, "Callback suite classification missing")
            if event["is_suite"]:
                if event["kind"] == "test-finished":
                    require(event.get("result") == "Passed", "Callback suite failed")
                continue
            name = event.get("full_name")
            target = starts if event["kind"] == "test-started" else finishes
            require(name not in target, "Duplicate callback case event")
            target[name] = index
            if event["kind"] == "test-finished":
                require(event.get("result") == "Passed", "Callback case failed")
                successful_counts(event, 1)
    exact_cases(list(starts))
    exact_cases(list(finishes))
    require(all(starts[name] < finishes[name] for name in starts), "Case finished before it started")
    require(events[-1].get("result") == "Passed", "Callback run did not pass")
    successful_counts(events[-1])
    return {"count": len(events), "case_names": sorted(finishes),
            "sha256": hashlib.sha256(raw).hexdigest()}


def validate_completion(value, results, events):
    require(value.get("test_profile") == "audio-mixer", "Unexpected completion profile")
    exact_cases(value.get("expected_cases", []))
    require(value.get("schema_version") == 1, "Unknown completion schema")
    require(type(value.get("total")) is int and value["total"] == len(EXPECTED), "Completion total differs")
    successful_counts(value)
    exact_cases(value.get("case_names", []))
    require(type(value.get("exit_code")) is int and value["exit_code"] == 0,
            "Callback did not request a successful exit")
    require(value.get("results_sha256") == results["sha256"] and
            value.get("events_sha256") == events["sha256"], "Completion artifact hashes differ")
    return value


def validate_build_report(value, player):
    require(value.get("schema_version") == 1 and value.get("result") == "Succeeded", "Build did not succeed")
    require(value.get("test_profile") == "audio-mixer", "Build is not the scoped audio mixer profile")
    exact_cases(value.get("expected_cases", []))
    require(value.get("target") == "StandaloneWindows64" and type(value.get("errors")) is int and value["errors"] == 0,
            "Expected an error-free Windows x64 test build")
    require(type(value.get("bytes")) is int and value["bytes"] > 0, "Build size is not a final positive value")
    duration = value.get("duration_seconds")
    require(type(duration) in (int, float) and math.isfinite(duration) and duration > 0,
            "Build duration is not a final positive finite value")
    require(value.get("include_test_assemblies") is True and value.get("auto_run_player") is False,
            "Build must include tests without autorunning the player")
    require(value.get("connect_to_host") is False and value.get("development") is True,
            "Expected a disconnected development test player")
    require(Path(value.get("output_path", "")).absolute() == player, "Actual build output differs from player")
    scenes = value.get("scenes", [])
    require(isinstance(scenes, list) and len(scenes) == 2 and
            isinstance(scenes[0], str) and re.fullmatch(
                r"Assets/InitTestScene[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.unity", scenes[0]) and
            scenes[1] == "Assets/Scenes/ForsakenCastle.unity", "Expected exactly the UTF bootstrap then primary scene")
    return value


def snapshot_player(player):
    require(player.suffix.lower() == ".exe", "Expected a Windows test executable")
    native.manifest.regular(player)
    rows = native.manifest.file_tree(player.parent)
    require(any(row["path"].endswith("/Shadows.PlayMode.Tests.dll") for row in rows),
            "Instrumented PlayMode test assembly missing")
    require(any(row["path"].endswith("/Shadows.StandaloneAudio.PlayerSupport.dll") for row in rows),
            "Separate native audio callback assembly missing")
    return rows


def compare_identity(before, after):
    require(bool(before) and before == after, "Test-player files changed, were added, or were removed")
    return True


def stop_reason(elapsed, timeout, current_free, evidence_bytes):
    if elapsed >= timeout:
        return "timeout"
    if current_free < RESERVE:
        return "disk reserve"
    if evidence_bytes > GROWTH_LIMIT:
        return "evidence size limit"
    return None


def output_bytes(output):
    total = 0
    for current, folders, names in os.walk(output, followlinks=False):
        require(all(not (Path(current) / name).is_symlink() for name in folders), "Linked evidence directory refused")
        for name in names:
            path = Path(current) / name
            require(not path.is_symlink(), "Linked evidence file refused")
            try:
                total += path.stat().st_size
            except FileNotFoundError:
                pass  # Atomic observer/fixture file replacement can occur.
    return total


def player_environment(output, inherited=None):
    environment = dict(os.environ if inherited is None else inherited)
    for name in ("SHADOWS_ROUTE_PLAYER_OUTPUT", "SHADOWS_ROUTE_PLAYER_BUILD", "SHADOWS_AUDIO_PLAYER_BUILD",
                 "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR", "SHADOWS_CAPTURE_DIR"):
        environment.pop(name, None)
    environment["SHADOWS_AUDIO_PLAYER_OUTPUT"] = str(output)
    environment["SHADOWS_AUDIO_PROBE_OUTPUT"] = str(output / "audio")
    return environment


def observe_parent(user, callback, pid, host):
    """Read actual ownership/parentage; command flags alone are not evidence."""
    windows = native.own_windows(user, callback, pid, host)
    rows = native.describe_windows(user, windows, pid, user.GetForegroundWindow())
    for row in rows:
        if row["owner_pid"] == pid:
            row["actual_parent"] = int(user.GetParent(row["window"]) or 0)
    matches = [row for row in rows if row.get("actual_parent") == host and
               row.get("class") == "UnityWndClass" and row.get("style", 0) & 0x40000000]
    return {"utc": native.utc(), "player_pid": pid, "host_handle": host,
            "rows": rows, "matching_parent_observed": bool(matches)}


def finish_process(process, user, callback, record, parent):
    if process is not None and process.poll() is not None:
        record["closure"] = {"mode": "self_exit", "forced": False, "pid": process.pid,
            "exit_code": process.poll(), "wm_close": [], "completed_utc": native.utc()}
        if parent is not None:
            try:
                parent.close(process)
            except Exception as error:
                record["errors"].append("Owned host cleanup: " + str(error))
    else:
        record["cleanup_intervened"] = True
        record["errors"].append("Runner intervention was required; this is not a qualifying self-exit")
        native.cleanup_owned_resources(process, user, callback, record, parent)


def qualify(record):
    conditions = {
        "exact_results_verified": record.get("results_verified") is True,
        "runtime_callback_verified": record.get("runtime_verified") is True and record.get("events_verified") is True
            and record.get("completion_verified") is True,
        "normal_integrity": all(record.get(key, {}).get("normal_integrity") is True
                                for key in ("integrity_before_launch", "child_integrity")),
        "self_exit_zero": record.get("closure", {}).get("mode") == "self_exit"
            and record["closure"].get("exit_code") == 0 and record["closure"].get("forced") is False,
        "no_intervention": not record.get("cleanup_intervened", False),
        "hidden_guard": record.get("foreground_or_visible_window_violation") is False,
        "owned_host_destroyed": record.get("hidden_parent", {}).get("destroyed") is True,
        "actual_owned_parent_verified": record.get("parent_relation_verified") is True,
        "power_restored": record.get("power_request", {}).get("restored") is True,
        "identity_unchanged": record.get("identity_unchanged") is True,
        "logs_reviewed": record.get("logs_scanned") is True,
        "observed_normal_renderer": any(row.get("mode") == "kGfxThreadingModeClientWorkerJobs"
                                        for row in record.get("graphics_threading_observations", [])),
        "no_fatal_audio_or_dump": not record.get("native_fatal_markers") and
            not record.get("audio_failure_markers") and record.get("crash_reports") == [],
        "no_callback_failure": record.get("callback_failure_present") is False,
        "mixer_verified": record.get("mixer_verified") is True,
    }
    record["qualification_checks"] = conditions
    record["errors"].extend("Qualification failed: " + key for key, okay in conditions.items() if not okay)
    passed = all(conditions.values()) and not record["errors"]
    record["status"] = "native_audio_mixer_verified" if passed else "native_audio_mixer_failed"
    return passed


def finite_number(value, label, minimum=0):
    require(type(value) in (int, float) and math.isfinite(value) and value >= minimum,
            "Invalid finite nonnegative number: " + label)
    return value


def bounded_integer(value, label, minimum=0, maximum=2**63 - 1):
    require(type(value) is int and minimum <= value <= maximum, "Invalid integer: " + label)
    return value


def validate_mixer(value):
    """Recompute the actual retained PCM windows; booleans alone never qualify."""
    require(value.get("unityVersion") == "6000.6.3f1" and value.get("platform") == "WindowsPlayer" and
            value.get("isEditor") is False, "Expected pinned non-Editor Windows mixer evidence")
    for flag in ("playingObserved", "baselineVerified", "nonzeroMixerVerified", "stoppedSilenceVerified",
                 "signalChecksPassed", "sourcesRestored", "listenersRestored", "globalSettingsRestored", "ownedObjectsDestroyed"):
        require(value.get(flag) is True, "Mixer signal/cleanup gate failed: " + flag)
    require(value.get("cleanupErrors") == [] and value.get("stage") == "signal-checks-complete",
            "Mixer diagnostic did not finish and clean up")
    rate = bounded_integer(value.get("outputSampleRate"), "output rate", 8000, 384000)
    frames = bounded_integer(value.get("dspBufferFrames"), "DSP frames", 1, 65536)
    buffers = bounded_integer(value.get("dspBuffers"), "DSP buffers", 1, 64)
    require(isinstance(value.get("speakerMode"), str) and 1 <= len(value["speakerMode"]) <= 64,
            "Missing actual speaker configuration")
    bounded_integer(value.get("borrowedSources"), "borrowed sources", 0, 10000)
    bounded_integer(value.get("borrowedListeners"), "borrowed listeners", 0, 10000)
    require(type(value.get("originalListenerPaused")) is bool, "Missing original listener pause state")
    require(finite_number(value.get("originalListenerVolume"), "original gain") <= 1, "Original gain outside range")
    require(value.get("clipFrames") == rate // 2 and type(value.get("clipFrames")) is int,
            "Expected the existing half-second generated clip")
    require(math.isclose(finite_number(value.get("clipSeconds"), "clip duration"), (rate // 2) / rate,
                         rel_tol=0, abs_tol=1e-8), "Clip length differs")
    require(math.isclose(finite_number(value.get("clipPeak"), "clip peak"), .003, rel_tol=0, abs_tol=1e-7),
            "Expected the existing quiet clip amplitude")
    bounded_integer(value.get("polls"), "mixer polls", 3)
    tap = value.get("tap")
    require(isinstance(tap, dict), "Missing audio-thread counters")
    bounded_integer(tap.get("callbacks"), "callbacks", 2)
    bounded_integer(tap.get("nonzeroSamples"), "nonzero callback samples", 128)
    require(type(tap.get("invalidSamples")) is int and tap["invalidSamples"] == 0, "Invalid callback PCM")
    require(finite_number(tap.get("peak"), "tap peak") > .00001, "No audio-thread signal")

    def sample(item):
        require(isinstance(item, dict) and item.get("stage") in
                ("prime-and-baseline", "playing", "stopped-silence"), "Invalid mixer sample phase")
        require(type(item.get("sourcePlaying")) is bool, "Missing source-playing state")
        for key in ("realtime", "dspTime", "sourcePeak", "masterPeak"):
            finite_number(item.get(key), key)
        return item

    signal, stopped = sample(value.get("signal")), sample(value.get("stopped"))
    require(signal["stage"] == "playing" and signal["sourcePlaying"] is True and
            stopped["stage"] == "stopped-silence" and stopped["sourcePlaying"] is False,
            "Signal/stop source states differ")
    play = finite_number(value.get("playDspTime"), "play DSP")
    stop = finite_number(value.get("stopDspTime"), "stop DSP")
    grace = (frames * buffers + 1024) / rate
    require(signal["dspTime"] > play + .02 and stop >= signal["dspTime"] and
            stopped["dspTime"] + 1e-6 >= stop + grace and stopped["realtime"] >= signal["realtime"],
            "Signal/silence lacks advancing freshly processed DSP intervals")
    require(stopped["masterPeak"] <= .0000001, "Master output did not return to silence")
    windows = {}
    for key, peak_name in (("sourceWindow", "sourcePeak"), ("masterWindow", "masterPeak")):
        data = value.get(key)
        require(isinstance(data, list) and len(data) == 1024, "Expected a complete 1024-sample PCM window")
        require(all(type(item) in (int, float) and math.isfinite(item) for item in data), "Nonfinite PCM window")
        peak = max(abs(item) for item in data)
        require(peak > .00001 and math.isclose(signal[peak_name], peak, rel_tol=1e-5, abs_tol=1e-8),
                "Reported signal peak differs from actual retained PCM")
        rms = math.sqrt(sum(item * item for item in data) / len(data))
        require(math.isfinite(rms) and rms > 0, "PCM RMS must be finite and positive")
        windows[key] = {"samples": len(data), "peak": peak, "rms": rms,
                        "nonzero_samples": sum(item != 0 for item in data)}
    samples = value.get("samples")
    require(isinstance(samples, list) and 1 <= len(samples) <= 512, "Missing/excessive bounded sample history")
    previous = None
    for item in samples:
        item = sample(item)
        if previous is not None:
            require(item["realtime"] >= previous["realtime"] and item["dspTime"] >= previous["dspTime"],
                    "Mixer sample clocks ran backwards")
        previous = item
    stamp = dt.datetime.fromisoformat(value["recordedUtc"].replace("Z", "+00:00"))
    require(stamp.tzinfo is not None and stamp.utcoffset() == dt.timedelta(0), "Mixer evidence needs actual UTC")
    return {"output_sample_rate": rate, "speaker_mode": value["speakerMode"], "dsp_frames": frames,
            "dsp_buffers": buffers, "signal_after_play_dsp_seconds": signal["dspTime"] - play,
            "silence_after_stop_dsp_seconds": stopped["dspTime"] - stop, "windows": windows,
            "tap": tap, "borrowed_sources": value["borrowedSources"], "borrowed_listeners": value["borrowedListeners"],
            "cleanup_errors": [], "recorded_utc": stamp.isoformat(),
            "scope": "Native test-only source/DSP/master processing and cleanup; physical output is unobserved."}


def validate_mixer_output(output, started_utc, completed_utc):
    audio = output / "audio"
    require(audio.resolve() == audio.absolute() and not audio.is_symlink(), "Audio evidence escaped its fresh output")
    children = list(audio.iterdir())
    require(len(children) == 1 and children[0].is_dir() and not children[0].is_symlink() and
            re.fullmatch(r"audio-mixer-[0-9a-f]{32}", children[0].name), "Expected exactly one fresh mixer probe bundle")
    path = children[0] / "evidence.json"
    require(path.resolve() == path.absolute() and native.manifest.regular(path).stat().st_size <= 512 * 1024,
            "Mixer evidence path/size is invalid")
    result = validate_mixer(native.manifest.read_json(path))
    stamp = dt.datetime.fromisoformat(result["recorded_utc"])
    start, end = (dt.datetime.fromisoformat(item.replace("Z", "+00:00")) for item in (started_utc, completed_utc))
    require(start <= stamp <= end, "Mixer evidence timestamp is outside this actual player run")
    result.update(path=path.relative_to(output).as_posix(), sha256=native.manifest.digest(path))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build-receipt", "--build-report", dest="build_report", type=Path, required=True)
    parser.add_argument("--build-receipt-sha256", "--build-report-sha256", dest="build_report_sha256", required=True)
    parser.add_argument("--timeout-seconds", type=float, default=120)
    args = parser.parse_args()
    require(os.name == "nt", "Runner requires native Windows")
    require(math.isfinite(args.timeout_seconds) and 5 <= args.timeout_seconds <= 180, "Use a 5-180 s timeout")
    player, out, build_path = args.player.absolute(), args.output.absolute(), args.build_report.absolute()
    require(out.resolve() == out and out != player.parent and player.parent not in out.parents,
            "Evidence must use a fresh directory outside the player")
    require(re.fullmatch(r"[0-9a-f]{64}", args.build_report_sha256 or ""), "Expected build report SHA-256")
    require(native.manifest.digest(build_path) == args.build_report_sha256, "Build report hash differs")
    report = validate_build_report(native.manifest.read_json(build_path), player)
    before = snapshot_player(player)
    out.mkdir(exist_ok=False)
    (out / "audio").mkdir()
    (out / "crashes").mkdir()
    (out / "build-report.json").write_bytes(build_path.read_bytes())
    native.write_new(out / "test-player-before.json", {"player": str(player), "files": before})
    sources = {}
    for name, path in (("runner.py", Path(__file__)), ("native-player-validation.py", ROOT / "tools/native_player_validation.py"),
                       ("unity-validation.py", ROOT / "tools/unity_validation.py"),
                       ("player-build-manifest.py", ROOT / "tools/player_build_manifest.py")):
        (out / name).write_bytes(path.read_bytes())
        sources[name] = native.manifest.digest(out / name)
    environment = player_environment(out)
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    kernel, user, callback = native.windows_api()
    user.GetParent.argtypes = [wintypes.HWND]
    user.GetParent.restype = wintypes.HWND
    volumes = {str(path.anchor): shutil.disk_usage(path.anchor).free for path in (ROOT, player, out)}
    record = {"schema_version": 1, "kind": "instrumented standalone audio mixer fixture execution",
        "started_utc": native.utc(), "player": str(player), "build_report_sha256": args.build_report_sha256,
        "actual_build_report": report, "shipping_player_receipt": False, "instrumented_test_player": True,
        "expected_cases": sorted(EXPECTED), "sources_sha256": sources, "timeout_seconds": args.timeout_seconds,
        "disk_reserve_bytes": RESERVE, "evidence_size_limit_bytes": GROWTH_LIMIT, "initial_volume_free_bytes": volumes,
        "player_mode": "embedded", "parent_mode": "owned", "graphics_mode": "legacy", "batch_mode": False,
        "audio_disabling_flag": False, "foreground_or_visible_window_violation": False,
        "child_environment_override": {"SHADOWS_AUDIO_PLAYER_OUTPUT": str(out), "SHADOWS_AUDIO_PROBE_OUTPUT": str(out / "audio"),
                                       "SHADOWS_ROUTE_PLAYER_OUTPUT": None, "SHADOWS_PLAYER_EVIDENCE_DIR": None,
                                       "SHADOWS_CAPTURE_DIR": None},
        "cleanup_intervened": False, "errors": [], "not_qualified": ["OS-input gameplay", "human playthrough",
        "human timing/readability", "audible-output acceptance", "hardware performance", "shipping-player identity"]}
    process, parent = None, None
    start = time.monotonic()
    try:
        require(all(free >= RESERVE for free in volumes.values()), "Initial disk reserve is insufficient")
        record["integrity_before_launch"] = native.process_integrity(kernel)
        require(record["integrity_before_launch"]["normal_integrity"], "Below-medium launcher integrity")
        with native.keep_awake(kernel, record), (out / "launcher-stdout.log").open("xb") as stdout, (out / "launcher-stderr.log").open("xb") as stderr:
            try:
                parent = native.HiddenParentWindow(kernel, user, record)
                parent.create()
                command = native.player_command(player, out, "legacy", parent.handle, "embedded")
                record["command"] = command
                process = subprocess.Popen(command, cwd=player.parent, env=environment, startupinfo=startup,
                    creationflags=subprocess.CREATE_NO_WINDOW, stdout=stdout, stderr=stderr)
                record["pid"] = process.pid
                record["child_integrity"] = native.process_integrity(kernel, process.pid)
                require(record["child_integrity"]["normal_integrity"], "Below-medium player integrity")
                native.write_new(out / "launch.json", record)
                while process.poll() is None:
                    parent.pump()
                    elapsed = time.monotonic() - start
                    if not native.check_hidden_windows(user, callback, process.pid, record, parent, elapsed):
                        break
                    if not record.get("parent_relation_verified"):
                        relation = observe_parent(user, callback, process.pid, parent.handle)
                        if relation["matching_parent_observed"]:
                            native.write_new(out / "window-parent-observation.json", relation)
                            record["parent_relation_verified"] = True
                    for volume in volumes:
                        free = shutil.disk_usage(volume).free
                        reason = stop_reason(elapsed, args.timeout_seconds, free, output_bytes(out))
                        if reason:
                            record["errors"].append(reason + ": " + volume)
                            record["stop_condition"] = {"reason": reason, "volume": volume, "free_bytes": free}
                            break
                    if record.get("stop_condition"):
                        break
                    time.sleep(.25)
            finally:
                finish_process(process, user, callback, record, parent)
    except (Exception, KeyboardInterrupt) as error:
        record["errors"].append(type(error).__name__ + ": " + str(error))
    record["seconds"] = time.monotonic() - start
    record["completed_utc"] = native.utc()
    if record["seconds"] > args.timeout_seconds:
        record["errors"].append("Lifecycle exceeded the bounded timeout")
    record["callback_failure_present"] = (out / "failure.json").exists()
    if record.get("window_violation_observation"):
        native.write_new(out / "window-violation.json", record["window_violation_observation"])
    for label, operation in (
        ("results", lambda: validate_results(native.shared_read(kernel, out / "results.xml", 10_000_000))),
        ("runtime", lambda: validate_runtime(native.manifest.read_json(out / "runtime.json"), record.get("pid"), out)),
        ("events", lambda: validate_events(native.shared_read(kernel, out / "events.jsonl"))),
    ):
        try:
            record[label + "_validation"] = operation()
            record[label + "_verified"] = True
        except Exception as error:
            record["errors"].append(label + ": " + str(error))
    try:
        record["completion_validation"] = validate_completion(native.manifest.read_json(out / "completion.json"),
            record["results_validation"], record["events_validation"])
        record["completion_verified"] = True
    except Exception as error:
        record["errors"].append("completion: " + str(error))
    try:
        record.update(native.scan_player_logs(kernel, out))
        record["logs_scanned"] = True
        record["log_review_required"] = bool(record["diagnostic_candidates"])
        record["crash_reports"] = native.manifest.file_tree(out / "crashes")
    except Exception as error:
        record["errors"].append("logs/crashes: " + str(error))
    try:
        after = snapshot_player(player)
        native.write_new(out / "test-player-after.json", {"player": str(player), "files": after})
        record["identity_unchanged"] = compare_identity(before, after)
        require(native.manifest.digest(build_path) == args.build_report_sha256, "Builder report changed")
    except Exception as error:
        record["identity_unchanged"] = False
        record["errors"].append("identity: " + str(error))
    try:
        record["mixer_validation"] = validate_mixer_output(out, record["started_utc"], record["completed_utc"])
        record["mixer_verified"] = True
    except Exception as error:
        record["errors"].append("mixer: " + str(error))
    passed = qualify(record)
    native.write_new(out / "execution.json", record)
    native.write_new(out / "evidence-file-inventory.json", {"utc": native.utc(), "files": native.manifest.file_tree(out)})
    print(json.dumps({key: record.get(key) for key in ("status", "pid", "closure", "qualification_checks",
        "graphics_threading_observations", "log_review_required", "errors")}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
