using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Column/row lookup for one foreground copy's tiles.
///
/// A collapse walks upward through a tower checking what still holds it up,
/// which is hundreds of neighbour lookups in a single frame. Transform.Find
/// is a linear scan by name over ~600 children, so doing it that way is a
/// visible hitch on mobile; this keeps a dictionary instead.
/// </summary>
public class ForegroundTileGrid : MonoBehaviour
{
    private readonly Dictionary<int, ForegroundTile> _tiles = new();

    private static int Key(int col, int row) => (col << 12) ^ row;

    public void Register(ForegroundTile tile) => _tiles[Key(tile.Col, tile.Row)] = tile;

    public void Unregister(ForegroundTile tile)
    {
        int k = Key(tile.Col, tile.Row);
        if (_tiles.TryGetValue(k, out var current) && current == tile)
            _tiles.Remove(k);
    }

    /// <summary>Tile at this cell that is still standing, or null.</summary>
    public ForegroundTile Get(int col, int row)
    {
        if (!_tiles.TryGetValue(Key(col, row), out var tile)) return null;
        if (tile == null) return null;
        return (tile.gameObject.activeSelf && !tile.IsFalling) ? tile : null;
    }

    /// <summary>
    /// Re-registers everything under this copy. The scroller reactivates tiles
    /// when the copy wraps off-screen ("healing"), which must also restore the
    /// structure — otherwise a healed tower would still be considered cut and
    /// would collapse the moment anything touched it.
    /// </summary>
    public void Rebuild()
    {
        _tiles.Clear();
        var tiles = GetComponentsInChildren<ForegroundTile>(true);
        foreach (var tile in tiles)
        {
            tile.IsFalling = false;
            Register(tile);
        }
    }
}
