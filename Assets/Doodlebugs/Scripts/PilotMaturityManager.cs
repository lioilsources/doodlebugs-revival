using UnityEngine;

public class PilotMaturityManager : MonoBehaviour
{
    public static PilotMaturityManager Instance { get; private set; }

    [Header("Testing - Select Default Profile")]
    [SerializeField] private PilotMaturityLevel defaultLevel = PilotMaturityLevel.Novice;

    [Header("Profiles (assign in editor)")]
    [SerializeField] private PilotMaturityProfile noviceProfile;
    [SerializeField] private PilotMaturityProfile advancedProfile;
    [SerializeField] private PilotMaturityProfile expertProfile;

    [Header("Runtime State")]
    [SerializeField] private PilotMaturityLevel currentLevel;

    public PilotMaturityProfile CurrentProfile => GetProfile(currentLevel);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        currentLevel = defaultLevel;
    }

    public PilotMaturityProfile GetProfile(PilotMaturityLevel level)
    {
        switch (level)
        {
            case PilotMaturityLevel.Novice:
                return noviceProfile ?? PilotMaturityProfile.CreateDefault(PilotMaturityLevel.Novice);
            case PilotMaturityLevel.Advanced:
                return advancedProfile ?? PilotMaturityProfile.CreateDefault(PilotMaturityLevel.Advanced);
            case PilotMaturityLevel.Expert:
                return expertProfile ?? PilotMaturityProfile.CreateDefault(PilotMaturityLevel.Expert);
            default:
                return noviceProfile ?? PilotMaturityProfile.CreateDefault(PilotMaturityLevel.Novice);
        }
    }

    public void SetLevel(PilotMaturityLevel level)
    {
        if (currentLevel != level)
        {
            currentLevel = level;
            Debug.Log($"[PilotMaturity] Level changed to: {level}");
        }
    }

    public PilotMaturityLevel GetLevel()
    {
        return currentLevel;
    }

    // For future: upgrade based on hits
    public void CheckLevelUpgrade(int totalHits)
    {
        if (totalHits >= 20 && currentLevel != PilotMaturityLevel.Expert)
        {
            SetLevel(PilotMaturityLevel.Expert);
        }
        else if (totalHits >= 10 && currentLevel == PilotMaturityLevel.Novice)
        {
            SetLevel(PilotMaturityLevel.Advanced);
        }
    }
}
