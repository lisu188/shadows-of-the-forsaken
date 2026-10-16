#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ForsakenStandaloneRenderedRouteSupport
{
    // Opt-in test-owned read-only evidence. This namespace deliberately is not
    // gameplay's namespace: fixture scene isolation must not suspend the probe.
    public sealed class RenderedRouteProbe : IDisposable
    {
        private const int VisitLimit = 256, SampleLimit = 1024, DetailLimit = 256, WorstLimit = 64, ErrorLimit = 16;
        private readonly Scene scene;
        private readonly Camera camera;
        private readonly LevelProgressionController progression;
        private readonly Func<bool> isRunning;
        private readonly string output, caseName, key, startedUtc;
        private readonly string scenePath;
        private readonly ulong sceneHandle, cameraId;
        private readonly double started;
        private readonly List<RenderedRoomEvidence> visits = new List<RenderedRoomEvidence>();
        private readonly List<RenderedStateSample> samples = new List<RenderedStateSample>();
        private readonly List<RenderedCallbackSample> details = new List<RenderedCallbackSample>();
        private readonly List<RenderedWorstInterval> worst = new List<RenderedWorstInterval>();
        private readonly List<string> errors = new List<string>();
        private readonly List<MetricLease> recorders = new List<MetricLease>();
        private readonly RenderedRouteCompletion completion = new RenderedRouteCompletion();
        private readonly RenderedCallbackCounts all = new RenderedCallbackCounts(), ordinary = new RenderedCallbackCounts();
        private readonly RenderedCallbackCounts captures = new RenderedCallbackCounts(), outside = new RenderedCallbackCounts();
        private GameObject helper;
        private RenderedRouteTick tick;
        private RenderedRouteVisitMetrics metrics;
        private RenderedRoomEvidence visit;
        private RenderedConfiguration initialConfiguration, lastConfiguration;
        private string lastConfigurationJson;
        private long nextVisit, ordinaryCycles, rejectedCycles, captureBegins, captureEnds, detailsDropped;
        private long postCompletionContexts;
        private string measurementEndedUtc;
        private double measurementEndedRealtime;
        private int completionFrame = -1;
        private double nextSample, worstThreshold;
        private int captureDepth, captureGeneration, beginGeneration, beginFrame, ownedBegins, ownedEnds;
        private Guid beginSession;
        private Guid visitSession;
        private Guid lastSession;
        private int lastObjectives;
        private int lastOrdinaryFrame = -1;
        private LevelRoom beginRoom;
        private bool contextOpen, beginEligible, disposed, paused, subscribed;
        private bool completed => completion.Completed;

        public static RenderedRouteProbe Begin(Scene ownedScene, Camera ownedCamera, LevelProgressionController ownedProgression,
            Func<bool> running, bool pipelineAtPreparation, bool pipelineAfterPreparation)
        {
            if (!RenderedRouteBinding.Active) return null;
            return new RenderedRouteProbe(ownedScene, ownedCamera, ownedProgression, running, pipelineAtPreparation, pipelineAfterPreparation);
        }

        private RenderedRouteProbe(Scene ownedScene, Camera ownedCamera, LevelProgressionController ownedProgression,
            Func<bool> running, bool pipelineAtPreparation, bool pipelineAfterPreparation)
        {
            scene = ownedScene; camera = ownedCamera; progression = ownedProgression; isRunning = running ?? throw new ArgumentNullException(nameof(running));
            output = RenderedRouteBinding.Output(); caseName = TestContext.CurrentContext.Test.FullName; key = RenderedRouteBinding.CaseKey(caseName);
            if (!scene.IsValid() || !scene.isLoaded || scene.path != "Assets/Scenes/ForsakenCastle.unity" ||
                camera == null || camera.gameObject.scene != scene || !camera.isActiveAndEnabled || camera.targetTexture != null ||
                progression == null || progression.gameObject.scene != scene ||
                scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Count() != 1)
                throw new InvalidOperationException("Rendered route requires the exact loaded owned saved scene and single unbound camera.");
            started = Time.realtimeSinceStartupAsDouble; startedUtc = RenderedRouteBinding.Utc();
            scenePath = scene.path; sceneHandle = scene.handle.GetRawData(); cameraId = EntityId.ToULong(camera.GetEntityId());
            lastSession = progression.Snapshot.SessionId; lastObjectives = (int)progression.Snapshot.CompletedObjectives;
            completion.Observe(lastSession, lastObjectives, false);
            initialConfiguration = lastConfiguration = RenderedConfiguration.Read(camera);
            lastConfigurationJson = JsonUtility.ToJson(lastConfiguration);
            try
            {
                helper = new GameObject("Test-owned rendered route observation"); SceneManager.MoveGameObjectToScene(helper, scene);
                tick = helper.AddComponent<RenderedRouteTick>(); tick.AfterLate = Tick; tick.Pause = value => { paused = value; Invalidate("pause-boundary"); };
                // Mark the lease before subscription so every partial setup path
                // attempts all removals and disposes every acquired recorder.
                subscribed = true;
                RenderPipelineManager.beginContextRendering += BeginContext;
                RenderPipelineManager.endContextRendering += EndContext;
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
                progression.Changed += ProgressionChanged;
                OpenRecorders(); SynchronizeVisit(); SampleState();
                var extra = camera.GetComponent<UniversalAdditionalCameraData>();
                if (extra == null || extra.renderPostProcessing) throw new InvalidOperationException("Readiness requires the guarded inactive post-processing camera.");
                RenderedRouteBinding.SaveNew(output, "case-" + key + "-ready.json", new RenderedReady
                {
                    pid = RenderedRouteBinding.Pid(), case_full_name = caseName, case_key = key, output_directory = output,
                    recorded_utc = RenderedRouteBinding.Utc(), scene_path = scene.path, scene_name = scene.name, scene_handle = sceneHandle,
                    camera_name = camera.name, camera_entity_id = cameraId, camera_scene_path = camera.gameObject.scene.path,
                    session_id = progression.Snapshot.SessionId.ToString(), owned_scene_prepared = true, owned_camera_count = 1,
                    camera_post_processing = extra.renderPostProcessing, pipeline_at_preparation = pipelineAtPreparation,
                    pipeline_after_preparation = pipelineAfterPreparation, pipeline_at_publication = RenderPipelineManager.currentPipeline != null,
                    realtime_seconds = Time.realtimeSinceStartupAsDouble, frame = Time.frameCount
                });
            }
            catch { Release(); throw; }
        }

        private void ProgressionChanged(ProgressionChange change)
        {
            Guard(() =>
            {
                if (completed) return;
                var current = progression.Snapshot;
                if (change.After.SessionId != current.SessionId) { Invalidate("stale-progression-callback"); return; }
                SynchronizeVisit();
            });
        }
        private void Tick()
        {
            if (disposed) return;
            try
            {
                SynchronizeVisit();
                if (completed) return;
                if (paused || Time.timeScale <= 0 || !isRunning()) Invalidate(paused ? "paused" : "not-running");
                if (Time.realtimeSinceStartupAsDouble >= nextSample) SampleState();
            }
            catch (Exception error) { RecordError(error); }
        }

        private void SynchronizeVisit()
        {
            if (completed) return; // Terminal R stays unchanged; its new session cannot relabel this route.
            var state = progression.Snapshot;
            completion.Observe(state.SessionId, (int)state.CompletedObjectives, state.IsCompleted);
            lastSession = completion.Session; lastObjectives = completion.Objectives;
            bool running = isRunning() && !state.IsCompleted;
            if (visit != null && (!running || visitSession != state.SessionId || visit.room_id != (int)state.Room))
                CloseVisit(state.IsCompleted ? "completion" : !running ? "not-running" : "room-or-session-boundary");
            if (visit == null && running)
            {
                if (visits.Count >= VisitLimit) throw new InvalidOperationException("Rendered visit limit exceeded.");
                string session = state.SessionId.ToString(), room = state.Room.ToString();
                visitSession = state.SessionId;
                double now = Time.realtimeSinceStartupAsDouble;
                visit = new RenderedRoomEvidence { session_id = session, room = room, room_id = (int)state.Room,
                    started_utc = RenderedRouteBinding.Utc(), configuration_at_start = RenderedConfiguration.Read(camera),
                    visit = checked(++nextVisit) };
                metrics = new RenderedRouteVisitMetrics(session, room, visit.visit, now);
            }
            if (completed)
            {
                completionFrame = Time.frameCount;
                SampleState(); // One final original-session snapshot before any terminal R/reset.
                measurementEndedUtc = RenderedRouteBinding.Utc(); measurementEndedRealtime = Time.realtimeSinceStartupAsDouble;
            }
        }
        private void CloseVisit(string reason)
        {
            if (visit == null) return;
            visit.ended_utc = RenderedRouteBinding.Utc(); visit.reason = reason;
            visit.configuration_at_end = camera != null ? RenderedConfiguration.Read(camera) : lastConfiguration;
            visit.configuration_changed |= JsonUtility.ToJson(visit.configuration_at_start) != JsonUtility.ToJson(visit.configuration_at_end);
            visit.intervals = metrics.Close(Time.realtimeSinceStartupAsDouble);
            visits.Add(visit); visit = null; metrics = null;
        }
        private void Invalidate(string reason) { metrics?.Invalidate(reason); }

        public IDisposable Capture(string name)
        {
            if (disposed || captureDepth != 0 || string.IsNullOrEmpty(name)) throw new InvalidOperationException("Invalid rendered capture bracket.");
            captureDepth = 1; captureGeneration = checked(captureGeneration + 1); captureBegins = checked(captureBegins + 1);
            Invalidate("capture-crossing");
            return new CaptureLease(this);
        }
        private void EndCapture()
        {
            if (captureDepth != 1) throw new InvalidOperationException("Unbalanced rendered capture bracket.");
            captureEnds = checked(captureEnds + 1); captureDepth = 0; captureGeneration = checked(captureGeneration + 1);
            Invalidate("capture-crossing");
        }
        private sealed class CaptureLease : IDisposable
        {
            private RenderedRouteProbe owner;
            public CaptureLease(RenderedRouteProbe value) { owner = value; }
            public void Dispose() { var lease = owner; owner = null; lease?.EndCapture(); }
        }

        private RenderedCallbackCounts Category()
        {
            if (captureDepth != 0) return captures;
            if (completed) return outside;
            return camera != null && camera.isActiveAndEnabled && camera.targetTexture == null ? ordinary : outside;
        }
        private void Record(string kind, bool owned, bool member, int cameraCount)
        {
            var category = Category(); all.Add(kind, owned); category.Add(kind, owned);
            if (details.Count < DetailLimit) details.Add(new RenderedCallbackSample
            {
                kind = kind, frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                classification = category == captures ? "explicit-capture" : category == ordinary ? "ordinary-unbound-state" : "outside-owned-unbound",
                capture_depth = captureDepth, owned_camera_callback = owned, owned_camera_in_context = member,
                context_camera_count = cameraCount, target_is_null = camera != null && camera.targetTexture == null
            });
            else detailsDropped = checked(detailsDropped + 1);
        }
        private void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (disposed) return;
            try
            {
                bool member = cameras != null && cameras.Contains(camera);
                Record("context-begin", false, member, cameras?.Count ?? -1);
                if (captureDepth != 0) return;
                if (contextOpen)
                {
                    rejectedCycles = checked(rejectedCycles + 1);
                    RecordError(new InvalidOperationException("Overlapping ordinary render contexts."));
                }
                contextOpen = true; ownedBegins = ownedEnds = 0; beginFrame = Time.frameCount; beginGeneration = captureGeneration;
                if (completed) { beginEligible = false; return; }
                var state = progression.Snapshot; beginSession = state.SessionId; beginRoom = state.Room;
                beginEligible = member && cameras.Count == 1 && camera != null && camera.isActiveAndEnabled && camera.targetTexture == null &&
                    camera.cameraType == CameraType.Game && isRunning() && !state.IsCompleted && !paused && Time.timeScale > 0;
            }
            catch (Exception error) { RecordError(error); }
        }
        private void BeginCamera(ScriptableRenderContext context, Camera item)
        {
            if (disposed) return;
            try
            {
                bool owned = item == camera; Record("camera-begin", owned, false, -1);
                if (captureDepth == 0 && contextOpen && owned) ownedBegins++;
            }
            catch (Exception error) { RecordError(error); }
        }
        private void EndCamera(ScriptableRenderContext context, Camera item)
        {
            if (disposed) return;
            try
            {
                bool owned = item == camera; Record("camera-end", owned, false, -1);
                if (captureDepth == 0 && contextOpen && owned) ownedEnds++;
            }
            catch (Exception error) { RecordError(error); }
        }
        private void EndContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (disposed) return;
            try
            {
                bool member = cameras != null && cameras.Contains(camera);
                Record("context-end", false, member, cameras?.Count ?? -1);
                if (captureDepth != 0) return;
                if (completed)
                {
                    if (!contextOpen) throw new InvalidOperationException("Post-completion context ended without its begin.");
                    postCompletionContexts = checked(postCompletionContexts + 1); contextOpen = false; return;
                }
                SynchronizeVisit(); var state = progression.Snapshot;
                if (completed) { postCompletionContexts = checked(postCompletionContexts + 1); contextOpen = false; return; }
                bool accepted = RenderedCycleEligibility.Accept(new RenderedCycleObservation
                {
                    context_open = contextOpen, begin_eligible = beginEligible, begin_frame = beginFrame, end_frame = Time.frameCount,
                    begin_generation = beginGeneration, end_generation = captureGeneration, end_owned_member = member,
                    camera_count = cameras?.Count ?? -1, owned_begins = ownedBegins, owned_ends = ownedEnds,
                    identity_matches = state.SessionId == beginSession && state.Room == beginRoom, visit_open = visit != null,
                    camera_active_unbound = camera != null && camera.isActiveAndEnabled && camera.targetTexture == null,
                    running = isRunning(), unpaused_positive_time = !paused && Time.timeScale > 0
                });
                contextOpen = false;
                if (!accepted) { rejectedCycles = checked(rejectedCycles + 1); Invalidate("rejected-render-cycle"); return; }
                if (Time.frameCount <= lastOrdinaryFrame)
                    throw new InvalidOperationException("More than one ordinary owned render cycle occupied a frame.");
                lastOrdinaryFrame = Time.frameCount;
                ordinaryCycles = checked(ordinaryCycles + 1);
                double now = Time.realtimeSinceStartupAsDouble;
                if (metrics.Observe(now, Time.frameCount, visit.session_id, visit.room)) RecordWorst(now);
            }
            catch (Exception error) { RecordError(error); }
        }

        private void RecordWorst(double now)
        {
            double ms = metrics.LastEligibleSeconds * 1000;
            if (worst.Count >= WorstLimit && ms <= worstThreshold) return;
            var row = new RenderedWorstInterval { frame = Time.frameCount, realtime = now, milliseconds = ms,
                session_id = visit.session_id, room = visit.room, visit = visit.visit, warm = metrics.LastWasWarm };
            if (worst.Count < WorstLimit) worst.Add(row);
            else { int index = 0; for (int i = 1; i < worst.Count; i++) if (worst[i].milliseconds < worst[index].milliseconds) index = i; worst[index] = row; }
            if (worst.Count == WorstLimit) { worstThreshold = worst[0].milliseconds; foreach (var item in worst) worstThreshold = Math.Min(worstThreshold, item.milliseconds); }
        }

        private void SampleState()
        {
            if (samples.Count >= SampleLimit) throw new InvalidOperationException("Rendered configuration/memory sample limit exceeded.");
            var configuration = RenderedConfiguration.Read(camera);
            string json = JsonUtility.ToJson(configuration);
            if (json != lastConfigurationJson)
            {
                Invalidate("configuration-boundary"); if (visit != null) visit.configuration_changed = true;
                lastConfigurationJson = json;
            }
            lastConfiguration = configuration;
            var memory = new RenderedMemory
            {
                managed_estimated_bytes = GC.GetTotalMemory(false), unity_used_bytes = Profiler.GetTotalAllocatedMemoryLong(),
                unity_reserved_bytes = Profiler.GetTotalReservedMemoryLong(), unity_unused_reserved_bytes = Profiler.GetTotalUnusedReservedMemoryLong()
            };
            memory.unity_allocator_available = memory.unity_reserved_bytes > 0;
            if (visit != null)
            {
                visit.memory_samples++;
                visit.peak_sampled_managed_bytes = Math.Max(visit.peak_sampled_managed_bytes, memory.managed_estimated_bytes);
                visit.peak_sampled_unity_used_bytes = Math.Max(visit.peak_sampled_unity_used_bytes, memory.unity_used_bytes);
            }
            var state = progression.Snapshot;
            samples.Add(new RenderedStateSample { utc = RenderedRouteBinding.Utc(), frame = Time.frameCount,
                realtime = Time.realtimeSinceStartupAsDouble, session_id = state.SessionId.ToString(), room = state.Room.ToString(),
                running = isRunning(), application_focused = Application.isFocused, paused = paused, time_scale = Time.timeScale,
                capture_depth = captureDepth, configuration = configuration, memory = memory,
                profiler = recorders.Select(item => item.Read()).ToArray() });
            nextSample = Time.realtimeSinceStartupAsDouble + 1;
        }

        private void OpenRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            if (handles.Count > 4096) throw new InvalidOperationException("Profiler metric enumeration limit exceeded.");
            foreach (string name in new[] { "Main Thread", "Render Thread", "Draw Calls Count", "SetPass Calls Count", "Shadow Casters Count", "Triangles Count" })
            {
                var lease = new MetricLease { name = name, unit = "unavailable", category = "unavailable", unavailableReason = "not-enumerated" };
                recorders.Add(lease);
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    if (description.Name != name) continue;
                    lease.category = description.Category.ToString(); lease.unit = description.UnitType.ToString();
                    try
                    {
                        lease.recorder = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                        lease.available = lease.recorder.Valid;
                        lease.unavailableReason = lease.available ? null : "recorder-invalid";
                    }
                    catch (Exception error) { lease.unavailableReason = "recorder-start-" + error.GetType().Name; }
                    break;
                }
            }
        }
        private sealed class MetricLease : IDisposable
        {
            public string name, unit, category, unavailableReason; public bool available; public ProfilerRecorder recorder;
            public RenderedProfilerSample Read() => new RenderedProfilerSample
            { name = name, category = category, unit = unit, available = available, valid = available && recorder.Valid, unavailable_reason = unavailableReason,
                count = available && recorder.Valid ? recorder.Count : 0, value = available && recorder.Valid && recorder.Count > 0 ? recorder.LastValue : 0 };
            public void Dispose() { if (recorder.Valid) recorder.Dispose(); }
        }
        private void Guard(Action action)
        {
            if (disposed) return;
            try { action(); }
            catch (Exception error) { RecordError(error); }
        }
        private void RecordError(Exception error)
        {
            Invalidate("observer-error");
            string message = error.GetType().Name + ": " + error.Message;
            if (message.Length > 1024) message = message.Substring(0, 1024);
            if (errors.Count < ErrorLimit) errors.Add(message);
            else if (errors[ErrorLimit - 1] != "Error detail limit reached") errors[ErrorLimit - 1] = "Error detail limit reached";
        }

        private void Release()
        {
            if (disposed) return;
            disposed = true;
            var cleanup = new List<Action>();
            if (subscribed)
            {
                cleanup.Add(() => RenderPipelineManager.beginContextRendering -= BeginContext);
                cleanup.Add(() => RenderPipelineManager.endContextRendering -= EndContext);
                cleanup.Add(() => RenderPipelineManager.beginCameraRendering -= BeginCamera);
                cleanup.Add(() => RenderPipelineManager.endCameraRendering -= EndCamera);
                cleanup.Add(() => { if (progression != null) progression.Changed -= ProgressionChanged; });
                subscribed = false;
            }
            cleanup.Add(() => { if (tick != null) { tick.AfterLate = null; tick.Pause = null; } });
            foreach (var recorder in recorders) cleanup.Add(recorder.Dispose);
            cleanup.Add(() => { if (helper != null) Object.DestroyImmediate(helper); });
            RenderedRouteCleanup.Run(cleanup, RecordError);
            helper = null; tick = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            try
            {
                if (progression == null || camera == null || !scene.IsValid() || !scene.isLoaded)
                { RecordError(new InvalidOperationException("Owned scene/camera/progression was lost before closure.")); CloseVisit("ownership-lost"); }
                else if (!completed) { SynchronizeVisit(); if (!completed) SampleState(); CloseVisit("fixture-end"); }
            }
            catch (Exception error) { RecordError(error); }
            finally { Release(); }
            var evidence = new RenderedRouteEvidence
            {
                pid = RenderedRouteBinding.Pid(), case_full_name = caseName, case_key = key, output_directory = output,
                scene_path = scenePath, scene_handle = sceneHandle, camera_entity_id = cameraId,
                started_utc = startedUtc, ended_utc = RenderedRouteBinding.Utc(), start_realtime = started,
                end_realtime = Time.realtimeSinceStartupAsDouble, disposed = disposed, completed = completed,
                measurement_ended_utc = measurementEndedUtc, measurement_end_realtime = measurementEndedRealtime,
                completion_frame = completionFrame, post_completion_contexts = postCompletionContexts,
                errors = errors.ToArray(), all_callbacks = all, ordinary_unbound_callbacks = ordinary,
                explicit_capture_callbacks = captures, outside_callbacks = outside, ordinary_cycles = ordinaryCycles,
                rejected_cycles = rejectedCycles, capture_begins = captureBegins, capture_ends = captureEnds, active_capture_depth = captureDepth,
                callback_details_dropped = detailsDropped, callback_details = details.ToArray(), visits = visits.ToArray(),
                samples = samples.ToArray(), worst_intervals = worst.OrderByDescending(row => row.milliseconds).ToArray(),
                configuration_at_start = initialConfiguration, configuration_at_end = lastConfiguration,
                final_objective_flags = lastObjectives, final_session_id = lastSession.ToString()
            };
            RenderedRouteBinding.SaveNew(output, "case-" + key + ".json", evidence);
            bool balanced = all.Balanced && ordinary.Balanced && captures.Balanced && outside.Balanced &&
                captureBegins == captureEnds && captureDepth == 0 && !contextOpen;
            RenderedRouteBinding.RegisterClosed(key, errors.Count == 0 && disposed && completed && ordinaryCycles > 0 && balanced && visits.Count > 0, output);
        }
    }

    [DefaultExecutionOrder(32000)]
    public sealed class RenderedRouteTick : MonoBehaviour
    {
        public Action AfterLate; public Action<bool> Pause;
        private void LateUpdate() => AfterLate?.Invoke();
        private void OnApplicationPause(bool value) => Pause?.Invoke(value);
    }
    [Serializable] public sealed class RenderedCallbackCounts
    {
        public long context_begin, context_end, all_camera_begin, all_camera_end, owned_camera_begin, owned_camera_end;
        public bool Balanced => context_begin == context_end && all_camera_begin == all_camera_end && owned_camera_begin == owned_camera_end;
        public void Add(string kind, bool owned)
        {
            switch (kind)
            {
                case "context-begin": context_begin = checked(context_begin + 1); break;
                case "context-end": context_end = checked(context_end + 1); break;
                case "camera-begin": all_camera_begin = checked(all_camera_begin + 1); if (owned) owned_camera_begin = checked(owned_camera_begin + 1); break;
                case "camera-end": all_camera_end = checked(all_camera_end + 1); if (owned) owned_camera_end = checked(owned_camera_end + 1); break;
                default: throw new ArgumentException("Unknown SRP callback.");
            }
        }
    }
    [Serializable] public sealed class RenderedReady
    {
        public int schema_version = 1, pid, owned_camera_count, frame;
        public ulong scene_handle, camera_entity_id;
        public string test_profile = RenderedRouteBinding.Profile, recorded_utc, case_full_name, case_key, output_directory;
        public string scene_path, scene_name, camera_name, camera_scene_path, session_id;
        public bool owned_scene_prepared, camera_post_processing, pipeline_at_preparation, pipeline_after_preparation, pipeline_at_publication;
        public double realtime_seconds;
    }
    [Serializable] public sealed class RenderedRouteEvidence
    {
        public int schema_version = 1, pid, active_capture_depth, final_objective_flags;
        public int completion_frame;
        public ulong scene_handle, camera_entity_id;
        public string test_profile = RenderedRouteBinding.Profile, case_full_name, case_key, output_directory, scene_path;
        public string started_utc, ended_utc, final_session_id;
        public string measurement_ended_utc;
        public double start_realtime, end_realtime;
        public double measurement_end_realtime;
        public bool disposed, completed;
        public string[] errors;
        public long ordinary_cycles, rejected_cycles, capture_begins, capture_ends, callback_details_dropped;
        public long post_completion_contexts;
        public RenderedCallbackCounts all_callbacks, ordinary_unbound_callbacks, explicit_capture_callbacks, outside_callbacks;
        public RenderedCallbackSample[] callback_details;
        public RenderedRoomEvidence[] visits;
        public RenderedStateSample[] samples;
        public RenderedWorstInterval[] worst_intervals;
        public RenderedConfiguration configuration_at_start, configuration_at_end;
        public RenderedLimits limits = new RenderedLimits();
        public string scope = "Concealed Development UTF ordinary SRP boundary intervals, not GPU completion/display FPS or shipping/human acceptance. Raw includes eligible cold intervals; warm excludes first five eligible seconds per visit. Existing Capture/framing requests and crossing intervals are excluded but their camera snaps remain part of this unchanged fixture workload. Long eligible intervals are retained. Actual focus is metadata, never spoofed. Profiler values are completed-frame statistics observed at read time, not proved originating-room attribution.";
    }
    [Serializable] public sealed class RenderedLimits
    { public int visits = 256, state_samples = 1024, callback_details = 256, worst_intervals = 64, error_details = 16, profiler_metrics = 6, histogram_buckets = 2049; public double sample_interval_seconds = 1, warmup_eligible_seconds = 5; }
    [Serializable] public sealed class RenderedRoomEvidence
    {
        public string session_id, room, started_utc, ended_utc, reason; public int room_id; public long visit;
        public RenderedVisitSummary intervals; public RenderedConfiguration configuration_at_start, configuration_at_end;
        public bool configuration_changed; public long memory_samples, peak_sampled_managed_bytes, peak_sampled_unity_used_bytes;
    }
    [Serializable] public sealed class RenderedWorstInterval
    { public string session_id, room; public long visit; public int frame; public double realtime, milliseconds; public bool warm; }
    [Serializable] public sealed class RenderedCallbackSample
    { public string kind, classification; public int frame, capture_depth, context_camera_count; public double realtime; public bool owned_camera_callback, owned_camera_in_context, target_is_null; }
    [Serializable] public sealed class RenderedMemory
    { public long managed_estimated_bytes, unity_used_bytes, unity_reserved_bytes, unity_unused_reserved_bytes; public bool unity_allocator_available; public string meaning = "Sampled managed estimate and Unity allocator pools; not complete native/process/GPU memory. No forced GC; short peaks may be missed."; }
    [Serializable] public sealed class RenderedProfilerSample
    { public string name, category, unit, unavailable_reason; public bool available, valid; public int count; public long value; public string attribution = "Completed-frame statistic at read time; originating frame/room unassigned."; }
    [Serializable] public sealed class RenderedStateSample
    {
        public string utc, session_id, room; public int frame, capture_depth; public double realtime; public float time_scale;
        public bool running, application_focused, paused; public RenderedConfiguration configuration; public RenderedMemory memory; public RenderedProfilerSample[] profiler;
    }
    [Serializable] public sealed class RenderedConfiguration
    {
        public int screen_width, screen_height, quality_index, vsync_count, target_frame_rate, render_frame_interval;
        public int pixel_width, pixel_height, target_display, msaa_samples, display_render_width, display_render_height, display_system_width, display_system_height;
        public string fullscreen_mode, quality_name, camera_type, pipeline_type, pipeline_asset;
        public float aspect, render_scale; public Rect pixel_rect;
        public bool target_is_null, camera_active, camera_post_processing, hdr_supported, display_active, on_demand_will_render, run_in_background;
        public static RenderedConfiguration Read(Camera camera)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var extra = camera.GetComponent<UniversalAdditionalCameraData>(); int index = QualitySettings.GetQualityLevel();
            var display = Display.displays[camera.targetDisplay];
            return new RenderedConfiguration
            {
                screen_width = Screen.width, screen_height = Screen.height, fullscreen_mode = Screen.fullScreenMode.ToString(),
                quality_index = index, quality_name = QualitySettings.names[index], vsync_count = QualitySettings.vSyncCount,
                target_frame_rate = Application.targetFrameRate, render_frame_interval = OnDemandRendering.renderFrameInterval,
                on_demand_will_render = OnDemandRendering.willCurrentFrameRender, run_in_background = Application.runInBackground,
                pixel_width = camera.pixelWidth, pixel_height = camera.pixelHeight, pixel_rect = camera.pixelRect, aspect = camera.aspect,
                target_display = camera.targetDisplay, camera_type = camera.cameraType.ToString(), target_is_null = camera.targetTexture == null,
                camera_active = camera.isActiveAndEnabled, camera_post_processing = extra != null && extra.renderPostProcessing,
                pipeline_type = RenderPipelineManager.currentPipeline?.GetType().FullName ?? "<null>", pipeline_asset = pipeline != null ? pipeline.name : "<null>",
                render_scale = pipeline != null ? pipeline.renderScale : 0, msaa_samples = pipeline != null ? pipeline.msaaSampleCount : 0,
                hdr_supported = pipeline != null && pipeline.supportsHDR, display_active = display.active,
                display_render_width = display.renderingWidth, display_render_height = display.renderingHeight,
                display_system_width = display.systemWidth, display_system_height = display.systemHeight
            };
        }
    }
}
#endif
