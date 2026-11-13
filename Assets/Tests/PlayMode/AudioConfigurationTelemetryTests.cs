#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Synthetic publisher regressions exercise ownership/threading, never
    // manufacture Unity callback evidence or change the audio configuration.
    public sealed class AudioConfigurationTelemetryTests
    {
        [Test, Timeout(5000)]
        public void CallbackQueueIsBoundedAndClosedAcceptanceIsImmutable()
        {
            var queue = new AudioConfigurationObservationQueue();
            int workerId = 0;
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    workerId = Thread.CurrentThread.ManagedThreadId;
                    for (int i = 0; i < 160; i++) queue.Record(i % 2 == 0, 1000 + i, 2000 + i, workerId, 3);
                }
                catch (Exception error) { failure = error; }
            });
            worker.Start();
            Assert.That(worker.Join(2000), Is.True);
            Assert.That(failure, Is.Null);
            var counts = queue.Snapshot();
            Assert.That(counts.observed, Is.EqualTo(160));
            Assert.That(counts.pending, Is.EqualTo(128));
            Assert.That(counts.dropped, Is.EqualTo(32));
            Assert.That(workerId, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.Throws<ArgumentException>(() => queue.DrainInto(new AudioConfigurationObservationQueue.Entry[127]));
            var retained = new AudioConfigurationObservationQueue.Entry[128];
            Assert.That(queue.DrainInto(retained), Is.EqualTo(128));
            for (int i = 0; i < retained.Length; i++)
            {
                Assert.That(retained[i].sequence, Is.EqualTo(i + 1));
                Assert.That(retained[i].utcTicks, Is.EqualTo(1000 + i));
                Assert.That(retained[i].clockTicks, Is.EqualTo(2000 + i));
                Assert.That(retained[i].threadId, Is.EqualTo(workerId));
                Assert.That(retained[i].phase, Is.EqualTo(3));
                Assert.That(retained[i].deviceWasChanged, Is.EqualTo(i % 2 == 0));
            }
            queue.CloseAcceptance();
            Assert.That(queue.Record(true, 9999, 9999, workerId, 4), Is.False);
            counts = queue.Snapshot();
            Assert.That(counts.observed, Is.EqualTo(160));
            Assert.That(counts.dropped, Is.EqualTo(32));
            Assert.That(counts.pending, Is.Zero);
            Assert.That(counts.accepting || counts.overflowed, Is.False);
        }

        [UnityTest, Timeout(10000)]
        public IEnumerator OwnedObserverDrainsOnMainThreadAndDetachesAcrossFaults()
        {
            GameObject owner = null;
            try
            {
                owner = new GameObject("Synthetic audio observer regression");
                var probe = owner.AddComponent<AudioConfigurationTelemetry>();
                int main = Thread.CurrentThread.ManagedThreadId;
                var events = new FakeEvents();
                var reader = new FakeReader(main);
                probe.Begin("synthetic regression only", "synthetic", "synthetic", events, reader);
                probe.MarkPhase("synthetic-capture");
                Action<bool> capturedBeforeDetach = events.callback;
                Exception rejected = null;
                var worker = new Thread(() =>
                {
                    capturedBeforeDetach(true); capturedBeforeDetach(false);
                    try { probe.DrainOnMainThread(); } catch (Exception error) { rejected = error; }
                });
                worker.Start(); Assert.That(worker.Join(2000), Is.True);
                Assert.That(rejected, Is.TypeOf<InvalidOperationException>());
                Assert.That(reader.reads, Is.EqualTo(1), "Callbacks and a rejected worker drain cannot read Unity configuration.");
                yield return null; // Actual Update owns the main-thread drain.
                var snapshot = probe.Close();
                Assert.That(probe.Close(), Is.SameAs(snapshot));
                Assert.That(events.subscriptions, Is.Zero);
                Assert.That(events.removals, Is.EqualTo(1));
                Assert.That(snapshot.retained.Length, Is.EqualTo(2));
                Assert.That(snapshot.callbackCount, Is.EqualTo(2));
                Assert.That(snapshot.queueDrops + snapshot.recordDrops + snapshot.pendingCallbacks, Is.Zero);
                Assert.That(snapshot.errors, Is.Empty);
                Assert.That(reader.reads, Is.EqualTo(3));
                foreach (var item in snapshot.retained)
                {
                    Assert.That(item.callbackThreadId, Is.Not.EqualTo(main));
                    Assert.That(item.configuration.readThreadId, Is.EqualTo(main));
                    Assert.That(item.observedDelaySeconds, Is.GreaterThanOrEqualTo(0));
                }
                string frozen = JsonUtility.ToJson(snapshot);
                capturedBeforeDetach(true); // Delegate captured before Close may run afterward.
                Assert.That(JsonUtility.ToJson(probe.Close()), Is.EqualTo(frozen));
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(events.removals, Is.EqualTo(1));

                owner = new GameObject("Synthetic setup fault regression");
                probe = owner.AddComponent<AudioConfigurationTelemetry>();
                events = new FakeEvents { throwAfterAttach = true };
                Assert.Throws<InvalidOperationException>(() => probe.Begin("synthetic", "synthetic", "synthetic", events, new FakeReader(main)));
                Assert.That(events.subscriptions, Is.Zero, "An attach-then-throw publisher still owns a cleanup lease.");
                Assert.That(events.removals, Is.EqualTo(1));
                Object.DestroyImmediate(owner); owner = null;

                owner = new GameObject("Synthetic read fault regression");
                probe = owner.AddComponent<AudioConfigurationTelemetry>();
                events = new FakeEvents(); reader = new FakeReader(main);
                probe.Begin("synthetic", "synthetic", "synthetic", events, reader);
                events.callback(true); reader.fail = true;
                Assert.Throws<InvalidOperationException>(() => probe.DrainOnMainThread());
                reader.fail = false;
                snapshot = probe.Close();
                Assert.That(snapshot.callbackCount, Is.EqualTo(1));
                Assert.That(snapshot.recordDrops, Is.EqualTo(1));
                Assert.That(snapshot.retained, Is.Empty);
                Assert.That(snapshot.errors, Has.Length.EqualTo(1));
                Object.DestroyImmediate(owner); owner = null;

                owner = new GameObject("Synthetic destruction regression");
                probe = owner.AddComponent<AudioConfigurationTelemetry>();
                events = new FakeEvents(); reader = new FakeReader(main);
                probe.Begin("synthetic", "synthetic", "synthetic", events, reader);
                Object.DestroyImmediate(owner); owner = null;
                Assert.That(events.subscriptions, Is.Zero);
                Assert.That(events.removals, Is.EqualTo(1));
                Assert.That(reader.reads, Is.EqualTo(2));
            }
            finally { if (owner != null) Object.DestroyImmediate(owner); }
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class FakeEvents : IAudioConfigurationEvents
        {
            public Action<bool> callback;
            public int subscriptions, removals;
            public bool throwAfterAttach;
            public void Subscribe(Action<bool> value)
            {
                callback = value; subscriptions++;
                if (throwAfterAttach) throw new InvalidOperationException("Synthetic attach fault");
            }
            public void Unsubscribe(Action<bool> value) { callback -= value; subscriptions--; removals++; }
        }
        private sealed class FakeReader : IAudioConfigurationReader
        {
            private readonly int main;
            public int reads;
            public bool fail;
            public FakeReader(int main) { this.main = main; }
            public AudioConfiguration Read()
            {
                if (Thread.CurrentThread.ManagedThreadId != main) throw new InvalidOperationException("Synthetic reader called from worker");
                if (fail) throw new InvalidOperationException("Synthetic configuration read fault");
                reads++;
                return new AudioConfiguration { sampleRate = 48000, dspBufferSize = 1024, numRealVoices = 32, numVirtualVoices = 512, speakerMode = AudioSpeakerMode.Stereo };
            }
        }
    }
}
#endif
