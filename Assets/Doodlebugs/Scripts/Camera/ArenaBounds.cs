using UnityEngine;

/// <summary>
/// The four arena edges (Left, Right, Ground, Space) in one place.
///
/// ScreenSetup creates them at runtime and registers them here; everything
/// that used to <c>GameObject.Find("Left")</c> or compare
/// <c>collider.name == "Space"</c> - five scripts, each with its own copy of
/// the string - asks this instead. The name lookup survives only as a
/// fallback for scripts that run before ScreenSetup.Awake, and as the one
/// place the names are spelled.
/// </summary>
public static class ArenaBounds
{
    public enum Edge { Left = 0, Right = 1, Ground = 2, Space = 3 }

    private static readonly Collider2D[] _edges = new Collider2D[4];

    /// <summary>The runtime object's name for an edge - ScreenSetup names
    /// its border objects after the enum.</summary>
    public static string NameOf(Edge edge) => edge.ToString();

    /// <summary>ScreenSetup: hand over a freshly created border.</summary>
    public static void Register(Edge edge, Collider2D collider) => _edges[(int)edge] = collider;

    /// <summary>The edge collider, or null if ScreenSetup has not created it yet.
    /// A destroyed border (scene reload) is looked up again, never cached dead.</summary>
    public static Collider2D Get(Edge edge)
    {
        var c = _edges[(int)edge];
        if (c == null)
        {
            var go = GameObject.Find(NameOf(edge));
            c = go != null ? go.GetComponent<Collider2D>() : null;
            _edges[(int)edge] = c;
        }
        return c;
    }

    public static Collider2D Left => Get(Edge.Left);
    public static Collider2D Right => Get(Edge.Right);
    public static Collider2D Ground => Get(Edge.Ground);
    public static Collider2D Space => Get(Edge.Space);

    /// <summary>True when <paramref name="other"/> is this edge's collider.</summary>
    public static bool Is(Component other, Edge edge)
    {
        if (other == null) return false;
        var c = Get(edge);
        return c != null && c.gameObject == other.gameObject;
    }

    public static bool IsSpace(Component other) => Is(other, Edge.Space);
}
