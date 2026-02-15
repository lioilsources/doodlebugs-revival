using UnityEngine;

/// <summary>
/// Scrolls two copies of a foreground sprite infinitely to the left.
/// Uses PolygonCollider2D generated from the sprite's alpha channel
/// so bullets only collide with visible (opaque) parts.
/// Purely local/visual - no networking needed.
/// Called by BackgroundManager when background/foreground changes.
/// </summary>
public class ForegroundScroller : MonoBehaviour
{
    public static ForegroundScroller Instance { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteA;
    [SerializeField] private SpriteRenderer spriteB;

    [Header("Collider Settings")]
    [Tooltip("Simplification tolerance for PolygonCollider2D path. Higher = fewer vertices.")]
    [SerializeField] private float colliderSimplifyTolerance = 0.05f;

    private float _scrollSpeed;
    private float _spriteWorldWidth;
    private bool _active;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Set the foreground sprite and start scrolling.
    /// Pass null sprite to use a generated placeholder.
    /// </summary>
    public void SetForeground(Sprite sprite, float scrollSpeed, float yPosition, float scale)
    {
        // Use placeholder if no sprite provided
        if (sprite == null)
        {
            sprite = ForegroundSpriteGenerator.CreatePlaceholderForeground();
            Debug.Log("[ForegroundScroller] Using generated placeholder foreground sprite");
        }

        spriteA.sprite = sprite;
        spriteB.sprite = sprite;
        spriteA.enabled = true;
        spriteB.enabled = true;

        _scrollSpeed = scrollSpeed;
        _spriteWorldWidth = sprite.bounds.size.x * scale;

        spriteA.transform.localScale = Vector3.one * scale;
        spriteB.transform.localScale = Vector3.one * scale;

        float startX = Camera.main.transform.position.x;
        spriteA.transform.position = new Vector3(startX, yPosition, 0f);
        spriteB.transform.position = new Vector3(startX + _spriteWorldWidth, yPosition, 0f);

        RebuildPolygonCollider(spriteA);
        RebuildPolygonCollider(spriteB);

        _active = true;
    }

    /// <summary>
    /// Disable the foreground entirely.
    /// </summary>
    public void DisableForeground()
    {
        _active = false;
        spriteA.enabled = false;
        spriteB.enabled = false;
        DisableCollider(spriteA);
        DisableCollider(spriteB);
    }

    private void Update()
    {
        if (!_active) return;

        float delta = _scrollSpeed * Time.deltaTime;
        spriteA.transform.position += Vector3.right * delta;
        spriteB.transform.position += Vector3.right * delta;

        float camLeft = Camera.main.transform.position.x
            - Camera.main.orthographicSize * Camera.main.aspect;

        WrapIfNeeded(spriteA, spriteB, camLeft);
        WrapIfNeeded(spriteB, spriteA, camLeft);
    }

    private void WrapIfNeeded(SpriteRenderer moving, SpriteRenderer other, float camLeft)
    {
        float rightEdge = moving.transform.position.x + _spriteWorldWidth / 2f;
        if (rightEdge < camLeft)
        {
            float otherRightEdge = other.transform.position.x + _spriteWorldWidth / 2f;
            moving.transform.position = new Vector3(
                otherRightEdge + _spriteWorldWidth / 2f,
                moving.transform.position.y,
                0f
            );
        }
    }

    /// <summary>
    /// Builds a PolygonCollider2D from the sprite's alpha channel.
    /// Removes any existing BoxCollider2D or PolygonCollider2D and creates a fresh one.
    /// </summary>
    private void RebuildPolygonCollider(SpriteRenderer sr)
    {
        if (sr.sprite == null) return;

        var go = sr.gameObject;

        // Remove old colliders
        var oldBox = go.GetComponent<BoxCollider2D>();
        if (oldBox != null) Destroy(oldBox);

        var oldPoly = go.GetComponent<PolygonCollider2D>();
        if (oldPoly != null) Destroy(oldPoly);

        // Ensure the foreground GameObject is on the Foreground layer
        go.layer = LayerMask.NameToLayer("Foreground");

        // Create new PolygonCollider2D - Unity auto-generates the path from sprite physics shape
        var poly = go.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;

        // If the sprite has physics shape data, Unity uses it automatically.
        // Otherwise we generate paths from the sprite's texture alpha.
        if (poly.pathCount == 0 || poly.GetPath(0).Length == 0)
        {
            GenerateColliderFromAlpha(poly, sr.sprite);
        }

        // Simplify paths to reduce vertex count
        if (colliderSimplifyTolerance > 0f)
        {
            SimplifyPaths(poly);
        }

        Debug.Log($"[ForegroundScroller] PolygonCollider2D built: {poly.pathCount} paths, " +
                  $"{TotalVertexCount(poly)} vertices");
    }

    /// <summary>
    /// Generates PolygonCollider2D paths from sprite texture alpha channel.
    /// Traces the outline of opaque regions using a simple marching approach.
    /// </summary>
    private void GenerateColliderFromAlpha(PolygonCollider2D poly, Sprite sprite)
    {
        var tex = sprite.texture;

        // Ensure texture is readable
        if (!tex.isReadable)
        {
            Debug.LogWarning("[ForegroundScroller] Sprite texture is not readable, cannot generate collider from alpha. " +
                             "Enable Read/Write in texture import settings.");
            return;
        }

        int w = tex.width;
        int h = tex.height;
        var pixels = tex.GetPixels32();

        // Create a binary alpha mask
        bool[,] solid = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                solid[x, y] = pixels[y * w + x].a > 128;
            }
        }

