using UnityEngine;

/// <summary>
/// Generates placeholder foreground sprites at runtime.
/// Creates a silhouette texture (e.g. hills/ruins) with transparent gaps
/// so PolygonCollider2D can be generated from the alpha channel.
/// </summary>
public static class ForegroundSpriteGenerator
{
    /// <summary>
    /// Creates a placeholder foreground sprite matching background width.
    /// Width matches the background (4096 by default), height is the bottom quarter.
    /// The bottom portion is opaque (dark), the top and gaps are transparent.
    /// </summary>
    public static Sprite CreatePlaceholderForeground(int width = 4096, int height = 2732, float pixelsPerUnit = 100f)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        var pixels = new Color32[width * height];

        // Fill all transparent first
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        // Generate a hilly terrain profile using layered sine waves
        float[] heightMap = new float[width];
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / width;
            float h = 0.35f; // base height ratio
            h += 0.15f * Mathf.Sin(t * Mathf.PI * 2f * 1.3f);
            h += 0.08f * Mathf.Sin(t * Mathf.PI * 2f * 3.7f + 1.2f);
            h += 0.05f * Mathf.Sin(t * Mathf.PI * 2f * 7.1f + 0.8f);

            // Add a gap/archway in the middle area
            float gapCenter = 0.5f;
            float gapWidth = 0.08f;
            float distFromGap = Mathf.Abs(t - gapCenter);
            if (distFromGap < gapWidth)
            {
                float gapFactor = 1f - (distFromGap / gapWidth);
                h -= 0.25f * gapFactor * gapFactor;
            }

            // Add a second smaller gap
            float gap2Center = 0.8f;
            float gap2Width = 0.05f;
            float distFromGap2 = Mathf.Abs(t - gap2Center);
            if (distFromGap2 < gap2Width)
            {
                float gapFactor = 1f - (distFromGap2 / gap2Width);
                h -= 0.2f * gapFactor * gapFactor;
            }

            heightMap[x] = Mathf.Clamp01(h);
        }

        // Fill pixels below the height map with a dark color
        var solidColor = new Color32(30, 20, 15, 255);
        var edgeColor = new Color32(50, 35, 25, 255);

        for (int x = 0; x < width; x++)
        {
            int maxY = Mathf.RoundToInt(heightMap[x] * height);
            for (int y = 0; y < maxY && y < height; y++)
            {
                // Slightly lighter at the top edge for visual definition
                Color32 col = (y >= maxY - 2) ? edgeColor : solidColor;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(
            tex,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit
        );
    }
}
