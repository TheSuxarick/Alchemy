using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Rasterises the rune icons, dotted path and leader-dot sprites used by the Main Menu.</summary>
public static class MainMenuAssetGenerator
{
    private const string Dir = "Assets/Art/assets_for_menu/";

    [MenuItem("Tools/Main Menu/Generate Icon Sprites")]
    public static void Generate()
    {
        Save(Render(32, 32, 3, 3f, new List<List<Vector2>> { new() { V(16, 5), V(28, 27), V(4, 27), V(16, 5), V(28, 27) } }, null), "rune_fire");
        Save(Render(32, 32, 3, 3f, new List<List<Vector2>> { new() { V(6, 6), V(26, 6), V(26, 26), V(6, 26), V(6, 6), V(26, 6) } }, null), "rune_earth");

        var wave = new List<Vector2>();
        Quad(wave, V(3, 16), V(9, 5), V(16, 16));
        Quad(wave, V(16, 16), V(23, 27), V(29, 16));
        Save(Render(32, 32, 3, 3f, new List<List<Vector2>> { wave }, null), "rune_water");

        var circle = new List<Vector2>();
        for (int i = 0; i <= 96; i++)
        {
            float a = i / 96f * Mathf.PI * 2f;
            circle.Add(V(16 + 11 * Mathf.Cos(a), 16 + 11 * Mathf.Sin(a)));
        }
        Save(Render(32, 32, 3, 3f, new List<List<Vector2>> { circle }, null), "rune_air");

        // Dotted wavy path: stroke-dasharray "1 10" with round caps = 4px dots every 11 units, plus end dot r=7.
        var path = new List<Vector2>();
        Cubic(path, V(8, 40), V(40, 8), V(70, 8), V(100, 36));
        Cubic(path, V(100, 36), V(130, 64), V(160, 56), V(190, 30));
        Cubic(path, V(190, 30), V(220, 4), V(250, 6), V(296, 30));
        var dots = new List<(Vector2, float)>();
        float acc = 0f, next = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            float seg = (path[i + 1] - path[i]).magnitude;
            while (next <= acc + seg)
            {
                dots.Add((Vector2.Lerp(path[i], path[i + 1], (next - acc) / seg), 2f));
                next += 11f;
            }
            acc += seg;
        }
        dots.Add((V(296, 30), 7f));
        Save(Render(360, 60, 2, 0f, new List<List<Vector2>>(), dots), "wavy_path");

        var leader = new Texture2D(6, 3, TextureFormat.RGBA32, false);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 6; x++)
                leader.SetPixel(x, y, x < 3 ? Color.white : new Color(1, 1, 1, 0));
        leader.Apply();
        Save(leader, "dot_leader", true);
        AssetDatabase.Refresh();
    }

    private static Vector2 V(float x, float y) => new Vector2(x, y);

    private static void Cubic(List<Vector2> l, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int n = 200)
    {
        for (int i = l.Count == 0 ? 0 : 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            l.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
        }
    }

    private static void Quad(List<Vector2> l, Vector2 a, Vector2 b, Vector2 c, int n = 40)
    {
        for (int i = l.Count == 0 ? 0 : 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            l.Add(u * u * a + 2 * u * t * b + t * t * c);
        }
    }

    private static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return (p - (a + ab * t)).magnitude;
    }

    private static Texture2D Render(int vw, int vh, int k, float w, List<List<Vector2>> lines, List<(Vector2, float)> dots)
    {
        int W = vw * k, H = vh * k;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int row = 0; row < H; row++)      // row 0 = top of the picture
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2((x + 0.5f) / k, (row + 0.5f) / k); // viewbox space, y down
                float best = 1e9f;
                foreach (var l in lines)
                    for (int i = 0; i < l.Count - 1; i++)
                        best = Mathf.Min(best, DistSeg(p, l[i], l[i + 1]));
                float a = lines.Count > 0 ? Mathf.Clamp01((w / 2f - best) * k + 0.5f) : 0f;
                if (dots != null)
                    foreach (var d in dots)
                        a = Mathf.Max(a, Mathf.Clamp01((d.Item2 - (p - d.Item1).magnitude) * k + 0.5f));
                px[(H - 1 - row) * W + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static void Save(Texture2D tex, string name, bool tile = false)
    {
        var path = Dir + name + ".png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.filterMode = tile ? FilterMode.Point : FilterMode.Bilinear;
        imp.spritePixelsPerUnit = 100;
        if (tile) imp.wrapMode = TextureWrapMode.Repeat;
        var s = new TextureImporterSettings();
        imp.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        imp.SetTextureSettings(s);
        imp.SaveAndReimport();
    }
}
