using System;
using System.Collections.Generic;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Encounters;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Deliberate authoring API; no import callbacks or runtime mesh generation.
public static class CastleActorBuilder
{
    private const string VisualName = "Castle Actor Visuals";
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    public static void Build(GameObject player, EnemyEncounter[] encounters)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Author actors outside Play Mode.");
        if (player == null || encounters == null) throw new ArgumentNullException("Assign the castle player and encounters.");
        CastleActorMeshes.Begin();
        MakeMaterials();
        BuildActor(player, 0);
        foreach (var encounter in encounters)
        {
            if (encounter == null) continue;
            int kind = encounter.objective == LevelObjective.MinibossDefeated ? 2 :
                encounter.objective == LevelObjective.FinalEnemyDefeated ? 3 : 1;
            BuildActor(encounter.gameObject, kind);
        }
        foreach (var root in player.scene.GetRootGameObjects())
            foreach (var occlusion in root.GetComponentsInChildren<CameraPlayerOcclusion>(true))
                if (occlusion.GetComponent<CameraFollow>().player == player.transform)
                    occlusion.playerVisuals = player.transform.Find(VisualName).GetComponentsInChildren<Renderer>(true);
        AssetDatabase.SaveAssets();
    }

    private static void BuildActor(GameObject actor, int kind)
    {
        if (actor.GetComponent<CombatHealth>() == null || actor.GetComponent<MeleeCombat>() == null ||
            actor.GetComponent<CombatFeedback>() == null)
            throw new InvalidOperationException("Presentation requires the existing combat components: " + actor.name);
        var previous = actor.transform.Find(VisualName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var oldAnimator = actor.GetComponent<CastleActorPresentation>();
        if (oldAnimator != null) UnityEngine.Object.DestroyImmediate(oldAnimator);
        foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        Transform root = Joint(VisualName, actor.transform, Vector3.zero);
        Transform motion = Joint("Motion", root, Vector3.zero);
        bool demon = kind == 1 || kind == 3;
        bool final = kind == 3, guard = kind == 2;
        string metal = guard ? "Blackened iron" : "Worn silver";
        string body = demon ? final ? "Obsidian hide" : "Demon hide" : metal;
        string cloth = guard ? "Oxblood cloth" : "Midnight cloak";
        Transform hips = Joint("Hips", motion, new Vector3(0, .91f, 0));
        Transform torso = Joint("Torso", hips, new Vector3(0, .09f, 0));
        Part("Pelvis", hips, "Torso", new Vector3(0, .04f, 0), new Vector3(.45f, .3f, .31f), body);
        Part("Breastplate", torso, "Torso", new Vector3(0, .29f, 0), new Vector3(guard ? .7f : .56f, .61f, demon ? .34f : .38f), body);
        Transform head = Joint("Head", torso, new Vector3(0, demon ? .68f : .73f, .01f));
        Part(demon ? "Demon skull" : "Closed helmet", head, "Head", new Vector3(0, .06f, 0), new Vector3(.34f, .38f, .35f), body);
        if (demon)
        {
            Horns(head, final);
            Part("Bone brow", head, "Band", new Vector3(0, .08f, .158f), new Vector3(.3f, .07f, .06f), "Ancient ivory");
            Part("Pointed jaw", head, "Crest", new Vector3(0, -.065f, .13f), new Vector3(.19f, .24f, .12f), "Ancient ivory");
            for (int side = -1; side <= 1; side += 2)
            {
                Part("Burning eye", head, "Crest", new Vector3(side * .078f, .072f, .194f), new Vector3(.073f, .032f, .035f), final ? "Cursed violet" : "Ember eyes");
                for (int rib = 0; rib < 3; rib++)
                    Part("Exposed rib", torso, "Band", new Vector3(side * .13f, .36f - rib * .085f, .167f),
                        new Vector3(.2f, .038f, .025f), "Ancient ivory", new Vector3(0, 0, side * 16));
            }
            if (final)
                for (int i = 0; i < 4; i++)
                    Part("Dorsal spine", torso, "HornRight", new Vector3(0, .51f - i * .14f, -.16f),
                        Vector3.one * (.48f - i * .065f), "Ancient ivory", new Vector3(-55, 0, 0));
        }
        else
        {
            Part("Visor darkness", head, "Band", new Vector3(0, .047f, .166f), new Vector3(.27f, .062f, .022f), "Deep shadow");
            Part("Visor central ridge", head, "Band", new Vector3(0, .035f, .18f), new Vector3(.028f, .2f, .022f), "Antique brass");
            Part("Chest crest", torso, "Crest", new Vector3(0, .35f, .195f), new Vector3(.19f, .27f, .09f), guard ? "Cursed violet" : "Antique brass");
            Part("Gorget", torso, "Plate", new Vector3(0, .61f, 0), new Vector3(.39f, .11f, .38f), "Antique brass");
            Part("Waist belt", hips, "Band", new Vector3(0, .01f, .018f), new Vector3(.47f, .09f, .35f), "Leather");
            Part("Belt clasp", hips, "Crest", new Vector3(0, .01f, .202f), new Vector3(.1f, .09f, .07f), "Antique brass");
            Part("Split surcoat", hips, "Tabard", new Vector3(0, .01f, .18f), new Vector3(.38f, .62f, .6f), cloth);
            if (guard)
            {
                Part("Crown crest", head, "Blade", new Vector3(0, .22f, -.04f), new Vector3(.13f, .26f, .15f), "Antique brass");
                for (int side = -1; side <= 1; side += 2)
                    Part("Crown shard", head, side < 0 ? "HornLeft" : "HornRight", new Vector3(side * .16f, .14f, -.04f), Vector3.one * .5f, "Blackened iron");
            }
            else Part("Helmet crest", head, "Plate", new Vector3(0, .26f, -.04f), new Vector3(.09f, .16f, .26f), "Antique brass");
        }

        Transform leftArm = null, rightArm = null, leftForearm = null, rightForearm = null;
        Transform leftLeg = null, rightLeg = null, leftShin = null, rightShin = null;
        for (int side = -1; side <= 1; side += 2)
        {
            string label = side < 0 ? "Left" : "Right";
            Transform arm = Joint(label + " shoulder", torso, new Vector3(side * (guard ? .36f : .3f), .54f, 0));
            Part(label + " upper arm", arm, "Limb", Vector3.zero, new Vector3(demon ? .22f : .25f, .34f, .24f), body);
            Part(label + " pauldron", arm, "Plate", new Vector3(side * .018f, -.025f, 0),
                new Vector3(guard ? .37f : .27f, guard ? .26f : .19f, .34f), demon ? "Ancient ivory" : metal);
            if (demon || guard)
                Part(label + " shoulder spike", arm, side < 0 ? "HornLeft" : "HornRight", new Vector3(side * .06f, .025f, -.05f),
                    Vector3.one * (final ? .7f : .38f), demon ? "Ancient ivory" : "Antique brass", new Vector3(0, 0, -side * 30));
            Transform forearm = Joint(label + " elbow", arm, new Vector3(0, -.34f, 0));
            Part(label + " forearm", forearm, "Limb", Vector3.zero, new Vector3(.2f, demon ? .37f : .31f, .21f), body);
            Part(label + " wrist band", forearm, "Plate", new Vector3(0, -.21f, 0), new Vector3(.21f, .11f, .22f), demon ? "Demon hide" : "Antique brass");
            Transform hand = Joint(label + " hand", forearm, new Vector3(0, demon ? -.37f : -.31f, .005f));
            Part(label + " gauntlet", hand, "Head", new Vector3(0, -.035f, 0), new Vector3(.17f, .17f, .17f), body);
            if (demon)
                for (int finger = 0; finger < 3; finger++)
                    Part(label + " talon " + finger, hand, "Claw", new Vector3((finger - 1) * .058f, -.05f, .03f),
                        Vector3.one * (final ? 1.18f : 1), "Ancient ivory");
            if (side > 0 && !demon) Weapon(hand, guard);

            Transform leg = Joint(label + " hip", hips, new Vector3(side * .145f, -.02f, 0));
            Part(label + " thigh", leg, "Limb", Vector3.zero, new Vector3(.26f, .41f, .28f), demon ? body : "Leather");
            Part(label + " tasset", leg, "Plate", new Vector3(0, -.13f, .12f), new Vector3(.25f, .34f, .13f), body);
            Transform shin = Joint(label + " knee", leg, new Vector3(0, -.41f, 0));
            Part(label + " knee guard", shin, "Plate", new Vector3(0, .012f, .095f), new Vector3(.2f, .18f, .14f), demon ? "Ancient ivory" : metal);
            Part(label + " shin", shin, "Limb", Vector3.zero, new Vector3(.21f, .37f, .22f), body);
            Part(label + " boot", shin, "Boot", new Vector3(0, -.47f, .035f), new Vector3(.23f, .15f, .32f), demon ? "Ancient ivory" : "Blackened iron");
            if (side < 0) { leftArm = arm; leftForearm = forearm; leftLeg = leg; leftShin = shin; }
            else { rightArm = arm; rightForearm = forearm; rightLeg = leg; rightShin = shin; }
        }
        Transform cloak = null;
        if (!demon)
        {
            cloak = Joint("Cloak hinge", torso, new Vector3(0, .55f, -.19f));
            Part("Tattered cloak", cloak, "Cape", Vector3.zero, new Vector3(guard ? .88f : .7f, 1.26f, 1), cloth);
            Part("Cloak clasp", torso, "Crest", new Vector3(-.22f, .5f, .17f), new Vector3(.085f, .1f, .07f), "Antique brass");
        }
        if (final) motion.localScale = new Vector3(1.08f, 1.08f, 1.08f);
        var presentation = actor.AddComponent<CastleActorPresentation>();
        presentation.motionRoot = motion; presentation.torso = torso; presentation.head = head;
        presentation.leftArm = leftArm; presentation.rightArm = rightArm;
        presentation.leftForearm = leftForearm; presentation.rightForearm = rightForearm;
        presentation.leftLeg = leftLeg; presentation.rightLeg = rightLeg;
        presentation.leftShin = leftShin; presentation.rightShin = rightShin;
        presentation.cloak = cloak; presentation.demonic = demon; presentation.strideRate = final ? 11 : guard ? 6 : 8;
        var feedback = actor.GetComponent<CombatFeedback>();
        if (feedback == null) throw new InvalidOperationException("Actor needs its existing CombatFeedback: " + actor.name);
        feedback.visuals = root.GetComponentsInChildren<Renderer>(true);
        if (kind == 0)
        {
            // Keep the knight distinct when both combatants prepare a swing.
            // Enemy telegraphs retain their existing warm amber/red palette.
            feedback.windupColor = new Color(.48f, .68f, .88f);
            feedback.activeColor = new Color(.72f, .9f, 1f);
        }
    }

    private static void Horns(Transform head, bool final)
    {
        foreach (int side in new[] { -1, 1 })
            Part("Swept horn", head, side < 0 ? "HornLeft" : "HornRight", new Vector3(side * .145f, .15f, -.025f),
                Vector3.one * (final ? .9f : .65f), "Ancient ivory", new Vector3(final ? -15 : 0, 0, -side * (final ? 20 : 0)));
    }

    private static void Weapon(Transform hand, bool guard)
    {
        Transform sword = Joint(guard ? "Executioner cleaver" : "Forsaken longsword", hand, new Vector3(0, -.01f, .07f));
        sword.localRotation = Quaternion.Euler(140, 0, 0);
        Part("Wrapped grip", sword, "Band", new Vector3(0, .015f, 0), new Vector3(.055f, .23f, .055f), "Leather");
        Part("Pommel", sword, "Head", new Vector3(0, -.12f, 0), Vector3.one * .09f, "Antique brass");
        Part("Crossguard", sword, "Band", new Vector3(0, .13f, 0), new Vector3(guard ? .3f : .27f, .055f, .085f), "Antique brass");
        Part("Forged blade", sword, guard ? "Cleaver" : "Blade", new Vector3(0, .15f, 0),
            guard ? new Vector3(.75f, .73f, 1) : new Vector3(.12f, .79f, .11f), "Blade steel");
    }

    private static Transform Joint(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        return go.transform;
    }

    private static Renderer Part(string name, Transform parent, string mesh, Vector3 position, Vector3 scale, string material, Vector3 angles = default)
    {
        var part = Joint(name, parent, position);
        part.localScale = scale; part.localRotation = Quaternion.Euler(angles);
        part.gameObject.AddComponent<MeshFilter>().sharedMesh = CastleActorMeshes.Get(mesh);
        var renderer = part.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = materials[material]; renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return renderer;
    }

    private static void MakeMaterials()
    {
        materials.Clear();
        Material("Worn silver", new Color(.39f, .47f, .52f), .7f, .35f);
        Material("Blackened iron", new Color(.12f, .15f, .18f), .8f, .3f);
        Material("Blade steel", new Color(.66f, .73f, .76f), .85f, .55f);
        Material("Antique brass", new Color(.55f, .35f, .12f), .7f, .3f);
        Material("Midnight cloak", new Color(.065f, .115f, .18f), 0, .15f);
        Material("Oxblood cloth", new Color(.25f, .028f, .045f), 0, .15f);
        Material("Leather", new Color(.09f, .065f, .052f), 0, .2f);
        Material("Deep shadow", new Color(.008f, .012f, .015f), 0, .1f);
        Material("Demon hide", new Color(.35f, .065f, .055f), .1f, .28f);
        Material("Obsidian hide", new Color(.12f, .045f, .14f), .25f, .35f);
        Material("Ancient ivory", new Color(.62f, .52f, .35f), .05f, .2f);
        Material("Ember eyes", new Color(1, .23f, .025f), 0, .5f, new Color(2, .3f, .015f));
        Material("Cursed violet", new Color(.6f, .12f, .85f), .2f, .4f, new Color(.55f, .025f, .8f));
    }

    private static void Material(string name, Color color, float metallic, float smoothness, Color emission = default)
    {
        string path = CastleActorMeshes.Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Import the pinned URP shader first.");
        if (material == null) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        material.SetColor("_BaseColor", color); material.SetColor("_Color", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_EmissionColor", emission);
        // Pinned URP derives the emission keyword from AnyEmissive on import.
        // Keep existing emission policy, or use the baked flag; this does not bake lighting.
        var illumination = material.globalIlluminationFlags;
        if (emission.maxColorComponent > 0)
        {
            illumination &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if ((illumination & MaterialGlobalIlluminationFlags.AnyEmissive) == 0)
                illumination |= MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
        }
        else
        {
            illumination = (illumination & ~MaterialGlobalIlluminationFlags.AnyEmissive) | MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.DisableKeyword("_EMISSION");
        }
        material.globalIlluminationFlags = illumination;
        EditorUtility.SetDirty(material); materials.Add(name, material);
    }
}
