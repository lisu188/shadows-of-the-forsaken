"""Concealed Win32 host for separately qualified Development diagnostics.

No native library is loaded at import and no evidence file is read here. The
launcher supplies its already loaded native_player_validation module, whose
HiddenParentWindow owns same-thread message pumping and destruction. This
module never assigns to that module or changes its never-shown validators.

make_concealed_host(native_module, expected_cases, *, profile) returns a host
class constructed with (kernel, user, record). record must contain an absolute
diagnostic_output_directory and an errors list. Both host and child stay hidden
until check_concealed_windows receives a validated active callback marker AND
the closed first-case owned-scene ready receipt plus its raw SHA256. The caller
must validate the actual runtime/events/ready/completion/XML bytes, fresh output
binding, exact case set, all counters and their hashes before passing tokens.

Required marker fields (additional fields are retained): utc_observed,
run_started_utc, first_case, first_case_started_utc, active_case, run_finished,
terminal_boundary, terminal_boundary_verified, complete_event_count,
complete_events_sha256, runtime_sha256, runtime_pid. terminal_boundary has a
boolean verified; when true it also has completion_sha256, results_sha256,
events_sha256, requested_exit_code and recorded_utc. A validated FAILED terminal
may permit normal window teardown; it cannot qualify the run as passed.

Required ready fields (additional profile fields are retained): schema_version,
pid, owned_camera_count, test_profile, recorded_utc, case_full_name,
output_directory, scene_path, scene_name, camera_name, camera_scene_path,
owned_scene_prepared, camera_post_processing, pipeline_at_preparation,
pipeline_after_preparation, pipeline_at_publication. Version is 1. The scene is
Assets/Scenes/ForsakenCastle.unity, exactly one camera belongs to it, preparation
succeeded and camera post processing is off. All three pipeline flags are
actual booleans. Ready must name the first currently active expected case.

After acceptance the caller keeps supplying that SAME first ready body and
raw SHA, while subsequent cases may publish separate receipts. Marker runtime
identity is immutable, complete-line event counts only increase, and a verified
terminal boundary cannot be revoked. No guessed delay authorizes a show.

cleanup=True is ONLY for a launcher's already-unqualified finalization path:
it checks visual concealment, records teardown and never consumes protocol
tokens or issues Show. The launcher must record intervention and reject normal
qualification independently. This module is not a lifecycle/result qualifier.

Win32 desktop/create/inspect, child enumeration/containment, both nonactivating
show methods, sampled visibility and bounded retention are extracted unchanged
(apart from class indentation) from the qualified 9b06cc98 helper. Polling is
discrete; the history limit is 32768, fail-closed on overflow. The 2 s deadline
applies to actual visibility after the single child queue request, not startup.
"""
import copy
import ctypes
from ctypes import wintypes
import datetime as dt
import json
import math
from pathlib import Path
import re
import time


MARKER_FIELDS = frozenset({
    "utc_observed", "run_started_utc", "first_case", "first_case_started_utc",
    "active_case", "run_finished", "terminal_boundary", "terminal_boundary_verified",
    "complete_event_count", "complete_events_sha256", "runtime_sha256", "runtime_pid",
})
READY_FIELDS = frozenset({
    "schema_version", "pid", "owned_camera_count", "test_profile", "recorded_utc",
    "case_full_name", "output_directory", "scene_path", "scene_name", "camera_name",
    "camera_scene_path", "owned_scene_prepared", "camera_post_processing",
    "pipeline_at_preparation", "pipeline_after_preparation", "pipeline_at_publication",
})
_SHA256 = re.compile(r"[0-9a-f]{64}")


def _stamp(value, require):
    require(type(value) is str and 0 < len(value) <= 64, "Invalid protocol UTC timestamp")
    stamp = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(stamp.tzinfo is not None and stamp.utcoffset() == dt.timedelta(0), "Protocol timestamp is not UTC")
    return stamp


def _digest(value, require):
    require(type(value) is str and _SHA256.fullmatch(value) is not None, "Invalid protocol SHA256")


def _token_bytes(value):
    """Type-sensitive JSON equality; Python True == 1 is not immutable evidence."""
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


