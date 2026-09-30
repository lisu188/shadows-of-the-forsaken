using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Encounters;
using ShadowsOfTheForsaken.Progression;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
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
    public const string NavigationPath = "Assets/LevelLayout/CastleNavigation.asset";

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
        follow.shoulderOffset = 1.4f;
        cameraObject.transform.position += player.transform.right * follow.shoulderOffset;
        cameraObject.transform.LookAt(data.spawn + Vector3.up + player.transform.right * follow.shoulderOffset);

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
        var gameplay = BuildGameplay(data, objects, materials, player, movement, follow, body.GetComponent<Renderer>());
        BakeNavigation(objects["Geometry"]);
        foreach (var agent in gameplay.GetComponentsInChildren<NavMeshAgent>(true))
        {
            if (!NavMesh.SamplePosition(agent.transform.position, out var point, .8f, agent.areaMask) ||
                Mathf.Abs(point.position.y - agent.transform.position.y) > .25f)
                throw new InvalidOperationException("Enemy spawn is not on its authored navigation floor: " + agent.name);
            agent.transform.position = point.position;
        }
        gameplay.SetActive(true); player.SetActive(true);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Unity did not save " + ScenePath);
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        scenes.Add(new EditorBuildSettingsScene(BaselinePath, true));
        scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != BaselinePath));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Saved playable castle scene and baked navigation. Authoring is not gameplay or visual acceptance.");
    }

    private static GameObject BuildGameplay(LayoutData data, Dictionary<string, GameObject> objects,
        Dictionary<string, Material> materials, GameObject player, PlayerMovement movement,
        CameraFollow follow, Renderer playerBody)
    {
        var gameplay = new GameObject("Gameplay"); gameplay.SetActive(false);
        var progression = Child("Level Progression", gameplay.transform).AddComponent<LevelProgressionController>();
        var session = Child("Level Session", gameplay.transform).AddComponent<LevelSessionController>();
        session.gameObject.AddComponent<PlayerValidationEvidence>().session = session;
        player.SetActive(false);
        var health = player.AddComponent<CombatHealth>(); health.progression = progression;
        health.maximumHealth = 200;
        player.AddComponent<MeleeCombat>().damage = 10; player.AddComponent<PlayerCombat>();
        var interactor = player.AddComponent<PlayerInteractor>(); interactor.progression = progression;
        player.AddComponent<CombatFeedback>().visuals = new[] { playerBody };
        var occlusion = follow.gameObject.AddComponent<CameraPlayerOcclusion>(); occlusion.playerVisuals = new[] { playerBody };
        session.progression = progression; session.player = movement; session.playerHealth = health; session.follow = follow;
        session.spawn = objects["Anchors"].transform.Find("Spawn");
        var hud = Child("Level HUD", gameplay.transform).AddComponent<LevelHUD>();
        hud.session = session; hud.progression = progression; hud.playerHealth = health; hud.interactor = interactor;

        foreach (var passage in data.passages)
        {
            var from = (LevelRoom)Enum.Parse(typeof(LevelRoom), passage.from);
            var to = (LevelRoom)Enum.Parse(typeof(LevelRoom), passage.to);
            var go = Child("Gate_" + from + "_" + to, gameplay.transform);
            go.transform.SetPositionAndRotation(passage.gatePosition, Quaternion.Euler(passage.gateRotation));
            var barrier = go.AddComponent<BoxCollider>(); barrier.center = new Vector3(0, 2.2f, 0); barrier.size = new Vector3(4, 4.4f, .35f);
            var visual = Visual("Gate panel", go.transform, new Vector3(0, 2.2f, 0), barrier.size, materials["Iron"]);
            var gate = go.AddComponent<ProgressionGate>(); gate.progression = progression; gate.from = from; gate.to = to;
            gate.closedVisuals = from == LevelRoom.FirstEncounter && to == LevelRoom.ThroneRoom
                ? new[] { visual }.Concat(RuneGlyph(go.transform, new Vector3(0, 2.2f, -.22f), materials["Rune"])).ToArray()
                : new[] { visual };
            var obstacle = go.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = barrier.center; obstacle.size = barrier.size; obstacle.carving = true; obstacle.carveOnlyStationary = false;
            bool open = new LevelProgression().IsPassageOpen(from, to);
            barrier.enabled = visual.enabled = obstacle.enabled = !open;
        }

        Room(gameplay.transform, session, LevelRoom.Courtyard, new Vector3(0, 1.5f, -40), new Vector3(7.9f, 3, 103.9f));
        Room(gameplay.transform, session, LevelRoom.FirstEncounter, new Vector3(0, 1.5f, 16), new Vector3(7.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.Puzzle, new Vector3(-8, 1.5f, 24), new Vector3(7.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.ThroneRoom, new Vector3(8, 1.5f, 32), new Vector3(7.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.Library, new Vector3(16, -.5f, 40), new Vector3(7.9f, 7, 7.9f));
        Room(gameplay.transform, session, LevelRoom.Catacombs, new Vector3(4, -2.5f, 48), new Vector3(31.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.BonusRoom, new Vector3(-8, -2.5f, 64), new Vector3(7.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.FinalArena, new Vector3(8, -2.5f, 64), new Vector3(7.9f, 3, 7.9f));
        Room(gameplay.transform, session, LevelRoom.Exit, new Vector3(8, -2.5f, 76), new Vector3(3.9f, 3, 2));

        Mechanism("Rune Lever", new Vector3(-10.5f, 1.45f, 25.5f), new Vector3(.22f, .7f, .22f), materials["Rune"],
            gameplay.transform, progression, hud, LevelObjective.MainPuzzleSolved, "Pull the marked rune lever",
            "The rune answers. The throne-room gate is open.", false, true, materials["DarkStone"]);
        Mechanism("Damaged Lever", new Vector3(-5.5f, 1.1f, 25.5f), new Vector3(.22f, .7f, .22f), materials["Iron"],
            gameplay.transform, progression, hud, LevelObjective.None, "Examine the damaged lever",
            "The lever is broken. Look for the glowing rune.", false, false);
        Mechanism("Marked Library Book", new Vector3(18.6f, -.45f, 40), new Vector3(.16f, .35f, .65f), materials["Rune"],
            gameplay.transform, progression, hud, LevelObjective.LibraryOpened, "Pull the marked book",
            "Stone shifts below. The catacomb passage is open.", false, true);
        Mechanism("Secret Lever", new Vector3(18.8f, -2.9f, 49.5f), new Vector3(.22f, .7f, .22f), materials["Rune"],
            gameplay.transform, progression, hud, LevelObjective.SecretLeverPulled, "Pull the concealed lever",
            "A hidden chamber opens. Its old passage returns to the throne room.", false, true);
        Mechanism("Forsaken Relic", new Vector3(-8, -3.05f, 66.5f), new Vector3(.5f, .4f, .5f), materials["BookGold"],
            gameplay.transform, progression, hud, LevelObjective.BonusDiscovered, "Recover the Forsaken relic",
            "Relic recovered: the last keeper guarded these halls even after the kingdom fell.", true, false);

        session.encounters = new[] {
            Enemy("First Demon", new Vector3(0, 0, 18), LevelRoom.FirstEncounter, LevelObjective.FirstEnemyDefeated,
                new Vector3(0, 0, 16), 50, 12, .55f, .12f, .8f, 2.8f, gameplay.transform, progression, health, materials["Blood"]),
            Enemy("Corrupted Guard", new Vector3(8, 0, 34), LevelRoom.ThroneRoom, LevelObjective.MinibossDefeated,
                new Vector3(8, 0, 32), 125, 22, .85f, .18f, 1, 2.2f, gameplay.transform, progression, health, materials["Iron"]),
            Enemy("Final Demon", new Vector3(8, -4, 66), LevelRoom.FinalArena, LevelObjective.FinalEnemyDefeated,
                new Vector3(8, -4, 64), 100, 18, .4f, .12f, .6f, 3.4f, gameplay.transform, progression, health, materials["Blood"])
        };
        return gameplay;
    }

    private static EnemyEncounter Enemy(string name, Vector3 feet, LevelRoom room, LevelObjective objective, Vector3 center,
        int maximumHealth, int damage, float windup, float active, float cooldown, float speed, Transform parent,
        LevelProgressionController progression, CombatHealth player, Material material)
    {
        var go = Child(name, parent); go.transform.position = feet; go.transform.rotation = Quaternion.Euler(0, 180, 0);
        var collider = go.AddComponent<CapsuleCollider>(); collider.radius = .4f; collider.height = 2; collider.center = Vector3.up;
        var health = go.AddComponent<CombatHealth>(); health.progression = progression; health.maximumHealth = maximumHealth;
        var melee = go.AddComponent<MeleeCombat>(); melee.damage = damage; melee.range = 1.8f; melee.arcDegrees = 80;
        melee.windup = windup; melee.activeWindow = active; melee.cooldown = cooldown;
        var nav = go.AddComponent<NavMeshAgent>(); nav.enabled = false; nav.radius = .4f; nav.height = 2; nav.baseOffset = 0;
        nav.speed = speed; nav.angularSpeed = 360; nav.stoppingDistance = 1.5f;
        var visual = Visual("Body", go.transform, Vector3.up, new Vector3(.8f, 1, .8f), material, PrimitiveType.Capsule);
        go.AddComponent<CombatFeedback>().visuals = new[] { visual };
        var enemy = go.AddComponent<EnemyEncounter>(); enemy.progression = progression; enemy.playerHealth = player;
        enemy.initializeNavigationOnStart = true;
        enemy.encounterRoom = room; enemy.objective = objective; enemy.arenaCenter = center;
        enemy.arenaHalfExtents = new Vector3(3.4f, 2, 3.4f); enemy.detectionRadius = 9; enemy.movementSpeed = speed;
        enemy.turnSpeed = 360; enemy.stoppingDistance = 1.5f;
        return enemy;
    }

    private static void Room(Transform parent, LevelSessionController session, LevelRoom room, Vector3 center, Vector3 size)
    {
        var go = Child("Region_" + room, parent); go.transform.position = center;
        var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = size;
        var trigger = go.AddComponent<LevelRoomTrigger>(); trigger.session = session; trigger.room = room;
    }

    private static void Mechanism(string name, Vector3 position, Vector3 size, Material material, Transform parent,
        LevelProgressionController progression, LevelHUD hud, LevelObjective objective, string prompt, string message, bool hide, bool moves,
        Material runeBacking = null)
    {
        var go = Child(name, parent); go.transform.position = position;
        var moving = Child("Moving mechanism", go.transform).transform;
        var visual = Visual("Marked part", moving, Vector3.zero, size, material);
        if (runeBacking != null)
        {
            go.transform.rotation = Quaternion.Euler(0, -90, 0); // Face the western room's central aisle.
            Visual("Rune plaque", moving, new Vector3(0, 0, -.12f), new Vector3(.65f, .85f, .08f), runeBacking);
            RuneGlyph(moving, new Vector3(0, 0, -.2f), material);
        }
        if (name == "Damaged Lever") visual.transform.localRotation = Quaternion.Euler(0, 0, 65);
        var focus = go.AddComponent<BoxCollider>(); focus.isTrigger = true; focus.size = size + Vector3.one * .15f;
        var target = go.AddComponent<InteractionTarget>(); target.progression = progression; target.objective = objective;
        target.focusCollider = focus; target.focusPoint = visual.transform; target.prompt = prompt;
        var feedback = go.AddComponent<MechanismFeedback>(); feedback.target = target; feedback.hud = hud;
        feedback.rewardVisuals = new[] { visual }; feedback.hideWhenConsumed = hide; feedback.activatedMessage = message;
        feedback.movingPart = moves ? moving : null; feedback.activatedEulerAngles = new Vector3(0, 0, 35);
    }

    private static Renderer[] RuneGlyph(Transform parent, Vector3 position, Material material)
    {
        var result = new Renderer[3];
        result[0] = Visual("Rune stem", parent, position, new Vector3(.09f, .65f, .04f), material);
        for (int index = 0; index < 2; index++)
        {
            float side = index == 0 ? -1 : 1;
            result[index + 1] = Visual("Rune branch " + index, parent, position + new Vector3(side * .13f, .18f, 0),
                new Vector3(.09f, .36f, .04f), material);
            result[index + 1].transform.localRotation = Quaternion.Euler(0, 0, -side * 45);
        }
        return result;
    }

    private static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
    }
    private static Renderer Visual(string name, Transform parent, Vector3 position, Vector3 size, Material material,
        PrimitiveType kind = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(kind); go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material; return renderer;
    }

    private static void BakeNavigation(GameObject geometry)
    {
        foreach (var collider in geometry.GetComponentsInChildren<Collider>())
            if (!collider.name.Contains("_Floor"))
            {
                var modifier = collider.gameObject.AddComponent<NavMeshModifier>(); modifier.overrideArea = true; modifier.area = 1;
            }
        var surface = geometry.AddComponent<NavMeshSurface>(); surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.Children; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true; surface.voxelSize = .1f;
        surface.overrideTileSize = true; surface.tileSize = 128;
        surface.BuildNavMesh();
        if (surface.navMeshData == null) throw new InvalidOperationException("Castle navigation bake produced no data.");
        var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavigationPath);
        if (existing == null) AssetDatabase.CreateAsset(surface.navMeshData, NavigationPath);
        else
        {
            var generated = surface.navMeshData; surface.RemoveData();
            EditorUtility.CopySerialized(generated, existing); surface.navMeshData = existing;
            UnityEngine.Object.DestroyImmediate(generated); surface.AddData(); EditorUtility.SetDirty(existing);
        }
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
        public string from, to, objectName;
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
