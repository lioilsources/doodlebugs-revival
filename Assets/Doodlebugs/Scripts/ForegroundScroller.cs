using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Scrolls N copies of a foreground sprite infinitely to the left.
/// N = ceil(cameraWidth / spriteWidth) + 1, so the wrap (and tile "heal")
/// always happens outside the viewport.
/// The foreground's bottom edge is anchored to the bottom of the visible
/// screen (plus an optional offset from the BackgroundProfile).
/// Splits each sprite into small destructible tiles with BoxCollider2D
/// so bullets destroy individual tiles on contact.
/// Uses NetworkManager.ServerTime for deterministic scroll position
/// so all clients have identical tile positions for collision sync.
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
    private float _spriteBoundsMinX; // world-space offset from transform.position to left edge
    private bool _active;

    private Sprite _sprite;
    private float _scale;
    private float _bottomOffset;
    private float _lastCamWidth;

    // Copy 0 = spriteA, copy 1 = spriteB (scene objects), 2+ = runtime-created extras
    private readonly List<SpriteRenderer> _copies = new();

    // Epoch for deterministic absolute positioning (synced via ServerTime)
    private double _scrollStartTime;
    private readonly List<float> _epochX = new();

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
    /// The sprite's bottom edge is placed at the bottom of the visible screen
    /// plus bottomOffset world units.
    /// </summary>
    public void SetForeground(Sprite sprite, float scrollSpeed, float bottomOffset, float scale)
    {
        if (sprite == null)
        {
            sprite = ForegroundSpriteGenerator.CreatePlaceholderForeground();
            // Debug.Log("[ForegroundScroller] Using generated placeholder foreground sprite");
        }

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[ForegroundScroller] No main camera found, cannot set foreground");
            return;
        }

        _sprite = sprite;
        _scale = scale;
        _bottomOffset = bottomOffset;
        _scrollSpeed = scrollSpeed;
        _spriteWorldWidth = sprite.bounds.size.x * scale;
        _spriteBoundsMinX = sprite.bounds.min.x * scale; // pivot-relative left edge offset

        if (_spriteWorldWidth <= 0.001f)
        {
            Debug.LogWarning("[ForegroundScroller] Foreground sprite has zero width, cannot scroll");
            return;
        }

        // Clean up any existing tiles before reassigning sprites
        foreach (var copy in _copies)
            DestroyTiles(copy);

        _lastCamWidth = cam.orthographicSize * 2f * cam.aspect;
        EnsureCopyCount(RequiredCopyCount(cam));

        // Position copies edge-to-edge starting at the camera's left edge.
        // transform.position is at the sprite's pivot, so we subtract the
        // left-edge offset to place the left edge exactly at camLeft.
        float camLeft = cam.transform.position.x - cam.orthographicSize * cam.aspect;
        float startX = camLeft - _spriteBoundsMinX;
        float y = ComputeAnchoredY(cam);
        for (int i = 0; i < _copies.Count; i++)
        {
            _copies[i].transform.position = new Vector3(startX + i * _spriteWorldWidth, y, 0f);
            BuildTiles(_copies[i]);
        }

        ResetEpoch();
        _active = true;
    }

    /// <summary>
    /// Disable the foreground entirely.
    /// </summary>
    public void DisableForeground()
    {
        _active = false;
        foreach (var copy in _copies)
        {
            DestroyTiles(copy);
            copy.enabled = false;
            if (copy != spriteA && copy != spriteB)
                Destroy(copy.gameObject);
        }
        _copies.Clear();
        _epochX.Clear();
    }

    private void Update()
    {
        if (!_active) return;

        var cam = Camera.main;
        if (cam == null) return;

        HandleCameraChange(cam);

        // Absolute position from network time — no deltaTime accumulation drift
        float elapsed = (float)(GetNetworkTime() - _scrollStartTime);
        float offset = _scrollSpeed * elapsed;
        float y = ComputeAnchoredY(cam);
        for (int i = 0; i < _copies.Count; i++)
            _copies[i].transform.position = new Vector3(_epochX[i] + offset, y, 0f);

        float camLeft = cam.transform.position.x - cam.orthographicSize * cam.aspect;
        WrapIfNeeded(camLeft);
    }

    /// <summary>
    /// Y position so the sprite's bottom edge sits at the bottom of the
    /// visible screen plus the profile's offset. Recomputed every frame,
    /// so aspect/orthoSize changes need no rebuild.
    /// </summary>
    private float ComputeAnchoredY(Camera cam)
    {
        float camBottom = cam.transform.position.y - cam.orthographicSize;
        float boundsMinY = _sprite.bounds.min.y * _scale; // pivot → bottom edge offset
        return camBottom + _bottomOffset - boundsMinY;
    }

    private int RequiredCopyCount(Camera cam)
    {
        float camWidth = cam.orthographicSize * 2f * cam.aspect;
        return Mathf.Max(2, Mathf.CeilToInt(camWidth / _spriteWorldWidth) + 1);
    }

    /// <summary>
    /// Grow or shrink the copy list to the requested count and (re)assign
    /// sprite, scale and visibility on every copy. Extras beyond the two
    /// scene objects are plain local GameObjects (non-networked visuals).
    /// </summary>
    private void EnsureCopyCount(int count)
    {
        if (_copies.Count == 0)
        {
            _copies.Add(spriteA);
            _copies.Add(spriteB);
        }

        while (_copies.Count < count)
        {
            var go = new GameObject($"SpriteCopy_{_copies.Count}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = spriteA.sortingLayerID;
            sr.sortingOrder = spriteA.sortingOrder;
            sr.color = spriteA.color;
            _copies.Add(sr);
        }

        while (_copies.Count > count)
        {
            var last = _copies[_copies.Count - 1];
            _copies.RemoveAt(_copies.Count - 1);
            DestroyTiles(last);
            Destroy(last.gameObject);
        }

        foreach (var copy in _copies)
        {
            copy.sprite = _sprite;
            copy.transform.localScale = Vector3.one * _scale;
        }

        while (_epochX.Count < _copies.Count) _epochX.Add(0f);
        while (_epochX.Count > _copies.Count) _epochX.RemoveAt(_epochX.Count - 1);
    }

    /// <summary>
    /// Reacts to runtime aspect/resolution changes (rotation, window resize).
    /// Only rebuilds tiles when the required copy count actually changes.
    /// </summary>
    private void HandleCameraChange(Camera cam)
    {
        float camWidth = cam.orthographicSize * 2f * cam.aspect;
        if (Mathf.Abs(camWidth - _lastCamWidth) < 0.01f) return;

        _lastCamWidth = camWidth;
        int required = RequiredCopyCount(cam);
        if (required == _copies.Count) return;

        int oldCount = _copies.Count;
        float maxRight = float.MinValue;
        foreach (var copy in _copies)
            maxRight = Mathf.Max(maxRight, copy.transform.position.x + _spriteBoundsMinX + _spriteWorldWidth);

        EnsureCopyCount(required);

        // Place new copies continuing rightward from the current rightmost edge
        float y = ComputeAnchoredY(cam);
        for (int i = oldCount; i < _copies.Count; i++)
        {
            _copies[i].transform.position = new Vector3(maxRight - _spriteBoundsMinX, y, 0f);
            maxRight += _spriteWorldWidth;
            BuildTiles(_copies[i]);
        }

        ResetEpoch();
    }

    private void WrapIfNeeded(float camLeft)
    {
        for (int i = 0; i < _copies.Count; i++)
        {
            float rightEdge = _copies[i].transform.position.x + _spriteBoundsMinX + _spriteWorldWidth;
            if (rightEdge >= camLeft) continue;

            float maxRight = float.MinValue;
            foreach (var copy in _copies)
                maxRight = Mathf.Max(maxRight, copy.transform.position.x + _spriteBoundsMinX + _spriteWorldWidth);

            var t = _copies[i].transform;
            t.position = new Vector3(maxRight - _spriteBoundsMinX, t.position.y, 0f);

            // Reactivate all tiles so foreground "heals" when scrolling back
            ReactivateTiles(_copies[i]);

            // Reset epoch so absolute position stays accurate after wrap
            ResetEpoch();
        }
    }

    private void ResetEpoch()
    {
        _scrollStartTime = GetNetworkTime();
        for (int i = 0; i < _copies.Count; i++)
            _epochX[i] = _copies[i].transform.position.x;
    }

    /// <summary>
    /// Splits a SpriteRenderer's sprite into a grid of small tile GameObjects.
    /// Each tile has its own SpriteRenderer, BoxCollider2D, and ForegroundTile script.
    /// The main SpriteRenderer is disabled since tiles replace it visually.
    /// </summary>
    private void BuildTiles(SpriteRenderer sr)
    {
        if (sr.sprite == null) return;

        // Visible by default; disabled again below once tiles replace it.
        // Stays enabled as a fallback when the texture is not readable.
        sr.enabled = true;

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

        // Sprite rect in downscaled texture coordinates
        int spriteX0 = Mathf.RoundToInt(sr.sprite.rect.x * ppuScale);
        int spriteY0 = Mathf.RoundToInt(sr.sprite.rect.y * ppuScale);
        int spriteW  = Mathf.RoundToInt(sr.sprite.rect.width  * ppuScale);
        int spriteH  = Mathf.RoundToInt(sr.sprite.rect.height * ppuScale);

        // Sprite pivot in downscaled texture coordinates
        // (Sprite.pivot returns pixel coords within the original texture)
        float pivotX = sr.sprite.pivot.x * ppuScale;
        float pivotY = sr.sprite.pivot.y * ppuScale;

        int foregroundLayer = LayerMask.NameToLayer("Foreground");
        int cols = Mathf.CeilToInt((float)spriteW / tilePixelSize);
        int rows = Mathf.CeilToInt((float)spriteH / tilePixelSize);
        int tileCount = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int px = spriteX0 + col * tilePixelSize;
                int py = spriteY0 + row * tilePixelSize;
                int w = Mathf.Min(tilePixelSize, spriteX0 + spriteW - px);
                int h = Mathf.Min(tilePixelSize, spriteY0 + spriteH - py);

                if (!HasOpaquePixel(pixels, texW, px, py, w, h))
                    continue;

                var rect = new Rect(px, py, w, h);
                var pivot = new Vector2(0.5f, 0.5f);
                var tileSprite = Sprite.Create(tex, rect, pivot, adjustedPPU);

                // Position relative to sprite pivot (which is at local 0,0)
                float localX = (px + w * 0.5f - pivotX) / adjustedPPU;
                float localY = (py + h * 0.5f - pivotY) / adjustedPPU;

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

                var ft = tileGO.AddComponent<ForegroundTile>();
                ft.Col = col;
                ft.Row = row;
                tileCount++;
            }
        }

        // Disable main SpriteRenderer - tiles replace it visually
        sr.enabled = false;

        // Debug.Log($"[ForegroundScroller] Built {tileCount} tiles for {sr.name} ({cols}x{rows} grid)");
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

    /// <summary>
    /// Blow away every active tile whose center lies within radius of the
    /// world-space point (bomb/rocket craters). Local-visual, like single-tile
    /// bullet hits - the caller syncs position+radius across clients.
    /// </summary>
    public void DestroyTilesInRadius(Vector2 center, float radius)
    {
        if (!_active) return;

        float r2 = radius * radius;
        foreach (var copy in _copies)
        {
            // Backwards: LaunchDebris() detaches the tile from this parent
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                var tile = copy.transform.GetChild(i);
                if (!tile.gameObject.activeSelf) continue;
                if (((Vector2)tile.position - center).sqrMagnitude > r2) continue;
                tile.GetComponent<ForegroundTile>()?.LaunchDebris();
            }
        }
    }

    private void ReactivateTiles(SpriteRenderer sr)
    {
        for (int i = 0; i < sr.transform.childCount; i++)
            sr.transform.GetChild(i).gameObject.SetActive(true);
    }

    private double GetNetworkTime()
    {
        var nm = NetworkManager.Singleton;
        return (nm != null && nm.IsListening) ? nm.ServerTime.Time : Time.timeAsDouble;
    }

    private void DestroyTiles(SpriteRenderer sr)
    {
        for (int i = sr.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(sr.transform.GetChild(i).gameObject);
        }
    }
}
