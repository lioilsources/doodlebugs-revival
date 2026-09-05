using UnityEngine;

/// <summary>
/// Plays a sprite sequence once and destroys itself. Deliberately not an
/// Animator: an Animator needs a controller asset per clip, and the element
/// flipbooks are generated art that arrives as a numbered folder of PNGs
/// (tools/weapons/generate_effects.py). Frames come from EffectLibrary,
/// which loads and caches them.
/// </summary>
public class FlipbookEffect : MonoBehaviour
{
    private Sprite[] _frames;
    private float _frameTime;
    private float _elapsed;
    private SpriteRenderer _renderer;

    /// <summary>
    /// Spawn a one-shot flipbook at a world position.
    /// </summary>
    /// <param name="frames">Ordered frames; nothing happens if empty.</param>
    /// <param name="fps">Playback rate - the whole sequence is frames/fps long.</param>
    /// <param name="scale">World scale multiplier (blast radius for explosions).</param>
    /// <param name="sortingOrder">Above bullets, below the HUD.</param>
    public static FlipbookEffect Play(Sprite[] frames, Vector3 position, float fps,
        float scale, int sortingOrder)
    {
        if (frames == null || frames.Length == 0) return null;

        var go = new GameObject("Flipbook");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        var fx = go.AddComponent<FlipbookEffect>();
        fx._frames = frames;
        fx._frameTime = 1f / Mathf.Max(1f, fps);
        fx._renderer = go.AddComponent<SpriteRenderer>();
        fx._renderer.sprite = frames[0];
        fx._renderer.sortingOrder = sortingOrder;
        return fx;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        int index = Mathf.FloorToInt(_elapsed / _frameTime);

        if (index >= _frames.Length)
        {
            Destroy(gameObject);
            return;
        }
        _renderer.sprite = _frames[index];
    }
}
