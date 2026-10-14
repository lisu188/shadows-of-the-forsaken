#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Test-only audio-device follow-up. A working mixer is not evidence that a
    // physical speaker is connected, audible, or accepted by a listener.
    public sealed class AudioMixerPlaybackTests
    {
        private const int Window = 1024;
        private const double PhaseTimeout = 3;
        private const float QuietPeak = .003f, SignalPeak = .00001f, SilencePeak = .0000001f;
        private readonly List<SourceState> sources = new List<SourceState>();
        private readonly List<ListenerState> listeners = new List<ListenerState>();
        private readonly List<SignalSample> samples = new List<SignalSample>();
        private readonly List<string> cleanupErrors = new List<string>();
        private readonly float[] sourceBuffer = new float[Window], masterBuffer = new float[Window];
        private Evidence evidence;
        private GameObject owned;
        private AudioSource source;
        private AudioClip clip;
        private AudioMixerPlaybackTap tap;
        private string path, stage;
        private bool settingsCaptured, cleanupComplete, evidenceSaved;
        private float originalVolume;
        private bool originalPause;
        private double nextEvidenceSample;

        [UnityTest, Timeout(20000)]
        public IEnumerator QuietClipTraversesMixerAndReturnsToSilence()
        {
            try
            {
                Prepare();
                double grace = (evidence.dspBufferFrames * (double)evidence.dspBuffers + Window) / evidence.outputSampleRate;
                stage = "prime-and-baseline";
                // Both APIs allocate their short history on first use. Empty
                // initial output is not accepted as a processed silence sample.
                source.GetOutputData(sourceBuffer, 0);
                AudioListener.GetOutputData(masterBuffer, 0);
                double began = AudioSettings.dspTime, deadline = Time.realtimeSinceStartupAsDouble + PhaseTimeout;
                int quiet = 0;
                bool baseline = false;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    var sample = Observe();
                    quiet = sample.masterPeak <= SilencePeak ? quiet + 1 : 0;
                    if (sample.dspTime - began >= grace && quiet >= 3) { baseline = true; break; }
                    yield return null;
                }
                Assert.That(baseline, Is.True, "An advancing DSP clock and freshly processed silent master baseline are required.");
                evidence.baselineVerified = true;

                stage = "playing";
                evidence.playDspTime = AudioSettings.dspTime;
                source.Play();
                deadline = Time.realtimeSinceStartupAsDouble + PhaseTimeout;
                bool signal = false;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    var sample = Observe();
                    evidence.playingObserved |= sample.sourcePlaying;
                    var counters = tap.Snapshot();
                    if (sample.sourcePeak > SignalPeak && sample.masterPeak > SignalPeak &&
                        sample.dspTime > evidence.playDspTime + .02 && counters.callbacks >= 2 &&
                        counters.nonzeroSamples >= 128 && counters.invalidSamples == 0 && evidence.playingObserved)
                    {
                        evidence.signal = sample;
                        evidence.sourceWindow = (float[])sourceBuffer.Clone();
                        evidence.masterWindow = (float[])masterBuffer.Clone();
                        signal = true;
                        break;
                    }
                    yield return null;
                }
                Assert.That(signal, Is.True, "The played clip must traverse the real audio callback, source history and master mixer.");
                evidence.nonzeroMixerVerified = true;

                stage = "stopped-silence";
                source.Stop();
                evidence.stopDspTime = AudioSettings.dspTime;
                deadline = Time.realtimeSinceStartupAsDouble + PhaseTimeout;
                quiet = 0;
                bool stopped = false;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    var sample = Observe();
                    // A stopped source's analysis history need not be cleared;
                    // require newly processed silence at the actual master.
                    quiet = !sample.sourcePlaying && sample.masterPeak <= SilencePeak ? quiet + 1 : 0;
                    if (sample.dspTime - evidence.stopDspTime >= grace && quiet >= 3)
                    {
                        evidence.stopped = sample;
                        stopped = true;
                        break;
                    }
                    yield return null;
                }
                Assert.That(stopped, Is.True, "Stopping the owned clip must return freshly processed master output to silence.");
                evidence.stoppedSilenceVerified = true;
                evidence.signalChecksPassed = true;
                stage = "signal-checks-complete";
            }
            finally { RestoreAndSave(); }
            AssertRestored();
            Assert.That(evidence.tap.invalidSamples, Is.Zero, "All observed audio callbacks must contain finite PCM.");
            LogAssert.NoUnexpectedReceived();
        }

        [TearDown]
        public void RestoreAfterExternalIteratorAbort()
        {
            // UTF can abandon a yielded iterator on an unexpected engine log
            // without resuming its finally. This fallback owns the same lease.
            RestoreAndSave();
            if (evidence != null) AssertRestored();
            LogAssert.NoUnexpectedReceived();
        }

        private void AssertRestored()
        {
            Assert.That(evidence.sourcesRestored && evidence.listenersRestored && evidence.globalSettingsRestored &&
                evidence.ownedObjectsDestroyed, Is.True, "The mixer diagnostic must release owned objects and restore borrowed audio state.");
            Assert.That(cleanupErrors, Is.Empty, "Audio probe cleanup failed; see its evidence.");
        }

        private void Prepare()
        {
            string directory = Environment.GetEnvironmentVariable("SHADOWS_AUDIO_PROBE_OUTPUT");
            if (string.IsNullOrWhiteSpace(directory)) directory = Application.temporaryCachePath;
            Assert.That(Path.IsPathRooted(directory), Is.True, "Audio evidence requires an absolute local directory.");
            directory = Path.GetFullPath(directory);
            Assert.That(directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal), Is.False);
            directory = Path.Combine(directory, "audio-mixer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "evidence.json");
            TestContext.WriteLine("Audio mixer diagnostic evidence: " + path);
            evidence = new Evidence { unityVersion = Application.unityVersion, platform = Application.platform.ToString(), isEditor = Application.isEditor };
            originalVolume = AudioListener.volume; originalPause = AudioListener.pause; settingsCaptured = true;
            foreach (var item in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            {
                sources.Add(new SourceState { source = item, muted = item.mute });
                item.mute = true; // Preserve each source's playing/paused state and natural clock.
            }
            foreach (var item in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            {
                listeners.Add(new ListenerState { listener = item, enabled = item.enabled });
                item.enabled = false;
            }
            AudioListener.volume = 1; AudioListener.pause = false;
            evidence.originalListenerVolume = originalVolume; evidence.originalListenerPaused = originalPause;
            evidence.borrowedSources = sources.Count; evidence.borrowedListeners = listeners.Count;
            evidence.outputSampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out evidence.dspBufferFrames, out evidence.dspBuffers);
            evidence.speakerMode = AudioSettings.GetConfiguration().speakerMode.ToString();
            Assert.That(evidence.outputSampleRate, Is.GreaterThan(0), "A real output sample rate is required.");
            Assert.That(evidence.dspBufferFrames > 0 && evidence.dspBuffers > 0, Is.True);
            foreach (string argument in Environment.GetCommandLineArgs())
                Assert.That(argument.Equals("-noaudio", StringComparison.OrdinalIgnoreCase) || argument.Equals("-disableaudio", StringComparison.OrdinalIgnoreCase), Is.False);

            owned = new GameObject("Bounded test-only mixer probe"); owned.SetActive(false);
            source = owned.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0; source.volume = 1;
            source.mute = false; source.ignoreListenerPause = false; source.ignoreListenerVolume = false;
            // One filter can belong to one source OR listener. Keep the owned
            // listener on its own child so the tap unambiguously observes source PCM.
            var listenerObject = new GameObject("Owned mixer probe listener");
            listenerObject.transform.SetParent(owned.transform, false);
            listenerObject.AddComponent<AudioListener>();
            tap = owned.AddComponent<AudioMixerPlaybackTap>();
            int count = evidence.outputSampleRate / 2;
            var data = new float[count];
            int fade = Math.Max(1, evidence.outputSampleRate / 100);
            for (int i = 0; i < count; i++)
            {
                double envelope = Math.Min(1, Math.Min(i / (double)fade, (count - 1 - i) / (double)fade));
                data[i] = (float)(QuietPeak * envelope * Math.Sin(2 * Math.PI * 440 * i / evidence.outputSampleRate));
            }
            clip = AudioClip.Create("Half-second quiet diagnostic clip", count, 1, evidence.outputSampleRate, false);
            Assert.That(clip.SetData(data, 0), Is.True);
            evidence.clipFrames = count; evidence.clipPeak = QuietPeak; evidence.clipSeconds = count / (double)evidence.outputSampleRate;
            source.clip = clip; owned.SetActive(true);
        }

        private SignalSample Observe()
        {
            source.GetOutputData(sourceBuffer, 0);
            AudioListener.GetOutputData(masterBuffer, 0);
            var sample = new SignalSample { stage = stage, realtime = Time.realtimeSinceStartupAsDouble, dspTime = AudioSettings.dspTime,
                sourcePlaying = source.isPlaying, sourcePeak = Peak(sourceBuffer), masterPeak = Peak(masterBuffer) };
            evidence.polls++;
            if (samples.Count < 512 && sample.realtime >= nextEvidenceSample)
            {
                samples.Add(sample); nextEvidenceSample = sample.realtime + .02;
            }
            return sample;
        }

        private static float Peak(float[] data)
        {
            float peak = 0;
            bool finite = true;
            foreach (float value in data)
            {
                finite &= !float.IsNaN(value) && !float.IsInfinity(value);
                peak = Math.Max(peak, Math.Abs(value));
            }
            Assert.That(finite, Is.True, "Mixer samples must be finite.");
            return peak;
        }

        private void RestoreAndSave()
        {
            if (evidence == null) return;
            if (cleanupComplete) { SaveEvidence(); return; }
            evidence.stage = stage; evidence.recordedUtc = DateTime.UtcNow.ToString("O");
            if (tap != null) evidence.tap = tap.Snapshot();
            Cleanup(() => { if (source != null) source.Stop(); });
            Cleanup(() => { if (source != null) source.clip = null; });
            Cleanup(() => { if (owned != null) Object.DestroyImmediate(owned); });
            Cleanup(() => { if (clip != null) Object.DestroyImmediate(clip); });
            evidence.ownedObjectsDestroyed = owned == null && clip == null;
            // Restore global gain/pause while borrowed sources are still muted
            // and listeners disabled; the mixer runs concurrently with cleanup.
            Cleanup(() => { if (settingsCaptured) AudioListener.volume = originalVolume; });
            Cleanup(() => { if (settingsCaptured) AudioListener.pause = originalPause; });
            evidence.sourcesRestored = true;
            foreach (var item in sources)
                Cleanup(() =>
                {
                    if (item.source != null) { item.source.mute = item.muted; evidence.sourcesRestored &= item.source.mute == item.muted; }
                    else evidence.sourcesRestored = false;
                });
            evidence.listenersRestored = true;
            foreach (var item in listeners)
                Cleanup(() =>
                {
                    if (item.listener != null) { item.listener.enabled = item.enabled; evidence.listenersRestored &= item.listener.enabled == item.enabled; }
                    else evidence.listenersRestored = false;
                });
            evidence.globalSettingsRestored = settingsCaptured && AudioListener.volume == originalVolume && AudioListener.pause == originalPause;
            evidence.samples = samples.ToArray(); evidence.cleanupErrors = cleanupErrors.ToArray();
            cleanupComplete = true;
            SaveEvidence();
        }

        private void SaveEvidence()
        {
            if (evidenceSaved) return;
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            evidenceSaved = true;
        }

        private void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { cleanupErrors.Add(error.GetType().Name + ": " + error.Message); }
        }

        private sealed class SourceState { public AudioSource source; public bool muted; }
        private sealed class ListenerState { public AudioListener listener; public bool enabled; }
        [Serializable] private sealed class SignalSample { public string stage; public double realtime, dspTime; public bool sourcePlaying; public float sourcePeak, masterPeak; }
        [Serializable] private sealed class Evidence
        {
            public string scope = "Test-only Unity source/DSP/master-mixer processing; not physical speaker output or audible acceptance. NUnit and complete native log review remain required.";
            public string unityVersion, platform, speakerMode, stage, recordedUtc;
            public bool isEditor, originalListenerPaused, playingObserved, baselineVerified, nonzeroMixerVerified, stoppedSilenceVerified, signalChecksPassed;
            public bool sourcesRestored, listenersRestored, globalSettingsRestored, ownedObjectsDestroyed;
            public float originalListenerVolume, clipPeak;
            public int borrowedSources, borrowedListeners, outputSampleRate, dspBufferFrames, dspBuffers, clipFrames, polls;
            public double clipSeconds, playDspTime, stopDspTime;
            public SignalSample signal, stopped;
            public SignalSample[] samples;
            public float[] sourceWindow, masterWindow;
            public AudioMixerTapSnapshot tap;
            public string[] cleanupErrors;
        }
    }

    [Serializable]
    public sealed class AudioMixerTapSnapshot { public long callbacks, nonzeroSamples, invalidSamples; public double peak; }

    // Unity invokes this on its audio thread. Do not call Unity APIs, log, write
    // files, allocate collections or alter PCM here; publish bounded counters.
    public sealed class AudioMixerPlaybackTap : MonoBehaviour
    {
        private long callbacks, nonzeroSamples, invalidSamples, peakNanounits;
        private void OnAudioFilterRead(float[] data, int channels)
        {
            long nonzero = 0, invalid = channels > 0 ? 0 : 1;
            double peak = 0;
            for (int i = 0; i < data.Length; i++)
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i])) invalid++;
                else { double value = Math.Abs((double)data[i]); peak = Math.Max(peak, value); if (value > .00001) nonzero++; }
            Interlocked.Increment(ref callbacks); Interlocked.Add(ref nonzeroSamples, nonzero); Interlocked.Add(ref invalidSamples, invalid);
            long quantized = (long)(peak * 1000000000);
            if (quantized > Interlocked.Read(ref peakNanounits)) Interlocked.Exchange(ref peakNanounits, quantized);
        }
        public AudioMixerTapSnapshot Snapshot() => new AudioMixerTapSnapshot
        {
            callbacks = Interlocked.Read(ref callbacks), nonzeroSamples = Interlocked.Read(ref nonzeroSamples),
            invalidSamples = Interlocked.Read(ref invalidSamples), peak = Interlocked.Read(ref peakNanounits) / 1000000000.0
        };
    }
}
#endif
