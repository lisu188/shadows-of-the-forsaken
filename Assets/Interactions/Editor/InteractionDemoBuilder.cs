using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class InteractionDemoBuilder
{
    public const string ScenePath = "Assets/Interactions/Demo/InteractionDemo.unity";

    [MenuItem("Shadows/Mechanics/Create Interaction Demo")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildForBatch();
    }

    // Explicit authoring only. Does not modify build settings or existing scenes.
    public static void BuildForBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before creating the demo.");
        if (!AssetDatabase.IsValidFolder("Assets/Interactions/Demo")) AssetDatabase.CreateFolder("Assets/Interactions", "Demo");
        var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (input == null || shader == null) throw new System.InvalidOperationException("Import the pinned input and URP packages first.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var stone = Material("Stone", new Color(0.25f, 0.29f, 0.34f), shader);
        var gold = Material("Lever", new Color(0.9f, 0.62f, 0.16f), shader);
        var red = Material("BlockedRune", new Color(0.65f, 0.12f, 0.12f), shader);
        var blue = Material("Gate", new Color(0.18f, 0.4f, 0.65f), shader);
        Box("Floor", new Vector3(0, -0.25f, 0), new Vector3(12, 0.5f, 16), stone);
        Box("Left boundary", new Vector3(-4, 2, 0), new Vector3(0.4f, 4, 12), stone);
        Box("Right boundary", new Vector3(4, 2, 0), new Vector3(0.4f, 4, 12), stone);
        Box("Back boundary", new Vector3(0, 2, -6), new Vector3(8, 4, 0.4f), stone);
        Box("Far boundary", new Vector3(0, 2, 6), new Vector3(8, 4, 0.4f), stone);
        Box("Gate left wall", new Vector3(-2.8f, 2, 1), new Vector3(2.4f, 4, 0.4f), stone);
        Box("Gate right wall", new Vector3(2.8f, 2, 1), new Vector3(2.4f, 4, 0.4f), stone);
        var progression = new GameObject("Demo Session").AddComponent<LevelProgressionController>();
        var spawn = new GameObject("Demo Spawn").transform;
        spawn.position = new Vector3(0, 0.05f, -3);
        var playerObject = new GameObject("Player") { tag = "Player" };
        playerObject.SetActive(false); playerObject.transform.position = spawn.position;
        var movement = playerObject.AddComponent<PlayerMovement>();
        movement.inputActions = input; movement.rotationSpeed = 120;
        var character = playerObject.GetComponent<CharacterController>();
        character.height = 2; character.radius = 0.3f; character.center = Vector3.up; character.skinWidth = 0.02f;
        var interactor = playerObject.AddComponent<PlayerInteractor>(); interactor.progression = progression;
        var body = Box("Player body", Vector3.zero, new Vector3(0.5f, 1.6f, 0.5f), blue);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(playerObject.transform, false); body.transform.localPosition = Vector3.up;
        playerObject.SetActive(true);
        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<AudioListener>();
        var follow = cameraObject.AddComponent<CameraFollow>(); follow.player = playerObject.transform;
        cameraObject.transform.position = spawn.position + new Vector3(0, 2, -5);
        cameraObject.transform.LookAt(spawn.position + Vector3.up);
        var light = new GameObject("Demo Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);

        var gateObject = Box("Progression Gate", new Vector3(0, 2, 1), new Vector3(3.2f, 4, 0.4f), blue);
        gateObject.SetActive(false);
        var gate = gateObject.AddComponent<ProgressionGate>(); gate.progression = progression;
        gate.from = LevelRoom.FirstEncounter; gate.to = LevelRoom.ThroneRoom;
        gate.closedVisuals = new[] { gateObject.GetComponent<Renderer>() }; gateObject.SetActive(true);
        var lever = Box("Gold lever", new Vector3(-1.2f, 1, -1.5f), new Vector3(0.3f, 1, 0.3f), gold);
        lever.SetActive(false);
        var target = lever.AddComponent<InteractionTarget>(); target.progression = progression;
        target.objective = LevelObjective.MainPuzzleSolved; target.prompt = "Use gold lever"; lever.SetActive(true);
        Box("Rune obstruction", new Vector3(2.5f, 1.5f, -0.8f), new Vector3(1.5f, 3, 0.3f), stone);
        var rune = Box("Blocked red rune", new Vector3(2.5f, 1, -0.3f), new Vector3(0.3f, 0.5f, 0.3f), red);
        rune.SetActive(false);
        var runeTarget = rune.AddComponent<InteractionTarget>(); runeTarget.progression = progression;
        runeTarget.prompt = "Use red rune"; rune.SetActive(true);
        var demo = progression.gameObject.AddComponent<InteractionDemoSession>();
        demo.progression = progression; demo.player = movement; demo.interactor = interactor;
        demo.follow = follow; demo.gate = gate; demo.spawn = spawn;
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Created interaction test fixture: " + ScenePath);
    }

    private static Material Material(string name, Color color, Shader shader)
    {
        string path = "Assets/Interactions/Demo/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        EditorUtility.SetDirty(material);
        return material;
    }
    private static GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
}
