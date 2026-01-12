using UnityEngine;

public class PilotMaturityManager : MonoBehaviour
{
    public static PilotMaturityManager Instance { get; private set; }

    /// <summary>
    /// Event fired when the pilot maturity level changes.
    /// Subscribe to this to react to level-ups (e.g., visual effects).
    /// </summary>
    public event System.Action<PilotMaturityLevel> OnLevelChanged;

    [Header("Current Level (set default in editor, changes at runtime)")]
    [SerializeField] private PilotMaturityLevel currentLevel = PilotMaturityLevel.Novice;

    [Header("Profiles (assign in editor)")]
    [SerializeField] private PilotMaturityProfile noviceProfile;
    [SerializeField] private PilotMaturityProfile advancedProfile;
    [SerializeField] private PilotMaturityProfile expertProfile;

    public PilotMaturityProfile CurrentProfile => GetProfile(currentLevel);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

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

    public void SetLevel(PilotMaturityLevel level)
    {
        if (currentLevel != level)
        {
            currentLevel = level;
            Debug.Log($"[PilotMaturity] Level changed to: {level}");
            OnLevelChanged?.Invoke(level);
        }
    }

    public PilotMaturityLevel GetLevel()
    {
        return currentLevel;
    }

    /// <summary>
    /// Check if player should level up based on total kills.
    /// Thresholds: 10 kills = Advanced, 20 kills = Expert
    /// </summary>
    public void CheckLevelUpgrade(int totalHits)
    {
        Debug.Log($"[PilotMaturity] CheckLevelUpgrade called with {totalHits} kills, currentLevel={currentLevel}");

        if (totalHits >= 20 && currentLevel != PilotMaturityLevel.Expert)
        {
            Debug.Log($"[PilotMaturity] Upgrading to Expert!");
            SetLevel(PilotMaturityLevel.Expert);
        }
        else if (totalHits >= 10 && currentLevel == PilotMaturityLevel.Novice)
        {
            Debug.Log($"[PilotMaturity] Upgrading to Advanced!");
            SetLevel(PilotMaturityLevel.Advanced);
        }
    }
}
