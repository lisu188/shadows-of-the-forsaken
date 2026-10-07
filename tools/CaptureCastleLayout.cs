// Diagnostic Editor helper. Keep outside Assets except for a coordinated,
// temporary import into the isolated native validation project. Never a player script.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CaptureCastleLayout
{
    private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
    private const string HelperPath = "Assets/LevelLayout/Editor/CaptureCastleLayout.cs";
    private const float PresentationCameraHeight = 60f;

    // -executeMethod CaptureCastleLayout.Run, with graphics enabled.
    // SHADOWS_MAP_CAPTURE_DIR must be an absolute, previously unused directory.
    public static void Run()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use a dedicated batch Editor outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("An actual graphics device is required; do not use -nographics.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Refusing to discard an unsaved scene.");
        string output = Environment.GetEnvironmentVariable("SHADOWS_MAP_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output))
            throw new InvalidOperationException("SHADOWS_MAP_CAPTURE_DIR must be absolute.");
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Refusing to overwrite existing evidence.");
        if (!File.Exists(HelperPath)) throw new FileNotFoundException("Import the reviewed helper at its exact temporary path.");
        var report = new Receipt
        {
            utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceType.ToString(), scenePath = ScenePath,
            sceneSha256 = Hash(ScenePath), helperPath = HelperPath, helperSha256 = Hash(HelperPath)
        };
        Directory.CreateDirectory(output);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        // A fresh batch Editor may have no loaded/active scene. Unity refuses
        // to restore that setup; discard only our diagnostic scene into a clean
        // empty scene before exit. A nonempty saved setup is restored normally.
        bool emptySetup = setup.All(item => !item.isLoaded && !item.isActive && string.IsNullOrEmpty(item.path));
        report.initialSceneSetupCount = setup.Length;
        report.sceneCleanup = emptySetup ? "Fresh empty scene for initially empty batch setup" : "Restore original scene setup";
        if (!emptySetup && (!setup.Any(item => item.isLoaded) || setup.Count(item => item.isActive) != 1))
            throw new InvalidOperationException("Unsupported initial Editor scene setup.");
        var previousActive = RenderTexture.active;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        Scene scene = default;
        GameObject cameraObject = null, lightObject = null;
        RendererState[] renderers = null;
        BehaviourState[] canvases = null, lights = null;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color ambient = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog;
        try
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // Snapshot after opening the saved scene; no gameplay updates or builder calls.
            ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight; fog = RenderSettings.fog;
            var roots = scene.GetRootGameObjects();
            var geometry = roots.Single(root => root.name == "Geometry");
            renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Select(renderer => new RendererState(renderer)).ToArray();
            canvases = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                .Select(canvas => new BehaviourState(canvas)).ToArray();
            lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Select(light => new BehaviourState(light)).ToArray();
            foreach (var canvas in canvases) canvas.component.enabled = false;
            report.canvasOverrides = canvases.Where(c => c.enabled).Select(c => c.path).ToArray();
            report.roomMarkers = roots.Single(root => root.name == "Rooms").GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("Room_", StringComparison.Ordinal))
                .Select(t => new Marker { path = Hierarchy(t), position = t.position }).ToArray();
            cameraObject = new GameObject("Diagnostic map camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .105f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 300; camera.useOcclusionCulling = false;
            camera.allowHDR = false; camera.allowMSAA = false;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            ShaderUtil.allowAsyncCompilation = false;
            // Saved environment bounds reach y=55 (mountains); the tallest castle
            // finial reaches 36.5. Verify actual transformed bounds before using
            // y=60, above all saved visible geometry but below the heavy fog at 120.
            report.savedVisibleMaximumY = renderers.Where(state => state.renderer.enabled &&
                !state.renderer.forceRenderingOff && state.renderer.gameObject.activeInHierarchy)
                .Max(state => state.renderer.bounds.max.y);
            if (report.savedVisibleMaximumY >= PresentationCameraHeight - camera.nearClipPlane)
                throw new InvalidOperationException("Diagnostic camera would clip saved visible geometry.");
            // x=-40..48, z=-8..80. DOCX's original 10x10 grid is x=-36..44, z=-4..76.
            report.images.Add(Capture(output, "01-saved-presentation-overhead", camera, renderers,
                new Vector3(4, PresentationCameraHeight, 36), 44, 1024, 1024, "Saved presentation; HUD omitted; original scene lighting/fog; camera y=60."));

            var structuralGroups = new HashSet<string> { "GroundStructure", "UpperStructure", "SecretStructure", "Ramps" };
            foreach (var state in renderers)
            {
                Transform t = state.renderer.transform;
                bool structural = t.IsChildOf(geometry.transform) && t.parent != null &&
                    structuralGroups.Contains(t.parent.name) && !t.name.Contains("_Ceiling");
                if (structural && !t.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("A structural object is inactive: " + state.path);
                state.renderer.enabled = structural;
                if (structural) state.renderer.forceRenderingOff = false;
            }
            report.structuralRenderers = renderers.Count(state => state.renderer.enabled);
            if (report.structuralRenderers == 0) throw new InvalidOperationException("No saved structural renderers found.");
            foreach (var light in lights) light.component.enabled = false;
            report.structuralLightOverrides = lights.Where(light => light.enabled).Select(light => light.path).ToArray();
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .65f;
            RenderSettings.fog = false;
            lightObject = new GameObject("Diagnostic structural light") { hideFlags = HideFlags.HideAndDontSave };
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            var diagnosticLight = lightObject.AddComponent<Light>();
            diagnosticLight.type = LightType.Directional; diagnosticLight.color = Color.white;
            diagnosticLight.intensity = 1; diagnosticLight.shadows = LightShadows.None;
            report.images.Add(Capture(output, "02-saved-structure-map-cutaway", camera, renderers,
                new Vector3(4, 120, 36), 44, 1024, 1024,
                "Actual saved structural meshes only; named ceilings, presentation, props, actors and gameplay gates hidden. Neutral diagnostic light."));
            // Include the extended 100 m approach and complete lower shortcut, at original relative positions.
            report.images.Add(Capture(output, "03-saved-structure-full-extent", camera, renderers,
                new Vector3(0, 120, -6), 92, 512, 1024,
                "Same structural cutaway; x=-46..46, z=-98..86. No compression of the approach or lower shortcut."));
            report.status = "captured_for_review";
        }
        catch (Exception error)
        {
            report.status = "failed"; report.error = error.GetType().Name + ": " + error.Message;
            throw;
        }
        finally
        {
            // Restore transient state and unload without saving. Do not call any
            // builder, SaveAssets, SaveScene, SetDirty, or progression method.
            if (renderers != null) foreach (var state in renderers) state.Restore();
            if (canvases != null) foreach (var state in canvases) state.Restore();
            if (lights != null) foreach (var state in lights) state.Restore();
            RenderTexture.active = previousActive; ShaderUtil.allowAsyncCompilation = previousAsync;
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.fog = fog;
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (lightObject != null) Object.DestroyImmediate(lightObject);
            try
            {
                if (emptySetup) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            catch (Exception error)
            {
                report.status = "failed";
                report.error = (report.error ?? "") + " Cleanup: " + error.GetType().Name + ": " + error.Message;
            }
            report.sceneSha256After = Hash(ScenePath);
            report.sceneBytesUnchanged = report.sceneSha256After == report.sceneSha256;
            if (!report.sceneBytesUnchanged) { report.status = "failed"; report.error = "Saved scene bytes changed."; }
            File.WriteAllText(Path.Combine(output, "capture-receipt.json"), JsonUtility.ToJson(report, true));
        }
        if (report.status == "failed") throw new InvalidOperationException(report.error ?? "Capture failed.");
        Debug.Log("Castle map editor renders written; visual comparison remains required.");
    }

    private static ImageReceipt Capture(string output, string name, Camera camera, RendererState[] renderers,
        Vector3 position, float size, int width, int height, string scope)
    {
        camera.transform.SetPositionAndRotation(position, Quaternion.Euler(90, 0, 0));
        camera.orthographicSize = size; camera.aspect = (float)width / height;
        var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D pixels = null;
        try
        {
            texture.Create(); camera.targetTexture = texture;
            var request = new RenderPipeline.StandardRequest { destination = texture };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("The saved scene pipeline does not support this render request.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = texture;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            string path = Path.Combine(output, name + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            return new ImageReceipt
            {
                file = name + ".png", sha256 = Hash(path), bytes = new FileInfo(path).Length,
                scope = scope, width = width, height = height, position = position,
                euler = new Vector3(90, 0, 0), orthographicSize = size, aspect = camera.aspect,
                near = camera.nearClipPlane, far = camera.farClipPlane,
                visibilityOverrides = renderers.Where(state => state.Changed).Select(state => state.Override()).ToArray()
            };
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            texture.Release(); Object.DestroyImmediate(texture);
        }
    }

    private static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private sealed class RendererState
    {
        public Renderer renderer; public string path; public bool enabled, forceOff;
        public RendererState(Renderer value) { renderer = value; path = Hierarchy(value.transform); enabled = value.enabled; forceOff = value.forceRenderingOff; }
        public bool Changed => renderer.enabled != enabled || renderer.forceRenderingOff != forceOff;
        public VisibilityOverride Override() => new VisibilityOverride { path = path, originalEnabled = enabled,
            capturedEnabled = renderer.enabled, originalForceOff = forceOff, capturedForceOff = renderer.forceRenderingOff };
        public void Restore() { if (renderer != null) { renderer.enabled = enabled; renderer.forceRenderingOff = forceOff; } }
    }
    private sealed class BehaviourState
    {
        public Behaviour component; public string path; public bool enabled;
        public BehaviourState(Behaviour value) { component = value; path = Hierarchy(value.transform); enabled = value.enabled; }
        public void Restore() { if (component != null) component.enabled = enabled; }
    }
    [Serializable] private sealed class Receipt
    {
        public string status = "incomplete", utc, unity, graphics, scenePath, sceneSha256, sceneSha256After, helperPath, helperSha256, error;
        public string kind = "Orthographic Editor camera renders of saved scene geometry; not SceneView or game screenshots.";
        public string structuralLighting = "Existing lights disabled; ambient Flat RGB(.65,.65,.65); fog disabled; temporary white directional light intensity 1, Euler(50,-30,0), no shadows. Materials unchanged.";
        public string limits = "Diagnostic cutaway omits ceilings, presentation meshes, props, actors and gameplay gates. It demonstrates spatial layout only, not art, traversal, progression, timing or human acceptance. North/up is +Z; east/right is +X.";
        public bool sceneBytesUnchanged;
        public int structuralRenderers, initialSceneSetupCount;
        public string sceneCleanup;
        public float savedVisibleMaximumY;
        public string[] canvasOverrides, structuralLightOverrides;
        public Marker[] roomMarkers;
        public List<ImageReceipt> images = new List<ImageReceipt>();
    }
    [Serializable] private sealed class ImageReceipt
    {
        public string file, sha256, scope; public long bytes; public int width, height;
        public Vector3 position, euler; public float orthographicSize, aspect, near, far;
        public VisibilityOverride[] visibilityOverrides;
    }
    [Serializable] private sealed class VisibilityOverride
    { public string path; public bool originalEnabled, capturedEnabled, originalForceOff, capturedForceOff; }
    [Serializable] private sealed class Marker { public string path; public Vector3 position; }
}
