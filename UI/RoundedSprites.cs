using UnityEngine;

namespace VBloodHunter.UI;

// Procedurally drawn, 9-sliced sprites for rounded corners, outlines and soft shadows (uGUI has no
// rounded rectangles built in). Each is an anti-aliased signed-distance rounded box drawn once into a
// small texture. Image.pixelsPerUnitMultiplier then scales the corner to the radius each element wants.
internal static class RoundedSprites
{
    /// <summary>Corner radius the fill/outline textures are drawn with (texels).</summary>
    public const float SourceRadius = 16f;
    const int Size = 64;
    const float OutlineWidth = 2.5f;

    /// <summary>How far the shadow sprite extends past the shape it sits behind (texels = UI units).</summary>
    public const float ShadowSpread = 24f;
    const int ShadowSize = 112;

    static Sprite _fill, _outline, _shadow;

    public static Sprite Fill => _fill ??= Build("VBH.RoundedFill", Size, Size / 2f, SourceRadius, SourceRadius + 1,
        d => Mathf.Clamp01(0.5f - d));

    public static Sprite Outline => _outline ??= Build("VBH.RoundedOutline", Size, Size / 2f, SourceRadius, SourceRadius + 1,
        d => Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + OutlineWidth));

    public static Sprite Shadow => _shadow ??= Build("VBH.Shadow", ShadowSize, ShadowSize / 2f - ShadowSpread, SourceRadius,
        ShadowSpread + SourceRadius + 1,
        d => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, ShadowSpread - 2f, d)));

    /// <summary>Multiplier that makes a sliced sprite drawn at SourceRadius show a <paramref name="radius"/> corner.</summary>
    public static float MultiplierFor(float radius) => SourceRadius / Mathf.Max(0.5f, radius);

    static Sprite Build(string name, int size, float halfExtent, float radius, float border, Func<float, float> alpha)
    {
        var pixels = new Color32[size * size];
        float center = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = RoundedBoxDistance(x + 0.5f - center, y + 0.5f - center, halfExtent, radius);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(d) * 255f));
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// <summary>Signed distance from a point to a rounded box centred on the origin (negative = inside).</summary>
    static float RoundedBoxDistance(float px, float py, float halfExtent, float radius)
    {
        float qx = Mathf.Abs(px) - (halfExtent - radius);
        float qy = Mathf.Abs(py) - (halfExtent - radius);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }
}
