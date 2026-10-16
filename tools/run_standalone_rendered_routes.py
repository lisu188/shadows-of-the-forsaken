"""Run the existing main and secret routes in a concealed Development player.

The separate opt-in observer measures ordinary SRP boundary intervals and memory.
The exact two existing fixtures use normal Unity updates and their unchanged
InputSystem controls. No displayed FPS/GPU/Release/human acceptance is implied.
"""
import argparse
from ctypes import wintypes
import datetime as dt
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]


def load_tool(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


native = load_tool("native_player_validation")
evidence = load_tool("rendered_route_evidence")
concealed = load_tool("concealed_native_window")
require = evidence.require
RESERVE = 32 * 2**30
GROWTH_LIMIT = 5 * 2**30
CONFLICTS = ("SHADOWS_ROUTE_PLAYER_BUILD", "SHADOWS_ROUTE_PLAYER_OUTPUT", "SHADOWS_AUDIO_PLAYER_BUILD",
             "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PROBE_OUTPUT", "SHADOWS_PRESENTATION_PLAYER_BUILD",
             "SHADOWS_PRESENTATION_PLAYER_OUTPUT", "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR",
             "SHADOWS_CAPTURE_DIR", "SHADOWS_RENDERED_ROUTE_BUILD", "SHADOWS_RENDERED_ROUTE_OUTPUT")


def player_environment(output, inherited=None):
    result = dict(os.environ if inherited is None else inherited)
    for name in CONFLICTS:
        result.pop(name, None)
    result["SHADOWS_RENDERED_ROUTE_OUTPUT"] = str(output)
    result["SHADOWS_CAPTURE_DIR"] = str(output / "captures")
    return result


def snapshot_player(player):
    require(player.suffix.lower() == ".exe", "Expected a Windows executable")
    native.manifest.regular(player)
    rows = native.manifest.file_tree(player.parent)
    for name in ("Shadows.PlayMode.Tests.dll", "Shadows.StandaloneRenderedRoutes.PlayerSupport.dll"):
        require(any(item["path"].endswith("/" + name) for item in rows), "Missing diagnostic assembly: " + name)
    return rows


def guard_output_parent(path):
    """Reject links/junctions before creating a fresh output, without changing it."""
    require(path.is_absolute() and path.is_dir(), "Evidence parent must be an existing absolute directory")
    for parent in (path, *path.parents):
        metadata = parent.lstat()
        require(stat.S_ISDIR(metadata.st_mode) and not parent.is_symlink() and
                not getattr(metadata, "st_file_attributes", 0) & 0x400,
                "Evidence parent traverses a link or reparse point")


class MarkerReader:
    """Only closed fresh bytes authorize shows; complete callbacks are append-only."""
    def __init__(self, kernel, output, record):
        self.kernel, self.output, self.record = kernel, output, record
        self.marker, self.ready, self.ready_sha, self.previous_events = None, None, None, b""
        self.transients = record["protocol_publication_transients"] = {"count": 0, "samples": [], "samples_dropped": 0}

    def read(self, name, limit, trusted=False, optional=False):
        path = self.output / name
        if optional and not path.exists() and not trusted:
            return None
        if path.exists():
            native.manifest.regular(path)
        try:
            return native.shared_read(self.kernel, path, limit)
        except OSError as error:
            if not trusted and (isinstance(error, FileNotFoundError) or getattr(error, "winerror", None) in (2, 32)):
                self.transients["count"] += 1
                if len(self.transients["samples"]) < 128:
                    self.transients["samples"].append({"utc": native.utc(), "file": name,
                        "winerror": getattr(error, "winerror", None), "error_type": type(error).__name__})
                else:
                    self.transients["samples_dropped"] += 1
                return None
            raise

    def observe(self, pid):
        raw = self.read("runtime.json", 1024 * 1024, trusted=self.marker is not None, optional=True)
        callbacks = self.read("events.jsonl", 8 * 1024 * 1024, trusted=self.marker is not None, optional=True)
        if raw is None or callbacks is None:
            return None, None, None
        try:
            runtime = evidence.parse_json(raw, 1024 * 1024)
        except (ValueError, UnicodeError):
            require(self.marker is None, "Verified runtime became malformed")
            return None, None, None
        evidence.validate_runtime(runtime, pid, self.output)
        now = native.utc()
        evidence.fresh(runtime["recorded_utc"], self.record["started_utc"], now)
        if b"\n" not in callbacks:
            require(self.marker is None, "Verified callbacks became empty")
            return None, None, None
        history = evidence.event_history(callbacks, self.record["started_utc"], now)
        require(history["complete"].startswith(self.previous_events), "Previously observed callback history changed or shrank")
        self.previous_events = history["complete"]
        if not history["starts"]:
            return None, None, None
        terminal = {"verified": False}
        if history["run_finished"]:
            complete_raw = self.read("completion.json", 1024 * 1024, optional=True,
                                     trusted=bool(self.marker and self.marker["terminal_boundary_verified"]))
            result_raw = self.read("results.xml", 10_000_000, optional=True,
                                   trusted=bool(self.marker and self.marker["terminal_boundary_verified"]))
            if complete_raw is not None and result_raw is not None:
                completion = evidence.parse_json(complete_raw, 1024 * 1024)
                results = evidence.validate_results(result_raw, passed=False)
                evidence.validate_completion(completion, results, history, self.record["started_utc"], now, passed=False)
                terminal = {"verified": True, "completion_sha256": evidence.sha(complete_raw),
                    "results_sha256": results["sha256"], "events_sha256": history["sha256"],
                    "requested_exit_code": completion["exit_code"], "recorded_utc": completion["recorded_utc"]}
        first = next(iter(history["starts"]))
        marker = {"utc_observed": now, "run_started_utc": history["events"][0]["utc"], "first_case": first,
            "first_case_started_utc": history["starts"][first]["utc"], "active_case": history["active_case"],
            "run_finished": history["run_finished"], "terminal_boundary": terminal,
            "terminal_boundary_verified": terminal["verified"], "complete_event_count": history["count"],
            "complete_events_sha256": history["sha256"], "runtime_sha256": evidence.sha(raw), "runtime_pid": pid}
        if self.marker is not None:
            require(marker["runtime_sha256"] == self.marker["runtime_sha256"], "Verified runtime bytes changed")
        key = next(key for key, name in evidence.CASES.items() if name == first)
        ready_raw = self.read("case-" + key + "-ready.json", 64 * 1024, trusted=self.ready is not None, optional=True)
        if ready_raw is not None:
            ready = evidence.validate_ready(evidence.parse_json(ready_raw, 64 * 1024), key, pid, self.output,
                                            history["starts"][first]["utc"], now)
            digest = evidence.sha(ready_raw)
            if self.ready is not None:
                require(ready == self.ready and digest == self.ready_sha, "Verified readiness bytes changed")
            self.ready, self.ready_sha = ready, digest
        self.marker = marker
        return marker, self.ready, self.ready_sha


def finish_process(process, user, callback, record, parent):
    """Self-exit qualifies; a bounded owned-only cleanup path is explicitly failed."""
    try:
        if process is None:
            return
        if process.poll() is not None:
            record["closure"] = {"mode": "self_exit", "forced": False, "pid": process.pid,
                                 "exit_code": process.poll(), "wm_close": [], "completed_utc": native.utc()}
            return
        record["cleanup_intervened"] = True
        record["errors"].append("Runner intervention was required; no qualifying self-exit")
        closure = record["closure"] = {"mode": "WM_CLOSE", "forced": False, "pid": process.pid,
            "requested_utc": native.utc(), "wm_close": [], "other_processes_touched": False}
        for window in native.own_windows(user, callback, process.pid, parent.handle if parent else None):
            if process.poll() is None and native.window_pid(user, window) == process.pid:
                closure["wm_close"].append({"window": window, "owner_pid": process.pid,
                    "posted": bool(user.PostMessageW(window, 0x0010, 0, 0))})
        deadline = time.monotonic() + 5
        while process.poll() is None and time.monotonic() < deadline:
            if parent:
                parent.pump()
                concealed.check_concealed_windows(user, callback, process.pid, record, parent=parent,
                                                  seconds=None, cleanup=True)
            time.sleep(.025)
        if process.poll() is None:
            closure.update(mode="TerminateProcess_only_spawned_handle", forced=True)
            process.terminate()
            process.wait(timeout=5)
        closure.update(exit_code=process.poll(), completed_utc=native.utc())
    except Exception as error:
        record["errors"].append("Owned cleanup: " + type(error).__name__ + ": " + str(error))
        if process is not None and process.poll() is None:
            record["cleanup_intervened"] = True
            record["closure"] = {"mode": "TerminateProcess_only_spawned_handle_after_cleanup_error", "forced": True, "pid": process.pid}
            process.terminate()
            process.wait(timeout=5)
            record["closure"].update(exit_code=process.poll(), completed_utc=native.utc())
    finally:
        if parent is not None:
            try:
                parent.close(process)
            except Exception as error:
                record["errors"].append("Owned host cleanup: " + str(error))


def qualify(record):
    child = record.get("child_visibility_guard", {})
    ready = record.get("diagnostics_readiness_guard", {})
    checks = {
        "exact_results_verified": record.get("results_verified") is True,
        "runtime_callback_verified": all(record.get(key + "_verified") is True for key in ("runtime", "events", "completion")),
        "rendered_route_evidence_verified": record.get("rendered_routes_verified") is True,
        "existing_capture_evidence_verified": record.get("captures_verified") is True,
        "normal_integrity": all(record.get(key, {}).get("normal_integrity") is True for key in ("integrity_before_launch", "child_integrity")),
        "self_exit_zero": record.get("closure", {}).get("mode") == "self_exit" and record["closure"].get("exit_code") == 0 and record["closure"].get("forced") is False,
        "no_intervention": record.get("cleanup_intervened") is False,
        "visually_concealed_guard": record.get("foreground_or_visually_exposed_window_violation") is False,
        "transparent_host_observed": record.get("transparent_offscreen_guard", {}).get("shown_after_child_verified") is True and record.get("transparent_offscreen_guard", {}).get("samples", 0) > 0,
        "owned_scene_prepared_before_show": ready.get("verified") is True and ready.get("receipt", {}).get("pid") == record.get("pid") and
            all(not sample.get("host", {}).get("visible") and not sample.get("visible_owned_descendants") for sample in child.get("history", []) if sample.get("utc", "") < ready.get("verified_utc", "")),
        "actual_child_visible": child.get("visibility_established") is True and child.get("visibility_lost") is False and child.get("actual_utf_started_marker", {}).get("runtime_pid") == record.get("pid"),
        "both_cases_observed": set(child.get("case_visibility_observed", [])) == evidence.EXPECTED,
        "complete_visibility_history": child.get("history_limit_reached") is False and child.get("dropped_samples") == 0 and child.get("samples", 0) > 0,
        "bounded_nonactivating_child_show": len(child.get("show_requests", [])) <= 1 and all(item.get("queued") is True and item.get("observed_visible") is True for item in child.get("show_requests", [])),
        "actual_live_parent_verified": record.get("parent_relation_verified") is True,
        "owned_host_destroyed": record.get("hidden_parent", {}).get("destroyed") is True,
        "power_restored": record.get("power_request", {}).get("restored") is True,
        "identity_unchanged": record.get("identity_unchanged") is True,
        "bounded_resources": record.get("resource_budget", {}).get("checks", 0) > 0 and record.get("final_resource_check") is True and not record.get("stop_condition"),
        "logs_reviewed": record.get("logs_scanned") is True,
        "normal_renderer_observed": any(item.get("mode") == "kGfxThreadingModeClientWorkerJobs" for item in record.get("graphics_threading_observations", [])),
        "no_fatal_audio_or_dump": not record.get("native_fatal_markers") and not record.get("audio_failure_markers") and record.get("crash_reports") == [],
        "no_callback_failure": record.get("callback_failure_present") is False,
    }
    record["qualification_checks"] = checks
    record["errors"].extend("Qualification failed: " + key for key, passed in checks.items() if not passed)
    passed = all(checks.values()) and not record["errors"]
    record["status"] = "concealed_rendered_routes_verified" if passed else "concealed_rendered_routes_failed"
    return passed


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build-receipt", type=Path, required=True)
    parser.add_argument("--build-receipt-sha256", required=True)
    parser.add_argument("--timeout-seconds", type=float, default=360)
    args = parser.parse_args(argv)
    require(os.name == "nt", "Runner requires native Windows")
    require(math.isfinite(args.timeout_seconds) and 5 <= args.timeout_seconds <= 600, "Use a finite 5–600 s timeout")
    player, out, build_path = args.player.absolute(), args.output.absolute(), args.build_receipt.absolute()
    require(out.resolve() == out and player.parent not in out.parents and out != player.parent,
            "Use a fresh nonlinked evidence directory outside the player")
    guard_output_parent(out.parent)
    require(re.fullmatch(r"[0-9a-f]{64}", args.build_receipt_sha256 or ""), "Expected build receipt SHA256")
    require(native.manifest.digest(build_path) == args.build_receipt_sha256, "Build receipt hash differs")
    report = evidence.validate_build_report(native.manifest.read_json(build_path), player)
    before = snapshot_player(player)
    kernel, user, callback = native.windows_api()
    user.GetParent.argtypes, user.GetParent.restype = [wintypes.HWND], wintypes.HWND
    overlap = native.no_validation_overlap(kernel)
    volumes = sorted({str(path.anchor) for path in (ROOT, player, out)})
    initial_free = {volume: shutil.disk_usage(volume).free for volume in volumes}
    require(all(free >= RESERVE for free in initial_free.values()), "Initial disk reserve is below 32 GiB")
    out.mkdir(exist_ok=False)
    (out / "captures").mkdir()
    (out / "crashes").mkdir()
    (out / "build-report.json").write_bytes(build_path.read_bytes())
    native.write_new(out / "test-player-before.json", {"player": str(player), "files": before})
    sources = {}
    source_names = ("run_standalone_rendered_routes", "rendered_route_evidence", "rendered_route_capture_evidence",
                    "concealed_native_window", "native_player_validation", "player_build_manifest", "presentation_evidence")
    for name in source_names:
        path = ROOT / "tools" / (name + ".py")
        (out / path.name).write_bytes(path.read_bytes())
        sources[path.name] = native.manifest.digest(out / path.name)
    record = {"schema_version": 1, "test_profile": evidence.PROFILE,
        "kind": "concealed Development existing main and secret rendered-route diagnostic",
        "started_utc": native.utc(), "player": str(player), "build_report_sha256": args.build_receipt_sha256,
        "actual_build_report": report, "shipping_player_receipt": False, "instrumented_test_player": True,
        "diagnostic_output_directory": str(out), "expected_cases": sorted(evidence.EXPECTED),
        "sources_sha256": sources, "timeout_seconds": args.timeout_seconds, "poll_seconds": .025,
        "disk_reserve_bytes": RESERVE, "evidence_size_limit_bytes": GROWTH_LIMIT,
        "initial_volume_free_bytes": initial_free, "no_overlap_preflight": overlap,
        "player_mode": "embedded", "parent_mode": "owned", "graphics_mode": "legacy", "batch_mode": False,
        "audio_disabling_flag": False, "foreground_or_visually_exposed_window_violation": False,
        "child_environment_override": {name: player_environment(out, {}).get(name) for name in CONFLICTS},
        "cleanup_intervened": False, "errors": [],
        "not_qualified": ["GPU completion timing", "displayed FPS", "ordinary shipping Release route performance",
                          "OS-input gameplay", "human timing/readability/art", "physical audible-output acceptance"]}
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process, parent = None, None
    host_class = concealed.make_concealed_host(native, evidence.EXPECTED, profile=evidence.PROFILE)
    budget = native.SoakBudget(out, volumes)
    reader = MarkerReader(kernel, out, record)
    start = time.monotonic()
    try:
        budget.check(0)
        record["integrity_before_launch"] = native.process_integrity(kernel)
        require(record["integrity_before_launch"]["normal_integrity"], "Below-medium launcher integrity")
        with native.keep_awake(kernel, record), (out / "launcher-stdout.log").open("xb") as stdout, (out / "launcher-stderr.log").open("xb") as stderr:
            try:
                parent = host_class(kernel, user, record)
                parent.create()
                record["command"] = native.player_command(player, out, "legacy", parent.handle, "embedded")
                process = subprocess.Popen(record["command"], cwd=player.parent, env=player_environment(out),
                    startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW, stdout=stdout, stderr=stderr)
                record["pid"] = process.pid
                record["child_integrity"] = native.process_integrity(kernel, process.pid)
                require(record["child_integrity"]["normal_integrity"], "Below-medium player integrity")
                native.write_new(out / "launch.json", record)
                while process.poll() is None:
                    parent.pump()
                    elapsed = time.monotonic() - start
                    if elapsed >= args.timeout_seconds:
                        record["stop_condition"] = {"reason": "timeout", "seconds": elapsed}
                        record["errors"].append("Bounded runtime timeout reached")
                        break
                    try:
                        budget.check(elapsed)
                    except Exception as error:
                        record["stop_condition"] = {"reason": "resource guard", "seconds": elapsed, "error": str(error)}
                        raise
                    marker, ready, ready_sha = reader.observe(process.pid)
                    if not concealed.check_concealed_windows(user, callback, process.pid, record, parent=parent,
                            seconds=elapsed, marker=marker, readiness=ready, readiness_sha256=ready_sha):
                        break
                    if not record.get("parent_relation_verified"):
                        relation = native.observe_live_parent(process, user, callback, parent)
                        if relation and relation["matching_parent_observed"]:
                            native.write_new(out / "window-parent-observation.json", relation)
                            record["parent_relation_verified"] = True
                    time.sleep(.025)
            finally:
                finish_process(process, user, callback, record, parent)
    except (Exception, KeyboardInterrupt) as error:
        record["errors"].append(type(error).__name__ + ": " + str(error))
    record.update(seconds=time.monotonic() - start, completed_utc=native.utc(), resource_budget=budget.summary(),
                  callback_failure_present=(out / "failure.json").exists())
    if record["seconds"] > args.timeout_seconds:
        record["errors"].append("Lifecycle exceeded its bounded timeout")
    if record.get("window_violation_observation"):
        native.write_new(out / "window-violation.json", record["window_violation_observation"])
    history, completion, summaries = None, None, {}
    for label, operation in (
        ("results", lambda: evidence.validate_results(native.shared_read(kernel, out / "results.xml", 10_000_000))),
        ("runtime", lambda: evidence.validate_runtime(native.manifest.read_json(out / "runtime.json"), record.get("pid"), out)),
    ):
        try:
            record[label + "_validation"] = operation()
            record[label + "_verified"] = True
        except Exception as error:
            record["errors"].append(label + ": " + str(error))
    try:
        history = evidence.event_history(native.shared_read(kernel, out / "events.jsonl"),
                                         record["started_utc"], record["completed_utc"], final=True)
        record["events_validation"] = {key: history[key] for key in ("count", "sha256")}
        record["events_verified"] = True
        completion = evidence.validate_completion(native.manifest.read_json(out / "completion.json"),
            record["results_validation"], history, record["started_utc"], record["completed_utc"])
        record["completion_validation"], record["completion_verified"] = completion, True
    except Exception as error:
        record["errors"].append("events/completion: " + str(error))
    try:
        require(history is not None and completion is not None, "Missing callback/completion binding")
        for item in completion["case_evidence"]:
            key = item["case_key"]
            ready_raw = native.shared_read(kernel, out / item["ready_file"], 64 * 1024)
            case_raw = native.shared_read(kernel, out / item["evidence_file"], evidence.MAX_JSON)
            require(evidence.sha(ready_raw) == item["ready_sha256"] and evidence.sha(case_raw) == item["evidence_sha256"],
                    "Closed case evidence hashes differ")
            ready = evidence.validate_ready(evidence.parse_json(ready_raw, 64 * 1024), key, record.get("pid"), out,
                history["starts"][evidence.CASES[key]]["utc"], history["finishes"][evidence.CASES[key]]["utc"])
            summaries[key] = evidence.validate_case(evidence.parse_json(case_raw), key, ready, record.get("pid"), out,
                history["starts"][evidence.CASES[key]], history["finishes"][evidence.CASES[key]])
        require(set(summaries) == set(evidence.CASES), "Missing both route evidence results")
        record["rendered_routes_validation"], record["rendered_routes_verified"] = summaries, True
    except Exception as error:
        record["errors"].append("rendered routes: " + str(error))
    try:
        record["captures_validation"] = load_tool("rendered_route_capture_evidence").validate_captures(out)
        record["captures_verified"] = True
    except Exception as error:
        record["errors"].append("captures: " + str(error))
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
        require(before == after and bool(before), "Player files changed, were added or were removed")
        require(native.manifest.digest(build_path) == args.build_receipt_sha256, "Build receipt changed")
        record["identity_unchanged"] = True
    except Exception as error:
        record["errors"].append("identity: " + str(error))
    try:
        budget.check(time.monotonic() - start)
        record["resource_budget"] = budget.summary()
        record["final_resource_check"] = True
    except Exception as error:
        record["errors"].append("Final resource guard: " + str(error))
        record["stop_condition"] = {"reason": "final resource guard", "error": str(error)}
    passed = qualify(record)
    native.write_soak_publication(out / "execution.json", record)
    native.write_soak_publication(out / "evidence-file-inventory.json", {"utc": native.utc(), "files": native.manifest.file_tree(out)})
    print(json.dumps({key: record.get(key) for key in ("status", "pid", "seconds", "closure", "qualification_checks", "errors")}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
