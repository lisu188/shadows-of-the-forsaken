"""Validate bounded hidden, audio-enabled primary Windows player startup/exit.

Uses batch mode because SW_HIDE alone cannot guarantee no foreground activation.
No screenshots, input, focus activation, clock changes or progression commands.
Only the spawned process's windows may receive WM_CLOSE; fallback termination
uses that Popen handle, never process-name matching or a process-tree kill.
"""
import argparse
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from uuid import UUID

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("player_build_manifest", ROOT / "tools/player_build_manifest.py")
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)


class WindowMessage(ctypes.Structure):
    _fields_ = [("hwnd", wintypes.HWND), ("message", wintypes.UINT),
                ("wParam", ctypes.c_size_t), ("lParam", ctypes.c_ssize_t),
                ("time", wintypes.DWORD), ("pt", wintypes.POINT), ("lPrivate", wintypes.DWORD)]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def write_new(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False)
        stream.write("\n")


def windows_api():
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                  wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.ReadFile.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
                               ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
    kernel.ReadFile.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.restype = wintypes.BOOL
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.GetCurrentThreadId.restype = wintypes.DWORD
    kernel.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
    kernel.GetModuleHandleW.restype = wintypes.HMODULE
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.SetThreadExecutionState.argtypes = [wintypes.DWORD]
    kernel.SetThreadExecutionState.restype = wintypes.DWORD
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user.EnumWindows.restype = wintypes.BOOL
    user.EnumChildWindows.argtypes = [wintypes.HWND, callback_type, wintypes.LPARAM]
    user.EnumChildWindows.restype = wintypes.BOOL
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.GetWindowThreadProcessId.restype = wintypes.DWORD
    user.IsWindowVisible.argtypes = [wintypes.HWND]
    user.IsWindowVisible.restype = wintypes.BOOL
    user.IsWindow.argtypes = [wintypes.HWND]
    user.IsWindow.restype = wintypes.BOOL
    user.CreateWindowExW.argtypes = [wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
                                   ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                   wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, wintypes.LPVOID]
    user.CreateWindowExW.restype = wintypes.HWND
    user.DestroyWindow.argtypes = [wintypes.HWND]
    user.DestroyWindow.restype = wintypes.BOOL
    user.PeekMessageW.argtypes = [ctypes.POINTER(WindowMessage), wintypes.HWND, wintypes.UINT, wintypes.UINT, wintypes.UINT]
    user.PeekMessageW.restype = wintypes.BOOL
    user.DispatchMessageW.argtypes = [ctypes.POINTER(WindowMessage)]
    user.DispatchMessageW.restype = ctypes.c_ssize_t
    user.GetForegroundWindow.restype = wintypes.HWND
    user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetWindowTextW.restype = ctypes.c_int
    user.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetClassNameW.restype = ctypes.c_int
    user.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user.GetWindowRect.restype = wintypes.BOOL
    window_long = getattr(user, "GetWindowLongPtrW", None) or user.GetWindowLongW
    window_long.argtypes = [wintypes.HWND, ctypes.c_int]
    window_long.restype = ctypes.c_ssize_t
    user.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user.PostMessageW.restype = wintypes.BOOL
    return kernel, user, callback_type


