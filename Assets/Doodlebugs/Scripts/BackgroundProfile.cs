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

    [Tooltip("Y position of foreground in world space")]
    public float foregroundYPosition = -8f;

    [Tooltip("Scale of foreground sprite")]
    public float foregroundScale = 1f;
}
