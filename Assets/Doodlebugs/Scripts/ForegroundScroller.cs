using UnityEngine;

/// <summary>
/// Scrolls two copies of a foreground sprite infinitely to the left.
/// Splits each sprite into small destructible tiles with BoxCollider2D
/// so bullets destroy individual tiles on contact.
/// Purely local/visual - no networking needed.
/// Called by BackgroundManager when background/foreground changes.
/// </summary>
public class ForegroundScroller : MonoBehaviour
{
    public static ForegroundScroller Instance { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteA;
    [SerializeField] private SpriteRenderer spriteB;

    [Header("Tile Settings")]
    [Tooltip("Size of each destructible tile in pixels.")]
    [SerializeField] private int tilePixelSize = 100;

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
        if (sprite == null)
        {
            sprite = ForegroundSpriteGenerator.CreatePlaceholderForeground();
            Debug.Log("[ForegroundScroller] Using generated placeholder foreground sprite");
        }

        // Clean up any existing tiles
        DestroyTiles(spriteA);
        DestroyTiles(spriteB);

        spriteA.sprite = sprite;
        spriteB.sprite = sprite;
        spriteA.enabled = true;
        spriteB.enabled = true;

        _scrollSpeed = scrollSpeed;
        _spriteWorldWidth = sprite.bounds.size.x * scale;

        spriteA.transform.localScale = Vector3.one * scale;
        spriteB.transform.localScale = Vector3.one * scale;

        // Position SpriteA so its left edge aligns with camera left edge
        float camLeft = Camera.main.transform.position.x
            - Camera.main.orthographicSize * Camera.main.aspect;
        float startX = camLeft + _spriteWorldWidth / 2f;
        spriteA.transform.position = new Vector3(startX, yPosition, 0f);
        spriteB.transform.position = new Vector3(startX + _spriteWorldWidth, yPosition, 0f);

        BuildTiles(spriteA);
        BuildTiles(spriteB);

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
        DestroyTiles(spriteA);
        DestroyTiles(spriteB);
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
            // Regenerate tiles so foreground "heals" when scrolling back
            DestroyTiles(moving);
            BuildTiles(moving);
        }
    }

    /// <summary>
    /// Splits a SpriteRenderer's sprite into a grid of small tile GameObjects.
    /// Each tile has its own SpriteRenderer, BoxCollider2D, and ForegroundTile script.
    /// The main SpriteRenderer is disabled since tiles replace it visually.
    /// </summary>
    private void BuildTiles(SpriteRenderer sr)
    {
        if (sr.sprite == null) return;

        var tex = sr.sprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogWarning("[ForegroundScroller] Sprite texture is not readable. " +
                             "Enable Read/Write in texture import settings.");
            return;
        }

        int texW = tex.width;
        int texH = tex.height;
        var pixels = tex.GetPixels32();
        float ppu = sr.sprite.pixelsPerUnit;

        // Account for possible texture downscaling (maxTextureSize < source)
        float ppuScale = (float)texW / sr.sprite.rect.width;
        float adjustedPPU = ppu * ppuScale;

        int foregroundLayer = LayerMask.NameToLayer("Foreground");
        int cols = Mathf.CeilToInt((float)texW / tilePixelSize);
        int rows = Mathf.CeilToInt((float)texH / tilePixelSize);
        int tileCount = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int px = col * tilePixelSize;
                int py = row * tilePixelSize;
                int w = Mathf.Min(tilePixelSize, texW - px);
                int h = Mathf.Min(tilePixelSize, texH - py);

                if (!HasOpaquePixel(pixels, texW, px, py, w, h))
                    continue;

                var rect = new Rect(px, py, w, h);
                var pivot = new Vector2(0.5f, 0.5f);
                var tileSprite = Sprite.Create(tex, rect, pivot, adjustedPPU);

                // Position relative to sprite center (which is at local 0,0)
                float localX = (px + w * 0.5f - texW * 0.5f) / adjustedPPU;
                float localY = (py + h * 0.5f - texH * 0.5f) / adjustedPPU;

                var tileGO = new GameObject($"Tile_{col}_{row}");
                tileGO.transform.SetParent(sr.transform, false);
                tileGO.transform.localPosition = new Vector3(localX, localY, 0f);
                tileGO.layer = foregroundLayer;

                var tileSR = tileGO.AddComponent<SpriteRenderer>();
                tileSR.sprite = tileSprite;
                tileSR.sortingLayerName = sr.sortingLayerName;
                tileSR.sortingOrder = sr.sortingOrder;

                var box = tileGO.AddComponent<BoxCollider2D>();
                box.isTrigger = true;

                tileGO.AddComponent<ForegroundTile>();
                tileCount++;
            }
        }

        // Disable main SpriteRenderer - tiles replace it visually
        sr.enabled = false;

        Debug.Log($"[ForegroundScroller] Built {tileCount} tiles for {sr.name} ({cols}x{rows} grid)");
    }

    private bool HasOpaquePixel(Color32[] pixels, int texWidth, int startX, int startY, int w, int h)
    {
        for (int y = startY; y < startY + h; y++)
        {
            for (int x = startX; x < startX + w; x++)
            {
                if (pixels[y * texWidth + x].a > 128)
                    return true;
            }
        }
        return false;
    }

    private void DestroyTiles(SpriteRenderer sr)
    {
        for (int i = sr.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(sr.transform.GetChild(i).gameObject);
        }
    }
}
