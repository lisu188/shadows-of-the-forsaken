"""Concealed-window regressions with fake APIs only; no DLL or player calls."""
import copy
import datetime as dt
import importlib.util
from pathlib import Path
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("concealed_native_window", ROOT / "tools/concealed_native_window.py")
concealed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(concealed)
CASES = frozenset({"Tests.FullRoute.Main", "Tests.FullRoute.Secret"})


def require(condition, message):
    if not condition:
        raise ValueError(message)


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


class Api:
    """A Python callable with writable ctypes signature attributes."""
    def __init__(self, call):
        self.call = call

    def __call__(self, *args):
        return self.call(*args)


class FakeUser:
    def __init__(self):
        self.rows = {}
        self.foreground = 300
        self.alpha, self.layered_flags = 0, 2
        self.desktop = {76: 0, 77: 0, 78: 1920, 79: 1080}
        self.shows = []
        self.queue_result = True
        self.queue_visible = False
        self.show_hook = self.queue_hook = None
        self.queried = []
        for name in ("CreateWindowExW", "SetLayeredWindowAttributes", "GetLayeredWindowAttributes",
                     "GetSystemMetrics", "ShowWindow", "ShowWindowAsync", "IsWindow", "IsWindowVisible",
                     "IsChild", "GetParent", "IsIconic", "GetWindowLongPtrW", "GetForegroundWindow"):
            setattr(self, name, Api(getattr(self, "_" + name)))
        self.rows[300] = self.row(300, 333, "OtherApp", parent=0, visible=True, rect=(10, 10, 500, 500))

    @staticmethod
    def row(handle, owner, kind, parent=0, visible=False, rect=(-5376, -4816, -4096, -4096), style=0, ex=0):
        return dict(window=handle, owner_pid=owner, **{"class": kind}, parent=parent,
                    raw_visible=visible, style=style | (0x10000000 if visible else 0), extended_style=ex,
                    rect_available=True, rect=dict(zip(("left", "top", "right", "bottom"), rect)),
                    minimized=False, title="fake", exists=True, userdata=0)

    def _CreateWindowExW(self, ex, kind, title, style, left, top, width, height, *unused):
        self.rows[100] = self.row(100, 111, kind, style=style, ex=ex,
                                  rect=(left, top, left + width, top + height))
        return 100

    def _SetLayeredWindowAttributes(self, handle, key, alpha, flags):
        self.alpha, self.layered_flags = alpha, flags
        return True

    def _GetLayeredWindowAttributes(self, handle, key, alpha, flags):
        key._obj.value, alpha._obj.value, flags._obj.value = 0, self.alpha, self.layered_flags
        return True

    def _GetSystemMetrics(self, index):
        return self.desktop[index]

    def set_visible(self, handle, visible):
        self.rows[handle]["raw_visible"] = visible
        if visible:
            self.rows[handle]["style"] |= 0x10000000
        else:
            self.rows[handle]["style"] &= ~0x10000000

    def _ShowWindow(self, handle, mode):
        self.shows.append(("host", handle, mode))
        self.set_visible(handle, True)
        if self.show_hook:
            self.show_hook()
        return True

    def _ShowWindowAsync(self, handle, mode):
        self.shows.append(("child", handle, mode))
        if self.queue_visible:
            self.set_visible(handle, True)
        if self.queue_hook:
            self.queue_hook()
        return self.queue_result

    def _IsWindow(self, handle):
        return handle in self.rows and self.rows[handle]["exists"]

    def _IsWindowVisible(self, handle):
        row = self.rows.get(handle)
        if not row or not row["exists"] or not row["raw_visible"]:
            return False
        return not row["parent"] or self._IsWindowVisible(row["parent"])

    def _IsChild(self, parent, handle):
        visited = set()
        while handle in self.rows and handle not in visited:
            visited.add(handle)
            handle = self.rows[handle]["parent"]
            if handle == parent:
                return True
        return False

    def _GetParent(self, handle):
        self.queried.append(("parent", handle))
        return self.rows[handle]["parent"]

    def _IsIconic(self, handle):
        self.queried.append(("iconic", handle))
        return self.rows[handle]["minimized"]

    def _GetWindowLongPtrW(self, handle, index):
        self.queried.append(("long", handle))
        return self.rows[handle]["userdata"]

    def _GetForegroundWindow(self):
        return self.foreground


