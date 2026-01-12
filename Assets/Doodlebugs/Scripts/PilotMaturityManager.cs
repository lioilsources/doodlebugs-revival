using UnityEngine;

/// <summary>
/// Provides maturity profile data (speed, agility settings).
/// Profile selection is now per-player via PlayerController.MaturityLevel.
/// This manager only holds the profile ScriptableObjects.
/// </summary>
public class PilotMaturityManager : MonoBehaviour
{
    public static PilotMaturityManager Instance { get; private set; }

    [Header("Profiles (assign in editor)")]
    [SerializeField] private PilotMaturityProfile noviceProfile;
    [SerializeField] private PilotMaturityProfile advancedProfile;
    [SerializeField] private PilotMaturityProfile expertProfile;

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
    /// Get profile for a specific maturity level.
    /// Used by PlayerController to get settings based on player's own maturity.
    /// </summary>
    public PilotMaturityProfile GetProfile(PilotMaturityLevel level)
    {
        // Fallback to Novice if prefab is missing
        var fallback = PilotMaturityProfile.CreateFallback();

        switch (level)
        {
            case PilotMaturityLevel.Novice:
                return noviceProfile ?? fallback;
            case PilotMaturityLevel.Advanced:
                return advancedProfile ?? fallback;
            case PilotMaturityLevel.Expert:
                return expertProfile ?? fallback;
            default:
                return noviceProfile ?? fallback;
        }
    }
}
