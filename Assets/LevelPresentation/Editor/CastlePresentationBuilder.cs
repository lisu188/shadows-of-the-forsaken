using System;
using System.Collections.Generic;
using System.Linq;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// DOCX sections 2, 4 and 5: saved, original gothic architecture. This explicit
// authoring pass never changes a collider, route, NavMesh input or actor.
public static class CastlePresentationBuilder
{
    public const string AssetPath = "Assets/LevelPresentation/Environment";
    public const int TriangleBudget = 180000;
    public const int RendererBudget = 180;

    public struct BuildResult
    {
        public int triangles, renderers, meshAssets;
    }

    public static BuildResult Build(GameObject geometry, GameObject gameplay, GameObject lighting)
    {
        if (Application.isPlaying || geometry == null || gameplay == null || lighting == null)
            throw new InvalidOperationException("Gothic presentation requires the authored scene roots outside Play Mode.");
        if (geometry.scene.GetRootGameObjects().Any(o => o.name == "Presentation"))
            throw new InvalidOperationException("Rebuild the castle before creating its presentation again.");
        Folder(AssetPath);
        var context = new Context(new GameObject("Presentation").transform);
        context.Materials();
        context.Architecture(geometry);
        context.Exterior();
        context.Interiors();
        context.Gates(gameplay);
        context.Mechanisms(gameplay);
        context.Illumination(lighting);
        var result = context.Save();
        if (result.triangles > TriangleBudget || result.renderers > RendererBudget)
            throw new InvalidOperationException($"Presentation exceeds budget: {result.triangles} triangles, {result.renderers} renderers.");
        AssetDatabase.SaveAssets();
        Debug.Log($"Saved gothic environment: {result.triangles} triangles, {result.renderers} renderers, {result.meshAssets} mesh assets; physics preserved.");
        return result;
    }

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        Folder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }

    private sealed class Batch
    {
        public readonly Draft draft = new Draft();
        public string name;
        public Material material;
        public Transform parent;
        public bool visible = true;
        public Action<Renderer> bind;
    }

    private sealed class Context
    {
        private readonly Transform root;
        private readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly List<Action> afterSave = new List<Action>();
        public Context(Transform root) { this.root = root; }

        private Batch Get(string zone, string material, Transform parent = null)
        {
            string key = zone + "_" + material;
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new Batch { name = key, material = materials[material], parent = parent != null ? parent : root };
                batches.Add(key, batch);
            }
            return batch;
        }
        private Draft D(string zone, string material) => Get(zone, material).draft;
        private static string Zone(Vector3 p) => "Masonry_" + Mathf.FloorToInt(p.x / 24) + "_" + Mathf.FloorToInt(p.z / 24) + "_" + (p.y < -6 ? "Deep" : "Upper");

        public void Materials()
        {
            var stone = Texture("Assets/LevelPresentation/Textures/WeatheredLimestone.png");
            var floor = Texture("Assets/LevelPresentation/Textures/WornFlagstone.png");
            Material("Limestone", new Color(.65f, .67f, .69f), .2f, 0, stone);
            Material("Flagstone", new Color(.64f, .65f, .67f), .3f, 0, floor);
            Material("CarvedStone", new Color(.38f, .40f, .43f), .2f, 0, stone);
            Material("DarkStone", new Color(.18f, .21f, .25f), .18f, 0, stone);
            Material("Iron", new Color(.095f, .12f, .15f), .38f, .65f);
            Material("Bronze", new Color(.39f, .25f, .095f), .45f, .65f);
            Material("Oak", new Color(.16f, .065f, .032f), .17f, 0);
            Material("Pages", new Color(.49f, .43f, .31f), .12f, 0);
            Material("LeatherRed", new Color(.26f, .035f, .05f), .24f, 0);
            Material("LeatherGreen", new Color(.055f, .15f, .13f), .24f, 0);
            Material("Bone", new Color(.43f, .42f, .34f), .12f, 0);
            Material("Blood", new Color(.18f, .012f, .019f), .62f, 0);
            Material("Wax", new Color(.68f, .55f, .32f), .2f, 0);
            Material("Flame", new Color(1, .51f, .12f), .1f, 0, null, new Color(4, 1.6f, .22f));
            Material("Rune", new Color(.12f, .64f, .72f), .4f, .15f, null, new Color(.16f, 1.5f, 1.8f));
            Material("GlassBlue", new Color(.12f, .22f, .36f), .65f, .2f, null, new Color(.12f, .24f, .43f));
            Material("GlassWine", new Color(.27f, .08f, .15f), .65f, .2f, null, new Color(.28f, .08f, .17f));
            Material("Mountain", new Color(.075f, .095f, .13f), .05f, 0, stone);
        }

        private static Texture2D Texture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Import the original presentation albedo first: " + path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.maxTextureSize != 1024 || importer.wrapMode != TextureWrapMode.Repeat || !importer.mipmapEnabled || !importer.sRGBTexture)
            {
                importer.maxTextureSize = 1024; importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true; importer.sRGBTexture = true; importer.SaveAndReimport();
            }
            return texture;
        }

        private void Material(string name, Color color, float smoothness, float metallic, Texture texture = null, Color emission = default)
        {
            string path = AssetPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name; material.enableInstancing = true;
            material.SetColor("_BaseColor", color); material.SetColor("_Color", color);
            material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetFloat("_Smoothness", smoothness); material.SetFloat("_Metallic", metallic);
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

        public void Architecture(GameObject geometry)
        {
            foreach (var renderer in geometry.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = renderer.name;
                var t = renderer.transform;
                Vector3 size = t.lossyScale;
                if (name.Contains("_Flame") || name.StartsWith("Approach_Torch") || name.Contains("_PointedArch") ||
                    name.Contains("_Book_") || name == "SecretLever_Handle" || name == "Puzzle_RuneFace")
                { renderer.enabled = false; continue; }
                if (name.Contains("WindowGlass"))
                {
                    Window(name, t.position + Vector3.down * 1.1f, 1.4f, 2.15f);
                    renderer.enabled = false; continue;
                }
                if (name.Contains("_Shelf_") && !name.Contains("Plinth"))
                { Shelf(name, t.position, size); renderer.enabled = false; continue; }
                if (name.Contains("Sarcophagus"))
                { Tomb(name, t.position, size); renderer.enabled = false; continue; }
                if (name.StartsWith("Throne_Column_") && !name.EndsWith("Base"))
                {
                    Column(D("Throne", "CarvedStone"), t.position - Vector3.up * size.y / 2, size.y, size.x / 2);
                    renderer.enabled = false; continue;
                }
                if (name.Contains("_Ceiling"))
                {
                    Quaternion rotation = t.rotation;
                    float width = size.x, length = size.z;
                    if (width > length) { float swap = width; width = length; length = swap; rotation *= Quaternion.Euler(0, 90, 0); }
                    var bottom = t.position - t.up * size.y / 2;
                    D(Zone(bottom), "CarvedStone").Vault(bottom, width, length, Mathf.Min(width * .32f, 2.25f), rotation);
                    for (float z = -length / 2 + .12f; z <= length / 2; z += 4)
                        D(Zone(bottom), "Limestone").Arch(bottom + rotation * new Vector3(0, .06f, z), width - .2f, 0,
                            Mathf.Min(width * .32f, 2.25f), .13f, .17f, rotation);
                    renderer.enabled = false; continue;
                }
                string material = name.Contains("_Floor") ? "Flagstone" : name.Contains("_Wall") || name.Contains("_Side_") ? "Limestone" : "CarvedStone";
                if (name.Contains("BloodTrace")) material = "Blood";
                else if (name.Contains("CultMark")) material = "Rune";
                else if (name.Contains("Throne_Back")) material = "Iron";
                else if (name.Contains("Reliquary")) material = "DarkStone";
                // Adjacent structural boxes meet exactly at their original
                // boundaries. Chamfering both sides opens a slit to the sky.
                bool structural = name.Contains("_Floor") || name.Contains("_Wall") || name.Contains("_Side_");
                float bevel = structural ? 0 : Mathf.Min(.055f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .15f);
                D(Zone(t.position), material).Box(t.position, size, bevel, t.rotation);
                renderer.enabled = false;
            }
        }

        public void Exterior()
        {
            var stone = D("Exterior", "CarvedStone");
            var trim = D("Exterior", "Limestone");
            // Every exterior addition is beyond the existing side-wall faces.
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = -88; z <= 4; z += 8)
                {
                    stone.Box(new Vector3(side * 4.65f, 2.5f, z), new Vector3(.8f, 5, 1), .09f);
                    trim.Box(new Vector3(side * 4.55f, 5.1f, z), new Vector3(.9f, .35f, 1.15f), .08f);
                    trim.Tube(new Vector3(side * 4.55f, 5.27f, z), new Vector3(side * 4.55f, 6.2f, z), .4f, 0, 8);
                }
                for (float z = -91; z <= 7; z += 2)
                    trim.Box(new Vector3(side * 4.2f, 4.85f, z), new Vector3(.42f, .7f, .75f), .045f);
                Tower(new Vector3(side * 7, 0, 8), 2.2f, side < 0 ? 17 : 20);
                Tower(new Vector3(side * 18, 0, 27), 2.7f, side < 0 ? 27 : 23);
            }
            trim.Arch(new Vector3(0, 0, 10), 8.4f, 4.5f, 5, .5f, .85f, Quaternion.identity);
            stone.Box(new Vector3(0, 12.4f, 10.55f), new Vector3(9, 5.2f, .7f), .12f);
            trim.Ring(new Vector3(0, 12.4f, 10.12f), 1.55f, .12f, 24, Quaternion.identity);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                trim.Beam(new Vector3(0, 12.4f, 10.1f), new Vector3(Mathf.Cos(a) * 1.38f, 12.4f + Mathf.Sin(a) * 1.38f, 10.1f), .07f);
            }
            D("Exterior", "GlassBlue").Disc(new Vector3(0, 12.4f, 10.17f), 1.45f, 24, Quaternion.identity);
            // Angular, original mountain silhouettes behind the playable castle.
            for (int i = 0; i < 14; i++)
            {
                float angle = i * Mathf.PI * 2 / 14;
                var point = new Vector3(Mathf.Sin(angle) * 130, -15, 25 + Mathf.Cos(angle) * 145);
                D("Mountains", "Mountain").Mountain(point, 28 + i % 3 * 9, 34 + i % 5 * 9, i);
            }
        }

        private void Tower(Vector3 bottom, float radius, float height)
        {
            var stone = D("Exterior", "CarvedStone"); var trim = D("Exterior", "Limestone");
            stone.Tube(bottom, bottom + Vector3.up * height, radius, radius * .9f, 8);
            for (float y = 3; y < height; y += 4)
                trim.Tube(bottom + Vector3.up * y, bottom + Vector3.up * (y + .25f), radius * 1.03f, radius * 1.03f, 8);
            trim.Tube(bottom + Vector3.up * height, bottom + Vector3.up * (height + .6f), radius * 1.05f, radius * 1.05f, 8);
            D("Exterior", "DarkStone").Tube(bottom + Vector3.up * (height + .6f), bottom + Vector3.up * (height + 8), radius * 1.12f, .04f, 8);
            trim.Tube(bottom + Vector3.up * (height + 8), bottom + Vector3.up * (height + 9.5f), .13f, 0, 6);
            for (int side = 0; side < 4; side++)
            {
                var q = Quaternion.Euler(0, side * 90, 0);
                var face = bottom + q * new Vector3(0, height - 5, -radius - .02f);
                D("Exterior", "GlassBlue").Box(face + Vector3.up * 1.2f, new Vector3(.6f, 2.4f, .04f), .01f, q);
                trim.Arch(face, .9f, 2.4f, .8f, .13f, .15f, q);
            }
        }

        public void Interiors()
        {
            var throne = D("Throne", "Bronze");
            // Crowned back and finials use the existing throne's solid footprint.
            for (int i = -2; i <= 2; i++)
            {
                var b = new Vector3(8 + i * .28f, 2.85f, 35.45f);
                throne.Tube(b, b + Vector3.up * (.7f - Mathf.Abs(i) * .15f), .09f, 0, 6);
            }
            throne.Box(new Vector3(8, 2.1f, 35.28f), new Vector3(.85f, 1.2f, .06f), .025f);
            D("Throne", "LeatherRed").Box(new Vector3(8, .72f, 34.45f), new Vector3(1.2f, .9f, .08f), .025f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 2.4f;
                D("Throne", "Blood").Disc(new Vector3(6 + Mathf.Sin(a) * .6f, .018f, 34 + Mathf.Cos(a) * .8f),
                    .07f + (i % 3) * .05f, 7, Quaternion.Euler(90, 0, 0));
            }
            foreach (var room in new[] { new Vector3(16, -4, 48), new Vector3(-8, -4, 64), new Vector3(8, -4, 64) })
            {
                string key = "Crypt_" + room.x;
                for (int side = -1; side <= 1; side += 2)
                {
                    var pos = room + new Vector3(side * 3.94f, 0, .7f);
                    Quaternion q = Quaternion.Euler(0, side * -90, 0);
                    D(key, "DarkStone").Box(pos + Vector3.up * 1.5f, new Vector3(1.2f, 2.5f, .04f), .04f, q);
                    D(key, "CarvedStone").Arch(pos, 1.4f, 2.5f, .65f, .13f, .1f, q);
                    Candles(key, room + new Vector3(side * 2.9f, .92f, 3));
                }
            }
            // Radial cult inscription lies on the existing final arena floor.
            var rune = D("FinalSeal", "Bronze");
            var center = new Vector3(8, -3.987f, 64);
            rune.Ring(center, 2.05f, .024f, 32, Quaternion.Euler(90, 0, 0));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.7f;
                rune.Beam(p, p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .23f, .035f);
            }
        }

        private void Window(string key, Vector3 bottom, float width, float height)
        {
            var stone = D(key, "CarvedStone");
            stone.Arch(bottom, width, height * .68f, height * .32f, .12f, .12f, Quaternion.identity);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * width * .28f;
                D(key, i == 1 ? "GlassWine" : "GlassBlue").Box(bottom + new Vector3(x, height * .42f, 0),
                    new Vector3(width * .25f, height * .82f, .035f), .015f);
                stone.Beam(bottom + new Vector3(x - width * .14f, 0, -.045f), bottom + new Vector3(x - width * .14f, height * .8f, -.045f), .045f);
            }
            stone.Beam(bottom + new Vector3(-width / 2, height * .4f, -.045f), bottom + new Vector3(width / 2, height * .4f, -.045f), .045f);
            stone.Ring(bottom + new Vector3(0, height * .76f, -.065f), width * .22f, .055f, 16, Quaternion.identity);
            var lightObject = new GameObject(key + " moonlight"); lightObject.transform.SetParent(root, false);
            lightObject.transform.position = bottom + new Vector3(0, height * .65f, -.3f);
            lightObject.transform.rotation = Quaternion.LookRotation(new Vector3(0, -.22f, -1));
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Spot;
            light.color = new Color(.35f, .5f, .83f); light.intensity = 3; light.range = 8; light.spotAngle = 90;
            light.shadows = LightShadows.Soft;
        }

        private void Shelf(string key, Vector3 center, Vector3 size)
        {
            var q = Quaternion.Euler(0, center.x > 16 ? 90 : -90, 0);
            var wood = D("Library", "Oak");
            Vector3 At(Vector3 offset) => center + q * offset;
            wood.Box(At(new Vector3(0, 0, .28f)), new Vector3(1.38f, 2.5f, .08f), .03f, q);
            foreach (float side in new[] { -.65f, .65f }) wood.Box(At(new Vector3(side, 0, 0)), new Vector3(.1f, 2.5f, .65f), .025f, q);
            for (int row = 0; row < 5; row++)
            {
                float y = -1.2f + row * .57f;
                wood.Box(At(new Vector3(0, y, 0)), new Vector3(1.4f, .075f, .65f), .018f, q);
                if (row == 4) continue;
                for (int book = 0; book < 8; book++)
                {
                    float h = .3f + (book + row) % 3 * .06f;
                    var p = new Vector3(-.54f + book * .15f, y + .05f + h / 2, -.07f);
                    string material = (book + row) % 3 == 0 ? "LeatherGreen" : "LeatherRed";
                    D("Library", material).Box(At(p), new Vector3(.12f, h, .4f), .012f, q);
                    D("Library", "Bronze").Box(At(p + new Vector3(0, -h * .28f, -.205f)), new Vector3(.105f, .025f, .012f), .002f, q);
                    D("Library", "Pages").Box(At(p + new Vector3(0, h / 2, .02f)), new Vector3(.09f, .012f, .31f), .002f, q);
                }
            }
            wood.Arch(center + Vector3.up * 1.12f, 1.3f, 0, .32f, .07f, .3f, q);
        }

        private void Tomb(string key, Vector3 center, Vector3 size)
        {
            var dark = D(Zone(center), "DarkStone"); var trim = D(Zone(center), "CarvedStone");
            var bottom = center - Vector3.up * size.y / 2;
            dark.Box(bottom + Vector3.up * .35f, new Vector3(size.x * .94f, .7f, size.z * .94f), .08f);
            trim.Box(bottom + Vector3.up * .12f, new Vector3(size.x, .18f, size.z), .035f);
            trim.Box(bottom + Vector3.up * .79f, new Vector3(size.x, .22f, size.z), .085f);
            D(Zone(center), "Bronze").Beam(bottom + new Vector3(0, .905f, -.4f), bottom + new Vector3(0, .905f, .4f), .04f);
            D(Zone(center), "Bronze").Beam(bottom + new Vector3(-.22f, .905f, .1f), bottom + new Vector3(.22f, .905f, .1f), .04f);
            for (int side = -1; side <= 1; side += 2)
            {
                var panel = bottom + new Vector3(side * size.x * .477f, .43f, 0);
                dark.Box(panel, new Vector3(.022f, .35f, .8f), .005f);
                trim.Ring(panel, .13f, .025f, 12, Quaternion.Euler(0, 90, 0));
            }
        }

        private void Candles(string key, Vector3 floor)
        {
            for (int i = 0; i < 3; i++)
            {
                float h = .18f + i * .08f;
                var p = floor + new Vector3((i - 1) * .13f, 0, i % 2 * .12f);
                D(key, "Wax").Tube(p, p + Vector3.up * h, .04f, .035f, 7);
                D(key, "Flame").Tube(p + Vector3.up * h, p + Vector3.up * (h + .11f), .028f, 0, 6);
            }
        }

        public void Gates(GameObject gameplay)
        {
            foreach (var gate in gameplay.GetComponentsInChildren<ProgressionGate>(true))
            {
                var panel = gate.transform.Find("Gate panel").GetComponent<Renderer>(); panel.enabled = false;
                var retained = gate.closedVisuals.Where(r => r != null && r != panel).ToList();
                bool closed = gate.GetComponent<BoxCollider>().enabled;
                Draft Part(string material)
                {
                    var batch = Get(gate.name, material, gate.transform);
                    batch.visible = closed; batch.bind = r => retained.Add(r); return batch.draft;
                }
                bool Connects(LevelRoom a, LevelRoom b) => (gate.from == a && gate.to == b) || (gate.from == b && gate.to == a);
                if (Connects(LevelRoom.Library, LevelRoom.Catacombs))
                {
                    // A solid bookcase masks the passage; all parts follow the
                    // existing closedVisuals state without changing its barrier.
                    var oak = Part("Oak");
                    oak.Box(new Vector3(0, 2.2f, .13f), new Vector3(3.99f, 4.4f, .08f), .015f);
                    foreach (float x in new[] { -1.9f, -.64f, .64f, 1.9f })
                        oak.Box(new Vector3(x, 2.2f, 0), new Vector3(.16f, 4.4f, .33f), .02f);
                    for (int row = 0; row <= 6; row++)
                    {
                        float y = .12f + row * .69f;
                        oak.Box(new Vector3(0, y, 0), new Vector3(3.99f, .11f, .33f), .015f);
                        if (row == 6) continue;
                        for (int bay = 0; bay < 3; bay++)
                            for (int book = 0; book < 6; book++)
                            {
                                float h = .36f + (book + row) % 3 * .065f;
                                var p = new Vector3(-1.74f + bay * 1.28f + book * .185f, y + .06f + h / 2, -.005f);
                                Part((book + row) % 3 == 0 ? "LeatherGreen" : "LeatherRed")
                                    .Box(p, new Vector3(.15f, h, .24f), .008f);
                                Part("Bronze").Box(p + new Vector3(0, -h * .28f, -.127f), new Vector3(.125f, .025f, .014f), .002f);
                                Part("Pages").Box(p + Vector3.up * (h / 2), new Vector3(.11f, .01f, .19f), .002f);
                            }
                    }
                }
                else if (Connects(LevelRoom.Catacombs, LevelRoom.BonusRoom) || Connects(LevelRoom.BonusRoom, LevelRoom.ThroneRoom))
                {
                    // Mortared stone matches the surrounding masonry. There is
                    // no visible portcullis identifying the concealed branch.
                    Part("CarvedStone").Box(new Vector3(0, 2.2f, 0), new Vector3(4, 4.4f, .29f), .01f);
                    var stone = Part("Limestone");
                    for (int row = 0; row < 8; row++)
                        for (int col = 0; col < 4; col++)
                        {
                            float width = col == 0 || col == 3 ? (row % 2 == 0 ? .99f : .49f) : (row % 2 == 0 ? .99f : 1.49f);
                            float x = row % 2 == 0 ? -1.5f + col : new[] { -1.75f, -.75f, .75f, 1.75f }[col];
                            stone.Box(new Vector3(x, .275f + row * .55f, 0), new Vector3(width, .535f, .34f), .015f);
                        }
                }
                else
                {
                    var iron = Part("Iron"); var bronze = Part("Bronze");
                    for (int i = -4; i <= 4; i++)
                    {
                        float x = i * .43f;
                        iron.Tube(new Vector3(x, .1f, 0), new Vector3(x, 4.38f, 0), .075f, .065f, 6);
                        bronze.Tube(new Vector3(x, .1f, 0), new Vector3(x, -.015f, 0), .1f, 0, 6);
                    }
                    for (int row = 0; row < 4; row++) iron.Box(new Vector3(0, .65f + row * 1.05f, 0), new Vector3(3.95f, .12f, .16f), .03f);
                    bronze.Ring(new Vector3(0, 2.2f, -.11f), .49f, .045f, 16, Quaternion.identity);
                }
                afterSave.Add(() => gate.closedVisuals = retained.ToArray());
                // Crowns sit wholly above the existing four-metre opening.
                D("PortalCrowns", "Limestone").Arch(gate.transform.position, 4.15f, 4.1f, 1.65f, .2f, .4f, gate.transform.rotation);
            }
        }

        public void Mechanisms(GameObject gameplay)
        {
            foreach (var feedback in gameplay.GetComponentsInChildren<MechanismFeedback>(true))
            {
                var moving = feedback.transform.Find("Moving mechanism");
                if (moving == null) throw new InvalidOperationException("Authored mechanism moving part is missing.");
                foreach (var renderer in moving.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                string key = feedback.name.Replace(' ', '_');
                var renderers = new List<Renderer>();
                Batch Part(string material)
                {
                    var batch = Get(key, material, moving); batch.bind = r => renderers.Add(r); return batch;
                }
                if (feedback.hideWhenConsumed)
                {
                    var gold = Part("Bronze").draft;
                    gold.Ring(Vector3.zero, .23f, .03f, 16, Quaternion.identity);
                    gold.Box(new Vector3(0, -.21f, 0), new Vector3(.26f, .055f, .18f), .02f);
                    Part("Rune").draft.Crystal(new Vector3(0, .015f, 0), .11f, .3f);
                }
                else if (feedback.name.Contains("Book"))
                {
                    Part("Pages").draft.Box(Vector3.zero, new Vector3(.12f, .3f, .58f), .015f);
                    var leather = Part("LeatherGreen").draft;
                    for (int side = -1; side <= 1; side += 2) leather.Box(new Vector3(side * .085f, 0, 0), new Vector3(.035f, .37f, .67f), .012f);
                    var rune = Part("Rune").draft;
                    rune.Ring(new Vector3(-.108f, 0, 0), .11f, .018f, 12, Quaternion.Euler(0, 90, 0));
                }
                else
                {
                    bool damaged = feedback.name.Contains("Damaged");
                    var q = Quaternion.Euler(0, 0, damaged ? 60 : 0);
                    var iron = Part("Iron").draft;
                    iron.Tube(new Vector3(0, -.24f, 0), q * new Vector3(0, .3f, 0), .038f, .03f, 8);
                    iron.Box(new Vector3(0, -.28f, 0), new Vector3(.3f, .11f, .25f), .04f);
                    Part("Bronze").draft.Tube(q * new Vector3(-.13f, .29f, 0), q * new Vector3(.13f, .29f, 0), .05f, .05f, 8);
                    if (!damaged)
                    {
                        Part("DarkStone").draft.Box(new Vector3(0, 0, -.12f), new Vector3(.62f, .82f, .08f), .04f);
                        var rune = Part("Rune").draft;
                        rune.Beam(new Vector3(0, -.3f, -.175f), new Vector3(0, .32f, -.175f), .055f);
                        rune.Beam(new Vector3(0, -.04f, -.175f), new Vector3(-.2f, .25f, -.175f), .055f);
                        rune.Beam(new Vector3(0, -.04f, -.175f), new Vector3(.2f, .25f, -.175f), .055f);
                    }
                }
                afterSave.Add(() => feedback.rewardVisuals = renderers.ToArray());
            }
        }

        public void Illumination(GameObject lighting)
        {
            var originalSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/FreeNightSky/Materials/nightsky1.mat");
            if (originalSky == null) throw new InvalidOperationException("The existing night sky material is required.");
            string path = AssetPath + "/CastleNight.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(originalSky); AssetDatabase.CreateAsset(sky, path); }
            else EditorUtility.CopySerialized(originalSky, sky);
            sky.name = "CastleNight"; sky.SetFloat("_Exposure", .65f); sky.SetColor("_Tint", new Color(.34f, .4f, .5f));
            EditorUtility.SetDirty(sky); RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.2f, .25f, .33f);
            RenderSettings.ambientEquatorColor = new Color(.12f, .15f, .2f);
            RenderSettings.ambientGroundColor = new Color(.07f, .065f, .08f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.09f, .12f, .16f); RenderSettings.fogStartDistance = 25; RenderSettings.fogEndDistance = 130;
            foreach (var light in lighting.GetComponentsInChildren<Light>())
            {
                if (light.type == LightType.Directional)
                { light.color = new Color(.53f, .66f, .88f); light.intensity = .8f; continue; }
                if (light.name == "Torch_00") light.transform.position = new Vector3(-3.7f, 2.8f, 1);
                if (light.name == "Torch_01") light.transform.position = new Vector3(-11.7f, 2.8f, 24);
                if (light.name == "Torch_02") light.transform.position = new Vector3(11.7f, 2.8f, 35);
                if (light.name == "Torch_03") light.transform.position = new Vector3(19.6f, 1.45f, 40);
                if (light.name == "Torch_04") light.transform.position = new Vector3(19.7f, -1.2f, 47);
                if (light.name == "Torch_05") light.transform.position = new Vector3(-6, -1.2f, 67.7f);
                if (light.name == "Torch_06") light.transform.position = new Vector3(11.7f, -1.2f, 64);
                if (light.name == "Torch_07") light.transform.position = new Vector3(-8, -1.2f, 30.2f);
                if (light.name == "Torch_08") light.transform.position = new Vector3(-24, -9.2f, 30.2f);
                if (light.name == "Torch_09") light.transform.position = new Vector3(-31.8f, -9.2f, 48);
                if (light.name == "Torch_10") light.transform.position = new Vector3(-20, -5.2f, 62.2f);
                light.color = new Color(1, .48f, .19f); light.intensity = 3.2f; light.range = 7;
                var pos = light.transform.position;
                var iron = D("Lanterns", "Iron"); var fire = D("Lanterns", "Flame");
                iron.Tube(pos + Vector3.down * .28f, pos + Vector3.down * .18f, .15f, .22f, 8);
                iron.Tube(pos + Vector3.up * .2f, pos + Vector3.up * .42f, .23f, 0, 8);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 2;
                    var offset = new Vector3(Mathf.Cos(a) * .16f, 0, Mathf.Sin(a) * .16f);
                    iron.Beam(pos + offset + Vector3.down * .2f, pos + offset + Vector3.up * .22f, .035f);
                }
                fire.Tube(pos + Vector3.down * .15f, pos + Vector3.up * .17f, .1f, 0, 8);
            }
        }

        public BuildResult Save()
        {
            var result = new BuildResult();
            foreach (var batch in batches.Values.OrderBy(b => b.name, StringComparer.Ordinal))
            {
                if (batch.draft.indices.Count == 0) continue;
                var mesh = batch.draft.Mesh(batch.name);
                string path = AssetPath + "/" + batch.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
                var go = new GameObject(batch.name); go.transform.SetParent(batch.parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = batch.material;
                renderer.enabled = batch.visible; renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                batch.bind?.Invoke(renderer);
                result.triangles += batch.draft.indices.Count / 3; result.renderers++; result.meshAssets++;
            }
            foreach (var bind in afterSave) bind();
            return result;
        }
    }

    private static void Column(Draft d, Vector3 bottom, float height, float radius)
    {
        d.Tube(bottom, bottom + Vector3.up * .16f, radius, radius, 12);
        d.Tube(bottom + Vector3.up * .16f, bottom + Vector3.up * (height - .25f), radius * .68f, radius * .6f, 12);
        d.Tube(bottom + Vector3.up * (height - .25f), bottom + Vector3.up * height, radius * .74f, radius, 12);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4;
            var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius * .62f;
            d.Tube(bottom + offset + Vector3.up * .2f, bottom + offset + Vector3.up * (height - .25f), radius * .12f, radius * .1f, 5);
        }
    }

    // Meshes contain explicit bevels, curved arch profiles and metre-scaled UVs.
    // Only renderers consume them; no MeshCollider or NavMeshModifier is added.
    private sealed class Draft
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> indices = new List<int>();

        private static Vector2 UV(Vector3 p, Vector3 n)
        {
            n = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            return n.y >= n.x && n.y >= n.z ? new Vector2(p.x, p.z) * .55f :
                n.x >= n.z ? new Vector2(p.z, p.y) * .55f : new Vector2(p.x, p.y) * .55f;
        }
        private void Vertex(Vector3 p, Vector3 normal) { vertices.Add(p); normals.Add(normal); uv.Add(UV(p, normal)); }
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { var swap = b; b = d; d = swap; }
            int n = vertices.Count; normal.Normalize(); Vertex(a, normal); Vertex(b, normal); Vertex(c, normal); Vertex(d, normal);
            indices.Add(n); indices.Add(n + 1); indices.Add(n + 2); indices.Add(n); indices.Add(n + 2); indices.Add(n + 3);
        }
        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { var swap = b; b = c; c = swap; }
            int n = vertices.Count; normal.Normalize(); Vertex(a, normal); Vertex(b, normal); Vertex(c, normal);
            indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
        }
        private static Vector3 Axis(int axis, float value) { var p = Vector3.zero; p[axis] = value; return p; }

        public void Box(Vector3 center, Vector3 size, float bevel, Quaternion rotation = default)
        {
            if (rotation.Equals(default(Quaternion))) rotation = Quaternion.identity;
            Vector3 h = size * .5f;
            float b = Mathf.Clamp(bevel, 0, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * .8f);
            Vector3 P(Vector3 p) => center + rotation * p;
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                var origin = Axis(axis, sign * h[axis]); var a = Axis(u, h[u] - b); var c = Axis(v, h[v] - b);
                Quad(P(origin - a - c), P(origin + a - c), P(origin + a + c), P(origin - a + c), rotation * Axis(axis, sign));
            }
            if (b == 0) return;
            for (int a = 0; a < 3; a++) for (int c = a + 1; c < 3; c++)
            for (int sa = -1; sa <= 1; sa += 2) for (int sc = -1; sc <= 1; sc += 2)
            {
                int along = 3 - a - c;
                var one = Axis(a, sa * h[a]) + Axis(c, sc * (h[c] - b));
                var two = Axis(a, sa * (h[a] - b)) + Axis(c, sc * h[c]);
                var span = Axis(along, h[along] - b);
                Quad(P(one - span), P(one + span), P(two + span), P(two - span), rotation * (Axis(a, sa) + Axis(c, sc)));
            }
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                Tri(P(new Vector3(x * h.x, y * (h.y - b), z * (h.z - b))), P(new Vector3(x * (h.x - b), y * h.y, z * (h.z - b))),
                    P(new Vector3(x * (h.x - b), y * (h.y - b), z * h.z)), rotation * new Vector3(x, y, z));
        }

        public void Beam(Vector3 a, Vector3 b, float width)
        {
            Vector3 direction = b - a;
            if (direction.sqrMagnitude < .000001f) return;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
            Box((a + b) / 2, new Vector3(width, width, direction.magnitude), width * .15f, Quaternion.LookRotation(direction, up));
        }

        public void Tube(Vector3 a, Vector3 b, float first, float last, int segments)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(axis, side);
            for (int i = 0; i < segments; i++)
            {
                float one = i * Mathf.PI * 2 / segments, two = (i + 1) * Mathf.PI * 2 / segments;
                var u = side * Mathf.Cos(one) + up * Mathf.Sin(one); var v = side * Mathf.Cos(two) + up * Mathf.Sin(two);
                var p = a + u * first; var q = a + v * first; var r = b + v * last; var s = b + u * last;
                var normal = (u + v).normalized + axis * ((first - last) / Vector3.Distance(a, b));
                if (last > .00001f) { Quad(p, q, r, s, normal); Tri(b, s, r, axis); }
                else Tri(p, q, b, normal);
                Tri(a, q, p, -axis);
            }
        }

        private static List<Vector2> ArchProfile(float width, float spring, float rise)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f, s = 1 - t;
                points.Add(new Vector2(-width * .5f * (1 - t * t), spring + rise * (1.44f * s * t + t * t)));
            }
            for (int i = 11; i >= 0; i--) points.Add(new Vector2(-points[i].x, points[i].y));
            return points;
        }

        public void Arch(Vector3 bottom, float width, float spring, float rise, float thickness, float depth, Quaternion rotation)
        {
            var points = ArchProfile(width, spring, rise);
            Vector3 P(Vector2 point, float z) => bottom + rotation * new Vector3(point.x, point.y, z);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 tangent = (points[i + 1] - points[i]).normalized;
                Vector2 outside = new Vector2(-tangent.y, tangent.x) * thickness / 2;
                var a = points[i] - outside; var b = points[i] + outside;
                var c = points[i + 1] + outside; var d = points[i + 1] - outside;
                Quad(P(a, -depth / 2), P(b, -depth / 2), P(c, -depth / 2), P(d, -depth / 2), rotation * Vector3.back);
                Quad(P(a, depth / 2), P(b, depth / 2), P(c, depth / 2), P(d, depth / 2), rotation * Vector3.forward);
                var n = rotation * new Vector3(outside.x, outside.y, 0).normalized;
                Quad(P(b, -depth / 2), P(c, -depth / 2), P(c, depth / 2), P(b, depth / 2), n);
                Quad(P(a, -depth / 2), P(d, -depth / 2), P(d, depth / 2), P(a, depth / 2), -n);
            }
        }

        public void Vault(Vector3 bottom, float width, float length, float rise, Quaternion rotation)
        {
            var profile = ArchProfile(width + .04f, 0, rise);
            Vector3 P(Vector2 p, float z) => bottom + rotation * new Vector3(p.x, p.y, z);
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector3 a = new Vector3(profile[i].x, profile[i].y, -length / 2 - .02f);
                Vector3 b = new Vector3(profile[i + 1].x, profile[i + 1].y, -length / 2 - .02f);
                Vector3 c = b + Vector3.forward * (length + .04f), d = a + Vector3.forward * (length + .04f);
                var tangent = b - a; var normal = new Vector3(tangent.y, -tangent.x, 0).normalized;
                Quad(bottom + rotation * a, bottom + rotation * b, bottom + rotation * c, bottom + rotation * d, rotation * normal);
            }
            // The original walls end at this ceiling's spring line. Seal the
            // raised gables so underground rooms cannot reveal the night sky.
            // Every added vertex is on or above the original ceiling underside;
            // the gate openings and all collision/navigation geometry stay intact.
            foreach (float end in new[] { -1f, 1f })
            {
                float inside = end * (length / 2 + .005f), outside = end * (length / 2 + .08f);
                var inward = rotation * (Vector3.back * end);
                for (int i = 0; i < profile.Count - 1; i++)
                {
                    Tri(P(Vector2.zero, inside), P(profile[i], inside), P(profile[i + 1], inside), inward);
                    Tri(P(Vector2.zero, outside), P(profile[i], outside), P(profile[i + 1], outside), -inward);
                    var tangent = profile[i + 1] - profile[i];
                    var rimNormal = rotation * new Vector3(-tangent.y, tangent.x, 0).normalized;
                    Quad(P(profile[i], inside), P(profile[i + 1], inside),
                        P(profile[i + 1], outside), P(profile[i], outside), rimNormal);
                }
                Quad(P(profile[0], inside), P(profile[profile.Count - 1], inside),
                    P(profile[profile.Count - 1], outside), P(profile[0], outside), rotation * Vector3.down);
            }
        }

        public void Ring(Vector3 center, float radius, float thickness, int segments, Quaternion rotation)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Tube(center + rotation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0),
                    center + rotation * new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, 0), thickness, thickness, 5);
            }
        }
        public void Disc(Vector3 center, float radius, int segments, Quaternion rotation)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Tri(center, center + rotation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0),
                    center + rotation * new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, 0), rotation * Vector3.back);
            }
        }
        public void Crystal(Vector3 center, float radius, float height)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
                var p = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                var q = center + new Vector3(Mathf.Cos(b) * radius, 0, Mathf.Sin(b) * radius);
                Tri(p, q, center + Vector3.up * height / 2, (p + q - center * 2).normalized + Vector3.up);
                Tri(p, q, center - Vector3.up * height / 2, (p + q - center * 2).normalized - Vector3.up);
            }
        }
        public void Mountain(Vector3 center, float radius, float height, int seed)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5, b = (i + 1) * Mathf.PI / 5;
                var p = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                var q = center + new Vector3(Mathf.Cos(b) * radius, 0, Mathf.Sin(b) * radius);
                var peak = center + new Vector3((seed % 3 - 1) * 8, height, (seed % 4 - 2) * 6);
                var shoulder = (p + q + peak) / 3 + new Vector3(Mathf.Sin(i + seed) * 4, -4, Mathf.Cos(i) * 4);
                var normal = (p + q - center * 2).normalized + Vector3.up;
                Tri(p, q, shoulder, normal); Tri(q, peak, shoulder, normal); Tri(peak, p, shoulder, normal);
            }
        }
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
