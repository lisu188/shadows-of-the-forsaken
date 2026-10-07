using System;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Deliberate test-fixture authoring only. Never executes on import or play.
public static class CombatDemoBuilder
{
    public const string ScenePath = "Assets/Combat/Demo/CombatDemo.unity";

    [MenuItem("Shadows/Demos/Create Combat Arena")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildForBatch();
    }

    public static void BuildForBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (input == null || shader == null) throw new InvalidOperationException("Import the pinned Input System and URP first.");
        if (!AssetDatabase.IsValidFolder("Assets/Combat/Demo")) AssetDatabase.CreateFolder("Assets/Combat", "Demo");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var stone = Material("Stone", shader, new Color(.24f, .27f, .30f));
        var gold = Material("Player", shader, new Color(.65f, .48f, .25f));
        var red = Material("Target", shader, new Color(.50f, .12f, .12f));
        Cube("Floor", new Vector3(0, -.25f, 0), new Vector3(16, .5f, 16), stone);
        Cube("Far wall", new Vector3(0, 1.5f, 8), new Vector3(16, 3, .5f), stone);
        Cube("LOS pillar", new Vector3(3, 1.25f, 1), new Vector3(1, 2.5f, 1), stone);
        var progression = new GameObject("Scene session").AddComponent<LevelProgressionController>();
        var player = Actor("Player", new Vector3(0, .05f, -1.5f), gold, progression, input);
        var target = Actor("Target", new Vector3(0, 0, 2), red, progression, null);
        target.transform.rotation = Quaternion.Euler(0, 180, 0);
        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        var follow = cameraObject.AddComponent<CameraFollow>();
        follow.player = player.transform; follow.findTaggedPlayer = false;
        follow.height = 8; // See both actors over the player's silhouette at melee range.
        Vector3 cameraPosition = player.transform.position - player.transform.forward * follow.distance + Vector3.up * follow.height;
        cameraObject.transform.SetPositionAndRotation(cameraPosition,
            Quaternion.LookRotation(player.transform.position + Vector3.up * follow.pivotHeight - cameraPosition));
        var sun = new GameObject("Arena light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50, -25, 0);
        sun.gameObject.AddComponent<UniversalAdditionalLightData>();
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.35f, .38f, .45f);
        var hud = new GameObject("Arena controls").AddComponent<CombatDemoHUD>();
        hud.player = player; hud.target = target; hud.progression = progression;
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save combat demo.");
        AssetDatabase.SaveAssets();
        Debug.Log("Saved combat test arena at " + ScenePath + ". No level progression objectives were completed.");
    }

    private static CombatHealth Actor(string name, Vector3 position, Material material,
        LevelProgressionController progression, InputActionAsset input)
    {
        var go = new GameObject(name); go.SetActive(false); go.transform.position = position;
        var health = go.AddComponent<CombatHealth>(); health.progression = progression;
        if (input != null)
        {
            go.tag = "Player";
            var character = go.AddComponent<CharacterController>();
            character.height = 2; character.radius = .3f; character.center = Vector3.up;
            character.skinWidth = .02f; character.stepOffset = .3f;
            var movement = go.AddComponent<PlayerMovement>(); movement.inputActions = input; movement.rotationSpeed = 120;
        }
        else
        {
            var collider = go.AddComponent<BoxCollider>(); collider.center = Vector3.up; collider.size = new Vector3(.8f, 2, .8f);
        }
        go.AddComponent<MeleeCombat>();
        if (input != null) go.AddComponent<PlayerCombat>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = name + " body";
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false); body.transform.localPosition = Vector3.up;
        body.transform.localScale = new Vector3(.7f, 1, .7f); body.GetComponent<Renderer>().sharedMaterial = material;
        go.AddComponent<CombatFeedback>(); go.SetActive(true);
        return health;
    }
    private static void Cube(string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
    }
    private static Material Material(string name, Shader shader, Color color)
    {
        string path = "Assets/Combat/Demo/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetColor("_Color", color); material.SetFloat("_Smoothness", .15f);
        EditorUtility.SetDirty(material); return material;
    }
}