        // Use sprite's rect and pivot to convert pixel coords to local coords
        Rect spriteRect = sprite.rect;
        Vector2 pivot = sprite.pivot;
        float ppu = sprite.pixelsPerUnit;

        // Trace the top edge of the solid region (simplified approach for terrain-like shapes)
        // For each column, find the highest solid pixel
        Vector2[] topEdge = new Vector2[w];
        Vector2[] bottomEdge = new Vector2[w];
        int validColumns = 0;

        for (int x = 0; x < w; x++)
        {
            int topY = -1;
            int bottomY = -1;

            for (int y = h - 1; y >= 0; y--)
            {
                if (solid[x, y])
                {
                    if (topY == -1) topY = y;
                    bottomY = y;
                }
            }

            if (topY != -1)
            {
                topEdge[validColumns] = PixelToLocal(x, topY + 1, pivot, ppu, spriteRect);
                bottomEdge[validColumns] = PixelToLocal(x, bottomY, pivot, ppu, spriteRect);
                validColumns++;
            }
        }

        if (validColumns < 2) return;

        // Build a closed polygon: top edge left-to-right, then bottom edge right-to-left
        // Sample every N pixels to reduce vertex count
        int step = Mathf.Max(1, validColumns / 128);
        var path = new System.Collections.Generic.List<Vector2>();

        // Top edge (left to right)
        for (int i = 0; i < validColumns; i += step)
            path.Add(topEdge[i]);
        if ((validColumns - 1) % step != 0)
            path.Add(topEdge[validColumns - 1]);

        // Bottom edge (right to left)
        for (int i = validColumns - 1; i >= 0; i -= step)
            path.Add(bottomEdge[i]);
        if (0 % step != 0)
            path.Add(bottomEdge[0]);

        poly.pathCount = 1;
        poly.SetPath(0, path.ToArray());
    }

    private Vector2 PixelToLocal(int px, int py, Vector2 pivot, float ppu, Rect spriteRect)
    {
        float localX = (px + spriteRect.x - pivot.x) / ppu;
        float localY = (py + spriteRect.y - pivot.y) / ppu;
        return new Vector2(localX, localY);
    }

    private void SimplifyPaths(PolygonCollider2D poly)
    {
        for (int i = 0; i < poly.pathCount; i++)
        {
            var path = poly.GetPath(i);
            if (path.Length <= 4) continue;

            var simplified = new System.Collections.Generic.List<Vector2>();
            simplified.Add(path[0]);

            for (int j = 1; j < path.Length - 1; j++)
            {
                // Keep point if it deviates enough from the line between prev and next
                Vector2 prev = simplified[simplified.Count - 1];
                Vector2 curr = path[j];
                Vector2 next = path[j + 1];

                float dist = DistancePointToLine(curr, prev, next);
                if (dist > colliderSimplifyTolerance)
                    simplified.Add(curr);
            }

            simplified.Add(path[path.Length - 1]);
            poly.SetPath(i, simplified.ToArray());
        }
    }

    private float DistancePointToLine(Vector2 point, Vector2 lineA, Vector2 lineB)
    {
        Vector2 ab = lineB - lineA;
        float abLen = ab.magnitude;
        if (abLen < 0.0001f) return Vector2.Distance(point, lineA);
        Vector2 ap = point - lineA;
        float cross = Mathf.Abs(ab.x * ap.y - ab.y * ap.x);
        return cross / abLen;
    }

    private int TotalVertexCount(PolygonCollider2D poly)
    {
        int total = 0;
        for (int i = 0; i < poly.pathCount; i++)
            total += poly.GetPath(i).Length;
        return total;
    }

    private void DisableCollider(SpriteRenderer sr)
    {
        var poly = sr.GetComponent<PolygonCollider2D>();
        if (poly != null) Destroy(poly);

        var box = sr.GetComponent<BoxCollider2D>();
        if (box != null) Destroy(box);
    }
}