class FakeBase:
    def __init__(self, kernel, user, record):
        self.kernel, self.user, self.handle = kernel, user, None
        self.owner_pid = 111
        self.data = record["hidden_parent"] = dict(owner_pid=111, created=False, destroyed=False,
            show_or_activation_requested=False, style_requested=0x80000000)

    def inspect(self, foreground):
        require(self.handle is not None and self.user.IsWindow(self.handle), "Owned hidden parent disappeared")
        require(self.user.rows[self.handle]["owner_pid"] == self.owner_pid, "Hidden parent ownership changed")
        return dict(window=self.handle, owner_pid=self.owner_pid,
                    visible=bool(self.user.IsWindowVisible(self.handle)), foreground=self.handle == foreground)


class FakeNative:
    HiddenParentWindow = FakeBase
    require = staticmethod(require)
    utc = staticmethod(utc)

    def __init__(self):
        self.forced_windows = None

    @staticmethod
    def window_pid(user, handle):
        return user.rows.get(handle, {}).get("owner_pid")

    def own_windows(self, user, callback_type, pid, host):
        if self.forced_windows is not None:
            return list(self.forced_windows)
        return [handle for handle, row in user.rows.items() if row["owner_pid"] == pid and row["exists"]]

    @staticmethod
    def describe_windows(user, handles, pid, foreground):
        rows = []
        for handle in handles:
            row = copy.deepcopy(user.rows[handle])
            row.update(visible=bool(user.IsWindowVisible(handle)), foreground=handle == foreground)
            if row["owner_pid"] != pid:
                row["ownership_changed"] = True
            rows.append(row)
        return rows


