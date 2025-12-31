using UnityEngine;

/// <summary>
/// Handles dynamic screen setup: scales background and creates boundary colliders
/// based on camera view. All boundaries are created with scale=1 to avoid size distortion.
/// </summary>
public class ScreenSetup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer background;

    [Header("Settings")]
    [SerializeField] private float borderThickness = 2f;

    [Header("Overlap Into View (%)")]
    [Tooltip("How much the right edge of Left boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float leftOverlap = 0.01f;

    [Tooltip("How much the left edge of Right boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float rightOverlap = 0.01f;

    [Tooltip("How much the top edge of Ground boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float groundOverlap = 0.02f;

    [Tooltip("How much the bottom edge of Space boundary extends into view")]
    [Range(0f, 0.5f)]
    [SerializeField] private float spaceOverlap = 0.10f;

    private Camera cam;
    private CameraAspectHandler aspectHandler;
    private float lastAspect;
    private float lastOrthoSize;
    private GameObject[] borders = new GameObject[4];
    private bool initialized = false;

    void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[ScreenSetup] Camera.main is null!");
            return;
        }

        aspectHandler = cam.GetComponent<CameraAspectHandler>();

        // Create borders in Awake - PlayerController.Start() needs to find them
        CreateBorderColliders();
    }

    void Start()
    {
        // Initial setup will happen in LateUpdate after CameraAspectHandler.Start() runs
    }

    void LateUpdate()
    {
        if (cam == null) return;

        float currentAspect = (float)Screen.width / Screen.height;
        float currentOrthoSize = cam.orthographicSize;

        // Check if aspect ratio OR orthoSize changed (CameraAspectHandler modifies orthoSize)
        bool aspectChanged = Mathf.Abs(currentAspect - lastAspect) > 0.01f;
        bool orthoChanged = Mathf.Abs(currentOrthoSize - lastOrthoSize) > 0.01f;

        if (!initialized || aspectChanged || orthoChanged)
        {
            lastAspect = currentAspect;
            lastOrthoSize = currentOrthoSize;
            UpdateBorders();
            ScaleBackground();
            initialized = true;

            Debug.Log($"[ScreenSetup] Updated. OrthoSize: {currentOrthoSize:F2}, Aspect: {currentAspect:F2}");
        }
    }

    void ScaleBackground()
    {
        if (background == null || cam == null) return;

        float camHeight = cam.orthographicSize * 2f;
        float camWidth = camHeight * cam.aspect;

        Vector2 spriteSize = background.sprite.bounds.size;

        Vector3 scale = background.transform.localScale;
        scale.x = camWidth / spriteSize.x;
        scale.y = camHeight / spriteSize.y;
        background.transform.localScale = scale;

        // Position background at camera position, behind everything
        background.transform.position = new Vector3(
            cam.transform.position.x,
            cam.transform.position.y,
            10f // Behind other objects
        );
    }

    void CreateBorderColliders()
    {
        if (cam == null) return;

        float camHeight = cam.orthographicSize * 2f;
        float camWidth = camHeight * cam.aspect;
        float halfHeight = camHeight / 2f;
        float halfWidth = camWidth / 2f;

        // Camera position offset
        Vector3 camPos = cam.transform.position;

        // Calculate overlap offsets (overlap pushes border INTO the view)
        float leftX = -halfWidth + (leftOverlap * camWidth);
        float rightX = halfWidth - (rightOverlap * camWidth);
        float bottomY = -halfHeight + (groundOverlap * camHeight);
        float topY = halfHeight - (spaceOverlap * camHeight);

        // Left boundary
        borders[0] = CreateCollider("Left",
            new Vector2(camPos.x + leftX - borderThickness / 2f, camPos.y),
            new Vector2(borderThickness, camHeight * 2));

        // Right boundary
        borders[1] = CreateCollider("Right",
            new Vector2(camPos.x + rightX + borderThickness / 2f, camPos.y),
            new Vector2(borderThickness, camHeight * 2));

        // Ground boundary (with Respawn tag)
        borders[2] = CreateCollider("Ground",
            new Vector2(camPos.x, camPos.y + bottomY - borderThickness / 2f),
            new Vector2(camWidth * 2, borderThickness),
            "Respawn");

        // Space boundary
        borders[3] = CreateCollider("Space",
            new Vector2(camPos.x, camPos.y + topY + borderThickness / 2f),
            new Vector2(camWidth * 2, borderThickness));

        Debug.Log($"[ScreenSetup] Created borders. Cam: {camWidth:F1}x{camHeight:F1}, OrthoSize: {cam.orthographicSize:F1}");
    }

    GameObject CreateCollider(string name, Vector2 position, Vector2 size, string tag = null)
    {
        GameObject border = new GameObject(name);
        border.transform.position = position;
        border.transform.localScale = Vector3.one; // IMPORTANT: scale = 1

        BoxCollider2D collider = border.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = true;

        if (!string.IsNullOrEmpty(tag))
        {
            border.tag = tag;
        }

        border.transform.parent = transform;

        return border;
    }

    void UpdateBorders()
    {
        // Destroy old borders and recreate
        foreach (var border in borders)
        {
            if (border != null) Destroy(border);
        }
        CreateBorderColliders();
    }

    // Force update - useful if camera settings change
    public void ForceUpdate()
    {
        UpdateBorders();
        ScaleBackground();
    }
}
