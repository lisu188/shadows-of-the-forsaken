using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Original low-poly mesh authoring. Called explicitly by CastleActorBuilder;
// all meshes are persisted assets, never allocated by the player runtime.
internal static class CastleActorMeshes
{
    internal const string Root = "Assets/LevelPresentation/Actors";
    private const string Meshes = Root + "/Meshes";
    private static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    internal static void Begin()
    {
        Folder(Root, "Meshes");
        Folder(Root, "Materials");
        cache.Clear();
    }

    internal static void Folder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    internal static Mesh Get(string name)
    {
        if (cache.TryGetValue(name, out var cached)) return cached;
        var shape = new Shape();
        switch (name)
        {
            case "Torso": shape.Rings(new[] { -.5f, -.25f, .28f, .5f }, new[] { .34f, .37f, .53f, .42f }, new[] { .38f, .45f, .5f, .35f }, 8); break;
            case "Head": shape.Rings(new[] { -.5f, -.32f, .2f, .43f, .5f }, new[] { .28f, .44f, .47f, .34f, .12f }, new[] { .32f, .45f, .5f, .4f, .14f }, 10); break;
            case "Limb": shape.Rings(new[] { -1f, -.85f, -.12f, 0f }, new[] { .31f, .36f, .5f, .4f }, new[] { .33f, .4f, .48f, .35f }, 8); break;
            case "Plate": shape.Rings(new[] { -.5f, -.18f, .35f, .5f }, new[] { .24f, .47f, .5f, .34f }, new[] { .32f, .5f, .5f, .3f }, 6); break;
            case "Boot": shape.Box(new Vector3(-.5f, 0, -.3f), new Vector3(.5f, 1, .7f)); break;
            case "Band": shape.Box(Vector3.one * -.5f, Vector3.one * .5f); break;
            case "Blade": shape.Rings(new[] { 0f, .08f, .78f, 1f }, new[] { .35f, .5f, .36f, 0f }, new[] { .22f, .25f, .18f, 0f }, 4); break;
            case "HornRight": shape.Horn(1); break;
            case "HornLeft": shape.Horn(-1); break;
            case "Claw": shape.Tube(new[] { Vector3.zero, new Vector3(0, -.1f, .07f), new Vector3(0, -.23f, .2f), new Vector3(0, -.2f, .3f) }, new[] { .05f, .045f, .027f, 0f }, 6); break;
            case "Cape": shape.Cloth(false); break;
            case "Tabard": shape.Cloth(true); break;
            case "Crest": shape.Crest(); break;
            case "Cleaver": shape.Cleaver(); break;
            default: throw new ArgumentException("Unknown actor mesh: " + name);
        }
        Mesh generated = shape.Finish(name);
        string path = Meshes + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = generated; AssetDatabase.CreateAsset(mesh, path); }
        else
        {
            EditorUtility.CopySerialized(generated, mesh);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(mesh);
        }
        cache.Add(name, mesh);
        return mesh;
    }

    private sealed class Shape
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int n = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
        }
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c); Tri(a, c, d);
        }
        internal void Rings(float[] ys, float[] widths, float[] depths, int sides)
        {
            var rings = new Vector3[ys.Length, sides];
            for (int row = 0; row < ys.Length; row++)
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides;
                    rings[row, i] = new Vector3(Mathf.Cos(angle) * widths[row], ys[row], Mathf.Sin(angle) * depths[row]);
                }
            for (int row = 0; row < ys.Length - 1; row++)
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Quad(rings[row, i], rings[row + 1, i], rings[row + 1, next], rings[row, next]);
                }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Tri(new Vector3(0, ys[0], 0), rings[0, i], rings[0, next]);
                int last = ys.Length - 1;
                Tri(new Vector3(0, ys[last], 0), rings[last, next], rings[last, i]);
            }
        }
        internal void Box(Vector3 lo, Vector3 hi)
        {
            var a = new Vector3(lo.x, lo.y, lo.z); var b = new Vector3(hi.x, lo.y, lo.z);
            var c = new Vector3(hi.x, hi.y, lo.z); var d = new Vector3(lo.x, hi.y, lo.z);
            var e = new Vector3(lo.x, lo.y, hi.z); var f = new Vector3(hi.x, lo.y, hi.z);
            var g = new Vector3(hi.x, hi.y, hi.z); var h = new Vector3(lo.x, hi.y, hi.z);
            Quad(b, a, d, c); Quad(e, f, g, h); Quad(a, e, h, d);
            Quad(f, b, c, g); Quad(d, h, g, c); Quad(a, b, f, e);
        }
        internal void Tube(Vector3[] centers, float[] radii, int sides)
        {
            var rings = new Vector3[centers.Length, sides];
            for (int row = 0; row < centers.Length; row++)
            {
                Vector3 tangent = (centers[Mathf.Min(row + 1, centers.Length - 1)] - centers[Mathf.Max(0, row - 1)]).normalized;
                Vector3 side = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (side.sqrMagnitude < .1f) side = Vector3.right;
                Vector3 outward = Vector3.Cross(side, tangent).normalized;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides;
                    rings[row, i] = centers[row] + (side * Mathf.Cos(angle) + outward * Mathf.Sin(angle)) * radii[row];
                }
            }
            for (int row = 0; row < centers.Length - 1; row++)
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    Quad(rings[row, i], rings[row + 1, i], rings[row + 1, next], rings[row, next]);
                }
            for (int i = 0; i < sides; i++) Tri(centers[0], rings[0, i], rings[0, (i + 1) % sides]);
        }
        internal void Horn(float side) => Tube(new[] { Vector3.zero, new Vector3(side * .1f, .17f, 0),
            new Vector3(side * .19f, .35f, -.1f), new Vector3(side * .14f, .51f, -.23f),
            new Vector3(side * .02f, .57f, -.3f) }, new[] { .12f, .1f, .075f, .035f, 0 }, 8);
        internal void Cloth(bool front)
        {
            const int columns = 6, rows = 5;
            var points = new Vector3[rows, columns + 1];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float down = y / (float)(rows - 1), across = x / (float)columns * 2 - 1;
                    float hem = y == rows - 1 ? (x % 2 == 0 ? .06f : -.03f) : 0;
                    points[y, x] = new Vector3(across * Mathf.Lerp(front ? .42f : .32f, .5f, down),
                        -down + hem, (front ? 1 : -1) * (.03f + down * .18f + .045f * Mathf.Cos(across * Mathf.PI * 3)));
                }
            for (int y = 0; y < rows - 1; y++)
                for (int x = 0; x < columns; x++)
                {
                    Vector3 a = points[y, x], b = points[y + 1, x], c = points[y + 1, x + 1], d = points[y, x + 1];
                    Quad(a, b, c, d); Quad(d, c, b, a);
                }
        }
        internal void Crest()
        {
            var top = new Vector3(0, .5f, 0); var left = new Vector3(-.5f, 0, 0);
            var bottom = new Vector3(0, -.5f, 0); var right = new Vector3(.5f, 0, 0);
            var center = new Vector3(0, 0, .2f);
            Tri(top, left, center); Tri(left, bottom, center); Tri(bottom, right, center); Tri(right, top, center);
            Tri(top, bottom, left); Tri(top, right, bottom);
        }
        internal void Cleaver()
        {
            // Asymmetric executioner's blade with a hooked point.
            var outline = new[] { new Vector2(-.12f, 0), new Vector2(.15f, 0), new Vector2(.32f, .67f),
                new Vector2(.2f, 1), new Vector2(-.24f, .88f), new Vector2(-.3f, .35f) };
            for (int i = 0; i < outline.Length; i++)
            {
                int j = (i + 1) % outline.Length;
                Vector3 a = new Vector3(outline[i].x, outline[i].y, -.035f), b = new Vector3(outline[j].x, outline[j].y, -.035f);
                Vector3 c = new Vector3(b.x, b.y, .035f), d = new Vector3(a.x, a.y, .035f);
                Quad(a, b, c, d);
                Tri(new Vector3(0, .45f, -.035f), b, a);
                Tri(new Vector3(0, .45f, .035f), d, c);
            }
        }
        internal Mesh Finish(string name)
        {
            var mesh = new Mesh { name = "Forsaken " + name };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) uv[i] = new Vector2(vertices[i].x, vertices[i].y);
            mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
