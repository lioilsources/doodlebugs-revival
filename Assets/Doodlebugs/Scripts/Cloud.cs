using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// Cloud behavior - moves right and wraps at screen edge.
/// Server-authoritative movement, synced via ClientNetworkTransform.
/// </summary>
public class Cloud : NetworkBehaviour
{
    // Speed - local variable, server moves clouds
    private float _speed = 1f;

    // Scale - local variable, applied on all clients via RPC
    private float _scale = 1f;

    // Pending initialization (if called before spawn)
    private float _pendingSpeed = 0f;
    private float _pendingScale = 0f;
    private bool _hasPendingInit = false;

    // Cached boundary and sprite info
    private Collider2D _rightBoundary;
    private float _spriteHalfWidth = 1f; // Will be calculated from SpriteRenderer

    void Awake()
    {
        Debug.Log($"[Cloud] Awake called on {gameObject.name}");

        // Cache sprite half width (at scale 1)
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            _spriteHalfWidth = sr.sprite.bounds.extents.x;
        }
    }

    void Start()
    {
        Debug.Log($"[Cloud] Start called, IsServer={IsServer}, IsSpawned={IsSpawned}, speed={_speed}, scale={_scale}");

        // Cache right boundary
        var rightObj = GameObject.Find("Right");
        if (rightObj != null)
        {
            _rightBoundary = rightObj.GetComponent<Collider2D>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"[Cloud] OnNetworkSpawn called, IsServer={IsServer}, hasPending={_hasPendingInit}");

        // Apply pending initialization if any
        if (_hasPendingInit && IsServer)
        {
            _speed = _pendingSpeed;
            _scale = _pendingScale;
            transform.localScale = Vector3.one * _scale;
            ApplyScaleClientRpc(_scale);
            Debug.Log($"[Cloud] Applied pending init: speed={_speed}, scale={_scale}");
        }
    }

    void FixedUpdate()
    {
        // Only server moves clouds
        if (!IsServer) return;
        if (!IsSpawned) return;

        // Move right
        Vector3 movement = Vector3.right * _speed * Time.fixedDeltaTime;
        transform.position += movement;

        // Check if left edge of cloud passed right boundary
        if (_rightBoundary != null)
        {
            float cloudLeftEdge = transform.position.x - (_spriteHalfWidth * _scale);
            float rightBoundaryX = _rightBoundary.bounds.max.x;

            if (cloudLeftEdge > rightBoundaryX)
            {
                if (CloudManager.Instance != null)
                {
                    CloudManager.Instance.OnCloudReachedRightEdge(this);
                }
            }
        }
    }

    /// <summary>
    /// Initialize cloud with speed and scale. Call from server.
    /// </summary>
    public void Initialize(float speed, float scale)
    {
        Debug.Log($"[Cloud] Initialize called: speed={speed}, scale={scale}, IsSpawned={IsSpawned}, IsServer={IsServer}");

        _pendingSpeed = speed;
        _pendingScale = scale;
        _hasPendingInit = true;

        // Always apply locally on server
        _speed = speed;
        _scale = scale;
        transform.localScale = Vector3.one * scale;

        // If already spawned, sync to clients
        if (IsSpawned && IsServer)
        {
            ApplyScaleClientRpc(scale);
            Debug.Log($"[Cloud] Applied via RPC: speed={speed}, scale={scale}");
        }
        else
        {
            Debug.Log($"[Cloud] Stored as pending (will apply on spawn)");
        }
    }

    [ClientRpc]
    private void ApplyScaleClientRpc(float scale)
    {
        _scale = scale;
        transform.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// Teleport cloud to new position (used for wrapping)
    /// </summary>
    public void SetPosition(Vector3 newPosition)
    {
        if (!IsServer) return;
        transform.position = newPosition;
    }

    /// <summary>
    /// Teleport cloud with hide/show to avoid visible jump on clients
    /// </summary>
    public void TeleportWithHide(Vector3 newPosition)
    {
        if (!IsServer) return;
        StartCoroutine(TeleportSequence(newPosition));
    }

    private System.Collections.IEnumerator TeleportSequence(Vector3 newPosition)
    {
        // Teleport on all clients simultaneously (hides, moves, shows)
        TeleportClientRpc(newPosition);

        // Also set on server
        transform.position = newPosition;

        yield return null;
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 newPosition)
    {
        StartCoroutine(TeleportLocalSequence(newPosition));
    }

    private System.Collections.IEnumerator TeleportLocalSequence(Vector3 newPosition)
    {
        var sr = GetComponent<SpriteRenderer>();

        // Hide
        if (sr != null) sr.enabled = false;

        // Wait a frame
        yield return null;

        // Move instantly
        transform.position = newPosition;

        // Wait another frame for position to settle
        yield return null;

        // Show
        if (sr != null) sr.enabled = true;
    }
}