class _ProtocolHost:
    """Pure checks over caller-validated tokens; never reads runtime files."""

    def accept_protocol(self, pid, marker, readiness, readiness_sha256):
        require = self._native.require
        require(type(pid) is int and pid > 0, "Invalid runtime PID")
        require(getattr(self, "_protocol_pid", pid) == pid, "Runtime PID changed")
        if marker is None:
            require(self.marker is None and self.ready is None and readiness is None and readiness_sha256 is None,
                    "Previously verified protocol disappeared or readiness preceded its marker")
            return None
        require(type(marker) is dict and MARKER_FIELDS <= marker.keys(), "Canonical UTF marker schema differs")
        require(type(marker["runtime_pid"]) is int and marker["runtime_pid"] == pid, "UTF marker PID differs")
        require(type(marker["first_case"]) is str and marker["first_case"] in self._expected and
                (marker["active_case"] is None or type(marker["active_case"]) is str and marker["active_case"] in self._expected),
                "UTF marker case differs")
        require(type(marker["run_finished"]) is bool and type(marker["terminal_boundary_verified"]) is bool,
                "UTF marker flags differ")
        require(type(marker["complete_event_count"]) is int and 2 <= marker["complete_event_count"] <= 128,
                "UTF complete event count differs")
        for key in ("runtime_sha256", "complete_events_sha256"):
            _digest(marker[key], require)
        now = dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=1)
        run_start = _stamp(marker["run_started_utc"], require)
        first_start = _stamp(marker["first_case_started_utc"], require)
        observed = _stamp(marker["utc_observed"], require)
        require(run_start <= first_start <= observed <= now, "UTF marker timestamp order differs")
        terminal = marker["terminal_boundary"]
        require(type(terminal) is dict and type(terminal.get("verified")) is bool and
                terminal["verified"] == marker["terminal_boundary_verified"], "Terminal verification flags differ")
        require(not marker["run_finished"] or marker["active_case"] is None, "Finished UTF marker retains an active case")
        if marker["terminal_boundary_verified"]:
            require(marker["run_finished"] and marker["complete_event_count"] >= 2 * len(self._expected) + 2,
                    "Terminal boundary precedes complete UTF callbacks")
            for key in ("completion_sha256", "results_sha256", "events_sha256"):
                _digest(terminal.get(key), require)
            require(terminal["events_sha256"] == marker["complete_events_sha256"] and
                    type(terminal.get("requested_exit_code")) is int and terminal["requested_exit_code"] in (0, 1),
                    "Terminal hashes or requested exit differ")
            require(first_start <= _stamp(terminal.get("recorded_utc"), require) <= now,
                    "Terminal timestamp differs")
        previous = self.marker
        if previous is not None:
            stable = ("runtime_pid", "runtime_sha256", "run_started_utc", "first_case", "first_case_started_utc")
            require(all(marker[key] == previous[key] for key in stable), "Verified UTF runtime or first case changed")
            require(marker["complete_event_count"] >= previous["complete_event_count"], "UTF callback history shrank")
            require(observed >= _stamp(previous["utc_observed"], require), "UTF marker observation clock regressed")
            if marker["complete_event_count"] == previous["complete_event_count"]:
                require(all(marker[key] == previous[key] for key in ("complete_events_sha256", "active_case", "run_finished")),
                        "UTF callback bytes/state changed without another complete event")
            else:
                require(marker["complete_events_sha256"] != previous["complete_events_sha256"],
                        "UTF callback count grew without changed complete bytes")
            require(not previous["run_finished"] or marker["run_finished"], "UTF run-finished marker was revoked")
            require(not previous["terminal_boundary_verified"] or
                    marker["terminal_boundary_verified"] and _token_bytes(terminal) == _token_bytes(previous["terminal_boundary"]),
                    "Verified terminal boundary changed or was revoked")
        if self.ready is not None:
            require(type(readiness) is dict and _token_bytes(readiness) == _token_bytes(self.ready) and
                    readiness_sha256 == self.ready_guard["receipt_sha256"], "Verified first readiness changed or disappeared")
        elif readiness is None:
            require(readiness_sha256 is None, "Ready hash exists without a readiness receipt")
        else:
            require(type(readiness) is dict and READY_FIELDS <= readiness.keys(), "Owned-scene readiness schema differs")
            _digest(readiness_sha256, require)
            require(type(readiness["schema_version"]) is int and readiness["schema_version"] == 1 and
                    type(readiness["pid"]) is int and readiness["pid"] == pid and
                    readiness["test_profile"] == self._profile, "Readiness version/profile/PID differs")
            require(readiness["case_full_name"] == marker["first_case"] == marker["active_case"] and
                    not marker["run_finished"], "First readiness requires its active first UTF case")
            require(type(readiness["output_directory"]) is str and Path(readiness["output_directory"]).is_absolute() and
                    Path(readiness["output_directory"]).absolute() == self.output, "Readiness output directory differs")
            scene = "Assets/Scenes/ForsakenCastle.unity"
            require(readiness["scene_path"] == scene and readiness["camera_scene_path"] == scene and
                    readiness["scene_name"] == "ForsakenCastle" and type(readiness["camera_name"]) is str and
                    0 < len(readiness["camera_name"]) <= 256 and type(readiness["owned_camera_count"]) is int and
                    readiness["owned_camera_count"] == 1 and readiness["owned_scene_prepared"] is True and
                    readiness["camera_post_processing"] is False and
                    all(type(readiness[key]) is bool for key in ("pipeline_at_preparation", "pipeline_after_preparation", "pipeline_at_publication")),
                    "Owned scene/camera preparation evidence differs")
            require(first_start <= _stamp(readiness["recorded_utc"], require) <= now,
                    "Readiness timestamp differs from its active first case")
        self._protocol_pid = pid
        self.marker = copy.deepcopy(marker)
        if previous is None:
            self.child_guard["actual_utf_started_marker"] = copy.deepcopy(marker)
        self.child_guard["last_utf_marker"] = copy.deepcopy(marker)
        if self.ready is None and readiness is not None:
            self.ready = copy.deepcopy(readiness)
            self.ready_guard.update(verified=True, verified_utc=self._native.utc(), receipt_sha256=readiness_sha256,
                                    receipt=copy.deepcopy(readiness), actual_utf_marker=copy.deepcopy(marker))
        return self.marker


