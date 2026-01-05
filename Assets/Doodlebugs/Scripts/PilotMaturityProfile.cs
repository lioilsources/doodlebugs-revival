using UnityEngine;

[CreateAssetMenu(fileName = "PilotMaturityProfile", menuName = "Doodlebugs/Pilot Maturity Profile")]
public class PilotMaturityProfile : ScriptableObject
{
    public PilotMaturityLevel level;

    // Default values = Novice profile (fallback if prefabs don't exist)
    [Header("Movement")]
    [Tooltip("Base rotation speed")]
    public float rotateSpeed = 50f;

    [Tooltip("Maximum flight speed")]
    public float maxSpeed = 10f;

    [Header("Gravity (Engine Off)")]
    [Tooltip("Maximum gravity when engine is off")]
    public float maxGravity = 0.125f;

    [Tooltip("How fast gravity increases when engine is off")]
    public float gravityIncreaseRate = 0.0875f;

    [Header("Engine Off")]
    [Tooltip("Rotation speed multiplier when engine is off (higher = easier to recover)")]
    public float engineOffRotateMultiplier = 16f;  // 50 * 16 = 800

    [Tooltip("Minimum rotation.z value for engine restart (more negative = steeper dive required)")]
    public float engineRestartMinRotation = -0.91f;

    [Tooltip("Maximum rotation.z value for engine restart (less negative = shallower dive allowed)")]
    public float engineRestartMaxRotation = -0.41f;

    [Header("Shooting")]
    [Tooltip("Multiplier for bullet force (1 = normal, 2 = double speed)")]
    public float bulletForceMultiplier = 1f;

    [Tooltip("Gravity scale for bullets (0 = straight, 1 = normal, 2 = heavy drop)")]
    public float bulletGravityScale = 0f;

    // Fallback factory - creates Novice profile (uses class default values)
    public static PilotMaturityProfile CreateFallback()
    {
        var profile = CreateInstance<PilotMaturityProfile>();
        profile.level = PilotMaturityLevel.Novice;
        // All values come from class defaults (Novice)
        return profile;
    }
}
