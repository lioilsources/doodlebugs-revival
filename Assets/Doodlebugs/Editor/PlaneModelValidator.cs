using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Doodlebugs -> Validate Plane Models
///
/// Re-runs the envelope contract from tools/planes/gate.py over every model
/// PlaneModelCatalog lists (Prompts/23-CLAUDE-PLAN-plane-shapes.md, section
/// 1). The gate is what makes shapes fair: PlaneHolder's BoxCollider2D is
/// one shared 50x50 px box at the sprite centre, so every model must keep
/// solid body under it, be about the same size and sit centred, nose right.
/// Reads the PNGs straight from disk (LoadImage), so import settings don't
/// matter here. Prints a table; a failing gate is a LogError.
///
/// Thresholds mirror gate.py - change both or neither.
/// </summary>
public static class PlaneModelValidator
{
    private const int Size = 128;
    private const int CoreMin = 39, CoreMax = 88;        // inclusive - 50x50 px hitbox footprint
    private const float CoreMinCoverage = 0.55f;         // G1 (BiPlane1 itself: 0.66)
    private const int WidthMin = 96, WidthMax = 118;     // G2
    private const int HeightMin = 44, HeightMax = 72;
    private const float FillMin = 0.42f, FillMax = 0.66f; // G3
    private const float CentroidTol = 8f;                // G4 (mass centroid vs canvas centre; BiPlane1 sits 4.7 px high)
    private const int NoseMin = 108, NoseMax = 122;      // G5
    private const int TailMin = 4, TailMax = 18;
    private const int Margin = 3;                        // G6
    private const float LiveryMin = 0.35f;               // G7 - red livery share of the body

    private const string BasePlanePng = "Assets/Doodlebugs/Sprites/BiPlane/BiPlane1.png";
    private const string ModelsDir = "Assets/Doodlebugs/Resources/Sprites/PlaneModels";

    [MenuItem("Doodlebugs/Validate Plane Models")]
    public static void Validate()
    {
        var table = new StringBuilder();
        table.AppendLine("model         w    h   fill  core  cx     cy     nose tail red   gates");
        int shipped = 0, failed = 0;

        foreach (var def in PlaneModelCatalog.All)
        {
            string path = def.Id == PlaneModelCatalog.BaseModelId
                ? BasePlanePng
                : $"{ModelsDir}/model_{def.Key}.png";
            if (!File.Exists(path))
            {
                table.AppendLine($"{def.Key,-13} (not shipped)");
                continue;
            }
            shipped++;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            Object.DestroyImmediate(tex);

            var fails = new StringBuilder();
            if (w != Size || h != Size)
            {
                fails.Append($"size {w}x{h} ");
            }

            var m = Measure(px, w, h);
            if (m.core < CoreMinCoverage) fails.Append("G1core ");
            if (m.w < WidthMin || m.w > WidthMax || m.h < HeightMin || m.h > HeightMax) fails.Append("G2extent ");
            if (m.fill < FillMin || m.fill > FillMax) fails.Append("G3mass ");
            if (Mathf.Abs(m.cx - 64f) > CentroidTol || Mathf.Abs(m.cy - 64f) > CentroidTol) fails.Append("G4centre ");
            if (m.nose < NoseMin || m.nose > NoseMax || m.tail < TailMin || m.tail > TailMax) fails.Append("G5nose ");
            if (m.marginHit) fails.Append("G6margin ");
            if (m.red < LiveryMin) fails.Append("G7livery ");

            if (def.Id != PlaneModelCatalog.BaseModelId)
            {
                string maskPath = $"{ModelsDir}/model_{def.Key}_mask.png";
                if (!File.Exists(maskPath)) fails.Append("mask-missing ");
                else if (!MaskMatches(maskPath, px, w, h)) fails.Append("mask-alpha ");
            }

            bool ok = fails.Length == 0;
            if (!ok) failed++;
            table.AppendLine(
                $"{def.Key,-13} {m.w,3} {m.h,4}  {m.fill:0.00}  {m.core:0.00}  {m.cx,5:0.0}  {m.cy,5:0.0}  {m.nose,4} {m.tail,4} {m.red:0.00}  " +
                (ok ? "PASS" : "FAIL " + fails.ToString().TrimEnd()));
        }

        table.AppendLine($"{shipped} model(s) checked, {failed} failing");
        if (failed > 0) Debug.LogError("[PlaneModelValidator]\n" + table);
        else Debug.Log("[PlaneModelValidator]\n" + table);
    }

    private struct Metrics
    {
        public int w, h, nose, tail;
        public float fill, core, cx, cy, red;
        public bool marginHit;
    }

    // LoadImage stores rows bottom-up; everything below works in image
    // space (y = 0 at the top) so the numbers match gate.py exactly.
    private static Metrics Measure(Color32[] px, int w, int h)
    {
        int minX = w, maxX = -1, minY = h, maxY = -1;
        long sumX = 0, sumY = 0;
        int opaque = 0, core = 0, red = 0;
        bool marginHit = false;

        for (int yImg = 0; yImg < h; yImg++)
        {
            int row = (h - 1 - yImg) * w;
            for (int x = 0; x < w; x++)
            {
                var c = px[row + x];
                if (c.a < 128) continue;
                opaque++;
                sumX += x; sumY += yImg;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (yImg < minY) minY = yImg;
                if (yImg > maxY) maxY = yImg;
                if (x >= CoreMin && x <= CoreMax && yImg >= CoreMin && yImg <= CoreMax) core++;
                if (x < Margin || x >= w - Margin || yImg < Margin || yImg >= h - Margin) marginHit = true;
                if (IsLiveryRed(c)) red++;
            }
        }

        var m = new Metrics { marginHit = marginHit };
        if (opaque == 0) return m;
        m.w = maxX - minX + 1;
        m.h = maxY - minY + 1;
        m.nose = maxX;
        m.tail = minX;
        m.fill = opaque / (float)(m.w * m.h);
        m.core = core / (float)((CoreMax - CoreMin + 1) * (CoreMax - CoreMin + 1));
        m.cx = sumX / (float)opaque;
        m.cy = sumY / (float)opaque;
        m.red = red / (float)opaque;
        return m;
    }

    // Same rule as gate.py is_livery_red: a red pixel, tolerant of the
    // tinted and dark reds a quantised AI render produces - BiPlane1's own
    // (v,0,0) shading (v >= 141) passes trivially; near-black outlines don't.
    private static bool IsLiveryRed(Color32 c)
    {
        int gb = Mathf.Max(c.g, c.b);
        return c.r >= 70 && c.r > 1.8f * gb && c.r - gb >= 35;
    }

    private static bool MaskMatches(string maskPath, Color32[] basePx, int w, int h)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(maskPath));
        bool ok = tex.width == w && tex.height == h;
        if (ok)
        {
            var mp = tex.GetPixels32();
            for (int i = 0; i < mp.Length && ok; i++)
            {
                ok = (mp[i].a >= 128) == (basePx[i].a >= 128);
            }
        }
        Object.DestroyImmediate(tex);
        return ok;
    }
}
