using System.Collections.Generic;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShadowsOfTheForsaken.Level
{
    /// <summary>Authored coordinates from DOCX §8; one cell = 12 m. Presentation, not progression.</summary>
    internal sealed class CastleArchitecture
    {
        private readonly Transform root;
        private readonly Material worldText;
        private readonly Material stone, floor, trim, iron, blood, bone, gold, glass, glow, cloth;
        private readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        public readonly List<Light> TorchLights = new List<Light>();

        public CastleArchitecture(Transform owner, Material seed, Material inscriptions, List<Material> lifetime)
        {
            root = owner;
            worldText = inscriptions;
            stone = Material(seed, "Weathered basalt", new Color(.20f, .23f, .25f), lifetime);
            floor = Material(seed, "Flagstone", new Color(.27f, .29f, .30f), lifetime);
            trim = Material(seed, "Carved limestone", new Color(.37f, .39f, .39f), lifetime);
            iron = Material(seed, "Blackened iron", new Color(.08f, .10f, .12f), lifetime);
            blood = Material(seed, "Fresh blood", new Color(.27f, .014f, .02f), lifetime);
            bone = Material(seed, "Old ivory", new Color(.64f, .60f, .48f), lifetime);
            gold = Material(seed, "Tarnished brass", new Color(.54f, .37f, .14f), lifetime);
            glass = Material(seed, "Moonlit stained glass", new Color(.18f, .33f, .59f), lifetime, .28f);
            glow = Material(seed, "Flame", new Color(1, .46f, .10f), lifetime, 2);
            cloth = Material(seed, "Wanderer cloak", new Color(.15f, .22f, .26f), lifetime);
        }

        private static Material Material(Material seed, string name, Color color, List<Material> lifetime, float emission = 0)
        {
            if (seed == null) throw new System.InvalidOperationException("Assign the CastleSurface material to the level scene.");
            var material = new Material(seed) { name = name, color = color, enableInstancing = true };
            material.SetColor("_EmissionColor", color * emission);
            lifetime.Add(material);
            return material;
        }

        public void BuildShell(Material sky)
        {
            RenderSettings.skybox = sky;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.075f, .10f, .13f); RenderSettings.fogDensity = .009f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.24f, .29f, .35f);
            var moon = new GameObject("Moonlight"); moon.transform.SetParent(root, false);
            moon.transform.rotation = Quaternion.Euler(38, -32, 0);
            var sunlight = moon.AddComponent<Light>(); sunlight.type = LightType.Directional;
            sunlight.color = new Color(.52f, .65f, .86f); sunlight.intensity = .85f;
            sunlight.shadows = LightShadows.Soft;
            // Traversable §8 cells; x = column - 5, z = 10 - row.
            int[,] grid = { {0,0},{0,1},{0,2},{-1,2},{1,2},{-1,3},{1,3},{1,4},{2,4},
                {2,5},{2,6},{1,6},{0,6},{-1,6},{-1,7},{-1,8},{1,7},{1,8},{1,9} };
            for (int i = 0; i < grid.GetLength(0); i++) cells.Add(new Vector2Int(grid[i,0], grid[i,1]));
            foreach (var cell in cells)
            {
                Vector3 center = new Vector3(cell.x * 12, 0, cell.y * 12);
                Box("Foundation", center + Vector3.down * .4f, new Vector3(12, .8f, 12), stone);
                for (int x = -2; x < 2; x++) for (int z = -2; z < 2; z++)
                    Box("Worn flagstone", center + new Vector3(x * 3 + 1.5f, .01f, z * 3 + 1.5f), new Vector3(2.97f, .035f, 2.97f), floor, false);
                foreach (var direction in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    if (cells.Contains(cell + direction) || cell == Vector2Int.zero && direction == Vector2Int.down) continue;
                    Vector3 at = center + new Vector3(direction.x * 6, 3.5f, direction.y * 6);
                    // Two deliberate lower-tunnel openings: throne west and bonus east.
                    if (cell == new Vector2Int(1,4) && direction == Vector2Int.left ||
                        cell == new Vector2Int(-1,8) && direction == Vector2Int.right)
                    {
                        Box("Concealed doorway jamb", at + Vector3.back * 4.3f, new Vector3(.7f, 7, 3.4f), stone);
                        Box("Concealed doorway jamb", at + Vector3.forward * 4.3f, new Vector3(.7f, 7, 3.4f), stone);
                        Box("Concealed doorway lintel", at + Vector3.up * 2.2f, new Vector3(.7f, 2.6f, 5.2f), stone);
                        continue;
                    }
                    Box("Castle wall", at, direction.x == 0 ? new Vector3(12.7f, 7, .7f) : new Vector3(.7f, 7, 12.7f), stone);
                    Box("Wall cornice", at + Vector3.up * 3.1f,
                        direction.x == 0 ? new Vector3(12.9f, .5f, 1) : new Vector3(1, .5f, 12.9f), trim);
                }
                if (cell.y >= 2)
                {
                    float vaultHeight = cell.y == 6 ? 4.8f : 7.2f;
                    Box("Vault canopy", center + Vector3.up * vaultHeight, new Vector3(12, .4f, 12), stone);
                    Vault(center, vaultHeight);
                }
            }
            // The start remains calm and scenic; no timer delays access to the first demon.
            Box("Approach courtyard", new Vector3(0, -.4f, -31.5f), new Vector3(12, .8f, 51), stone);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Courtyard parapet", new Vector3(side * 6, .9f, -31.5f), new Vector3(.8f, 1.8f, 51), stone);
                for (int z = -50; z <= 10; z += 12)
                {
                    Column(new Vector3(side * 5, 0, z), 4.7f);
                    Torch(new Vector3(side * 4.9f, 2.1f, z + 2));
                }
            }
            // Leave space behind the starting camera; the boundary must not cover the opening view.
            Box("Entrance parapet", new Vector3(0, 1, -57), new Vector3(12, 2, 1), stone);
            Inscription(new Vector3(0, 2, -40), "SHADOWS OF THE FORSAKEN", 180);
            Inscription(new Vector3(0, 1.7f, -35), "The fallen kingdom waits beyond the gate.", 180);
            for (int i = 0; i < 8; i++)
            {
                float x = i % 2 == 0 ? -52 - i * 4 : 55 + i * 4;
                var mountain = Shape(PrimitiveType.Cube, "Mist-shrouded mountain", new Vector3(x, -9, -10 + i * 22),
                    new Vector3(30, 65 + i * 3, 30), stone, false);
                mountain.transform.rotation = Quaternion.Euler(0, i * 23, 38);
            }
            // The castle meets a rising hillside: the mandatory burial corridors lie below its ground.
            // Their floor stays aligned with the library; only the optional return stair changes elevation.
            Box("Rock overburden above the catacombs", new Vector3(6, 11.6f, 90), new Vector3(52, 8, 52), stone);
            LowerTunnel();
        }

        private void Vault(Vector3 center, float height)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Column(center + new Vector3(s * 5.2f, 0, -4.8f), height - 1.8f);
                Beam("Pointed vault rib", center + new Vector3(s * 5.2f, height - 2.2f, 0), center + Vector3.up * (height - .2f), .22f, trim);
            }
        }

        public GameObject Door(Vector3 position, Vector3 forward, bool concealed)
        {
            var doorway = new GameObject("Gothic passage"); doorway.transform.SetParent(root, false);
            doorway.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            for (int s = -1; s <= 1; s += 2)
            {
                LocalBox(doorway.transform, "Stone jamb", new Vector3(s * 4.3f, 3.5f, 0), new Vector3(3.4f, 7, .8f), stone, true);
                LocalBox(doorway.transform, "Carved doorpost", new Vector3(s * 2.65f, 2.4f, -.15f), new Vector3(.35f, 4.8f, 1), trim, false);
                Beam("Arch voussoir", position + Quaternion.LookRotation(forward) * new Vector3(s * 2.6f, 4.6f, 0),
                    position + Vector3.up * 6.3f, .28f, trim);
            }
            LocalBox(doorway.transform, "Lintel", new Vector3(0, 6.2f, 0), new Vector3(5.2f, 1.6f, .8f), stone, true);
            var barrier = new GameObject(concealed ? "Hidden stone door" : "Iron portcullis");
            barrier.transform.SetParent(root, false);
            barrier.transform.SetPositionAndRotation(position + Vector3.up * 3.5f, Quaternion.LookRotation(forward));
            var collider = barrier.AddComponent<BoxCollider>(); collider.size = new Vector3(5.2f, 7, .6f);
            if (concealed) LocalBox(barrier.transform, "Sealed panel", Vector3.zero, new Vector3(5.15f, 7, .5f), stone, false);
            else
            {
                for (int i = -3; i <= 3; i++) LocalBox(barrier.transform, "Iron bar", new Vector3(i * .7f, 0, 0), new Vector3(.13f, 7, .16f), iron, false);
                for (int i = -1; i <= 1; i++) LocalBox(barrier.transform, "Iron brace", new Vector3(0, i * 1.9f, 0), new Vector3(5.2f, .18f, .2f), iron, false);
            }
            return barrier;
        }

        public void ShortcutEntrance(LevelProgressionController progression)
        {
            var barrier = new GameObject("Throne concealed return seal"); barrier.transform.SetParent(root, false);
            barrier.transform.position = new Vector3(6, 2.4f, 48);
            var collider = barrier.AddComponent<BoxCollider>(); collider.size = new Vector3(.8f, 5, 5.2f);
            var visual = LocalBox(barrier.transform, "Secret masonry", Vector3.zero, collider.size, stone, false).GetComponent<Renderer>();
            barrier.AddComponent<PassageGate>().Configure(progression, LevelRoom.ThroneRoom, LevelRoom.BonusRoom, collider, new[] {visual});
        }

        private void LowerTunnel()
        {
            Ramp(new Vector3(6, 0, 48), new Vector3(-6, -6, 48), 4, openRightEnd: true);
            Box("Lower corner landing", new Vector3(-6, -6.3f, 48), new Vector3(4, .6f, 4), stone);
            Box("Lower secret tunnel floor", new Vector3(-6, -6.3f, 64), new Vector3(4, .6f, 32), stone);
            Box("Lower tunnel west wall", new Vector3(-8.2f, -3.5f, 65), new Vector3(.4f, 5, 34), stone);
            Box("Lower tunnel east wall", new Vector3(-3.8f, -3.5f, 64.8f), new Vector3(.4f, 5, 25.6f), stone);
            Box("Lower tunnel ceiling", new Vector3(-6, -1, 64), new Vector3(4.8f, .4f, 32), stone);
            // The return stair rises in the gap between bonus/final foundations; never through a floor slab.
            Box("Lower return corner", new Vector3(-3, -6.3f, 80), new Vector3(10, .6f, 4), stone);
            Box("Lower corner north wall", new Vector3(-5, -3.5f, 82.2f), new Vector3(6, 5, .4f), stone);
            Box("Lower corner south wall", new Vector3(0, -3.5f, 77.8f), new Vector3(4, 5, .4f), stone);
            Box("Lower return corner ceiling", new Vector3(-3, -1, 80), new Vector3(10, .4f, 4.8f), stone);
            Ramp(new Vector3(0, -6, 80), new Vector3(0, 0, 96), 4, openLeftStart: true, openLeftEnd: true);
            Box("Return landing", new Vector3(-4, -.3f, 96), new Vector3(4, .6f, 4), stone);
            Box("Return landing end wall", new Vector3(-4, 2.5f, 98), new Vector3(4, 5, .5f), stone);
            Box("Return landing south wall", new Vector3(-4, 2.5f, 94), new Vector3(4, 5, .5f), stone);
            Torch(new Vector3(-6, -3.6f, 60)); Torch(new Vector3(-6, -3.6f, 78));
        }

        private void Ramp(Vector3 start, Vector3 end, float width, bool openLeftStart = false, bool openLeftEnd = false, bool openRightEnd = false)
        {
            var rampRoot = new GameObject("Secret stair ramp"); rampRoot.transform.SetParent(root, false);
            Vector3 horizontal = end - start; horizontal.y = 0;
            rampRoot.transform.position = (start + end) * .5f;
            rampRoot.transform.rotation = Quaternion.LookRotation(horizontal) * Quaternion.Euler(-Mathf.Atan2(end.y - start.y, horizontal.magnitude) * Mathf.Rad2Deg, 0, 0);
            float length = (end - start).magnitude;
            LocalBox(rampRoot.transform, "Ramp floor", new Vector3(0, -.3f, 0), new Vector3(width, .6f, length + .7f), stone, true);
            for (int s = -1; s <= 1; s += 2)
            {
                float cutStart = s < 0 && openLeftStart ? 4 : 0;
                float cutEnd = s < 0 && openLeftEnd || s > 0 && openRightEnd ? 4 : 0;
                LocalBox(rampRoot.transform, "Ramp side", new Vector3(s * (width / 2 + .2f), 2.4f, (cutStart - cutEnd) * .5f),
                    new Vector3(.4f, 5.2f, length + .8f - cutStart - cutEnd), stone, true);
            }
            LocalBox(rampRoot.transform, "Ramp ceiling", new Vector3(0, 4.8f, 0), new Vector3(width + .8f, .4f, length + .8f), stone, true);
        }

        public void Furnish()
        {
            Inscription(new Vector3(-9, 2.6f, 24), "WEST: RUNES     EAST: THE THRONE", 180);
            Inscription(new Vector3(12, 3.7f, 52), "THE CORRUPTED CASTELLAN", 180);
            Box("Throne dais", new Vector3(15.5f, .15f, 51), new Vector3(2.5f, .3f, 2), trim);
            Box("Ruined throne seat", new Vector3(15.5f, .85f, 51), new Vector3(1.3f, 1.3f, 1.2f), stone);
            Box("Throne back", new Vector3(15.5f, 2.0f, 51.5f), new Vector3(1.5f, 2.8f, .35f), stone);
            for (int i = 0; i < 3; i++)
            {
                var column = Shape(PrimitiveType.Cylinder, "Fallen throne column", new Vector3(18 + i * 2, .55f, 46 + i), new Vector3(.85f, 2, .85f), trim, true);
                column.transform.rotation = Quaternion.Euler(0, 25 * i, 90);
                Shape(PrimitiveType.Cylinder, "Blood on flagstone", new Vector3(10 + i, .04f, 49 - i), new Vector3(1.4f, .008f, .9f), blood, false);
            }
            Window(new Vector3(29.55f, 3.7f, 48), 90);
            Window(new Vector3(17.55f, 3.7f, 36), 90);
            Window(new Vector3(-17.55f, 3.7f, 36), -90);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++) Shelf(new Vector3(24 + side * 4.4f, 0, 57.3f + i * 2.5f), side * 90);
            Inscription(new Vector3(24, 3.5f, 64), "THE CURSED LIBRARY", 180);
            Inscription(new Vector3(25.5f, 2.1f, 61.7f), "One forbidden book binds the hidden door.", 180);
            for (int i = 0; i < 4; i++)
            {
                Box("Catacomb sarcophagus", new Vector3(26.5f - i * 9, .55f, 74.5f), new Vector3(2.5f, 1.1f, 1.6f), stone);
                Box("Sarcophagus carved lid", new Vector3(26.5f - i * 9, 1.15f, 74.5f), new Vector3(2.7f, .22f, 1.8f), trim);
                Shape(PrimitiveType.Sphere, "Cult skull", new Vector3(26.5f - i * 9, 1.45f, 74.5f), new Vector3(.4f, .45f, .4f), bone, false);
            }
            Inscription(new Vector3(12, 3, 100.5f), "BREAK THE LAST CURSE", 180);
            Inscription(new Vector3(-12, 3, 100.5f), "THE RELIC OF A FALLEN KINGDOM", 180);
            Inscription(new Vector3(-14.6f, 2, 96), "Return to the throne through the concealed stair.", 90);
            Inscription(new Vector3(12, 3, 111.5f), "DAWN BEYOND THE RUINS", 180);
            // Cult sigil, contained within the final arena; decorative, never a progression condition.
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6;
                var mark = Box("Cult circle", new Vector3(12 + Mathf.Sin(a) * 2.7f, .065f, 94 + Mathf.Cos(a) * 2.7f), new Vector3(.14f, .03f, 1.25f), blood, false);
                mark.transform.rotation = Quaternion.Euler(0, i * 30, 0);
            }
            foreach (var at in new[] { new Vector3(4, 2.5f, 24), new Vector3(-16, 2.5f, 37), new Vector3(8, 2.5f, 50),
                new Vector3(28, 2.5f, 51), new Vector3(20, 2.5f, 60), new Vector3(28, 2.5f, 72),
                new Vector3(3, 2.5f, 73), new Vector3(-16, 2.5f, 94), new Vector3(8, 2.5f, 88), new Vector3(16, 2.5f, 98) }) Torch(at);
        }

        public GameObject Mechanism(Vector3 at, string title, bool lever)
        {
            var mechanism = new GameObject(title); mechanism.transform.SetParent(root, false); mechanism.transform.position = at;
            var collider = mechanism.AddComponent<BoxCollider>(); collider.center = Vector3.up; collider.size = new Vector3(1.1f, 2, 1.1f);
            LocalBox(mechanism.transform, "Stone plinth", Vector3.up * .5f, new Vector3(.9f, 1, .9f), trim, false);
            var top = LocalBox(mechanism.transform, lever ? "Brass lever" : "Rune tablet", Vector3.up * 1.15f,
                lever ? new Vector3(.15f, 1.0f, .15f) : new Vector3(.8f, .16f, .6f), gold, false);
            top.transform.localRotation = Quaternion.Euler(lever ? -35 : -20, 0, 0);
            Inscription(at + Vector3.up * 1.8f, title, 180);
            return mechanism;
        }

        public Transform Actor(Transform owner, bool enemy, bool human)
        {
            LocalShape(owner, PrimitiveType.Capsule, "Cloak and cuirass", new Vector3(0, .95f, 0), new Vector3(.62f, .58f, .45f), enemy ? human ? iron : blood : cloth);
            LocalShape(owner, PrimitiveType.Sphere, "Head", new Vector3(0, 1.67f, 0), new Vector3(.39f, .42f, .36f), human || !enemy ? iron : bone);
            for (int s = -1; s <= 1; s += 2)
            {
                LocalBox(owner, "Boot", new Vector3(s * .16f, .3f, .02f), new Vector3(.20f, .58f, .26f), iron, false);
                LocalBox(owner, "Arm", new Vector3(s * .38f, 1.08f, .03f), new Vector3(.16f, .62f, .18f), enemy ? bone : cloth, false);
                if (enemy && !human)
                {
                    var horn = LocalBox(owner, "Demonic horn", new Vector3(s * .22f, 1.92f, 0), new Vector3(.10f, .50f, .12f), bone, false);
                    horn.transform.localRotation = Quaternion.Euler(0, 0, -s * 30);
                }
                LocalShape(owner, PrimitiveType.Sphere, "Eye", new Vector3(s * .09f, 1.72f, .17f), Vector3.one * .055f, enemy ? glow : glass);
            }
            var sword = new GameObject("Sword"); sword.transform.SetParent(owner, false); sword.transform.localPosition = new Vector3(.4f, 1.1f, .20f);
            LocalBox(sword.transform, "Blade", new Vector3(0, 0, .54f), new Vector3(.1f, .06f, 1.1f), trim, false);
            LocalBox(sword.transform, "Crossguard", Vector3.zero, new Vector3(.4f, .1f, .1f), gold, false);
            return sword.transform;
        }

        private void Window(Vector3 position, float yaw)
        {
            var window = new GameObject("Gothic moonlit window"); window.transform.SetParent(root, false);
            window.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            LocalBox(window.transform, "Stained glass", Vector3.zero, new Vector3(2.3f, 3.8f, .07f), glass, false);
            for (int i = -1; i <= 1; i++) LocalBox(window.transform, "Lead mullion", new Vector3(i * .6f, 0, -.05f), new Vector3(.07f, 3.8f, .08f), gold, false);
            LocalBox(window.transform, "Window transom", new Vector3(0, -.3f, -.05f), new Vector3(2.4f, .08f, .08f), gold, false);
            var rose = LocalShape(window.transform, PrimitiveType.Cylinder, "Rose tracery", new Vector3(0, 1, -.1f), new Vector3(.8f, .03f, .8f), gold);
            rose.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var light = window.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(.3f, .45f, .85f); light.range = 10; light.intensity = 2;
        }

        private void Shelf(Vector3 at, float yaw)
        {
            var shelf = new GameObject("Forbidden books"); shelf.transform.SetParent(root, false); shelf.transform.SetPositionAndRotation(at, Quaternion.Euler(0,yaw,0));
            LocalBox(shelf.transform, "Shelf back", new Vector3(0, 1.4f, .4f), new Vector3(2.2f, 2.8f, .2f), iron, true);
            for (int i = 0; i < 3; i++)
            {
                LocalBox(shelf.transform, "Shelf", new Vector3(0, .35f + i * .95f, 0), new Vector3(2.4f, .12f, 1), stone, false);
                for (int j = 0; j < 6; j++) LocalBox(shelf.transform, "Book", new Vector3(-.9f + j * .33f, .66f + i * .95f, 0),
                    new Vector3(.2f, .5f + (j % 2) * .1f, .42f), j % 2 == 0 ? blood : gold, false);
            }
        }

        private void Column(Vector3 at, float height)
        {
            Shape(PrimitiveType.Cylinder, "Gothic column", at + Vector3.up * height * .5f, new Vector3(.5f, height * .5f, .5f), trim, true);
            Box("Column base", at + Vector3.up * .2f, new Vector3(.9f, .4f, .9f), stone);
            Box("Column capital", at + Vector3.up * height, new Vector3(.85f, .3f, .85f), trim, false);
        }

        private void Torch(Vector3 at)
        {
            Box("Torch sconce", at - Vector3.up * .4f, new Vector3(.12f, .8f, .12f), iron, false);
            Shape(PrimitiveType.Sphere, "Torch flame", at, new Vector3(.17f, .35f, .17f), glow, false);
            var lamp = new GameObject("Torch light"); lamp.transform.SetParent(root, false); lamp.transform.position = at;
            var light = lamp.AddComponent<Light>(); light.type = LightType.Point;
            light.color = new Color(1, .52f, .19f); light.intensity = 3.4f; light.range = 9;
            TorchLights.Add(light);
        }

        public void Inscription(Vector3 position, string text, float yaw)
        {
            var sign = new GameObject("Inscription"); sign.transform.SetParent(root, false);
            // TextMesh's readable face points along -Z, opposite the architectural facing convention.
            sign.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw - 180, 0));
            var label = sign.AddComponent<TextMesh>(); label.text = text; label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center; label.fontSize = 48; label.characterSize = .035f;
            label.color = new Color(.82f, .76f, .60f);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // The built-in GUI font material ignores scene depth and reveals labels through stone.
            sign.GetComponent<Renderer>().sharedMaterial = worldText;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, Material material, bool solid = true)
            => Shape(PrimitiveType.Cube, name, position, size, material, solid);

        private GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 size, Material material, bool solid)
        {
            var shape = GameObject.CreatePrimitive(type); shape.name = name; shape.transform.SetParent(root, false);
            shape.transform.position = position; shape.transform.localScale = size;
            shape.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) { var collider = shape.GetComponent<Collider>(); collider.enabled = false; Object.Destroy(collider); }
            return shape;
        }

        private GameObject LocalBox(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool solid)
        {
            var shape = Shape(PrimitiveType.Cube, name, Vector3.zero, size, material, solid);
            shape.transform.SetParent(parent, false); shape.transform.localPosition = position;
            return shape;
        }

        private GameObject LocalShape(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 size, Material material)
        {
            var shape = Shape(type, name, Vector3.zero, size, material, false);
            shape.transform.SetParent(parent, false); shape.transform.localPosition = position;
            return shape;
        }

        private void Beam(string name, Vector3 from, Vector3 to, float width, Material material)
        {
            var beam = Box(name, (from + to) / 2, new Vector3(width, (to - from).magnitude, width), material, false);
            beam.transform.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }
    }
}
