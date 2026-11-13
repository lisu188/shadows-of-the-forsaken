"""Native lifecycle rejection/cleanup regressions; never launch a real player."""
import copy
import datetime as dt
import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('native_player_validation', ROOT / 'tools/native_player_validation.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def state(sequence=1, published_seconds=None):
    published = dt.datetime(2026, 10, 16, tzinfo=dt.timezone.utc) + dt.timedelta(
        seconds=sequence - 1 if published_seconds is None else published_seconds)
    return dict(utc=published.isoformat().replace('+00:00', 'Z'),
                phase='Running', room='Courtyard', objectiveFlags=0, sequence=sequence,
                sessionId='90323dc2-7cc5-4999-b5f2-7267392b72fc', ioFailureCount=0,
                player=dict(health=200, maximumHealth=200),
                hardware=dict(unityVersion='6000.6.3f1', isEditor=False, graphicsApi='Direct3D11'),
                enemies=[dict(objective=objective, encounterPhase='Dormant', activated=False,
                              credited=False, pendingCredit=False, health=health, maximumHealth=health)
                         for objective, health in [('FirstEnemyDefeated', 50), ('MinibossDefeated', 125),
                                                   ('FinalEnemyDefeated', 100)]])


def record():
    return dict(startup_state_observed=True, errors=[], build_identity_unchanged=True,
                observation_seconds_required=120, matching_fresh_snapshot_count=121,
                startup_observation=dict(first_published_utc='2026-10-16T00:00:00.0000000Z',
                    last_published_utc='2026-10-16T00:02:00.0000000Z', published_span_100ns=1200000000,
                    published_span_seconds=120, monotonic_span_seconds=120, matching_fresh_snapshot_count=121),
                foreground_or_visible_window_violation=False,
                normal_exit_event_observed=True, integrity_before_launch=dict(normal_integrity=True),
                child_integrity=dict(normal_integrity=True), power_request=dict(restored=True),
                closure=dict(mode='WM_CLOSE', forced=False, exit_code=0),
                native_fatal_markers=[], audio_failure_markers=[])


class NativePlayerValidationTests(unittest.TestCase):
    def host(self):
        kernel, user, evidence = mock.Mock(), mock.Mock(), record()
        kernel.GetCurrentThreadId.return_value = 7
        kernel.GetModuleHandleW.return_value = 123
        user.CreateWindowExW.return_value = 900
        user.GetForegroundWindow.return_value = 0
        user.IsWindow.return_value = True
        user.IsWindowVisible.return_value = False
        parent = native.HiddenParentWindow(kernel, user, evidence)
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid), \
             mock.patch.object(native, 'describe_windows', return_value=[]):
            parent.create()
        return parent, kernel, user, evidence

    def test_exit_zero_and_normal_event_cannot_hide_actual_native_crash_excerpt(self):
        log = '''[Physics::Module] Cleanup current backend.
=================================================================
        Native Crash Reporting
=================================================================
Got a UNKNOWN while executing native code. This usually indicates
a fatal error in the mono runtime or one of the native libraries
used by your application.
'''
        evidence = record()
        evidence.update(native.log_findings(log))
        self.assertFalse(native.finish_record(evidence))
        self.assertFalse(evidence['graceful_player_exit'])
        self.assertEqual(evidence['closure']['exit_code'], 0)
        self.assertTrue(evidence['normal_exit_event_observed'])
        self.assertEqual(evidence['status'], 'startup_lifecycle_failed')
        self.assertEqual([row['line'] for row in evidence['native_fatal_markers']], [3, 5, 6])
        self.assertTrue(native.log_findings('Got a UNKNOWN while executing native code.')['native_fatal_markers'])

    def test_audio_device_failure_and_no_sound_reject_an_otherwise_clean_lifecycle(self):
        for text in ['Default audio device was changed, but the audio system failed to initialize it.',
                     'FMOD failed to initialize the output device.: "Error initializing output device. " (60)',
                     'FMOD failed to initialize any audio devices, running on emulated software output with no sound.',
                     'FMOD initialized on nosound output.']:
            with self.subTest(text=text):
                evidence = record()
                evidence.update(native.log_findings(text))
                self.assertFalse(native.finish_record(evidence))
                self.assertTrue(evidence['audio_failure_markers'])

    def test_generic_diagnostics_remain_separate_from_known_fatal_audio_failures(self):
        findings = native.log_findings('Failed to load plugin optional-vendor.dll\n'
                                      '[Licensing] Error: validation refresh deferred\n'
                                      'Unused DepthOfField pass is not supported\n'
                                      'FMOD initialized successfully\n'
                                      'GfxDevice: creating device client; kGfxThreadingModeClientWorkerJobs')
        self.assertEqual(findings['native_fatal_markers'], [])
        self.assertEqual(findings['audio_failure_markers'], [])
        self.assertEqual(len(findings['diagnostic_candidates']), 2)
        self.assertEqual(findings['graphics_threading_observations'][0]['mode'],
                         'kGfxThreadingModeClientWorkerJobs')

    def test_fatal_only_in_launcher_stderr_still_rejects_exit_zero(self):
        logs = {'player.log': b'Normal engine shutdown\n', 'launcher-stdout.log': b'',
                'launcher-stderr.log': b'Got a UNKNOWN while executing native code.\n'}
        with mock.patch.object(native, 'shared_read', side_effect=lambda kernel, path: logs[path.name]):
            findings = native.scan_player_logs(None, Path('evidence'))
        self.assertEqual(findings['native_fatal_markers'][0]['file'], 'launcher-stderr.log')
        self.assertEqual(len(findings['log_receipts']), 3)
        evidence = record()
        evidence.update(findings)
        self.assertFalse(native.finish_record(evidence))

    def test_window_diagnostics_capture_own_metadata_and_skip_foreign_titles(self):
        user = mock.Mock()
        user.GetWindowTextW.side_effect = lambda window, buffer, length: setattr(buffer, 'value', 'Unity child')
        user.GetClassNameW.side_effect = lambda window, buffer, length: setattr(buffer, 'value', 'UnityWndClass')
        def rectangle(window, target):
            target._obj.left, target._obj.top, target._obj.right, target._obj.bottom = 10, 20, 1290, 740
            return True
        user.GetWindowRect.side_effect = rectangle
        user.GetWindowLongPtrW.return_value = 0x10000000
        user.IsWindowVisible.return_value = True
        with mock.patch.object(native, 'window_pid', side_effect=[42, 99]):
            rows = native.describe_windows(user, [100, 200], 42, 100)
        self.assertEqual(rows[0]['title'], 'Unity child')
        self.assertEqual(rows[0]['class'], 'UnityWndClass')
        self.assertEqual(rows[0]['rect'], dict(left=10, top=20, right=1290, bottom=740))
        self.assertTrue(rows[0]['visible'])
        self.assertTrue(rows[0]['foreground'])
        self.assertEqual(rows[0]['style'], 0x10000000)
        self.assertEqual(rows[1], dict(window=200, owner_pid=99, ownership_changed=True))
        self.assertEqual(user.GetWindowTextW.call_count, 1)

    def test_forced_early_missing_event_or_nonzero_exit_never_qualify(self):
        for change in [dict(closure=dict(mode='TerminateProcess_only_spawned_handle', forced=True, exit_code=0)),
                       dict(closure=dict(mode='already_exited', forced=False, exit_code=0)),
                       dict(closure=dict(mode='WM_CLOSE', forced=False, exit_code=-1073741819)),
                       dict(normal_exit_event_observed=False), dict(build_identity_unchanged=False),
                       dict(startup_state_observed=False), dict(power_request=dict(restored=False))]:
            with self.subTest(change=change):
                evidence = record()
                evidence.update(change)
                self.assertFalse(native.finish_record(evidence))

    def test_unknown_or_low_integrity_never_qualifies(self):
        for key in ['integrity_before_launch', 'child_integrity']:
            for value in [dict(normal_integrity=False), {}]:
                evidence = record()
                evidence[key] = value
                self.assertFalse(native.finish_record(evidence))
        self.assertTrue(native.finish_record(record()))

    def test_actual_crash_artifact_rejects_clean_log_and_zero_exit(self):
        evidence = record()
        evidence['crash_reports'] = [dict(path='crash_123/crash.dmp', bytes=42, sha256='a' * 64)]
        self.assertFalse(native.finish_record(evidence))
        self.assertIn('Crash-report artifacts', evidence['errors'][-1])

    def test_same_session_must_publish_advancing_matching_samples(self):
        observation = native.StartupObservation(3)
        self.assertFalse(observation.accept(state(1), 0))
        self.assertFalse(observation.accept(state(2), 1))
        self.assertFalse(observation.accept(state(2), 2))
        self.assertTrue(observation.accept(state(3, 3), 3))

    def test_poll_span_120_cannot_qualify_published_span_119_point_9(self):
        observation = native.StartupObservation(120)
        for second in range(120):
            self.assertFalse(observation.accept(state(second + 1, second), second))
        self.assertFalse(observation.accept(state(121, 119.9), 120),
                         'Runner polling for120s cannot replace120s of published observations.')
        self.assertTrue(observation.accept(state(122, 120.15), 120.25))

    def test_published_span_cannot_replace_the_required_monotonic_span(self):
        observation = native.StartupObservation(120)
        for second in range(120):
            self.assertFalse(observation.accept(state(second + 1, second * 1.01), second))
        self.assertFalse(observation.accept(state(121, 121.2), 119.9))
        self.assertTrue(observation.accept(state(122, 121.45), 120.15))

    def test_seventh_fractional_digit_is_retained_at_duration_boundary(self):
        observation = native.StartupObservation(2)
        samples = [('2026-10-16T00:00:00.0000001Z', 0),
                   ('2026-10-16T00:00:01.0000001+00:00', 1),
                   ('2026-10-16T00:00:02.0000000Z', 2),
                   ('2026-10-16T00:00:02.0000001Z', 2.25)]
        outcomes = [observation.accept({**state(index + 1), 'utc': stamp}, now)
                    for index, (stamp, now) in enumerate(samples)]
        self.assertEqual(outcomes, [False, False, False, True])

    def test_aware_offset_timestamps_are_compared_as_utc_instants(self):
        observation = native.StartupObservation(2)
        self.assertFalse(observation.accept({**state(1), 'utc': '2026-10-16T02:00:00+02:00'}, 0))
        self.assertFalse(observation.accept({**state(2), 'utc': '2026-10-16T00:00:01Z'}, 1))
        self.assertTrue(observation.accept({**state(3), 'utc': '2026-10-15T23:00:02-01:00'}, 2))

    def test_120_second_window_short_by_one_100ns_tick_cannot_round_up(self):
        observation = native.StartupObservation(120)
        began = dt.datetime(2026, 10, 16)
        for second in range(120):
            stamp = (began + dt.timedelta(seconds=second)).strftime('%Y-%m-%dT%H:%M:%S') + '.0000001Z'
            self.assertFalse(observation.accept({**state(second + 1), 'utc': stamp}, second))
        self.assertFalse(observation.accept({**state(121), 'utc': '2026-10-16T00:02:00.0000000Z'}, 120))
        self.assertTrue(observation.accept({**state(122), 'utc': '2026-10-16T00:02:00.0000001Z'}, 120.25))

    def test_missing_invalid_or_naive_published_timestamps_fail_closed(self):
        for stamp in [None, '', 1, [], 'not-a-date', '2026-10-16T00:00:00',
                      '2026-13-16T00:00:00Z', '2026-10-16T00:00:00+25:00',
                      '2026-10-16T00:00:00+00:99']:
            with self.subTest(stamp=stamp):
                observation = native.StartupObservation(2)
                with self.assertRaisesRegex(ValueError, 'published UTC timestamp'):
                    observation.accept({**state(), 'utc': stamp}, 0)

    def test_published_time_must_advance_with_sequence_and_not_change_without_it(self):
        for sequence, published in [(2, 0), (2, -.1), (1, .1), (1, -.1)]:
            with self.subTest(sequence=sequence, published=published):
                observation = native.StartupObservation(2)
                observation.accept(state(1, 0), 0)
                with self.assertRaisesRegex(ValueError, 'published UTC timestamp'):
                    observation.accept(state(sequence, published), .25)

    def test_lifecycle_requires_both_duration_spans_even_when_startup_bool_is_true(self):
        for change in [dict(last_published_utc='2026-10-16T00:01:59.9000000Z',
                            published_span_100ns=1199000000, published_span_seconds=119.9),
                       dict(monotonic_span_seconds=119.9),
                       dict(published_span_seconds=121), dict(matching_fresh_snapshot_count=2),
                       dict(first_published_utc=None), dict(monotonic_span_seconds=float('nan'))]:
            with self.subTest(change=change):
                evidence = record()
                evidence['startup_observation'].update(change)
                self.assertFalse(native.finish_record(evidence))
                self.assertTrue(evidence['startup_state_observed'])
                self.assertIn('Startup duration', '\n'.join(evidence['errors']))
        evidence = record()
        evidence.pop('startup_observation')
        self.assertFalse(native.finish_record(evidence))

    def test_stalled_snapshot_cannot_satisfy_the_observation_window(self):
        observation = native.StartupObservation(3)
        observation.accept(state(1), 0)
        observation.accept(state(2), .25)
        observation.accept(state(3), .5)
        with self.assertRaisesRegex(ValueError, 'stopped publishing'):
            observation.accept(state(3), 3)

    def test_read_gap_sequence_rollback_state_change_and_session_change_fail(self):
        observation = native.StartupObservation(4)
        observation.accept(state(2), 0)
        with self.assertRaisesRegex(ValueError, 'stopped publishing'):
            observation.check_freshness(2.01)
        for changed, reason in [(state(1), 'backwards'),
                                ({**state(3), 'room': 'FirstEncounter'}, 'startup state changed'),
                                ({**state(3), 'sessionId': 'f8dbdeae-c8a2-48c6-bba3-615b791d490a'}, 'Session changed')]:
            with self.subTest(reason=reason):
                with self.assertRaisesRegex(ValueError, reason):
                    observation.accept(changed, 1)

    def test_exit_event_must_belong_to_observed_session_and_follow_started(self):
        sample = state()
        events = [dict(kind=kind, sequence=sequence, sessionId=sample['sessionId'],
                       room='Courtyard', phase='Running', objectiveFlags=0)
                  for kind, sequence in [('started', 1), ('normal-exit', 20)]]
        native.check_events(events, sample['sessionId'])
        for change in ['foreign_session', 'reversed', 'missing_exit', 'progressed']:
            changed = copy.deepcopy(events)
            if change == 'foreign_session':
                changed[-1]['sessionId'] = 'f8dbdeae-c8a2-48c6-bba3-615b791d490a'
            elif change == 'reversed':
                changed.reverse()
            elif change == 'missing_exit':
                changed.pop()
            else:
                changed[-1]['objectiveFlags'] = 1
            with self.subTest(change=change):
                with self.assertRaises(ValueError):
                    native.check_events(changed, sample['sessionId'])

    def test_power_request_restores_previous_thread_flags_even_on_exception(self):
        kernel = mock.Mock()
        kernel.SetThreadExecutionState.side_effect = [0x80000001, 0x80000003]
        evidence = {'errors': []}
        with self.assertRaisesRegex(RuntimeError, 'launch failed'):
            with native.keep_awake(kernel, evidence):
                raise RuntimeError('launch failed')
        self.assertTrue(evidence['power_request']['restored'])
        self.assertEqual(kernel.SetThreadExecutionState.call_args_list,
                         [mock.call(0x80000003), mock.call(0x80000001)])

    def test_failed_power_request_release_is_recorded_and_rejects_qualification(self):
        kernel = mock.Mock()
        kernel.SetThreadExecutionState.side_effect = [0x80000000, 0]
        evidence = record()
        with native.keep_awake(kernel, evidence):
            pass
        self.assertFalse(evidence['power_request']['restored'])
        self.assertFalse(native.finish_record(evidence))

    def test_only_current_child_windows_receive_close_messages(self):
        process, user = mock.Mock(), mock.Mock()
        process.pid = 42
        process.poll.return_value = None
        process.wait.side_effect = lambda **kwargs: setattr(process.poll, 'return_value', 0)
        user.PostMessageW.return_value = True
        evidence = {}
        with mock.patch.object(native, 'own_windows', return_value=[100, 200]), \
             mock.patch.object(native, 'window_pid', side_effect=[42, 99]):
            native.close_child(process, user, None, evidence)
        user.PostMessageW.assert_called_once_with(100, 0x0010, 0, 0)
        process.terminate.assert_not_called()
        self.assertEqual(evidence['closure']['mode'], 'WM_CLOSE')
        self.assertEqual(evidence['closure']['exit_code'], 0)

    def test_close_timeout_terminates_only_spawned_handle_and_stays_forced(self):
        process = mock.Mock()
        process.pid = 42
        process.poll.return_value = None
        process.wait.side_effect = [subprocess.TimeoutExpired('owned-child', 5), None]
        evidence = {}
        with mock.patch.object(native, 'own_windows', return_value=[]):
            native.close_child(process, mock.Mock(), None, evidence)
        process.terminate.assert_called_once_with()
        self.assertTrue(evidence['closure']['forced'])
        self.assertEqual(evidence['closure']['mode'], 'TerminateProcess_only_spawned_handle')

    def test_every_threading_diagnostic_keeps_hidden_batch_d3d11_and_audio_enabled(self):
        for mode, expected in [('default', []), ('multithreaded', ['-force-gfx-mt']),
                               ('legacy', ['-force-gfx-jobs', 'legacy'])]:
            with self.subTest(mode=mode):
                command = native.player_command(Path('ShadowsOfTheForsaken.exe'), Path('evidence'), mode)
                self.assertIn('-batchmode', command)
                self.assertEqual(command[command.index('-parentHWND') + 1], 'delayed')
                self.assertIn('-force-d3d11', command)
                self.assertNotIn('-nographics', command)
                self.assertNotIn('-disableaudio', command)
                self.assertEqual(command[command.index('-crash-report-folder') + 1], str(Path('evidence/crashes')))
                for arg in expected:
                    self.assertIn(arg, command)

    def test_owned_hwnd_is_explicit_and_delayed_remains_default(self):
        default = native.player_command(Path('game.exe'), Path('evidence'))
        owned = native.player_command(Path('game.exe'), Path('evidence'), 'legacy', 900)
        self.assertEqual(default[default.index('-parentHWND') + 1], 'delayed')
        self.assertEqual(owned[owned.index('-parentHWND') + 1], '900')
        self.assertIn('-batchmode', owned)
        self.assertEqual(owned[owned.index('-force-gfx-jobs') + 1], 'legacy')
        for handle in [0, -1, '900', True]:
            with self.subTest(handle=handle), self.assertRaises(ValueError):
                native.player_command(Path('game.exe'), Path('evidence'), parent_handle=handle)

    def test_embedded_is_explicit_requires_owned_host_and_preserves_audio_d3d11_arguments(self):
        command = native.player_command(Path('game.exe'), Path('evidence'), 'legacy', 900, 'embedded')
        self.assertNotIn('-batchmode', command)
        self.assertEqual(command[command.index('-parentHWND') + 1], '900')
        self.assertEqual(command[command.index('-force-gfx-jobs') + 1], 'legacy')
        self.assertIn('-force-d3d11', command)
        self.assertNotIn('-disableaudio', command)
        self.assertNotIn('-nographics', command)
        with self.assertRaisesRegex(ValueError, 'requires an owned parent'):
            native.player_command(Path('game.exe'), Path('evidence'), player_mode='embedded')
        with self.assertRaisesRegex(ValueError, 'Unknown player'):
            native.player_command(Path('game.exe'), Path('evidence'), player_mode='interactive')

    def test_owned_host_requests_no_visible_style_and_no_show_or_activation_call(self):
        parent, kernel, user, evidence = self.host()
        args = user.CreateWindowExW.call_args.args
        self.assertEqual(args[0], 0x08000080)  # NOACTIVATE | TOOLWINDOW
        self.assertEqual(args[1], 'STATIC')
        self.assertFalse(args[3] & 0x10000000)  # WS_VISIBLE
        self.assertEqual(args[8], None)
        self.assertEqual(parent.handle, 900)
        self.assertFalse(evidence['hidden_parent']['show_or_activation_requested'])
        user.ShowWindow.assert_not_called()
        user.SetForegroundWindow.assert_not_called()
        user.SetFocus.assert_not_called()

    def test_owned_host_refuses_destruction_until_child_exit_then_checks_ownership(self):
        parent, kernel, user, evidence = self.host()
        process = mock.Mock()
        process.poll.return_value = None
        with self.assertRaisesRegex(ValueError, 'before player exit'):
            parent.close(process)
        user.DestroyWindow.assert_not_called()
        process.poll.return_value = 0
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid + 1), \
             self.assertRaisesRegex(ValueError, 'foreign parent'):
            parent.close(process)
        user.DestroyWindow.assert_not_called()
        user.IsWindow.side_effect = [True, False]
        user.DestroyWindow.return_value = True
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid):
            parent.close(process)
            parent.close(process)
        user.DestroyWindow.assert_called_once_with(900)
        self.assertTrue(evidence['hidden_parent']['destroyed'])

    def test_failed_launch_still_cleans_owned_host_and_failed_destroy_rejects_qualification(self):
        parent, kernel, user, evidence = self.host()
        evidence['parent_mode_requested'] = 'owned'
        user.DestroyWindow.return_value = False
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid):
            native.cleanup_owned_resources(None, user, None, evidence, parent)
        user.DestroyWindow.assert_called_once_with(900)
        self.assertFalse(native.finish_record(evidence))
        self.assertTrue(any('Hidden parent cleanup' in error for error in evidence['errors']))

    def test_owned_host_message_pump_dispatches_on_creator_thread_and_rejects_foreign_messages(self):
        parent, kernel, user, evidence = self.host()
        def message(pointer, hwnd, low, high, flags):
            pointer._obj.hwnd, pointer._obj.message = 900, 0x000f
            return True
        user.PeekMessageW.side_effect = [True, False]
        # Fill the actual pointer rather than mirroring the pump implementation.
        calls = [True, False]
        def read_message(*args):
            available = calls.pop(0)
            if available:
                message(*args)
            return available
        user.PeekMessageW.side_effect = read_message
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid):
            parent.pump()
        self.assertEqual(user.DispatchMessageW.call_count, 1)
        self.assertEqual(evidence['hidden_parent']['dispatched_messages'], 1)
        self.assertEqual(user.PeekMessageW.call_args.args[1], 900)
        kernel.GetCurrentThreadId.return_value = 8
        with self.assertRaisesRegex(ValueError, 'changed thread'):
            parent.pump()
        kernel.GetCurrentThreadId.return_value = 7
        user.PeekMessageW.side_effect = message
        with mock.patch.object(native, 'window_pid', return_value=parent.owner_pid + 1), \
             self.assertRaisesRegex(ValueError, 'outside the owned host'):
            parent.pump()
        self.assertEqual(user.DispatchMessageW.call_count, 1)

    def test_nested_player_windows_are_enumerated_without_foreign_or_duplicate_handles(self):
        user = mock.Mock()
        user.EnumWindows.side_effect = lambda callback, data: [callback(window, data) for window in [100, 200, 900]] and True
        user.EnumChildWindows.side_effect = lambda host, callback, data: [callback(window, data) for window in [300, 100, 400]] and 0
        owners = {100: 42, 200: 99, 300: 42, 400: 99, 900: 77}
        with mock.patch.object(native, 'window_pid', side_effect=lambda api, window: owners[window]):
            found = native.own_windows(user, lambda callback: callback, 42, 900)
        self.assertEqual(found, [100, 300])
        self.assertEqual(user.EnumChildWindows.call_args.args[0], 900)

    def test_foreground_or_visible_owned_host_fails_even_when_player_windows_are_hidden(self):
        parent, kernel, user, evidence = self.host()
        for visible, foreground in [(True, 0), (False, 900)]:
            with self.subTest(visible=visible, foreground=foreground):
                case = record()
                user.GetForegroundWindow.return_value = foreground
                user.IsWindowVisible.return_value = visible
                with mock.patch.object(native, 'own_windows', return_value=[]), \
                     mock.patch.object(native, 'window_pid', return_value=parent.owner_pid), \
                     mock.patch.object(native, 'describe_windows', return_value=[]):
                    self.assertFalse(native.check_hidden_windows(user, None, 42, case, parent))
                self.assertTrue(case['foreground_or_visible_window_violation'])
                self.assertTrue(case['errors'])

    def test_child_closes_before_owned_host_is_destroyed(self):
        process, user, parent = mock.Mock(), mock.Mock(), mock.Mock()
        process.pid, process.poll.return_value, parent.handle = 42, None, 900
        chronology = []
        def post(*args):
            chronology.append('player-close')
            process.poll.return_value = 0
            return True
        def destroy(owned_process):
            self.assertIs(owned_process, process)
            self.assertEqual(process.poll(), 0)
            chronology.append('host-destroy')
        user.PostMessageW.side_effect = post
        parent.close.side_effect = destroy
        evidence = record()
        with mock.patch.object(native, 'own_windows', return_value=[100]), \
             mock.patch.object(native, 'window_pid', return_value=42):
            native.cleanup_owned_resources(process, user, None, evidence, parent)
        self.assertEqual(chronology, ['player-close', 'host-destroy'])
        process.terminate.assert_not_called()

    def test_owned_parent_pumps_and_checks_hidden_state_during_graceful_wait(self):
        process, user, parent = mock.Mock(), mock.Mock(), mock.Mock()
        process.pid, process.poll.return_value, parent.handle = 42, None, 900
        parent.pump.side_effect = lambda: setattr(process.poll, 'return_value', 0)
        evidence = record()
        with mock.patch.object(native, 'own_windows', return_value=[100]), \
             mock.patch.object(native, 'window_pid', return_value=42), \
             mock.patch.object(native, 'check_hidden_windows', return_value=True) as guard, \
             mock.patch.object(native.time, 'sleep'):
            native.close_child(process, user, None, evidence, parent=parent)
        parent.pump.assert_called_once_with()
        guard.assert_called_once_with(user, None, 42, evidence, parent)
        self.assertEqual(evidence['closure']['mode'], 'WM_CLOSE')


if __name__ == '__main__':
    unittest.main()
