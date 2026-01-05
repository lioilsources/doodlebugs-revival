using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Initializes game systems (ScoreManager, GameHUD).
/// Auto-initializes when game scene loads - no manual setup required.
/// </summary>
public class GameSetup : MonoBehaviour
{
    private static GameSetup _instance;

    private GameHUD _hud;
    private ScoreManager _scoreManager;

    /// <summary>
    /// Auto-initialize when any scene loads
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoaded()
    {
        // Check if we're in a game scene (has Canvas)
        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null) return;

        // Check if GameSetup already exists
        if (_instance != null) return;

        // Create GameSetup object
        var setupObj = new GameObject("GameSetup");
        _instance = setupObj.AddComponent<GameSetup>();
        DontDestroyOnLoad(setupObj);

        Debug.Log("[GameSetup] Auto-initialized");
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Create ScoreManager
        CreateScoreManager();
    }

    private void Start()
    {
        // Find Canvas and create HUD
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            CreateHUD(canvas);
        }
        else
        {
            Debug.LogError("[GameSetup] No Canvas found in scene!");
        }
    }

    private void CreateScoreManager()
    {
        // Check if ScoreManager already exists
        _scoreManager = FindObjectOfType<ScoreManager>();
        if (_scoreManager != null)
        {
            Debug.Log("[GameSetup] ScoreManager already exists");
            return;
        }

        // Create ScoreManager
        var scoreManagerObj = new GameObject("ScoreManager");
        _scoreManager = scoreManagerObj.AddComponent<ScoreManager>();
        DontDestroyOnLoad(scoreManagerObj);
        Debug.Log("[GameSetup] ScoreManager created");
    }

    private void CreateHUD(Canvas canvas)
    {
        // Check if HUD already exists
        _hud = FindObjectOfType<GameHUD>();
        if (_hud != null)
        {
            Debug.Log("[GameSetup] GameHUD already exists");
            return;
        }

        // Create HUD programmatically
        _hud = GameHUD.CreateHUD(canvas);
        Debug.Log("[GameSetup] GameHUD created");
    }
}