def process_integrity(kernel, pid=None):
    """Read the actual process token; unknown integrity is never a qualified pass."""
    security = ctypes.WinDLL("advapi32", use_last_error=True)
    security.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    security.OpenProcessToken.restype = wintypes.BOOL
    security.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.LPVOID,
                                             wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
    security.GetTokenInformation.restype = wintypes.BOOL
    security.GetSidSubAuthorityCount.argtypes = [wintypes.LPVOID]
    security.GetSidSubAuthorityCount.restype = ctypes.POINTER(ctypes.c_ubyte)
    security.GetSidSubAuthority.argtypes = [wintypes.LPVOID, wintypes.DWORD]
    security.GetSidSubAuthority.restype = ctypes.POINTER(wintypes.DWORD)
    class SidAndAttributes(ctypes.Structure):
        _fields_ = [("sid", wintypes.LPVOID), ("attributes", wintypes.DWORD)]
    process = kernel.GetCurrentProcess() if pid is None else kernel.OpenProcess(0x1000, False, pid)
    require(bool(process), "Cannot open process to inspect integrity")
    token = wintypes.HANDLE()
    try:
        if not security.OpenProcessToken(process, 0x0008, ctypes.byref(token)):
            raise ctypes.WinError(ctypes.get_last_error())
        length = wintypes.DWORD()
        security.GetTokenInformation(token, 25, None, 0, ctypes.byref(length))
        require(length.value >= ctypes.sizeof(SidAndAttributes), "Missing token integrity label")
        buffer = ctypes.create_string_buffer(length.value)
        if not security.GetTokenInformation(token, 25, buffer, length, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        sid = ctypes.cast(buffer, ctypes.POINTER(SidAndAttributes)).contents.sid
        count = security.GetSidSubAuthorityCount(sid).contents.value
        require(count > 0, "Empty token integrity SID")
        rid = security.GetSidSubAuthority(sid, count - 1).contents.value
        label = ("untrusted" if rid < 0x1000 else "low" if rid < 0x2000 else
                 "medium" if rid < 0x3000 else "high" if rid < 0x4000 else "system_or_higher")
        return {"pid": os.getpid() if pid is None else pid, "rid": rid, "label": label,
                "normal_integrity": rid >= 0x2000, "utc": utc()}
    finally:
        if token.value:
            kernel.CloseHandle(token)
        if pid is not None:
            kernel.CloseHandle(process)


@contextmanager
def keep_awake(kernel, record):
    """Scope the thread's display/system request to this one owned player run."""
    request = record["power_request"] = {"flags": 0x80000003, "requested_utc": utc(), "restored": False}
    previous = kernel.SetThreadExecutionState(request["flags"])
    request["previous_flags"] = previous
    require(bool(previous), "Temporary display/system power request failed")
    try:
        yield
    finally:
        restored = kernel.SetThreadExecutionState(previous | 0x80000000)
        request["restored"] = bool(restored)
        request["released_utc"] = utc()
        if not restored:
            record["errors"].append("Temporary display/system power request could not be restored")


def log_findings(log):
    """Known native/audio failures reject a lifecycle even with process exit zero.

    Other diagnostic candidates remain available for review without treating
    every plugin/unused-render-pass message as the same native failure.
    """
    fatal = re.compile(r"Native Crash Reporting|Got a UNKNOWN while executing native code|"
                       r"\ba fatal error in (?:the )?(?:mono|runtime)|"
                       r"\b(?:SIGSEGV|SIGABRT|EXCEPTION_ACCESS_VIOLATION)\b|"
                       r"\b(?:fatal (?:error|signal)|fatal-runtime|FATAL UNHANDLED EXCEPTION)\b|Crash!!!", re.I)
    audio = re.compile(r"FMOD[^\r\n]*(?:failed|nosound|no sound)|"
                       r"default audio device[^\r\n]*failed|"
                       r"audio system failed to initialize|Error initializing output device", re.I)
    candidate = re.compile(r"(?:\b(?:exception|fatal|crash)\b|\berror\s*:|\[error\]|Failed to|evidence disabled)", re.I)
    result = {"native_fatal_markers": [], "audio_failure_markers": [], "diagnostic_candidates": [],
              "graphics_threading_observations": []}
    for number, line in enumerate(log.splitlines(), 1):
        row = {"line": number, "text": line}
        if fatal.search(line):
            result["native_fatal_markers"].append(row)
        if audio.search(line):
            result["audio_failure_markers"].append(row)
        if candidate.search(line):
            result["diagnostic_candidates"].append(row)
        threading = re.search(r"\bkGfxThreadingMode\w+", line)
        if threading:
            result["graphics_threading_observations"].append({**row, "mode": threading.group()})
    return result


def scan_player_logs(kernel, directory):
    findings = {"native_fatal_markers": [], "audio_failure_markers": [], "diagnostic_candidates": [],
                "graphics_threading_observations": [], "log_receipts": []}
    for name in ("player.log", "launcher-stdout.log", "launcher-stderr.log"):
        raw = shared_read(kernel, directory / name)
        findings["log_receipts"].append({"path": name, "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()})
        if name == "player.log":
            findings["log_sha256"] = hashlib.sha256(raw).hexdigest()
        for key, rows in log_findings(raw.decode("utf-8-sig", errors="replace")).items():
            findings[key].extend({"file": name, **row} for row in rows)
    return findings


def player_command(player, output, graphics_mode="default", parent_handle=None, player_mode="batch"):
    modes = {"default": [], "multithreaded": ["-force-gfx-mt"], "legacy": ["-force-gfx-jobs", "legacy"]}
    require(graphics_mode in modes, "Unknown graphics threading diagnostic mode")
    require(parent_handle is None or type(parent_handle) is int and parent_handle > 0, "Invalid owned parent HWND")
    require(player_mode in ("batch", "embedded"), "Unknown player lifecycle diagnostic mode")
    require(player_mode != "embedded" or parent_handle is not None, "Embedded mode requires an owned parent HWND")
    # Delayed remains the default; an optional owned invisible host supplies a
    # real HWND for the separate graphics-threading diagnostic comparison.
    return [str(player), *(["-batchmode"] if player_mode == "batch" else []),
            "-parentHWND", "delayed" if parent_handle is None else str(parent_handle),
            "-force-d3d11", *modes[graphics_mode], "-screen-fullscreen", "0",
            "-screen-width", "1280", "-screen-height", "720", "-logFile", str(output / "player.log"),
            "-crash-report-folder", str(output / "crashes")]


class StartupObservation:
    """Require advancing same-session snapshots throughout a bounded interval."""
    def __init__(self, seconds, maximum_gap=2):
        self.seconds, self.maximum_gap = seconds, maximum_gap
        self.first_at = self.last_at = self.sequence = self.session = None
        self.count = 0

    def check_freshness(self, now):
        if self.last_at is not None:
            require(now - self.last_at <= self.maximum_gap, "Startup observer stopped publishing fresh snapshots")

    def accept(self, state, now):
        self.check_freshness(now)
        checks = state_check(state)
        sequence = state.get("sequence")
        require(type(sequence) is int and sequence >= 0, "Observer sequence is invalid")
        if self.session is not None:
            require(state.get("sessionId") == self.session, "Session changed during startup observation")
            require(sequence >= self.sequence, "Observer sequence moved backwards")
            require(all(checks.values()), "Previously matched startup state changed: " + json.dumps(checks))
        if not all(checks.values()):
            return False
        if self.session is None:
            self.first_at = now
            self.session = state["sessionId"]
        if self.sequence != sequence:
            self.last_at, self.sequence = now, sequence
            self.count += 1
            return now - self.first_at >= self.seconds and self.count >= 3
        return False


def check_events(events, session):
    require(bool(events), "No observer events")
    require(session is not None and all(item.get("sessionId") == session for item in events),
            "Observer events do not belong to the observed startup session")
    require(all(item.get("objectiveFlags") == 0 and item.get("room") == "Courtyard" and
                item.get("phase") == "Running" for item in events), "Observer events include unexpected progression")
    sequences = [item.get("sequence") for item in events]
    require(all(type(value) is int for value in sequences) and
            all(a < b for a, b in zip(sequences, sequences[1:])), "Observer event sequences are not increasing")
    require(events[0].get("kind") == "started", "No initial observer startup event")
    require(events[-1].get("kind") == "normal-exit", "No final matching normal-exit observer event")


def finish_record(record):
    close = record.get("closure", {})
    record["graceful_player_exit"] = (close.get("forced") is False and close.get("mode") == "WM_CLOSE" and
        close.get("exit_code") == 0 and record.get("normal_exit_event_observed") is True and
        not record.get("native_fatal_markers"))
    if not record["graceful_player_exit"]:
        record["errors"].append("Player lifecycle lacks requested graceful closure and matching normal-exit proof")
    if record.get("native_fatal_markers"):
        record["errors"].append("Native fatal/crash markers reject the lifecycle regardless of process exit code")
    if record.get("audio_failure_markers"):
        record["errors"].append("Audio initialization/reset/no-sound markers reject the audio-enabled lifecycle")
    if record.get("crash_reports"):
        record["errors"].append("Crash-report artifacts reject the lifecycle even without matching log markers")
    if record.get("parent_mode_requested") == "owned" and not record.get("hidden_parent", {}).get("destroyed"):
        record["errors"].append("Owned hidden parent cleanup is not verified")
    complete = (record["startup_state_observed"] and record.get("build_identity_unchanged") is True and
                record.get("integrity_before_launch", {}).get("normal_integrity") is True and
                record.get("child_integrity", {}).get("normal_integrity") is True and
                record.get("power_request", {}).get("restored") is True and not record["errors"])
    record["status"] = "startup_lifecycle_verified" if complete else "startup_lifecycle_failed"
    record["meaning_of_runner_exit_zero"] = ("Bounded hidden normal-integrity startup and requested graceful exit verified, "
        "with unchanged identity and no known native-fatal/audio-failure markers. Generic diagnostics remain separate; "
        "audible output, foreground gameplay, timing and performance are not qualified.")
    return complete


def shared_read(kernel, path, limit=8 * 1024 * 1024):
    # FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE avoids preventing
    # the existing observer's atomic File.Replace publication on Windows.
    handle = kernel.CreateFileW(str(path), 0x80000000, 0x1 | 0x2 | 0x4,
                                None, 3, 0x80, None)
    if handle == wintypes.HANDLE(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        chunks, total = [], 0
        while True:
            count = min(65536, limit + 1 - total)
            buffer = ctypes.create_string_buffer(count)
            read = wintypes.DWORD()
            if not kernel.ReadFile(handle, buffer, count, ctypes.byref(read), None):
                raise ctypes.WinError(ctypes.get_last_error())
            if not read.value:
                break
            chunks.append(buffer.raw[:read.value])
            total += read.value
            require(total <= limit, "Observer/log input exceeds bounded read limit")
        return b"".join(chunks)
    finally:
        kernel.CloseHandle(handle)


def window_pid(user, window):
    pid = wintypes.DWORD()
    user.GetWindowThreadProcessId(window, ctypes.byref(pid))
    return pid.value


def own_windows(user, callback_type, pid, parent_handle=None):
    windows = []
    @callback_type
    def callback(window, _):
        if window_pid(user, window) == pid and int(window) not in windows:
            windows.append(int(window))
        return True
    require(bool(user.EnumWindows(callback, 0)), "Cannot enumerate child-owned windows")
    if parent_handle is not None:
        # EnumChildWindows includes all descendants. Its documented return
        # value is not used (zero is not evidence that enumeration failed).
        user.EnumChildWindows(parent_handle, callback, 0)
    require(len(windows) <= 32, "Player exposed more than 32 windows")
    return windows


def describe_windows(user, windows, pid, foreground):
    """Bounded read-only metadata for child windows; never inspect other titles."""
    result = []
    window_long = getattr(user, "GetWindowLongPtrW", None) or user.GetWindowLongW
    require(len(windows) <= 32, "Child exposed more than 32 top-level windows")
    for window in windows:
        owner = window_pid(user, window)
        if owner != pid:
            result.append({"window": window, "owner_pid": owner, "ownership_changed": True})
            continue
        title, window_class, rect = ctypes.create_unicode_buffer(512), ctypes.create_unicode_buffer(256), wintypes.RECT()
        user.GetWindowTextW(window, title, len(title))
        user.GetClassNameW(window, window_class, len(window_class))
        rect_available = bool(user.GetWindowRect(window, ctypes.byref(rect)))
        result.append({"window": window, "owner_pid": owner, "title": title.value,
                       "class": window_class.value, "visible": bool(user.IsWindowVisible(window)),
                       "foreground": window == foreground, "rect_available": rect_available,
                       "rect": {"left": rect.left, "top": rect.top, "right": rect.right, "bottom": rect.bottom}
                               if rect_available else None,
                       "style": int(window_long(window, -16)) & 0xffffffff,
                       "extended_style": int(window_long(window, -20)) & 0xffffffff})
    return result


class HiddenParentWindow:
    """One never-shown STATIC host, created/pumped/destroyed by this thread."""
    def __init__(self, kernel, user, record):
        self.kernel, self.user, self.handle = kernel, user, None
        self.owner_pid = os.getpid()
        self.data = record["hidden_parent"] = {"owner_pid": self.owner_pid, "created": False,
            "destroyed": False, "class": "STATIC", "style_requested": 0x80000000,
            "extended_style_requested": 0x08000080, "show_or_activation_requested": False,
            "pump_calls": 0, "dispatched_messages": 0}

    def create(self):
        self.data["owner_thread_id"] = self.kernel.GetCurrentThreadId()
        handle = self.user.CreateWindowExW(self.data["extended_style_requested"], "STATIC",
            "Forsaken hidden validation host " + str(self.owner_pid), self.data["style_requested"],
            0, 0, 1280, 720, None, None, self.kernel.GetModuleHandleW(None), None)
        require(bool(handle), "Cannot create hidden validation parent window")
        self.handle = int(handle)
        self.data.update(window=self.handle, created=True, created_utc=utc())
        foreground = self.user.GetForegroundWindow()
        self.data["initial_snapshot"] = describe_windows(self.user, [self.handle], self.owner_pid, foreground)
        snapshot = self.inspect(foreground)
        require(not snapshot["visible"] and not snapshot["foreground"], "Hidden parent became visible or foreground on creation")

    def inspect(self, foreground):
        require(self.handle is not None and self.user.IsWindow(self.handle), "Owned hidden parent disappeared")
        require(window_pid(self.user, self.handle) == self.owner_pid, "Hidden parent ownership changed")
        snapshot = {"window": self.handle, "owner_pid": self.owner_pid,
                    "visible": bool(self.user.IsWindowVisible(self.handle)), "foreground": self.handle == foreground}
        self.data["last_observed"] = snapshot
        return snapshot

    def pump(self):
        require(self.kernel.GetCurrentThreadId() == self.data["owner_thread_id"], "Hidden parent message pump changed thread")
        self.data["pump_calls"] += 1
        message = WindowMessage()
        for _ in range(64):
            if not self.user.PeekMessageW(ctypes.byref(message), self.handle, 0, 0, 1):
                return
            require(message.message != 0x0012, "Hidden parent thread unexpectedly received WM_QUIT")
            require(message.hwnd and window_pid(self.user, message.hwnd) == self.owner_pid,
                    "Refuse dispatch of a message outside the owned host thread")
            self.user.DispatchMessageW(ctypes.byref(message))
            self.data["dispatched_messages"] += 1
        self.data["message_batch_limit_reached"] = True

    def close(self, process):
        if self.handle is None or self.data["destroyed"]:
            return
        require(process is None or process.poll() is not None, "Refuse destroying hidden parent before player exit")
        require(self.kernel.GetCurrentThreadId() == self.data["owner_thread_id"], "Hidden parent cleanup changed thread")
        require(self.user.IsWindow(self.handle) and window_pid(self.user, self.handle) == self.owner_pid,
                "Refuse destroying a missing or foreign parent window")
        self.data["cleanup_requested_utc"] = utc()
        require(bool(self.user.DestroyWindow(self.handle)), "DestroyWindow failed for owned hidden parent")
        self.data["destroyed"] = not bool(self.user.IsWindow(self.handle))
        self.data["cleanup_completed_utc"] = utc()
        require(self.data["destroyed"], "Owned hidden parent still exists after DestroyWindow")


def check_hidden_windows(user, callback_type, pid, record, parent=None, seconds=None):
    windows = own_windows(user, callback_type, pid, parent.handle if parent is not None else None)
    foreground = user.GetForegroundWindow()
    foreground_pid = window_pid(user, foreground) if foreground else None
    visible_windows = [window for window in windows if user.IsWindowVisible(window)]
    host = parent.inspect(foreground) if parent is not None else None
    violation = (foreground_pid == pid or bool(visible_windows) or
                 host is not None and (host["visible"] or host["foreground"]))
    if violation and not record["foreground_or_visible_window_violation"]:
        record["foreground_or_visible_window_violation"] = True
        record["window_violation_observation"] = {"seconds": seconds, "utc": utc(),
            "foreground_window": int(foreground) if foreground else None,
            "foreground_owner_pid": foreground_pid, "trigger_visible_windows": visible_windows,
            "child_windows": describe_windows(user, windows, pid, foreground),
            "hidden_parent": describe_windows(user, [parent.handle], parent.owner_pid, foreground)
                             if parent is not None else None}
        record["errors"].append("Player or hidden host became foreground or exposed a visible window; no other app was activated.")
    return not violation


def cleanup_owned_resources(process, user, callback_type, record, parent=None):
    try:
        if process is not None:
            close_child(process, user, callback_type, record, parent=parent)
    except Exception as error:
        record["errors"].append("Close failure: " + type(error).__name__ + ": " + str(error))
        if process is not None and process.poll() is None:
            try:
                process.terminate()
                process.wait(timeout=5)
            except Exception as stop_error:
                record["errors"].append("Owned process termination failed: " + str(stop_error))
        record["closure"] = {"mode": "TerminateProcess_only_spawned_handle_after_close_error",
            "forced": True, "pid": process.pid, "exit_code": process.poll()}
    finally:
        if parent is not None:
            try:
                parent.close(process)
            except Exception as error:
                record["errors"].append("Hidden parent cleanup: " + type(error).__name__ + ": " + str(error))


def close_child(process, user, callback_type, record, grace_seconds=5, parent=None):
    close = {"requested_utc": utc(), "wm_close": [], "forced": False,
             "pid": process.pid, "other_processes_touched": False}
    record["closure"] = close
    if process.poll() is not None:
        close["mode"] = "already_exited"
    else:
        for window in own_windows(user, callback_type, process.pid, parent.handle if parent is not None else None):
            # Recheck ownership immediately before posting; never target a
            # window merely because it was observed earlier.
            if process.poll() is None and window_pid(user, window) == process.pid:
                sent = bool(user.PostMessageW(window, 0x0010, 0, 0))
                close["wm_close"].append({"window": window, "owner_pid": process.pid, "posted": sent})
        try:
            if parent is None:
                process.wait(timeout=grace_seconds)
            else:
                deadline = time.monotonic() + grace_seconds
                while process.poll() is None and time.monotonic() < deadline:
                    parent.pump()
                    check_hidden_windows(user, callback_type, process.pid, record, parent)
                    time.sleep(.025)
                if process.poll() is None:
                    raise subprocess.TimeoutExpired("owned-player", grace_seconds)
            close["mode"] = "WM_CLOSE" if any(item["posted"] for item in close["wm_close"]) else "exited_while_closing"
        except subprocess.TimeoutExpired:
            close["forced"] = True
            close["mode"] = "TerminateProcess_only_spawned_handle"
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                close["mode"] = "termination_requested_process_still_running"
    close["exit_code"] = process.poll()
    close["completed_utc"] = utc()


def state_check(state):
    player, enemies = state.get("player", {}), state.get("enemies", [])
    expected = {"FirstEnemyDefeated", "MinibossDefeated", "FinalEnemyDefeated"}
    checks = {"running_courtyard": state.get("phase") == "Running" and state.get("room") == "Courtyard",
        "zero_objective_flags": state.get("objectiveFlags") == 0,
        "full_200_health": player.get("health") == player.get("maximumHealth") == 200,
        "three_expected_actors": len(enemies) == 3 and {e.get("objective") for e in enemies} == expected,
        "all_dormant": len(enemies) == 3 and all(e.get("encounterPhase") == "Dormant" and
            e.get("activated") is False and e.get("credited") is False and e.get("pendingCredit") is False
            and e.get("health", 0) == e.get("maximumHealth", -1) and e.get("health", 0) > 0 for e in enemies),
        "observer_no_io_failures": state.get("ioFailureCount") == 0,
        "pinned_native_player": state.get("hardware", {}).get("unityVersion") == "6000.6.3f1" and
            state.get("hardware", {}).get("isEditor") is False,
        "observed_D3D11": state.get("hardware", {}).get("graphicsApi") == "Direct3D11"}
    try:
        UUID(state.get("sessionId", ""))
        checks["valid_session_id"] = True
    except (ValueError, AttributeError):
        checks["valid_session_id"] = False
    return checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--timeout-seconds", type=float, default=45)
    parser.add_argument("--observe-seconds", type=float, default=3)
    parser.add_argument("--graphics-mode", choices=("default", "multithreaded", "legacy"), default="default",
                        help="Optional threading comparison; the observed log mode, not the flag, is evidence")
    parser.add_argument("--parent-mode", choices=("delayed", "owned"), default="delayed",
                        help="Keep delayed default, or diagnose with a never-shown runner-owned parent HWND")
    parser.add_argument("--player-mode", choices=("batch", "embedded"), default="batch",
                        help="Embedded omits batch mode and requires the owned hidden parent; no input or activation")
    args = parser.parse_args()
    require(os.name == "nt", "Runner requires native Windows")
    require(args.player_mode != "embedded" or args.parent_mode == "owned",
            "Embedded player mode requires --parent-mode owned")
    require(5 <= args.timeout_seconds <= 180 and 1 <= args.observe_seconds <= 120 and
            args.observe_seconds < args.timeout_seconds,
            "Bounded startup timeout must be 5-180 seconds; observation 1-120 seconds and shorter than timeout")
    player, out = args.player.absolute(), args.output.absolute()
    require(player.name == "ShadowsOfTheForsaken.exe", "Expected primary castle executable")
    before = manifest.verify(player.parent, args.receipt_sha256)
    receipt = manifest.read_json(player.parent / manifest.RECEIPT)
    require(receipt.get("executable") == player.name, "Receipt executable differs")
    require(out.resolve() == out and player.parent not in out.parents and out != player.parent,
            "Use a fresh evidence directory outside the player directory")
    out.mkdir(exist_ok=False)
    evidence = out / "observer"
    evidence.mkdir()
    (out / "crashes").mkdir()
    (out / "runner.py").write_bytes(Path(__file__).read_bytes())
    write_new(out / "build-identity-before.json", before)
    hash_rows = [item for item in receipt["engine_files"] if item["path"] in
                 (player.name, "UnityPlayer.dll") or
                 (item["path"].startswith(player.stem + "_Data/Managed/") and item["path"].endswith(".dll"))]
    require(any(item["path"].endswith("/Assembly-CSharp.dll") for item in hash_rows), "Missing gameplay assembly identity")
    write_new(out / "executable-assembly-hashes.json", {"receipt_sha256": args.receipt_sha256, "files": hash_rows})
    command = None  # The actual owned HWND is known only after the hidden host is created.
    environment = os.environ.copy()
    environment["SHADOWS_PLAYER_EVIDENCE_DIR"] = str(evidence)
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    kernel, user, callback_type = windows_api()
    record = {"schema_version": 1, "kind": ("embedded hidden native startup lifecycle validation" if args.player_mode == "embedded"
                                           else "hidden audio-enabled native startup lifecycle validation"),
        "started_utc": utc(), "source_commit": receipt["source_commit"], "command": command,
        "window_style": "Hidden", "batch_mode": args.player_mode == "batch", "audio_disabling_flag": False,
        "player_mode_requested": args.player_mode,
        "graphics_mode_requested": args.graphics_mode,
        "parent_mode_requested": args.parent_mode,
        "crash_report_directory": str(out / "crashes"),
        "audio_request": "enabled; no audio disabling flag; audible output not evaluated",
        "child_environment_override": {"SHADOWS_PLAYER_EVIDENCE_DIR": str(evidence)},
        "runner_sha256": manifest.digest(out / "runner.py"), "manifest_tool_sha256": manifest.digest(ROOT / "tools/player_build_manifest.py"),
        "receipt_sha256": args.receipt_sha256, "startup_state_observed": False,
        "observation_seconds_required": args.observe_seconds, "timeout_seconds": args.timeout_seconds,
        "errors": [], "transient_read_errors": [], "foreground_or_visible_window_violation": False,
        "no_input_focus_time_or_progression_injection": True,
        "not_executed": ["screenshots", "visual acceptance", "audio acceptance", "performance qualification",
                         "full-route standalone gameplay", "OS-input gameplay", "human playthrough"]}
    process, last_state, last_sequence, observations = None, None, None, 0
    parent = None
    tracker = StartupObservation(args.observe_seconds)
    start = time.monotonic()
    try:
        record["integrity_before_launch"] = process_integrity(kernel)
        write_new(out / "integrity-before-launch.json", record["integrity_before_launch"])
        require(record["integrity_before_launch"]["normal_integrity"], "Below-medium runner integrity is not qualified")
        with keep_awake(kernel, record), (out / "launcher-stdout.log").open("xb") as stdout, (out / "launcher-stderr.log").open("xb") as stderr:
            try:
                if args.parent_mode == "owned":
                    parent = HiddenParentWindow(kernel, user, record)
                    parent.create()
                command = player_command(player, out, args.graphics_mode, parent.handle if parent is not None else None,
                                         args.player_mode)
                record["command"] = command
                process = subprocess.Popen(command, cwd=player.parent, env=environment, startupinfo=startup,
                    creationflags=subprocess.CREATE_NO_WINDOW, stdout=stdout, stderr=stderr)
                record["pid"] = process.pid
                record["child_integrity"] = process_integrity(kernel, process.pid)
                write_new(out / "child-integrity.json", record["child_integrity"])
                require(record["child_integrity"]["normal_integrity"], "Below-medium child integrity is not qualified")
                write_new(out / "launch.json", record)
                with (out / "observed-status.jsonl").open("x", encoding="utf-8", newline="\n") as snapshots:
                    while time.monotonic() - start < args.timeout_seconds and process.poll() is None:
                        if parent is not None:
                            parent.pump()
                        tracker.check_freshness(time.monotonic())
                        if not check_hidden_windows(user, callback_type, process.pid, record, parent, time.monotonic() - start):
                            write_new(out / "window-violation.json", record["window_violation_observation"])
                            break
                        try:
                            state = json.loads(shared_read(kernel, evidence / "status.json", 1024 * 1024).decode("utf-8-sig"))
                            if state.get("sequence") != last_sequence:
                                snapshots.write(json.dumps(state, ensure_ascii=False) + "\n")
                                snapshots.flush()
                                observations += 1
                                last_sequence = state.get("sequence")
                            last_state = state
                            if tracker.accept(state, time.monotonic()):
                                record["startup_state_observed"] = True
                                break
                        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
                            if len(record["transient_read_errors"]) < 50:
                                record["transient_read_errors"].append({"seconds": time.monotonic() - start,
                                    "type": type(error).__name__, "message": str(error)})
                        time.sleep(.25)
                if not record["startup_state_observed"]:
                    record["errors"].append("Required startup state was not continuously observed within the bounded interval.")
            finally:
                cleanup_owned_resources(process, user, callback_type, record, parent)
    except (Exception, KeyboardInterrupt) as error:
        record["errors"].append(type(error).__name__ + ": " + str(error))
    record["seconds"] = time.monotonic() - start
    if record.get("window_violation_observation") and not (out / "window-violation.json").exists():
        write_new(out / "window-violation.json", record["window_violation_observation"])
    record["completed_utc"] = utc()
    record["observed_snapshot_count"] = observations
    record["matched_session_id"] = tracker.session
    record["matching_fresh_snapshot_count"] = tracker.count
    record["last_observed_state_checks"] = state_check(last_state) if last_state else None
    if last_state:
        write_new(out / "last-observed-status.json", last_state)
    try:
        events_raw = shared_read(kernel, evidence / "events.jsonl")
        event_lines = events_raw.decode("utf-8-sig").splitlines()
        events = [json.loads(line) for line in event_lines if line.strip()]
        record["events_count"] = len(events)
        record["normal_exit_event_observed"] = False
        check_events(events, tracker.session)
        record["normal_exit_event_observed"] = True
        record["events_sha256"] = hashlib.sha256(events_raw).hexdigest()
    except Exception as error:
        record["errors"].append("Event evidence: " + type(error).__name__ + ": " + str(error))
    try:
        record.update(scan_player_logs(kernel, out))
        record["log_review_required"] = bool(record["diagnostic_candidates"])
    except Exception as error:
        record["errors"].append("Player log: " + type(error).__name__ + ": " + str(error))
    try:
        record["crash_reports"] = manifest.file_tree(out / "crashes")
    except Exception as error:
        record["errors"].append("Crash-report inventory: " + type(error).__name__ + ": " + str(error))
    try:
        after = manifest.verify(player.parent, args.receipt_sha256)
        require(after == before, "Build identity changed during startup observation")
        write_new(out / "build-identity-after.json", after)
        record["build_identity_unchanged"] = True
    except Exception as error:
        record["build_identity_unchanged"] = False
        record["errors"].append("Build identity: " + type(error).__name__ + ": " + str(error))
    forced = record.get("closure", {}).get("forced", False)
    if process is not None and process.poll() is None:
        record["errors"].append("Task-owned player has not exited; cleanup is incomplete.")
    elif process is not None and not forced and process.returncode != 0:
        record["errors"].append("Player exited unexpectedly with code " + str(process.returncode))
    complete = finish_record(record)
    write_new(out / "execution.json", record)
    write_new(out / "evidence-file-inventory.json", {"utc": utc(), "files": manifest.file_tree(out)})
    print(json.dumps({key: record.get(key) for key in ("status", "pid", "startup_state_observed", "closure",
        "graceful_player_exit", "log_review_required", "build_identity_unchanged", "graphics_threading_observations",
        "integrity_before_launch", "child_integrity", "native_fatal_markers", "audio_failure_markers", "errors")}, indent=2))
    return 0 if complete else 1


if __name__ == "__main__":
    sys.exit(main())
