using UnityEngine;

[CreateAssetMenu(fileName = "BackgroundProfile", menuName = "Doodlebugs/Background Profile")]
public class BackgroundProfile : ScriptableObject
{
    [Header("Background")]
    public Sprite backgroundSprite;

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