def make_concealed_host(native_module, expected_cases, *, profile):
    """Return an injected host class; does not construct a window or load Win32."""
    native = native_module
    require = native.require
    require(type(profile) is str and re.fullmatch(r"[a-z][a-z0-9-]{0,63}", profile) is not None,
            "Explicit diagnostic profile required")
    require(type(expected_cases) in (tuple, list, set, frozenset) and 1 <= len(expected_cases) <= 32 and
            all(type(name) is str and 0 < len(name) <= 1024 for name in expected_cases) and
            len(set(expected_cases)) == len(expected_cases), "Explicit unique expected cases required")
    EXPECTED = frozenset(expected_cases)

    class TransparentOffscreenHost(native.HiddenParentWindow):
        def __init__(self, kernel, user, record):
            super().__init__(kernel, user, record)
            self.record = record
            self.data.update(extended_style_requested=0x08080080,
                             visual_mode="alpha0_fully_offscreen_noactivate", shown_without_activation=False)
            self.guard = record["transparent_offscreen_guard"] = {"samples": 0, "history": [], "shown_after_child_verified": False,
                "contract": "Readback alpha0 AND host/visible descendants fully outside virtual desktop; never foreground."}
            user.SetLayeredWindowAttributes.argtypes = [wintypes.HWND, wintypes.DWORD, ctypes.c_ubyte, wintypes.DWORD]
            user.SetLayeredWindowAttributes.restype = wintypes.BOOL
            user.GetLayeredWindowAttributes.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD),
                                                       ctypes.POINTER(ctypes.c_ubyte), ctypes.POINTER(wintypes.DWORD)]
            user.GetLayeredWindowAttributes.restype = wintypes.BOOL
            user.GetSystemMetrics.argtypes, user.GetSystemMetrics.restype = [ctypes.c_int], ctypes.c_int
            user.ShowWindow.argtypes, user.ShowWindow.restype = [wintypes.HWND, ctypes.c_int], wintypes.BOOL
            user.IsChild.argtypes, user.IsChild.restype = [wintypes.HWND, wintypes.HWND], wintypes.BOOL
            user.GetParent.argtypes, user.GetParent.restype = [wintypes.HWND], wintypes.HWND

        def desktop(self):
            left, top, width, height = [self.user.GetSystemMetrics(i) for i in (76, 77, 78, 79)]
            require(width > 0 and height > 0, "Virtual desktop unavailable")
            return {"left": left, "top": top, "right": left + width, "bottom": top + height}

        @staticmethod
        def outside(rect, desktop):
            return (rect["right"] <= desktop["left"] or rect["left"] >= desktop["right"] or
                    rect["bottom"] <= desktop["top"] or rect["top"] >= desktop["bottom"])

        def create(self):
            self.data["owner_thread_id"] = self.kernel.GetCurrentThreadId()
            desktop = self.desktop()
            left, top = desktop["left"] - 5376, desktop["top"] - 4816
            require(-2**31 < left < 2**31 and -2**31 < top < 2**31, "Offscreen coordinate overflow")
            handle = self.user.CreateWindowExW(self.data["extended_style_requested"], "STATIC",
                "Forsaken transparent offscreen diagnostic " + str(self.owner_pid), 0x80000000,
                left, top, 1280, 720, None, None, self.kernel.GetModuleHandleW(None), None)
            require(bool(handle), "Cannot create owned transparent offscreen host")
            self.handle = int(handle)
            self.data.update(window=self.handle, created=True, created_utc=native.utc())
            require(bool(self.user.SetLayeredWindowAttributes(self.handle, 0, 0, 2)), "Cannot set owned host alpha0")
            first = self.inspect(self.user.GetForegroundWindow())
            require(not first["visible"] and not first["foreground"], "Host exposed before alpha/offscreen proof")
            self.data["initial_snapshot"] = first

        def inspect(self, foreground):
            snapshot = super().inspect(foreground)
            rows = native.describe_windows(self.user, [self.handle], self.owner_pid, foreground)
            require(len(rows) == 1 and rows[0]["rect_available"], "Host rectangle unavailable")
            row = rows[0]
            alpha, flags, key = ctypes.c_ubyte(), wintypes.DWORD(), wintypes.DWORD()
            require(bool(self.user.GetLayeredWindowAttributes(self.handle, ctypes.byref(key), ctypes.byref(alpha), ctypes.byref(flags))),
                    "Host opacity readback failed")
            desktop = self.desktop()
            required_styles = 0x08080080  # NOACTIVATE | LAYERED | TOOLWINDOW
            require(row["extended_style"] & required_styles == required_styles, "Host protection styles changed")
            require(not row["extended_style"] & 0x00040008, "Host acquired APPWINDOW or TOPMOST style")
            require(alpha.value == 0 and flags.value & 2, "Host opacity is not alpha0")
            require(self.outside(row["rect"], desktop), "Host is no longer fully outside the virtual desktop")
            require(not snapshot["foreground"], "Owned host became foreground")
            require(snapshot["visible"] == self.data["shown_without_activation"],
                    "Host visibility differs from the recorded contrast state")
            snapshot.update(alpha=int(alpha.value), layered_flags=int(flags.value), rect=row["rect"],
                            extended_style=row["extended_style"], virtual_desktop=desktop, offscreen=True)
            self.data["last_observed"] = snapshot
            return snapshot


    class ChildVisibilityHost(_ProtocolHost, TransparentOffscreenHost):
        """One bounded post-readiness child show behind an alpha0 offscreen host."""
        HISTORY_LIMIT = 32768
        SHOW_TIMEOUT = 2.0
        _native = native
        _expected = EXPECTED
        _profile = profile

        def __init__(self, kernel, user, record):
            super().__init__(kernel, user, record)
            self.output = Path(record["diagnostic_output_directory"])
            self.child_guard = record["child_visibility_guard"] = {
                "history_limit": self.HISTORY_LIMIT, "history_limit_reached": False,
                "history": [], "samples": 0, "dropped_samples": 0,
                "show_requests": [], "show_request_limit": 1,
                "visibility_established": False, "visibility_lost": False,
                "case_visibility_observed": [], "poll_seconds": .02,
                "marker_publication_transients": {"count": 0, "samples": [], "sample_limit": 128,
                    "samples_dropped": 0, "by_path_code": {}, "first_utc": None, "last_utc": None,
                    "bound": "Existing launcher timeout_seconds; repeated initial missing/sharing polls are aggregated, not a startup deadline."},
                "contract": "One guarded post-UTF SW_SHOWNOACTIVATE queue request, then actual child visibility sustained at every active-test sample. Original never-shown profile unchanged."
            }
            self.marker = None
            self.ready = None
            self.ready_guard = record["diagnostics_readiness_guard"] = {
                "verified": False, "publication_transients": 0,
                "contract": "Both parent and child stay Win32-hidden until the exact active UTF case and closed owned-scene diagnostic preparation receipt are verified."}
            self.selected_child = None
            self.queued_at = None
            self.established = False
            self.user.ShowWindowAsync.argtypes = [wintypes.HWND, ctypes.c_int]
            self.user.ShowWindowAsync.restype = wintypes.BOOL
            self.user.IsIconic.argtypes, self.user.IsIconic.restype = [wintypes.HWND], wintypes.BOOL

        def child_rows(self, pid, windows, foreground):
            rows = native.describe_windows(self.user, windows, pid, foreground)
            window_long = getattr(self.user, "GetWindowLongPtrW", None) or self.user.GetWindowLongW
            for row in rows:
                handle = row["window"]
                live_owned = (row.get("owner_pid") == pid and not row.get("ownership_changed") and
                              bool(self.user.IsWindow(handle)) and native.window_pid(self.user, handle) == pid)
                row["live_owned"] = live_owned
                if not live_owned:
                    row["gone_or_ownership_changed"] = True
                    continue  # Preserve the bounded owned-handle observation; never query a newly foreign window.
                row.update(actual_parent=int(self.user.GetParent(handle) or 0),
                           is_owned_host_descendant=bool(self.user.IsChild(self.handle, handle)),
                           is_child_style=bool(row["style"] & 0x40000000),
                           has_visible_style=bool(row["style"] & 0x10000000),
                           minimized=bool(self.user.IsIconic(handle)),
                           userdata=int(window_long(handle, -21)))
            return rows

        def concealed_unity(self, row, pid, host):
            return (row.get("live_owned") is True and row.get("owner_pid") == pid and row.get("class") == "UnityWndClass" and
                    row.get("is_child_style") is True and row.get("actual_parent") == self.handle and
                    row.get("is_owned_host_descendant") is True and row.get("rect_available") is True and
                    row["rect"]["right"] > row["rect"]["left"] and row["rect"]["bottom"] > row["rect"]["top"] and
                    self.outside(row["rect"], host["virtual_desktop"]) and not row.get("foreground") and
                    not row.get("minimized") and not row.get("extended_style", 0) & 0x00040008)

        def expose_only_to_windows(self, pid, windows):
            if self.data["shown_without_activation"]:
                return
            if self.ready is None:
                return  # No startup parent show: even a WS_VISIBLE child stays behind a hidden ancestor.
            require(self.marker is not None and self.marker.get("active_case") == self.ready["case_full_name"] and
                    not self.marker.get("run_finished"), "Refuse first parent show after readiness case ended")
            before = int(self.user.GetForegroundWindow() or 0)
            host = self.inspect(before)
            rows = self.child_rows(pid, windows, before)
            matching = [row for row in rows if self.concealed_unity(row, pid, host)]
            if not matching:
                return
            require(len(matching) == 1, "Expected one exactly contained Unity child before host show")
            require(native.window_pid(self.user, before) not in (pid, self.owner_pid),
                    "Owned process is already foreground before concealed host show")
            self.guard["before_show"] = host
            self.guard["verified_child_before_show"] = matching
            self.data["show_or_activation_requested"] = True
            self.user.ShowWindow(self.handle, 4)  # Same-thread SW_SHOWNOACTIVATE, prior visual guards retained.
            after = int(self.user.GetForegroundWindow() or 0)
            require(after == before, "Foreground changed during owned nonactivating host show")
            self.data["shown_without_activation"] = True
            self.guard["shown_after_child_verified"] = True
            self.guard["after_show"] = self.inspect(after)
            require(self.guard["after_show"]["visible"], "Concealed host did not become Win32-visible")

        def request_child_visibility(self, pid, child, marker, seconds):
            require(self.ready is not None and self.ready_guard["verified"] is True and
                    marker is not None and marker.get("active_case") == self.ready["case_full_name"],
                    "Refuse first child show without owned-scene readiness in the same active case")
            require(marker is not None and marker.get("active_case") in EXPECTED and not marker.get("run_finished"),
                    "Refuse child show without a verified active UTF case")
            require(not self.child_guard["show_requests"], "Refuse repeated child show requests")
            before = int(self.user.GetForegroundWindow() or 0)
            host = self.inspect(before)
            require(host["visible"] and host["alpha"] == 0 and host["offscreen"], "Concealed host is not ready for child show")
            require(native.window_pid(self.user, before) not in (pid, self.owner_pid), "Owned process already foreground")
            rows = self.child_rows(pid, [child], before)
            require(len(rows) == 1 and self.concealed_unity(rows[0], pid, host),
                    "Refuse child show after live containment/style/rectangle validation failed")
            receipt = {"requested_utc": native.utc(), "seconds": seconds, "window": child, "pid": pid,
                       "method": "ShowWindowAsync", "show_mode": 4, "show_mode_name": "SW_SHOWNOACTIVATE",
                       "before_foreground": before, "before_host": host, "before_child": rows[0],
                       "actual_utf_marker": marker, "diagnostics_ready_sha256": self.ready_guard["receipt_sha256"],
                       "queued": False, "observed_visible": False}
            self.child_guard["show_requests"].append(receipt)
            ctypes.set_last_error(0)
            queued = bool(self.user.ShowWindowAsync(child, 4))
            receipt.update(queued=queued, last_error=ctypes.get_last_error(),
                           after_queue_foreground=int(self.user.GetForegroundWindow() or 0))
            require(queued, "Nonactivating child show event was not queued")
            require(receipt["after_queue_foreground"] == before, "Foreground changed during nonactivating child show queue")
            receipt["after_queue_host"] = self.inspect(receipt["after_queue_foreground"])
            receipt["after_queue_child"] = self.child_rows(pid, [child], receipt["after_queue_foreground"])[0]
            require(self.concealed_unity(receipt["after_queue_child"], pid, receipt["after_queue_host"]),
                    "Child containment/style/rectangle failed after nonactivating show queue")
            self.queued_at = time.monotonic()

        def inspect_child_visibility(self, pid, rows, host, marker, seconds):
            if self.ready is None:
                require(not host["visible"] and not self.data["shown_without_activation"] and
                        not self.child_guard["show_requests"], "A parent/child show preceded diagnostics readiness")
                return
            if marker is None or marker.get("terminal_boundary_verified") is True:
                return
            require(all(row.get("live_owned") is True for row in rows),
                    "Owned handle disappeared or changed ownership during the active UTF interval")
            require(host["visible"], "Concealed host is hidden after UTF began")
            matching = [row for row in rows if self.concealed_unity(row, pid, host)]
            require(len(matching) == 1,
                    "Active UTF interval lost the unique exactly contained owned Unity child")
            child = matching[0]
            if self.selected_child is None:
                self.selected_child = child["window"]
                self.child_guard["selected_child"] = self.selected_child
            require(child["window"] == self.selected_child, "Owned Unity child HWND changed during UTF interval")
            if child["visible"] and child["has_visible_style"]:
                if not self.established:
                    self.established = True
                    self.child_guard.update(visibility_established=True, visibility_established_utc=native.utc(),
                                            visibility_established_seconds=seconds, visibility_established_child=child)
                    if self.child_guard["show_requests"]:
                        self.child_guard["show_requests"][0].update(observed_visible=True, observed_visible_utc=native.utc())
                name = marker.get("active_case")
                if name and name not in self.child_guard["case_visibility_observed"]:
                    self.child_guard["case_visibility_observed"].append(name)
                return
            if self.established:
                self.child_guard.update(visibility_lost=True, visibility_lost_utc=native.utc(), visibility_lost_child=child)
                raise ValueError("Owned child lost Win32 visibility after it was established; no repeated show allowed")
            if not self.child_guard["show_requests"]:
                # Never mutate between cases; an actual active callback is mandatory.
                if marker.get("active_case") is not None:
                    self.request_child_visibility(pid, child["window"], marker, seconds)
            else:
                require(self.queued_at is not None and time.monotonic() - self.queued_at <= self.SHOW_TIMEOUT,
                        "Queued child show did not produce actual visibility within two seconds")

        def retain(self, sample):
            if len(self.child_guard["history"]) >= self.HISTORY_LIMIT:
                self.child_guard["history_limit_reached"] = True
                self.child_guard["dropped_samples"] += 1
                raise ValueError("Child visibility history capacity exhausted; refusing truncated qualification")
            self.child_guard["history"].append(sample)
            self.child_guard["samples"] += 1
            self.child_guard["last_sample"] = sample
            self.guard["samples"] += 1
            self.guard["last_sample"] = sample


    class ConcealedHost(ChildVisibilityHost):
        def __init__(self, kernel, user, record):
            require(type(record) is dict and type(record.get("errors")) is list and
                    type(record.get("diagnostic_output_directory")) is str and
                    Path(record["diagnostic_output_directory"]).is_absolute(),
                    "Concealed host requires an absolute output directory and errors list")
            super().__init__(kernel, user, record)
            record["concealed_window_profile"] = profile

    return ConcealedHost


