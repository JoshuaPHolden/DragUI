using UnityEngine;

namespace DragUI.UI;

// Small icons drawn in code (no image files to ship or license). Each is a white shape, anti-aliased by
// supersampling, drawn once into a texture at twice the size it's shown so it stays sharp when the UI is
// scaled up; tint it with the Image's colour.
internal static class Icons
{
    const int Size = 32;    // texels (shown at about 16 UI units)
    const int Samples = 4;  // per axis, for anti-aliasing

    static Sprite _resizeHorizontal;

    /// <summary>Column-resize icon: a bar between two arrowheads pointing out (like the ◀|▶ cursor).</summary>
    public static Sprite ResizeHorizontal => _resizeHorizontal ??= Build("DragUI.ResizeHorizontal", (x, y) =>
    {
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
        bool bar = ax <= 1.4f && ay <= 11f;
        bool arrowhead = ax >= 4.5f && ax <= 14.5f && ay <= 7f * (14.5f - ax) / 10f;
        return bar || arrowhead;
    });

    /// <param name="inside">Is a point inside the shape? Coordinates are texels from the centre.</param>
    static Sprite Build(string name, Func<float, float, bool> inside)
    {
        var pixels = new Color32[Size * Size];
        float center = Size / 2f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Samples; sy++)
                    for (int sx = 0; sx < Samples; sx++)
                        if (inside(x + (sx + 0.5f) / Samples - center, y + (sy + 0.5f) / Samples - center)) hits++;
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / (Samples * Samples)));
            }
        }

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
