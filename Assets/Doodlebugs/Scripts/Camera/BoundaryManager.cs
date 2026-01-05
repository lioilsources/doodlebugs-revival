using UnityEngine;

/// <summary>
/// Dynamically positions boundary colliders (Left, Right, Ground, Space) based on camera view.
/// Boundaries overlap into the visible area by configurable percentages.
/// Attach to the same GameObject as CameraAspectHandler or Main Camera.
/// </summary>
public class BoundaryManager : MonoBehaviour
{
    [Header("Boundary References")]
    [SerializeField] private Transform leftBoundary;
    [SerializeField] private Transform rightBoundary;
    [SerializeField] private Transform groundBoundary;
    [SerializeField] private Transform spaceBoundary;

    [Header("Settings")]
    [SerializeField] private float boundaryThickness = 2f;

    [Header("Overlap Into View (%)")]
    [Tooltip("How much the right edge of Left boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float leftOverlap = 0.01f; // 1%

    [Tooltip("How much the left edge of Right boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float rightOverlap = 0.01f; // 1%

    [Tooltip("How much the top edge of Ground boundary extends into view")]
    [Range(0f, 0.2f)]
    [SerializeField] private float groundOverlap = 0.02f; // 2%

    [Tooltip("How much the bottom edge of Space boundary extends into view")]
    [Range(0f, 0.5f)]
    [SerializeField] private float spaceOverlap = 0.10f; // 10%

    private Camera cam;
    private CameraAspectHandler aspectHandler;
    private float lastAspect;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            cam = Camera.main;
        }
        aspectHandler = GetComponent<CameraAspectHandler>();
    }

    private void Start()
    {
        // Auto-find boundaries if not assigned
        if (leftBoundary == null)
            leftBoundary = GameObject.Find("Left")?.transform;
        if (rightBoundary == null)
            rightBoundary = GameObject.Find("Right")?.transform;
        if (groundBoundary == null)
            groundBoundary = GameObject.Find("Ground")?.transform;
        if (spaceBoundary == null)
            spaceBoundary = GameObject.Find("Space")?.transform;

        UpdateBoundaries();
    }

    private void Update()
    {
        // Update when aspect ratio changes
        float currentAspect = (float)Screen.width / Screen.height;
        if (Mathf.Abs(currentAspect - lastAspect) > 0.01f)
        {
            lastAspect = currentAspect;
            UpdateBoundaries();
        }
    }

    private void UpdateBoundaries()
    {
        if (cam == null) return;

        // Get camera edges
        float camLeft, camRight, camBottom, camTop;
        if (aspectHandler != null)
        {
            aspectHandler.GetEdgePositions(out camLeft, out camRight, out camBottom, out camTop);
        }
        else
        {
            Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
            Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
            camLeft = bottomLeft.x;
            camRight = topRight.x;
            camBottom = bottomLeft.y;
            camTop = topRight.y;
        }

        float height = camTop - camBottom;
        float width = camRight - camLeft;
        float centerX = (camLeft + camRight) / 2f;
        float centerY = (camTop + camBottom) / 2f;

        // Left boundary: right edge at (camLeft + leftOverlap * width)
        if (leftBoundary != null)
        {
            float rightEdge = camLeft + leftOverlap * width;
            float leftX = rightEdge - boundaryThickness / 2f;
            leftBoundary.position = new Vector3(leftX, centerY, 0);

            var collider = leftBoundary.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.size = new Vector2(boundaryThickness, height * 2);
            }
        }

        // Right boundary: left edge at (camRight - rightOverlap * width)
        if (rightBoundary != null)
        {
            float leftEdge = camRight - rightOverlap * width;
            float rightX = leftEdge + boundaryThickness / 2f;
            rightBoundary.position = new Vector3(rightX, centerY, 0);

            var collider = rightBoundary.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.size = new Vector2(boundaryThickness, height * 2);
            }
        }

        // Ground boundary: top edge at (camBottom + groundOverlap * height)
        if (groundBoundary != null)
        {
            float topEdge = camBottom + groundOverlap * height;
            float groundY = topEdge - boundaryThickness / 2f;
            groundBoundary.position = new Vector3(centerX, groundY, 0);

            var collider = groundBoundary.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.size = new Vector2(width * 2, boundaryThickness);
            }
        }

        // Space boundary: bottom edge at (camTop - spaceOverlap * height)
        if (spaceBoundary != null)
        {
            float bottomEdge = camTop - spaceOverlap * height;
            float spaceY = bottomEdge + boundaryThickness / 2f;
            spaceBoundary.position = new Vector3(centerX, spaceY, 0);

            var collider = spaceBoundary.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.size = new Vector2(width * 2, boundaryThickness);
            }
        }

        Debug.Log($"[BoundaryManager] Cam: L={camLeft:F1}, R={camRight:F1}, B={camBottom:F1}, T={camTop:F1}");
    }

    // Call this if you need to force update
    public void ForceUpdate()
    {
        UpdateBoundaries();
    }
}