def check_concealed_windows(user, callback_type, pid, record, parent=None, seconds=None, *,
                            marker=None, readiness=None, readiness_sha256=None, cleanup=False):
    """Poll concealment; tokens are caller-validated, cleanup cannot qualify a run."""
    if not isinstance(parent, _ProtocolHost):
        raise ValueError("Diagnostic requires its owned common concealed host")
    native = parent._native
    require = native.require
    require(user is parent.user and record is parent.record, "Concealed host context differs")
    require(type(cleanup) is bool and type(pid) is int and pid > 0, "Invalid concealed poll context")
    require(seconds is None or type(seconds) in (int, float) and math.isfinite(seconds) and seconds >= 0,
            "Invalid concealed elapsed seconds")
    windows = native.own_windows(user, callback_type, pid, parent.handle)
    if cleanup:
        record["diagnostic_cleanup_guard_mode"] = True
        marker = None
    else:
        require(not record.get("diagnostic_cleanup_guard_mode"), "Refuse qualification after cleanup began")
        marker = parent.accept_protocol(pid, marker, readiness, readiness_sha256)
        parent.expose_only_to_windows(pid, windows)
    foreground = int(user.GetForegroundWindow() or 0)
    foreground_pid = native.window_pid(user, foreground) if foreground else None
    host = parent.inspect(foreground)
    rows = parent.child_rows(pid, windows, foreground)
    visible = [row for row in rows if row.get("visible")]
    escaped = [row for row in visible if row.get("live_owned") is not True or row.get("owner_pid") != pid or not row.get("is_child_style") or
               not row.get("is_owned_host_descendant") or not row.get("rect_available") or
               not parent.outside(row["rect"], host["virtual_desktop"])]
    sample = {"utc": native.utc(), "seconds": seconds, "foreground_window": foreground,
              "foreground_pid": foreground_pid, "host": host, "all_owned_windows": rows,
              "visible_owned_descendants": visible, "escaped": escaped,
              "utf_marker": marker, "child_visibility_established": parent.established,
              "cleanup_guard_mode": cleanup}
    parent.retain(sample)
    violation = foreground_pid in (pid, parent.owner_pid) or host["foreground"] or bool(escaped)
    if violation:
        record["foreground_or_visually_exposed_window_violation"] = True
        record["window_violation_observation"] = sample
        record["errors"].append("Concealed child diagnostic lost visual hiding or foreground guard")
        return False
    if not cleanup:
        parent.inspect_child_visibility(pid, rows, host, marker, seconds)
    return True
