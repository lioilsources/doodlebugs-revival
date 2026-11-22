using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vertical two-stop gradient on any Graphic (Prompts/31). Works on sliced
/// sprites too: vertex colours are recomputed from each vertex's height in
/// the rect, and a sliced mesh has vertices at its top and bottom edges, so
/// the gradient is exact. Multiplies the colour the Graphic already has.
/// </summary>
public class UiGradient : BaseMeshEffect
{
    public Color Top = Color.white;
    public Color Bottom = Color.white;

    public static UiGradient Set(Graphic g, Color top, Color bottom)
    {
        var e = g.GetComponent<UiGradient>() ?? g.gameObject.AddComponent<UiGradient>();
        e.Top = top;
        e.Bottom = bottom;
        e.enabled = true;
        g.SetVerticesDirty();
        return e;
    }

    public static void Clear(Graphic g)
    {
        var e = g.GetComponent<UiGradient>();
        if (e != null && e.enabled) { e.enabled = false; g.SetVerticesDirty(); }
    }

    private static readonly List<UIVertex> Verts = new();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        var rect = graphic.rectTransform.rect;
        Verts.Clear();
        vh.GetUIVertexStream(Verts);
        for (int i = 0; i < Verts.Count; i++)
        {
            var v = Verts[i];
            float t = Mathf.InverseLerp(rect.yMin, rect.yMax, v.position.y);
            v.color = (Color32)((Color)v.color * Color.Lerp(Bottom, Top, t));
            Verts[i] = v;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(Verts);
    }
}

/// <summary>
/// Letter spacing for a uGUI Text (it has none). Shifts every glyph quad by
/// its index times <see cref="Em"/> x font size, then re-centres the line for
/// centred / right alignment. Whitespace emits no quad, so a space counts as
/// no extra gap - close enough for labels and titles.
/// </summary>
[RequireComponent(typeof(Text))]
public class UiTracking : BaseMeshEffect, ILayoutElement
{
    public float Em;

    // Layout: report the width the spacing adds, so pills and AutoText grow with it
    private Text T => graphic as Text;
    public float minWidth => -1f;
    public float preferredWidth => T == null ? -1f : T.preferredWidth + Mathf.Max(0, Glyphs(T.text) - 1) * Em * T.fontSize;
    public float flexibleWidth => -1f;
    public float minHeight => -1f;
    public float preferredHeight => -1f;
    public float flexibleHeight => -1f;
    public int layoutPriority => 2;
    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }

    private static int Glyphs(string s)
    {
        int n = 0;
        foreach (char c in s) if (!char.IsWhiteSpace(c)) n++;
        return n;
    }

    private static readonly List<UIVertex> Verts = new();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || Mathf.Abs(Em) < 0.001f) return;
        var text = graphic as Text;
        if (text == null || text.text.Contains("\n")) return;
        if (text.cachedTextGenerator.lineCount > 1) return;   // wrapped: leave it alone
        Verts.Clear();
        vh.GetUIVertexStream(Verts);
        int quads = Verts.Count / 6;
        if (quads < 2) return;
        int size = text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0
            ? text.cachedTextGenerator.fontSizeUsedForBestFit : text.fontSize;
        float step = Em * size;
        float shift = 0f;
        switch (text.alignment)
        {
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter: shift = -step * (quads - 1) * 0.5f; break;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight: shift = -step * (quads - 1); break;
        }
        for (int q = 0; q < quads; q++)
        {
            float dx = q * step + shift;
            for (int k = 0; k < 6; k++)
            {
                var v = Verts[q * 6 + k];
                v.position.x += dx;
                Verts[q * 6 + k] = v;
            }
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(Verts);
    }
}
