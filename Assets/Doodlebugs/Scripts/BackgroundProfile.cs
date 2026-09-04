using UnityEngine;

[CreateAssetMenu(fileName = "BackgroundProfile", menuName = "Doodlebugs/Background Profile")]
public class BackgroundProfile : ScriptableObject
{
    [Header("Background")]
    public Sprite backgroundSprite;

    [Tooltip("Sorted to the top of the host's scene list and badged there. " +
             "Nothing is gated on it yet - every map ships in every build and " +
             "always will, because only an index travels over the network: a " +
             "client who has not paid still has to be able to draw whatever " +
             "arena the host picked. Ownership can only ever gate who may " +
             "CHOOSE a map, never who may see it.")]
    public bool isPremium;

    [Header("Foreground (optional)")]
    public Sprite foregroundSprite;

    [Header("Foreground Settings")]
    [Tooltip("Scroll speed in world units/sec, negative = scroll left")]
    public float foregroundScrollSpeed = -2f;

    [Tooltip("Offset of the foreground's bottom edge above the bottom of the visible screen, in world units. 0 = flush with screen bottom.")]
    public float foregroundBottomOffset = 0f;

    [Tooltip("Scale of foreground sprite")]
    public float foregroundScale = 1f;
}