class ConcealedWindowTests(unittest.TestCase):
    def setUp(self):
        self.native, self.user = FakeNative(), FakeUser()
        self.kernel = types.SimpleNamespace(GetCurrentThreadId=Api(lambda: 17), GetModuleHandleW=Api(lambda unused: 1))
        self.record = dict(diagnostic_output_directory=str((ROOT / "artifacts/fake-concealed-output").absolute()), errors=[])
        Host = concealed.make_concealed_host(self.native, CASES, profile="rendered-routes")
        self.host = Host(self.kernel, self.user, self.record)
        self.host.create()
        self.user.rows[200] = self.user.row(200, 222, "UnityWndClass", parent=100, style=0x40000000)
        self.marker = dict(utc_observed=utc(), run_started_utc=utc(), first_case="Tests.FullRoute.Main",
            first_case_started_utc=utc(), active_case="Tests.FullRoute.Main", run_finished=False,
            terminal_boundary=dict(verified=False), terminal_boundary_verified=False,
            complete_event_count=2, complete_events_sha256="a" * 64, runtime_sha256="b" * 64, runtime_pid=222)
        # Monotonic ordering is explicit, independent of fixture construction order.
        stamp = utc()
        self.marker.update(run_started_utc=stamp, first_case_started_utc=stamp, utc_observed=stamp)
        self.ready = dict(schema_version=1, pid=222, owned_camera_count=1, test_profile="rendered-routes",
            recorded_utc=stamp, case_full_name="Tests.FullRoute.Main", output_directory=self.record["diagnostic_output_directory"],
            scene_path="Assets/Scenes/ForsakenCastle.unity", scene_name="ForsakenCastle", camera_name="Main Camera",
            camera_scene_path="Assets/Scenes/ForsakenCastle.unity", owned_scene_prepared=True, camera_post_processing=False,
            pipeline_at_preparation=False, pipeline_after_preparation=False, pipeline_at_publication=False,
            case_key="main", scene_handle=9, camera_instance_id=10, session_id="actual-owned-session")
        self.last_error_set = mock.patch.object(concealed.ctypes, "set_last_error", create=True)
        self.last_error_get = mock.patch.object(concealed.ctypes, "get_last_error", return_value=0, create=True)
        self.last_error_set.start()
        self.last_error_get.start()
        self.addCleanup(self.last_error_set.stop)
        self.addCleanup(self.last_error_get.stop)

    def poll(self, marker=None, ready=None, digest=None, cleanup=False):
        return concealed.check_concealed_windows(self.user, object(), 222, self.record, self.host, seconds=1,
            marker=marker, readiness=ready, readiness_sha256=digest, cleanup=cleanup)

    def active_poll(self):
        return self.poll(self.marker, self.ready, "c" * 64)

    def establish(self):
        self.assertTrue(self.active_poll())
        self.user.set_visible(200, True)
        self.assertTrue(self.active_poll())
        self.assertTrue(self.host.established)

    def terminal(self, requested_exit_code=0):
        self.marker.update(complete_event_count=6, complete_events_sha256="d" * 64, active_case=None,
            run_finished=True, terminal_boundary_verified=True,
            terminal_boundary=dict(verified=True, completion_sha256="e" * 64, results_sha256="f" * 64,
                events_sha256="d" * 64, requested_exit_code=requested_exit_code, recorded_utc=utc()))

    def test_factory_never_mutates_injected_native_module(self):
        self.assertIs(self.native.HiddenParentWindow, FakeBase)
        self.assertFalse(hasattr(self.native, "check_hidden_windows"))
        self.assertEqual(self.record["concealed_window_profile"], "rendered-routes")

    def test_explicit_profile_cases_and_output_required(self):
        for cases, profile in [("case", "rendered-routes"), ([], "rendered-routes"), (["same", "same"], "rendered-routes"),
                               (CASES, ""), (CASES, "Presentation")]:
            with self.subTest(cases=cases, profile=profile), self.assertRaises(ValueError):
                concealed.make_concealed_host(self.native, cases, profile=profile)
        Host = concealed.make_concealed_host(self.native, CASES, profile="rendered-routes")
        with self.assertRaises(ValueError):
            Host(self.kernel, self.user, dict(diagnostic_output_directory="relative", errors=[]))

    def test_child_ws_visible_before_ready_does_not_show_either_window(self):
        self.user.set_visible(200, True)
        self.assertTrue(self.poll())
        self.assertFalse(self.user.IsWindowVisible(100))
        self.assertFalse(self.user.IsWindowVisible(200))
        self.assertEqual(self.user.shows, [])

    def test_marker_alone_does_not_authorize_host_or_child_show(self):
        self.assertTrue(self.poll(self.marker))
        self.assertEqual(self.user.shows, [])
        self.assertFalse(self.record["diagnostics_readiness_guard"]["verified"])

    def test_readiness_without_marker_is_rejected_before_any_show(self):
        with self.assertRaisesRegex(ValueError, "preceded"):
            self.poll(ready=self.ready, digest="c" * 64)
        self.assertEqual(self.user.shows, [])

    def test_both_show_once_and_queue_is_not_actual_visibility(self):
        self.assertTrue(self.active_poll())
        self.assertEqual(self.user.shows, [("host", 100, 4), ("child", 200, 4)])
        self.assertFalse(self.host.established)
        self.assertFalse(self.host.child_guard["show_requests"][0]["observed_visible"])
        self.user.set_visible(200, True)
        for _ in range(3):
            self.assertTrue(self.active_poll())
        self.assertTrue(self.host.established)
        self.assertTrue(self.host.child_guard["show_requests"][0]["observed_visible"])
        self.assertEqual(len(self.user.shows), 2)
        self.assertEqual(self.host.child_guard["samples"], 4)

    def test_already_visible_contained_child_needs_no_child_show(self):
        self.user.set_visible(200, True)
        self.assertTrue(self.active_poll())
        self.assertEqual(self.user.shows, [("host", 100, 4)])
        self.assertTrue(self.host.established)

    def test_secret_can_be_actual_first_case_without_order_assumption(self):
        self.marker.update(first_case="Tests.FullRoute.Secret", active_case="Tests.FullRoute.Secret")
        self.ready.update(case_full_name="Tests.FullRoute.Secret", case_key="secret")
        self.assertTrue(self.active_poll())

    def test_immutable_first_ready_stays_valid_across_second_case(self):
        self.establish()
        self.marker.update(complete_event_count=4, complete_events_sha256="d" * 64, active_case="Tests.FullRoute.Secret")
        self.assertTrue(self.active_poll())
        self.assertEqual(self.host.child_guard["case_visibility_observed"], ["Tests.FullRoute.Main", "Tests.FullRoute.Secret"])
        self.assertEqual(len(self.user.shows), 2)

    def test_ready_mutation_disappearance_or_digest_change_rejected(self):
        self.establish()
        for ready, digest in [(None, None), (dict(self.ready, camera_name="Changed"), "c" * 64), (self.ready, "d" * 64)]:
            with self.subTest(ready=ready, digest=digest), self.assertRaisesRegex(ValueError, "readiness changed"):
                self.poll(self.marker, ready, digest)

    def test_external_dict_mutation_does_not_mutate_retained_evidence(self):
        self.establish()
        self.marker["runtime_pid"] = 999
        self.ready["camera_name"] = "Changed"
        self.assertEqual(self.host.marker["runtime_pid"], 222)
        self.assertEqual(self.host.ready["camera_name"], "Main Camera")
        self.assertEqual(self.host.ready_guard["receipt"]["camera_name"], "Main Camera")

    def test_ready_type_mutation_equal_under_python_is_not_immutable(self):
        self.establish()
        changed = dict(self.ready, owned_camera_count=True)
        self.assertEqual(changed, self.ready)  # Demonstrate the Python equality trap.
        with self.assertRaisesRegex(ValueError, "readiness changed"):
            self.poll(self.marker, changed, "c" * 64)

    def test_missing_marker_after_verification_rejected(self):
        self.poll(self.marker)
        with self.assertRaisesRegex(ValueError, "disappeared"):
            self.poll()

    def test_marker_type_case_pid_and_boolean_guards(self):
        changes = [dict(runtime_pid=True), dict(runtime_pid=223), dict(first_case="Unexpected"),
                   dict(active_case="Unexpected"), dict(run_finished=1), dict(terminal_boundary_verified=1),
                   dict(complete_event_count=True), dict(complete_event_count=129), dict(runtime_sha256="B" * 64)]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.poll(dict(self.marker, **change), self.ready, "c" * 64)
        self.assertEqual(self.user.shows, [])

    def test_ready_owned_scene_camera_profile_and_timestamp_guards(self):
        changes = [dict(schema_version=True), dict(pid=223), dict(test_profile="presentation"),
                   dict(case_full_name="Tests.FullRoute.Secret"), dict(output_directory="relative"),
                   dict(scene_path="Assets/Other.unity"), dict(camera_scene_path="Assets/Other.unity"),
                   dict(owned_camera_count=True), dict(owned_camera_count=2), dict(owned_scene_prepared=False),
                   dict(camera_post_processing=True), dict(pipeline_at_publication=0), dict(camera_name=""),
                   dict(recorded_utc="2000-01-01T00:00:00Z"), dict(recorded_utc="2999-01-01T00:00:00Z"),
                   dict(recorded_utc="2026-01-01T00:00:00")]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.poll(self.marker, dict(self.ready, **change), "c" * 64)
        self.assertEqual(self.user.shows, [])

    def test_marker_timestamps_require_order_and_utc(self):
        for change in [dict(utc_observed="2000-01-01T00:00:00Z"), dict(first_case_started_utc="2999-01-01T00:00:00Z"),
                       dict(run_started_utc="2026-01-01T00:00:00+01:00")]:
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.poll(dict(self.marker, **change))

    def test_callback_truncation_or_same_count_byte_change_rejected(self):
        self.establish()
        for change in [dict(complete_event_count=1), dict(complete_events_sha256="d" * 64),
                       dict(active_case=None), dict(runtime_sha256="d" * 64)]:
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.poll(dict(self.marker, **change), self.ready, "c" * 64)

    def test_increased_event_count_requires_changed_bytes(self):
        self.establish()
        with self.assertRaisesRegex(ValueError, "grew without changed"):
            self.poll(dict(self.marker, complete_event_count=3), self.ready, "c" * 64)

    def test_marker_observation_clock_cannot_regress(self):
        earlier = dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=2)
        self.marker.update(run_started_utc=earlier.isoformat(), first_case_started_utc=earlier.isoformat())
        self.ready["recorded_utc"] = earlier.isoformat()
        self.establish()
        changed = dict(self.marker, utc_observed=(earlier + dt.timedelta(seconds=1)).isoformat())
        with self.assertRaisesRegex(ValueError, "clock regressed"):
            self.poll(changed, self.ready, "c" * 64)

    def test_verified_terminal_allows_expected_child_destruction(self):
        self.establish()
        self.terminal()
        self.user.rows[200]["exists"] = False
        self.assertTrue(self.active_poll())
        self.assertEqual(len(self.user.shows), 2)

    def test_failed_verified_terminal_only_permits_teardown(self):
        self.establish()
        self.terminal(requested_exit_code=1)
        self.user.set_visible(200, False)
        self.assertTrue(self.active_poll())
        self.assertEqual(self.host.marker["terminal_boundary"]["requested_exit_code"], 1)
        self.assertFalse(self.host.child_guard["visibility_lost"])

    def test_incomplete_terminal_does_not_excuse_mid_test_hiding(self):
        self.establish()
        self.marker.update(complete_event_count=6, complete_events_sha256="d" * 64, active_case=None, run_finished=True)
        self.user.set_visible(200, False)
        with self.assertRaisesRegex(ValueError, "lost Win32 visibility"):
            self.active_poll()

    def test_invalid_or_revoked_terminal_boundary_rejected(self):
        self.establish()
        self.terminal()
        bad = copy.deepcopy(self.marker)
        bad["terminal_boundary"]["events_sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "Terminal hashes"):
            self.poll(bad, self.ready, "c" * 64)
        self.assertTrue(self.active_poll())
        bad = copy.deepcopy(self.marker)
        bad.update(terminal_boundary_verified=False, terminal_boundary=dict(verified=False))
        with self.assertRaisesRegex(ValueError, "revoked"):
            self.poll(bad, self.ready, "c" * 64)

    def test_alpha_styles_rect_and_desktop_guard_precede_host_show(self):
        for change in ("alpha", "style", "rect", "desktop"):
            with self.subTest(change=change):
                alpha, row, desktop = self.user.alpha, copy.deepcopy(self.user.rows[100]), dict(self.user.desktop)
                if change == "alpha": self.user.alpha = 1
                elif change == "style": self.user.rows[100]["extended_style"] |= 0x8
                elif change == "rect": self.user.rows[100]["rect"] = dict(left=0, top=0, right=100, bottom=100)
                else: self.user.desktop.update({76: -10000, 77: -10000, 78: 20000, 79: 20000})
                with self.assertRaises(ValueError):
                    self.active_poll()
                self.assertEqual(self.user.shows, [])
                self.user.alpha, self.user.rows[100], self.user.desktop = alpha, row, desktop

    def test_foreground_player_or_host_rejected_before_host_show(self):
        self.user.foreground = 200
        self.assertFalse(self.active_poll())
        self.assertTrue(self.record["foreground_or_visually_exposed_window_violation"])
        self.assertEqual(self.user.shows, [])
        self.user.foreground = 100
        with self.assertRaises(ValueError):
            self.active_poll()
        self.assertEqual(self.user.shows, [])

    def test_child_bad_parent_rect_style_minimized_or_ambiguous_prevents_show(self):
        for change in ("parent", "rect", "style", "minimized", "two"):
            with self.subTest(change=change):
                saved = copy.deepcopy(self.user.rows[200])
                if change == "parent": self.user.rows[200]["parent"] = 300
                elif change == "rect": self.user.rows[200]["rect"] = dict(left=0, top=0, right=100, bottom=100)
                elif change == "style": self.user.rows[200]["style"] = 0
                elif change == "minimized": self.user.rows[200]["minimized"] = True
                else: self.user.rows[201] = self.user.row(201, 222, "UnityWndClass", parent=100, style=0x40000000)
                with self.assertRaises(ValueError):
                    self.active_poll()
                self.assertEqual(self.user.shows, [])
                self.user.rows[200] = saved
                self.user.rows.pop(201, None)

    def test_hidden_auxiliary_unity_window_is_retained_but_not_selected(self):
        self.user.rows[201] = self.user.row(201, 222, "UnityWndClass")
        self.establish()
        rows = self.host.child_guard["last_sample"]["all_owned_windows"]
        self.assertEqual({row["window"] for row in rows}, {200, 201})
        self.assertEqual(self.host.selected_child, 200)

    def test_visible_escaped_auxiliary_fails_guard_and_records_observation(self):
        self.establish()
        self.user.rows[201] = self.user.row(201, 222, "UnityWndClass", visible=True)
        self.assertFalse(self.active_poll())
        self.assertTrue(self.record["foreground_or_visually_exposed_window_violation"])
        self.assertEqual(self.record["window_violation_observation"]["escaped"][0]["window"], 201)

    def test_foreign_handle_is_recorded_without_additional_child_api_queries(self):
        self.establish()
        self.native.forced_windows = [200]
        self.user.rows[200]["owner_pid"] = 999
        self.user.queried.clear()
        self.assertFalse(self.active_poll())
        row = self.host.child_guard["last_sample"]["all_owned_windows"][0]
        self.assertTrue(row["gone_or_ownership_changed"])
        self.assertFalse(any(handle == 200 for _, handle in self.user.queried))

    def test_disappeared_handle_during_active_case_is_rejected(self):
        self.establish()
        self.native.forced_windows = [200]
        self.user.rows[200]["exists"] = False
        with self.assertRaisesRegex(ValueError, "disappeared or changed"):
            self.active_poll()

    def test_child_handle_replacement_is_rejected(self):
        self.establish()
        self.user.rows[200]["exists"] = False
        self.user.rows[201] = self.user.row(201, 222, "UnityWndClass", parent=100, style=0x40000000, visible=True)
        with self.assertRaisesRegex(ValueError, "HWND changed"):
            self.active_poll()

    def test_queue_failure_retains_attempt_and_never_retries(self):
        self.user.queue_result = False
        with self.assertRaisesRegex(ValueError, "not queued"):
            self.active_poll()
        self.assertEqual(len(self.host.child_guard["show_requests"]), 1)
        self.assertFalse(self.host.child_guard["show_requests"][0]["queued"])
        with self.assertRaises(ValueError):
            self.active_poll()
        self.assertEqual(len(self.user.shows), 2)

    def test_queue_foreground_change_is_fail_closed(self):
        self.user.queue_hook = lambda: setattr(self.user, "foreground", 200)
        with self.assertRaisesRegex(ValueError, "Foreground changed"):
            self.active_poll()
        self.assertEqual(len(self.host.child_guard["show_requests"]), 1)

    def test_parent_show_foreground_change_prevents_child_show(self):
        self.user.show_hook = lambda: setattr(self.user, "foreground", 200)
        with self.assertRaisesRegex(ValueError, "Foreground changed"):
            self.active_poll()
        self.assertEqual(self.user.shows, [("host", 100, 4)])
        self.assertEqual(self.host.child_guard["show_requests"], [])

    def test_queue_containment_readback_is_fail_closed(self):
        self.user.queue_hook = lambda: self.user.rows[200].update(parent=300)
        with self.assertRaisesRegex(ValueError, "containment/style/rectangle failed"):
            self.active_poll()
        self.assertEqual(len(self.host.child_guard["show_requests"]), 1)

    def test_queue_timeout_never_repeats_show(self):
        with mock.patch.object(concealed.time, "monotonic", return_value=10):
            self.active_poll()
        with mock.patch.object(concealed.time, "monotonic", return_value=12.001), self.assertRaisesRegex(ValueError, "two seconds"):
            self.active_poll()
        self.assertEqual(len(self.user.shows), 2)

    def test_visibility_loss_is_recorded_without_repeat_show(self):
        self.establish()
        self.user.set_visible(200, False)
        with self.assertRaisesRegex(ValueError, "lost Win32 visibility"):
            self.active_poll()
        self.assertTrue(self.host.child_guard["visibility_lost"])
        self.assertEqual(len(self.user.shows), 2)

    def test_history_overflow_is_failure_and_marks_dropped_sample(self):
        self.host.HISTORY_LIMIT = 1
        self.assertTrue(self.poll())
        with self.assertRaisesRegex(ValueError, "capacity exhausted"):
            self.poll()
        self.assertEqual(len(self.host.child_guard["history"]), 1)
        self.assertEqual(self.host.child_guard["dropped_samples"], 1)
        self.assertTrue(self.host.child_guard["history_limit_reached"])

    def test_cleanup_bypasses_broken_protocol_without_any_show(self):
        self.poll(self.marker)
        self.assertTrue(self.poll(cleanup=True))
        self.assertTrue(self.record["diagnostic_cleanup_guard_mode"])
        self.assertEqual(self.user.shows, [])
        with self.assertRaisesRegex(ValueError, "after cleanup"):
            self.active_poll()

    def test_cleanup_retains_visual_guards_after_terminal(self):
        self.establish()
        self.terminal()
        self.assertTrue(self.active_poll())
        self.user.rows[201] = self.user.row(201, 222, "Other", visible=True)
        self.assertFalse(self.poll(cleanup=True))
        self.assertTrue(self.record["child_visibility_guard"]["last_sample"]["cleanup_guard_mode"])
        self.assertEqual(len(self.user.shows), 2)

    def test_wrong_context_invalid_elapsed_and_bool_pid_refused(self):
        with self.assertRaises(ValueError):
            concealed.check_concealed_windows(self.user, None, True, self.record, self.host)
        with self.assertRaises(ValueError):
            concealed.check_concealed_windows(self.user, None, 222, dict(self.record), self.host)
        for seconds in (True, -1, float("nan"), float("inf")):
            with self.subTest(seconds=seconds), self.assertRaises(ValueError):
                concealed.check_concealed_windows(self.user, None, 222, self.record, self.host, seconds=seconds)


if __name__ == "__main__":
    unittest.main()
