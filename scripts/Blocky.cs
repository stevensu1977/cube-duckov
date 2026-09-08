using Godot;
using System.Collections.Generic;
namespace Duckov;

/// The Minecraft-style "texel" look for GLB models: every StandardMaterial3D gets a 16×16 grey noise albedo texture,
/// nearest-filtered and triplanar-mapped in object space at one tile per metre, so each face breaks into 1/16 m pixels
/// with 4 tone levels — the same texel grid the world shaders in Arena.cs use. Per-instance tints (ducks, barrels)
/// keep working because the texture only multiplies AlbedoColor.
public static class Blocky
{
    public const float TexelsPerMetre = 16f;
    static ImageTexture _noise;
    static readonly Dictionary<Material, StandardMaterial3D> _cache = new();

    public static ImageTexture Noise
    {
        get
        {
            if (_noise != null) return _noise;
            var rng = new RandomNumberGenerator { Seed = 99 };
            var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    // four tone levels, biased toward the base colour like a hand-drawn pixel texture
                    float r = rng.Randf();
                    float g = r < 0.45f ? 1.0f : r < 0.75f ? 0.93f : r < 0.92f ? 0.86f : 0.78f;
                    img.SetPixel(x, y, new Color(g, g, g));
                }
            _noise = ImageTexture.CreateFromImage(img);
            return _noise;
        }
    }

    /// Give one material the texel look (idempotent).
    public static StandardMaterial3D Texturize(StandardMaterial3D m)
    {
        // Textured models (Kenney colormap palettes) keep their own albedo; nearest filtering alone gives them the pixel edge
        if (m.AlbedoTexture != null) { m.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest; return m; }
        m.AlbedoTexture = Noise;
        m.Uv1Triplanar = true;
        m.Uv1TriplanarSharpness = 12f;
        m.Uv1Scale = new Vector3(1, 1, 1);        // one 16-texel tile per metre
        m.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        return m;
    }

    /// Override every surface of every mesh under `model` with a texturised copy of its material (copies are shared per
    /// source material, so a hundred crates still use one material).
    public static void Apply(Node model)
    {
        foreach (var mi in Meshes(model))
        {
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.GetSurfaceOverrideMaterial(s) != null) continue;
                if (mi.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D src) continue;
                if (!_cache.TryGetValue(src, out var dup))
                {
                    dup = Texturize((StandardMaterial3D)src.Duplicate());
                    dup.ResourceName = src.ResourceName;
                    _cache[src] = dup;
                }
                mi.SetSurfaceOverrideMaterial(s, dup);
            }
        }
    }

    public static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in Meshes(c)) yield return m;
    }
}
