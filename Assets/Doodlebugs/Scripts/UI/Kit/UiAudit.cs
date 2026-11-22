using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Development check (Prompts/30 §6): text under 12 units or a button under
/// 48 units tall on the MenuCanvas is a phone-readability bug. Logs a warning
/// with the hierarchy path; runs only in the editor and development builds.
/// </summary>
public static class UiAudit
{
    public static void Check(Transform root)
    {
        if (!Debug.isDebugBuild || root == null) return;
        foreach (var t in root.GetComponentsInChildren<Text>(false))
        {
            if (t.fontSize < UiTheme.MinText && !t.resizeTextForBestFit)
                Debug.LogWarning($"[UiAudit] text {t.fontSize} < {UiTheme.MinText}: {Path(t.transform)}");
        }
        foreach (var b in root.GetComponentsInChildren<Button>(false))
        {
            var r = (RectTransform)b.transform;
            float h = r.rect.height;
            if (h > 0f && h < UiTheme.MinTouch - 6f)
                Debug.LogWarning($"[UiAudit] touch target {h:0} < {UiTheme.MinTouch}: {Path(b.transform)}");
        }
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
