using UnityEngine;

[CreateAssetMenu(fileName = "PilotMaturityProfile", menuName = "Doodlebugs/Pilot Maturity Profile")]
public class PilotMaturityProfile : ScriptableObject
{
    public PilotMaturityLevel level;

    [Header("Movement")]
    [Tooltip("Base rotation speed")]
    public float rotateSpeed = 200f;

    [Tooltip("Maximum flight speed")]
    public float maxSpeed = 20f;

    [Header("Gravity (Engine Off)")]
    [Tooltip("Maximum gravity when engine is off")]
    public float maxGravity = 0.5f;

    [Tooltip("How fast gravity increases when engine is off")]
    public float gravityIncreaseRate = 0.35f;

    [Header("Engine Restart")]
    [Tooltip("Minimum rotation.z value for engine restart (more negative = steeper dive required)")]
    public float engineRestartMinRotation = -0.8f;

    [Tooltip("Maximum rotation.z value for engine restart (less negative = shallower dive allowed)")]
    public float engineRestartMaxRotation = -0.6f;

    [Header("Shooting")]
    [Tooltip("Multiplier for bullet force (1 = normal, 2 = double speed)")]
    public float bulletForceMultiplier = 1f;

    // Factory method to create default profiles
    public static PilotMaturityProfile CreateDefault(PilotMaturityLevel level)
    {
        var profile = CreateInstance<PilotMaturityProfile>();
        profile.level = level;

        switch (level)
        {
            case PilotMaturityLevel.Expert:
                // Hardest - baseline values, 2x bullet speed
                profile.rotateSpeed = 200f;
                profile.maxSpeed = 20f;
                profile.maxGravity = 0.5f;
                profile.gravityIncreaseRate = 0.35f;
                profile.engineRestartMinRotation = -0.8f;
                profile.engineRestartMaxRotation = -0.6f;
                profile.bulletForceMultiplier = 2f;
                break;

            case PilotMaturityLevel.Advanced:
                // Medium - half rotation, half gravity, wider engine restart
                profile.rotateSpeed = 100f;  // half
                profile.maxSpeed = 20f;
                profile.maxGravity = 0.25f;  // half
                profile.gravityIncreaseRate = 0.175f;  // half
                profile.engineRestartMinRotation = -0.85f;  // 10 degrees wider
                profile.engineRestartMaxRotation = -0.53f;  // 10 degrees wider
                profile.bulletForceMultiplier = 1f;
                break;

            case PilotMaturityLevel.Novice:
                // Easiest - half max speed, widest engine restart
                profile.rotateSpeed = 200f;
                profile.maxSpeed = 10f;  // half
                profile.maxGravity = 0.5f;
                profile.gravityIncreaseRate = 0.35f;
                profile.engineRestartMinRotation = -0.91f;  // 25 degrees wider
                profile.engineRestartMaxRotation = -0.41f;  // 25 degrees wider
                profile.bulletForceMultiplier = 1f;
                break;
        }

        return profile;
    }
}
