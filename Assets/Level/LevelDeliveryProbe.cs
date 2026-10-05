using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShadowsOfTheForsaken.Level
{
    /// <summary>Opt-in native player startup evidence. Normal launches never create this component.</summary>
    public sealed class LevelDeliveryProbe : MonoBehaviour
    {
        private ForsakenLevel level;
        private string output;
        public void Configure(ForsakenLevel owner, string directory) { level = owner; output = directory; }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(output);
            yield return null;
            yield return new WaitForSecondsRealtime(5);
            float started = Time.realtimeSinceStartup;
            int frames = 0;
            float longest = 0;
            while (Time.realtimeSinceStartup - started < 5)
            {
                frames++;
                longest = Mathf.Max(longest, Time.unscaledDeltaTime);
                yield return null;
            }
            var evidence = new StartupEvidence
            {
                unityVersion = Application.unityVersion, scene = gameObject.scene.path,
                room = level.Progression.Snapshot.Room.ToString(), enemies = level.Enemies.Count,
                passages = level.Transitions.Count, health = level.Combat.Health,
                cameraSafe = level.Follow.HasSafePose, width = Screen.width, height = Screen.height,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                averageUpdateFramesPerSecond = frames / (Time.realtimeSinceStartup - started),
                longestFrameSeconds = longest, batchMode = Application.isBatchMode,
                method = "Native Windows player startup and Update cadence. Explicit URP render request captures the world and real camera-space Canvas HUD; health-bar pixels are verified. Hidden-window presentation, manual play and full-route performance remain separate checks."
            };
            evidence.hudCaptureFile = "courtyard.png";
            evidence.cameraCaptureSaved = CaptureCamera(Path.Combine(output, evidence.hudCaptureFile), evidence);
            File.WriteAllText(Path.Combine(output, "startup.json"), JsonUtility.ToJson(evidence, true));
            bool ready = level.Combat.Health == 100 && level.Enemies.Count == 3 && level.Transitions.Count == 9 &&
                level.Follow.HasSafePose && level.Progression.Snapshot.Room == Progression.LevelRoom.Courtyard &&
                evidence.cameraCaptureSaved && evidence.hudCaptureSaved;
            Application.Quit(ready ? 0 : 1);
        }
        private bool CaptureCamera(string path, StartupEvidence evidence)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || Camera.main == null) return false;
            Canvas.ForceUpdateCanvases();
            var target = RenderTexture.GetTemporary(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), 24);
            var previous = RenderTexture.active;
            Texture2D capture = null;
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(Camera.main, request)) return false;
                RenderPipeline.SubmitRenderRequest(Camera.main, request);
                RenderTexture.active = target;
                capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                File.WriteAllBytes(path, capture.EncodeToPNG());
                var hud = level.GetComponent<LevelHud>();
                if (hud != null && hud.Ready)
                {
                    Vector2 point = hud.HealthBarViewportPoint;
                    evidence.hudMarkerViewport = point;
                    evidence.hudSampleColor = capture.GetPixel(Mathf.Clamp(Mathf.RoundToInt(point.x * (target.width - 1)), 0, target.width - 1),
                        Mathf.Clamp(Mathf.RoundToInt(point.y * (target.height - 1)), 0, target.height - 1));
                    Color sample = evidence.hudSampleColor, expected = hud.HealthBarColor;
                    evidence.hudCaptureSaved = point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1 &&
                        sample.r > sample.g * 1.5f && sample.r > sample.b * 1.5f &&
                        Mathf.Abs(sample.r - expected.r) < .3f && Mathf.Abs(sample.g - expected.g) < .3f && Mathf.Abs(sample.b - expected.b) < .3f;
                }
                var pixels = capture.GetPixels32();
                Color32 first = pixels[0];
                for (int i = 1; i < pixels.Length; i += 17)
                    if (Mathf.Abs(pixels[i].r - first.r) + Mathf.Abs(pixels[i].g - first.g) + Mathf.Abs(pixels[i].b - first.b) > 30)
                        return true;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (capture != null) Destroy(capture);
            }
        }
        [Serializable]
        private sealed class StartupEvidence
        {
            public string unityVersion, scene, room, graphicsDevice, method, hudCaptureFile;
            public int enemies, passages, health, width, height;
            public bool cameraSafe, cameraCaptureSaved, hudCaptureSaved, batchMode;
            public float averageUpdateFramesPerSecond, longestFrameSeconds;
            public Vector2 hudMarkerViewport;
            public Color hudSampleColor;
        }
    }
}
