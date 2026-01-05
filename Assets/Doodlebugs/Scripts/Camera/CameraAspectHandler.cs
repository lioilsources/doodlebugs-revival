using UnityEngine;

/// <summary>
/// Handles dynamic camera orthographic size based on screen aspect ratio.
/// Uses "Fixed Height" approach - vertical size stays constant, horizontal adapts.
/// </summary>
public class CameraAspectHandler : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float targetOrthoSize = 5f;  // Base orthographic size at reference aspect
    [SerializeField] private float referenceAspect = 1.78f; // 16:9 - aspect ratio the scene was designed for
    [SerializeField] private float minVisibleWidth = 17.8f; // Minimum horizontal units to always show

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private Camera cam;
    private float lastAspect;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("CameraAspectHandler: No Camera component found!");
        }
    }

    private void Start()
    {
        // Only update if component is enabled
        if (enabled && cam != null)
        {
            UpdateCameraSize();
        }
    }

    private void Update()
    {
        // Check if aspect ratio changed (screen rotation, window resize)
        float currentAspect = (float)Screen.width / Screen.height;
        if (Mathf.Abs(currentAspect - lastAspect) > 0.01f)
        {
            UpdateCameraSize();
        }
    }

    private void UpdateCameraSize()
    {
        if (cam == null) return;

        float aspect = (float)Screen.width / Screen.height;
        lastAspect = aspect;

        // Calculate required ortho size to show minimum width
        // orthoSize = visibleHeight / 2
        // visibleWidth = visibleHeight * aspect = orthoSize * 2 * aspect
        // So: orthoSize = minVisibleWidth / (2 * aspect)
        float requiredOrthoSize = minVisibleWidth / (2f * aspect);

        // Use the larger of target size or required size (to ensure min width is visible)
        float finalOrthoSize = Mathf.Max(targetOrthoSize, requiredOrthoSize);
        cam.orthographicSize = finalOrthoSize;

        if (showDebugInfo)
        {
            float visibleWidth = finalOrthoSize * 2f * aspect;
            float visibleHeight = finalOrthoSize * 2f;
            Debug.Log($"CameraAspectHandler: aspect={aspect:F2}, orthoSize={finalOrthoSize:F2}");
            Debug.Log($"  Visible area: {visibleWidth:F1}w x {visibleHeight:F1}h units");
        }
    }

    /// <summary>
    /// Returns the current visible world bounds of the camera
    /// </summary>
    public Bounds GetVisibleBounds()
    {
        if (cam == null) return new Bounds();

        float height = cam.orthographicSize * 2;
        float width = height * cam.aspect;

        return new Bounds(
            cam.transform.position,
            new Vector3(width, height, 0)
        );
    }

    /// <summary>
    /// Returns world position for viewport point (0-1)
    /// </summary>
    public Vector3 ViewportToWorld(Vector2 viewport)
    {
        if (cam == null) return Vector3.zero;
        return cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 0));
    }

    /// <summary>
    /// Get camera edge positions (for dynamic boundaries)
    /// </summary>
    public void GetEdgePositions(out float left, out float right, out float bottom, out float top)
    {
        if (cam == null)
        {
            left = right = bottom = top = 0;
            return;
        }

        Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));

        left = bottomLeft.x;
        right = topRight.x;
        bottom = bottomLeft.y;
        top = topRight.y;
    }
}
