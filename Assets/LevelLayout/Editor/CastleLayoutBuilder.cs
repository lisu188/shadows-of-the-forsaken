using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Explicit authoring only: no import hook, InitializeOnLoad or runtime generation.
public static class CastleLayoutBuilder
{
    public const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
    public const string SourcePath = "Assets/LevelLayout/Editor/CastleLayout.json";
    private const string MaterialsPath = "Assets/LevelLayout/Materials";
    private const string BaselinePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Shadows/Level/Rebuild Castle Layout")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before authoring the layout.");
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("Rebuild castle layout",
                "Rebuild ForsakenCastle and its layout materials from CastleLayout.json? " +
                "Manual edits to those generated assets will be replaced. Other scenes are preserved.",
                "Rebuild", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }
        BuildForBatch();
    }

    // Invoke deliberately with -executeMethod CastleLayoutBuilder.BuildForBatch.
    public static void BuildForBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Cannot author a scene during Play Mode.");
        var data = JsonUtility.FromJson<LayoutData>(File.ReadAllText(SourcePath));
        if (data == null || data.version != 1 || data.gridSize != 8 || data.rooms == null ||
            data.passages == null || data.objects == null || data.materials == null)
            throw new InvalidDataException("Unsupported or incomplete castle layout source.");
        var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/FreeNightSky/Materials/nightsky1.mat");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (input == null || sky == null || shader == null)
            throw new InvalidOperationException("Import the pinned project packages and existing input/sky assets first.");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var materials = MakeMaterials(data.materials, shader);
        var objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var name in new[] { "Geometry", "Rooms", "Passages", "Anchors", "Lighting" })
            objects.Add(name, new GameObject(name));

        foreach (var item in data.objects)
        {
            if (objects.ContainsKey(item.name))
                throw new InvalidDataException("Duplicate layout object: " + item.name);
            GameObject go;
            if (item.kind == "Empty") go = new GameObject(item.name);
            else
            {
                if (item.kind != "Cube" && item.kind != "Cylinder")
                    throw new InvalidDataException("Unsupported primitive: " + item.kind);
                go = GameObject.CreatePrimitive(item.kind == "Cube" ? PrimitiveType.Cube : PrimitiveType.Cylinder);
                go.name = item.name;
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                if (item.collider)
                    go.AddComponent<BoxCollider>().size = item.kind == "Cylinder" ? new Vector3(1, 2, 1) : Vector3.one;
                go.GetComponent<Renderer>().sharedMaterial = materials[item.material];
            }
            objects.Add(item.name, go);
        }
        foreach (var item in data.objects)
        {
            var go = objects[item.name];
            if (!string.IsNullOrEmpty(item.parent)) go.transform.SetParent(objects[item.parent].transform, false);
            go.transform.SetPositionAndRotation(item.position, Quaternion.Euler(item.rotation));
            go.transform.localScale = item.scale;
        }

        foreach (var room in data.rooms)
            objects.Add(room.objectName, Marker(room.objectName, objects["Rooms"].transform, room.position).gameObject);
        foreach (var passage in data.passages)
        {
            var parent = Marker(passage.objectName, objects["Passages"].transform, Vector3.zero);
            for (var i = 0; i < passage.waypoints.Length; i++)
                Marker("Waypoint_" + i.ToString("D2"), parent, passage.waypoints[i]);
            var gate = Marker("GateAnchor", parent, passage.gatePosition);
            gate.rotation = Quaternion.Euler(passage.gateRotation);
        }
        foreach (var anchor in data.anchors)
            Marker(anchor.name, objects[string.IsNullOrEmpty(anchor.parent) ? "Anchors" : anchor.parent].transform,
                anchor.position);

        var player = new GameObject("Player") { tag = "Player" };
        player.transform.position = data.spawn;
        var controller = player.AddComponent<CharacterController>();
        controller.height = 2;
        controller.radius = 0.3f;
        controller.center = Vector3.up;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.02f;
        controller.slopeLimit = 45;
        var movement = player.AddComponent<PlayerMovement>();
        movement.inputActions = input;
        movement.rotationSpeed = 120;
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Player Body";
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = Vector3.up;
        body.transform.localScale = new Vector3(0.6f, 1, 0.6f);
        body.GetComponent<Renderer>().sharedMaterial = materials[materials.ContainsKey("Player") ? "Player" : "Stone"];

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        cameraObject.transform.position = data.spawn + new Vector3(0, 2, -5);
        cameraObject.transform.LookAt(data.spawn + Vector3.up);
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000;
        camera.fieldOfView = 60;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var follow = cameraObject.AddComponent<CameraFollow>();
        follow.player = player.transform;
        follow.findTaggedPlayer = false;
        follow.obstructionMask = ~0;

        var moon = new GameObject("Directional Light");
        moon.transform.SetParent(objects["Lighting"].transform, false);
        moon.transform.position = new Vector3(0, 3, 0);
        moon.transform.rotation = Quaternion.Euler(50, -30, 0);
        var moonlight = moon.AddComponent<Light>();
        moonlight.type = LightType.Directional;
        moonlight.color = new Color(0.72f, 0.8f, 1);
        moonlight.intensity = 1;
        moonlight.shadows = LightShadows.Soft;
        moon.AddComponent<UniversalAdditionalLightData>();
        foreach (var item in data.lights)
        {
            var lamp = Marker(item.name, objects["Lighting"].transform, item.position).gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = item.color;
            lamp.intensity = item.intensity;
            lamp.range = item.range;
            lamp.shadows = LightShadows.None;
        }
        RenderSettings.skybox = sky;
        RenderSettings.sun = moonlight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.21f, 0.28f);
        RenderSettings.fog = false;
        new GameObject("Layout Preview").AddComponent<CastleLayoutPreview>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Unity did not save " + ScenePath);
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        scenes.Add(new EditorBuildSettingsScene(BaselinePath, true));
        scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != BaselinePath));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Saved walkable castle layout. This is authoring, not a PlayMode or visual acceptance result.");
    }

    private static Transform Marker(string name, Transform parent, Vector3 position)
    {
        var marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.position = position;
        return marker;
    }

    private static Dictionary<string, Material> MakeMaterials(MaterialData[] source, Shader shader)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsPath)) AssetDatabase.CreateFolder("Assets/LevelLayout", "Materials");
        var result = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            if (string.IsNullOrEmpty(item.name) || item.name.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
                throw new InvalidDataException("Invalid layout material name.");
            var path = MaterialsPath + "/" + item.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", item.color);
            material.SetColor("_Color", item.color);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0);
            material.SetColor("_EmissionColor", item.emission);
            if (Mathf.Max(item.emission.r, Mathf.Max(item.emission.g, item.emission.b)) > 0)
                material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            result.Add(item.name, material);
        }
        return result;
    }

    [Serializable] private sealed class LayoutData
    {
        public int version;
        public float gridSize;
        public Vector3 spawn;
        public MaterialData[] materials;
        public ObjectData[] objects;
        public RoomData[] rooms;
        public PassageData[] passages;
        public AnchorData[] anchors;
        public LightData[] lights;
    }
    [Serializable] private sealed class MaterialData
    {
        public string name;
        public Color color, emission;
    }
    [Serializable] private sealed class ObjectData
    {
        public string name, parent, kind, material;
        public Vector3 position, rotation, scale;
        public bool collider;
    }
    [Serializable] private sealed class RoomData
    {
        public string objectName;
        public Vector3 position;
    }
    [Serializable] private sealed class PassageData
    {
        public string objectName;
        public Vector3[] waypoints;
        public Vector3 gatePosition, gateRotation;
    }
    [Serializable] private sealed class AnchorData
    {
        public string name, parent;
        public Vector3 position;
    }
    [Serializable] private sealed class LightData
    {
        public string name;
        public Vector3 position;
        public Color color;
        public float intensity, range;
    }
}
