#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using ForsakenStandaloneRenderedRouteSupport;
using ForsakenPassiveAudioLifecycleTestSupport;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestRunner;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Synthetic lifecycle notifications are never native quit/device evidence.
    public sealed class AudioRunLifecycleObserverTests
    {
        [UnityTest, Timeout(10000)]
        public IEnumerator RunLeaseRetainsPostCaseCallbackAndSealsBeforeQuit()
        {
            GameObject owner = null, exitOwner = null;
            try
            {
                var calls = new List<string>();
                var original = new FakeCallbacks("route", calls); var companion = new FakeCallbacks("audio", calls);
                var dispatcher = new StandaloneMainTestCallbacks(original, companion);
                dispatcher.RunStarted(null); dispatcher.TestStarted(null); dispatcher.TestFinished(null); dispatcher.RunFinished(null);
                Assert.That(calls, Is.EqualTo(new[] { "route-run-started", "audio-run-started", "route-test-started", "audio-test-started",
                    "route-test-finished", "audio-test-finished", "route-run-finished", "audio-run-finished" }));
                calls.Clear(); original.failStart = true;
                Assert.Throws<InvalidOperationException>(() => dispatcher.RunStarted(null));
                Assert.That(calls, Is.EqualTo(new[] { "route-run-started", "audio-run-started" }));
                if (Application.isEditor)
                {
                    // Actual default delegates are inactive in Editor, even
                    // with null tokens. No files/owners/quit are requested.
                    dispatcher = new StandaloneMainTestCallbacks();
                    dispatcher.RunStarted(null); dispatcher.TestStarted(null); dispatcher.TestFinished(null); dispatcher.RunFinished(null);
                }
                owner = new GameObject("Synthetic audio lifecycle owner");
                var observer = owner.AddComponent<AudioRunLifecycleObserver>();
                Assert.That(observer.GetType().Namespace.StartsWith("ShadowsOfTheForsaken", StringComparison.Ordinal), Is.False,
                    "The unchanged route fixture must not suspend the passive observer's Update.");
                var publisher = new FakeEvents(); var reader = new FakeReader(); var sink = new FakeSink();
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                observer.ObserveTest("run-started", "suite", "synthetic suite", true, "");
                observer.ObserveTest("test-started", "main", RenderedRouteBinding.MainCase, false, "");
                observer.ObserveTest("test-finished", "main", RenderedRouteBinding.MainCase, false, "Passed");
                Action<bool> captured = publisher.callback;
                var worker = new Thread(() => captured(true));
                worker.Start(); Assert.That(worker.Join(2000), Is.True);
                Assert.That(reader.reads, Is.EqualTo(1), "The actual worker callback cannot read configuration.");
                yield return null; // Actual owned Update drains it on main thread.
                observer.ObserveTest("test-started", "secret", RenderedRouteBinding.SecretCase, false, "");
                observer.ObserveTest("test-finished", "secret", RenderedRouteBinding.SecretCase, false, "Passed");
                observer.ObserveTest("run-finished", "suite", "synthetic suite", true, "Passed");
                // Exit may become available after companion RunStarted.
                exitOwner = new GameObject("Standalone rendered-route test completion") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(exitOwner);
                var exit = exitOwner.AddComponent<StandaloneRenderedRoutePlayerExit>();
                Assert.That(Object.FindObjectsByType<StandaloneRenderedRoutePlayerExit>(FindObjectsInactive.Include), Has.No.Member(exit));
                Assert.That(Resources.FindObjectsOfTypeAll<StandaloneRenderedRoutePlayerExit>(), Has.Member(exit));
                yield return null; // Exercise actual discovery and GameObject ownership checks.
                Assert.That(sink.rows.FindAll(item => item.kind == "exit-attached"), Has.Count.EqualTo(1));
                exit.PrepareToQuit(); exit.PrepareToQuit();
                Assert.That(sink.prequit, Is.Not.Null);
                Assert.That(sink.prequit.callback_count, Is.EqualTo(1));
                Assert.That(sink.prequit.retained, Has.Length.EqualTo(1));
                Assert.That(sink.prequit.retained[0].callbackThreadId, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId));
                Assert.That(sink.prequit.subscribe_attempts, Is.EqualTo(1));
                Assert.That(sink.prequit.unsubscribe_attempts, Is.EqualTo(1));
                Assert.That(publisher.removals, Is.EqualTo(1));
                Assert.That(sink.prequit.errors, Is.Empty);
                string frozen = JsonUtility.ToJson(sink.prequit);
                int reads = reader.reads;
                captured(false); // A delegate already captured before detachment.
                Assert.That(observer.ObserveWantsToQuit(), Is.True);
                observer.ObserveApplicationQuit();
                Assert.That(reader.reads, Is.EqualTo(reads), "No configuration reads after quitting begins.");
                Assert.That(JsonUtility.ToJson(sink.prequit), Is.EqualTo(frozen));
                Assert.That(sink.completion, Is.Null, "OnDestroy cannot be reported before Unity invokes it.");
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(sink.completion, Is.Not.Null);
                Assert.That(sink.completion.on_destroy_count, Is.EqualTo(1));
                Assert.That(sink.completion.application_quit_count, Is.EqualTo(1));
                Assert.That(sink.completion.exit_attach_count, Is.EqualTo(1));
                Assert.That(sink.completion.exit_detach_count, Is.EqualTo(1));
                Assert.That(sink.completion.errors, Is.Empty);
                Assert.That(reader.reads, Is.EqualTo(reads));
            }
            finally { if (owner != null) Object.DestroyImmediate(owner); if (exitOwner != null) Object.DestroyImmediate(exitOwner); }
            LogAssert.NoUnexpectedReceived();
        }

        [Test, Timeout(5000)]
        public void BypassedQuitAndSetupFaultDetachWithoutLateConfigurationReads()
        {
            GameObject owner = null;
            try
            {
                owner = new GameObject("Synthetic bypassed quit");
                var observer = owner.AddComponent<AudioRunLifecycleObserver>();
                var publisher = new FakeEvents(); var reader = new FakeReader(); var sink = new FakeSink();
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                publisher.callback(true);
                Assert.That(observer.ObserveWantsToQuit(), Is.True);
                observer.ObserveApplicationQuit();
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(reader.reads, Is.EqualTo(1));
                Assert.That(publisher.removals, Is.EqualTo(1));
                Assert.That(sink.prequit, Is.Null);
                Assert.That(sink.completion.prequit_observed, Is.False);
                Assert.That(sink.completion.errors, Is.Not.Empty);

                owner = new GameObject("Synthetic attach-then-throw");
                observer = owner.AddComponent<AudioRunLifecycleObserver>();
                publisher = new FakeEvents { throwAfterAttach = true }; reader = new FakeReader(); sink = new FakeSink();
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                Assert.That(publisher.removals, Is.EqualTo(1));
                Assert.That(publisher.callback, Is.Null);
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(reader.reads, Is.Zero);
                Assert.That(sink.completion.errors, Is.Not.Empty);

                owner = new GameObject("Synthetic pending callback overflow");
                observer = owner.AddComponent<AudioRunLifecycleObserver>();
                publisher = new FakeEvents(); reader = new FakeReader(); sink = new FakeSink();
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                for (int i = 0; i < 160; i++) publisher.callback(true);
                observer.CloseBeforeQuit();
                Assert.That(sink.prequit.callback_count, Is.EqualTo(160));
                Assert.That(sink.prequit.queue_drops, Is.EqualTo(32));
                Assert.That(sink.prequit.retained, Has.Length.EqualTo(128));
                Object.DestroyImmediate(owner); owner = null;

                owner = new GameObject("Synthetic failed audio detach");
                observer = owner.AddComponent<AudioRunLifecycleObserver>();
                publisher = new FakeEvents { throwOnRemove = true }; reader = new FakeReader(); sink = new FakeSink();
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                observer.CloseBeforeQuit();
                Assert.That(sink.prequit.subscribed, Is.True);
                Assert.That(sink.prequit.errors, Is.Not.Empty);
                Object.DestroyImmediate(owner); owner = null;

                owner = new GameObject("Synthetic journal fault");
                observer = owner.AddComponent<AudioRunLifecycleObserver>();
                publisher = new FakeEvents(); reader = new FakeReader(); sink = new FakeSink { failAppend = true };
                observer.Begin("synthetic", 123, publisher, reader, sink, false);
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(reader.reads, Is.Zero);
                Assert.That(sink.completion.errors, Is.Not.Empty);
            }
            finally { if (owner != null) Object.DestroyImmediate(owner); }
            LogAssert.NoUnexpectedReceived();
        }
        private sealed class FakeEvents : IAudioConfigurationEvents
        {
            public Action<bool> callback;
            public int removals;
            public bool throwAfterAttach, throwOnRemove;
            public void Subscribe(Action<bool> value) { callback = value; if (throwAfterAttach) throw new InvalidOperationException("Synthetic attach fault"); }
            public void Unsubscribe(Action<bool> value) { removals++; if (throwOnRemove) throw new InvalidOperationException("Synthetic detach fault"); callback -= value; }
        }
        private sealed class FakeCallbacks : ITestRunCallback
        {
            private readonly string label;
            private readonly List<string> calls;
            public bool failStart;
            public FakeCallbacks(string label, List<string> calls) { this.label = label; this.calls = calls; }
            public void RunStarted(ITest test) { calls.Add(label + "-run-started"); if (failStart) throw new InvalidOperationException("Synthetic callback fault"); }
            public void TestStarted(ITest test) { calls.Add(label + "-test-started"); }
            public void TestFinished(ITestResult result) { calls.Add(label + "-test-finished"); }
            public void RunFinished(ITestResult result) { calls.Add(label + "-run-finished"); }
        }
        private sealed class FakeReader : IAudioConfigurationReader
        {
            public int reads;
            public AudioConfiguration Read()
            {
                reads++;
                return new AudioConfiguration { sampleRate = 48000, dspBufferSize = 1024, numRealVoices = 32, numVirtualVoices = 512, speakerMode = AudioSpeakerMode.Stereo };
            }
        }
        private sealed class FakeSink : IAudioRunLifecycleSink
        {
            public readonly List<AudioRunLifecycleObserver.Marker> rows = new List<AudioRunLifecycleObserver.Marker>();
            public AudioRunLifecycleObserver.PreQuit prequit;
            public AudioRunLifecycleObserver.Completion completion;
            public bool failAppend;
            public void Append(AudioRunLifecycleObserver.Marker value) { if (failAppend) throw new InvalidOperationException("Synthetic journal failure"); rows.Add(value); }
            public string SavePreQuit(AudioRunLifecycleObserver.PreQuit value) { prequit = value; return new string('a', 64); }
            public void Complete(AudioRunLifecycleObserver.Completion value) { completion = value; }
        }
    }
}
#endif
