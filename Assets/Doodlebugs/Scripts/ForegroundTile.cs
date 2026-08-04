using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForegroundTile : MonoBehaviour
{
    [HideInInspector] public int Col;
    [HideInInspector] public int Row;

    /// <summary>Already toppling — no longer holds anything up.</summary>
    [HideInInspector] public bool IsFalling;

    // Shooting the legs out from under a billboard tower should drop whatever
    // was standing on them, but an unbounded collapse would put hundreds of
    // moving sprites on screen at once on a phone. Cap the rubble; past the cap
    // tiles simply vanish, which at that point nobody can follow anyway.
    private const int MaxFalling = 80;
    private static int _falling;

    // How far above a hole to keep looking for tiles that lost their footing.
    private const int MaxCollapseRows = 40;

    private ForegroundTileGrid _grid;

    private void Awake()
    {
        _grid = GetComponentInParent<ForegroundTileGrid>();
    }

    private void OnEnable()
    {
        // The scroller reactivates tiles when a copy wraps off-screen; a healed
        // tile is standing again and must count as support once more.
        IsFalling = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.gameObject.CompareTag("Bullet")) return;

        // Arming mines pass through terrain without carving it
        var bullet = other.GetComponent<Bullet>();
        if (bullet != null && !bullet.IsArmed) return;

        // Destroy 2x2 square: hit tile + right + below + below-right
        int[,] offsets = { { 0, 0 }, { 1, 0 }, { 0, -1 }, { 1, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int c = Col + offsets[i, 0];
            int r = Row + offsets[i, 1];
            if (i == 0)
            {
                LaunchDebris();
                continue;
            }
            var neighbor = FindTile(c, r);
            if (neighbor != null && neighbor.gameObject.activeSelf)
                neighbor.LaunchDebris();
        }

        // The 2x2 bite spans Col..Col+1; anything resting on either column, or
        // leaning on them from the side, may have just lost its footing.
        CollapseAbove(Col - 1, Col + 2, Row - 1);
    }

    private ForegroundTile FindTile(int col, int row)
    {
        if (_grid != null) return _grid.Get(col, row);
        var t = transform.parent?.Find($"Tile_{col}_{row}");
        return t != null ? t.GetComponent<ForegroundTile>() : null;
    }

    /// <summary>
    /// Drops whatever the new hole left unsupported.
    ///
    /// A tile counts as held up by the one directly below it OR by either
    /// diagonal below — that is roughly how a rigid panel spreads its load, and
    /// without it a single bullet through the middle of a billboard would slice
    /// the tower cleanly in half instead of taking a bite out of it.
    /// </summary>
    public void CollapseAbove(int colFrom, int colTo, int fromRow)
    {
        if (_grid == null) return;

        var toDrop = new List<ForegroundTile>();
        int startRow = Mathf.Max(0, fromRow);

        for (int col = colFrom; col <= colTo; col++)
        {
            // Walk up the column. Stop at the first tile that still has
            // support: everything above it is resting on that one.
            for (int row = startRow; row < startRow + MaxCollapseRows; row++)
            {
                var tile = _grid.Get(col, row);
                if (tile == null) continue;          // gap — keep looking upward
                if (tile.HasSupport(_grid)) break;
                toDrop.Add(tile);
            }
        }

        foreach (var tile in toDrop)
            tile.Topple();
    }

    public bool HasSupport(ForegroundTileGrid grid)
    {
        if (Row == 0) return true;                   // sitting on the ground row
        return grid.Get(Col, Row - 1) != null
            || grid.Get(Col - 1, Row - 1) != null
            || grid.Get(Col + 1, Row - 1) != null;
    }

    /// <summary>Structure gave way: lean over and fall under gravity.</summary>
    public void Topple()
    {
        if (IsFalling) return;
        IsFalling = true;
        _grid?.Unregister(this);

        if (_falling >= MaxFalling)
        {
            gameObject.SetActive(false);
            return;
        }

        _falling++;
        transform.SetParent(null, true);
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        StartCoroutine(Fall());
    }

    private IEnumerator Fall()
    {
        // Topple rather than drop straight down: a lean that builds as it goes
        // reads as a structure giving way, where a vertical slide reads as a
        // sprite being moved.
        float lean = Random.Range(40f, 140f) * (Random.value > 0.5f ? 1f : -1f);
        float drift = Random.Range(-1.2f, 1.2f);
        float vy = 0f;
        const float gravity = -22f;
        float elapsed = 0f;

        while (elapsed < 6f && transform.position.y > -40f)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            vy += gravity * dt;
            transform.position += new Vector3(drift * dt, vy * dt, 0f);
            transform.Rotate(0f, 0f, lean * dt);
            yield return null;
        }

        _falling--;
        Destroy(gameObject);
    }

    public void LaunchDebris()
    {
        if (IsFalling) return;
        IsFalling = true;
        _grid?.Unregister(this);

        // Detach from the scrolling parent, keeping current world position
        transform.SetParent(null, true);
        GetComponent<Collider2D>().enabled = false;
        StartCoroutine(FlyAway());
    }

    private IEnumerator FlyAway()
    {
        float rotSpeed = Random.Range(180f, 450f) * (Random.value > 0.5f ? 1f : -1f);
        var velocity = new Vector3(
            Random.Range(-2f, 2f),
            Random.Range(5f, 10f),
            0f);

        float elapsed = 0f;
        const float duration = 4f; // enough to fly well above the camera (~35 world units)

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, 0f, rotSpeed * Time.deltaTime);
            yield return null;
        }

        Destroy(gameObject);
    }
}
