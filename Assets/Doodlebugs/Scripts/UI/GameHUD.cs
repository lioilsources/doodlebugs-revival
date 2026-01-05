using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Game HUD displaying scores, speed bars, and match timer for both players.
/// Attach to a GameObject under Canvas.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("Player 1 (Host/Blue)")]
    [SerializeField] private Text p1ScoreText;
    [SerializeField] private Image p1SpeedBarFill;
    [SerializeField] private Image p1SpeedBarBg;

    [Header("Player 2 (Client/Red)")]
    [SerializeField] private Text p2ScoreText;
    [SerializeField] private Image p2SpeedBarFill;
    [SerializeField] private Image p2SpeedBarBg;

    [Header("Match Timer")]
    [SerializeField] private Text matchTimeText;

    [Header("Score Effect")]
    [SerializeField] private float pulseDuration = 0.2f;
    [SerializeField] private float pulseScale = 1.3f;
    [SerializeField] private GameObject floatingTextPrefab;

    [Header("Speed Bar Settings")]
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private Color engineOnColor = new Color(0.2f, 0.8f, 0.2f); // Green
    [SerializeField] private Color engineOffColor = new Color(0.5f, 0.5f, 0.5f); // Gray

    // Cached player references
    private PlayerController _player1;
    private PlayerController _player2;

    // Animation coroutines
    private Coroutine _p1PulseCoroutine;
    private Coroutine _p2PulseCoroutine;

    // Original scales for pulse animation
    private Vector3 _p1ScoreOriginalScale;
    private Vector3 _p2ScoreOriginalScale;

    private void Start()
    {
        // Store original scales
        if (p1ScoreText != null)
            _p1ScoreOriginalScale = p1ScoreText.transform.localScale;
        if (p2ScoreText != null)
            _p2ScoreOriginalScale = p2ScoreText.transform.localScale;

        // Initialize display
        UpdateScoreDisplay(0, 0);
        UpdateScoreDisplay(1, 0);
        if (matchTimeText != null)
            matchTimeText.text = "0:00.0";
    }

    private void OnEnable()
    {
        // Subscribe to score changes
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
        }
        else
        {
            StartCoroutine(WaitForScoreManager());
        }
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
        }
    }

    private IEnumerator WaitForScoreManager()
    {
        while (ScoreManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
    }

    private void Update()
    {
        // Find players if not cached
        CachePlayers();

        // Update speed bars
        UpdateSpeedBars();

        // Update timer
        UpdateTimer();
    }

    private void CachePlayers()
    {
        // Re-cache if players are missing (they may spawn later)
        if (_player1 == null || _player2 == null)
        {
            var players = FindObjectsOfType<PlayerController>();
            foreach (var player in players)
            {
                if (player.OwnerClientId == 0)
                    _player1 = player;
                else
                    _player2 = player;
            }

            // Debug log when players are found
            if (_player1 != null || _player2 != null)
            {
                Debug.Log($"[GameHUD] Players cached: P1={_player1 != null}, P2={_player2 != null}");
            }
        }
    }

    private void UpdateSpeedBars()
    {
        // Player 1 speed bar (left-to-right fill)
        if (_player1 != null && p1SpeedBarFill != null)
        {
            float speed = _player1.Speed;
            float normalizedSpeed = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
            UpdateSpeedBarFill(p1SpeedBarFill, normalizedSpeed, false);
            p1SpeedBarFill.color = _player1.IsEngineOff ? engineOffColor : engineOnColor;
        }
        else if (p1SpeedBarFill != null)
        {
            UpdateSpeedBarFill(p1SpeedBarFill, 0f, false);
        }

        // Player 2 speed bar (right-to-left fill)
        if (_player2 != null && p2SpeedBarFill != null)
        {
            float speed = _player2.Speed;
            float normalizedSpeed = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
            UpdateSpeedBarFill(p2SpeedBarFill, normalizedSpeed, true);
            p2SpeedBarFill.color = _player2.IsEngineOff ? engineOffColor : engineOnColor;
        }
        else if (p2SpeedBarFill != null)
        {
            UpdateSpeedBarFill(p2SpeedBarFill, 0f, true);
        }
    }

    private void UpdateSpeedBarFill(Image fillImage, float normalizedValue, bool rightToLeft)
    {
        var rect = fillImage.rectTransform;
        if (rightToLeft)
        {
            // Right-to-left: anchor from right, adjust left anchor
            rect.anchorMin = new Vector2(1f - normalizedValue, 0);
            rect.anchorMax = new Vector2(1, 1);
        }
        else
        {
            // Left-to-right: anchor from left, adjust right anchor
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(normalizedValue, 1);
        }
    }

    private void UpdateTimer()
    {
        if (matchTimeText != null && ScoreManager.Instance != null)
        {
            if (ScoreManager.Instance.MatchStarted)
            {
                matchTimeText.text = ScoreManager.Instance.GetFormattedTime();
            }
            else
            {
                matchTimeText.text = "0:00.0";
            }
        }
    }

    private void OnScoreChanged(ulong scorerClientId, int newScore)
    {
        UpdateScoreDisplay((int)scorerClientId, newScore);
        PlayScoreEffect((int)scorerClientId);
    }

    private void UpdateScoreDisplay(int playerId, int score)
    {
        if (playerId == 0 && p1ScoreText != null)
        {
            p1ScoreText.text = score.ToString();
        }
        else if (playerId == 1 && p2ScoreText != null)
        {
            p2ScoreText.text = score.ToString();
        }
    }

    private void PlayScoreEffect(int playerId)
    {
        Text targetText = playerId == 0 ? p1ScoreText : p2ScoreText;
        Vector3 originalScale = playerId == 0 ? _p1ScoreOriginalScale : _p2ScoreOriginalScale;

        if (targetText == null) return;

        // Stop any running pulse
        if (playerId == 0 && _p1PulseCoroutine != null)
            StopCoroutine(_p1PulseCoroutine);
        else if (playerId == 1 && _p2PulseCoroutine != null)
            StopCoroutine(_p2PulseCoroutine);

        // Start pulse animation
        var coroutine = StartCoroutine(PulseAnimation(targetText.transform, originalScale));
        if (playerId == 0)
            _p1PulseCoroutine = coroutine;
        else
            _p2PulseCoroutine = coroutine;

        // Spawn floating +1 text
        SpawnFloatingText(targetText.transform.position, playerId);
    }

    private IEnumerator PulseAnimation(Transform target, Vector3 originalScale)
    {
        float elapsed = 0f;
        float halfDuration = pulseDuration / 2f;

        // Scale up
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale, originalScale * pulseScale, t);
            yield return null;
        }

        // Scale down
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale * pulseScale, originalScale, t);
            yield return null;
        }

        target.localScale = originalScale;
    }

    private void SpawnFloatingText(Vector3 position, int playerId)
    {
        if (floatingTextPrefab == null) return;

        // Spawn slightly above the score
        Vector3 spawnPos = position + Vector3.up * 30f;
        var floatingText = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity, transform);

        // Set color based on player
        var text = floatingText.GetComponent<Text>();
        if (text != null)
        {
            text.text = "+1";
            text.color = playerId == 0 ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.3f, 0.3f);
        }

        // Animate and destroy
        StartCoroutine(FloatingTextAnimation(floatingText));
    }

    private IEnumerator FloatingTextAnimation(GameObject floatingText)
    {
        if (floatingText == null) yield break;

        var text = floatingText.GetComponent<Text>();
        var rectTransform = floatingText.GetComponent<RectTransform>();

        float duration = 0.8f;
        float elapsed = 0f;
        Vector3 startPos = rectTransform.anchoredPosition;
        Color startColor = text != null ? text.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Move up
            rectTransform.anchoredPosition = startPos + Vector3.up * (50f * t);

            // Fade out
            if (text != null)
            {
                Color c = startColor;
                c.a = 1f - t;
                text.color = c;
            }

            yield return null;
        }

        Destroy(floatingText);
    }

    /// <summary>
    /// Create HUD UI programmatically. Call this from a setup script if prefab not available.
    /// </summary>
    public static GameHUD CreateHUD(Canvas canvas)
    {
        // Create HUD container
        var hudObj = new GameObject("GameHUD");
        hudObj.transform.SetParent(canvas.transform, false);
        var hudRect = hudObj.AddComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        var hud = hudObj.AddComponent<GameHUD>();

        // Create P1 Speed Bar (top-left, above score)
        hud.p1SpeedBarBg = CreateSpeedBar(hudObj.transform, "P1SpeedBar",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -20),
            out hud.p1SpeedBarFill, false); // left-to-right fill

        // Create P1 Score (top-left, below speed bar)
        hud.p1ScoreText = CreateText(hudObj.transform, "P1Score", "0",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -55),
            48, TextAnchor.MiddleLeft, new Color(0.3f, 0.5f, 1f));

        // Create P2 Speed Bar (top-right, above score)
        hud.p2SpeedBarBg = CreateSpeedBar(hudObj.transform, "P2SpeedBar",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-100, -20),
            out hud.p2SpeedBarFill, true); // right-to-left fill

        // Create P2 Score (top-right, below speed bar)
        hud.p2ScoreText = CreateText(hudObj.transform, "P2Score", "0",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, -55),
            48, TextAnchor.MiddleRight, new Color(1f, 0.3f, 0.3f));

        // Create Match Time (top-center)
        hud.matchTimeText = CreateText(hudObj.transform, "MatchTime", "0:00.0",
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40),
            36, TextAnchor.MiddleCenter, Color.white);

        // Create floating text prefab
        hud.floatingTextPrefab = CreateFloatingTextPrefab();

        return hud;
    }

    private static Text CreateText(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos,
        int fontSize, TextAnchor alignment, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(150, 60);

        var textComp = obj.AddComponent<Text>();
        textComp.text = text;
        textComp.fontSize = fontSize;
        textComp.alignment = alignment;
        textComp.color = color;
        textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return textComp;
    }

    private static Image CreateSpeedBar(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos,
        out Image fillImage, bool rightToLeft = false)
    {
        // Background
        var bgObj = new GameObject(name + "Bg");
        bgObj.transform.SetParent(parent, false);

        var bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = anchorMin;
        bgRect.anchorMax = anchorMax;
        bgRect.anchoredPosition = anchoredPos;
        bgRect.sizeDelta = new Vector2(150, 20);

        var bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // Fill - anchors will be set dynamically in UpdateSpeedBarFill
        var fillObj = new GameObject(name + "Fill");
        fillObj.transform.SetParent(bgObj.transform, false);

        var fillRect = fillObj.AddComponent<RectTransform>();
        // Start with full bar, will be adjusted by UpdateSpeedBarFill
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2, 2);
        fillRect.offsetMax = new Vector2(-2, -2);

        fillImage = fillObj.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.8f, 0.2f);

        return bgImage;
    }

    private static GameObject CreateFloatingTextPrefab()
    {
        var obj = new GameObject("FloatingText");
        obj.SetActive(false);

        var rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100, 40);

        var text = obj.AddComponent<Text>();
        text.text = "+1";
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        obj.SetActive(true);
        return obj;
    }
}
