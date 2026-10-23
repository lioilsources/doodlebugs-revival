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

    // Which skin from Resources/Sprites/Clouds this cloud wears (-1 = the
    // prefab sprite). Server-write and replicated, because CloudManager keeps
    // its cloud list only on the server: a client skinning from that list
    // would never touch a single cloud and would keep the prefab sprite for
    // the whole match.
    public NetworkVariable<int> NetSkin = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Pending initialization (if called before spawn)
    private float _pendingSpeed = 0f;
    private float _pendingScale = 0f;
    private bool _hasPendingInit = false;

    // Cached boundary and sprite info
    private Collider2D _rightBoundary;
    private float _spriteHalfWidth = 1f; // Will be calculated from SpriteRenderer
    private NetworkTransform _networkTransform;

    // Kinematic (prefab): the server moves it with MovePosition, so Box2D
    // sees a moving body with contact continuity instead of a Dynamic body
    // teleported by transform writes every step - the last suspect for
    // bullets stopping where no cloud is drawn.
    private Rigidbody2D _rb;

    void Awake()
    {

        // Cache sprite half width (at scale 1)
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            _spriteHalfWidth = sr.sprite.bounds.extents.x;
        }

        // The prefab-authored collider path belongs to whatever sprite the
        // prefab happens to wear - refit it before anyone can hide behind it.
        CloudManager.FitColliderToSprite(gameObject);

        // Cache NetworkTransform for teleport
        _networkTransform = GetComponent<NetworkTransform>();
        _rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>Re-read the sprite's half width after a skin change.
    /// Awake caches it once, but CloudManager swaps sprites every round and
    /// the photo clouds are twice the width of the old flat ones — a stale
    /// value puts the off-screen wrap point several units wrong.</summary>
    public void RefreshSpriteMetrics()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            _spriteHalfWidth = sr.sprite.bounds.extents.x;
        }
    }

    void Start()
    {

        // Cache right boundary
        _rightBoundary = ArenaBounds.Right;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // The skin may already ride in the spawn snapshot (no OnValueChanged
        // then) or arrive as a delta a tick later - handle both.
        NetSkin.OnValueChanged += OnSkinChanged;
        ApplySkin(NetSkin.Value);

        // Apply pending initialization if any
        if (_hasPendingInit && IsServer)
        {
            _speed = _pendingSpeed;
            _scale = _pendingScale;
            transform.localScale = Vector3.one * _scale;
            ApplyScaleClientRpc(_scale);
        }
    }

    public override void OnNetworkDespawn()
    {
        NetSkin.OnValueChanged -= OnSkinChanged;
        base.OnNetworkDespawn();
    }

    private void OnSkinChanged(int previousValue, int newValue)
    {
        ApplySkin(newValue);
    }

    /// <summary>Wear the replicated skin. CloudManager owns the sprite set (and
    /// the collider rebuild), this only keeps the wrap maths in step.</summary>
    private void ApplySkin(int skinIndex)
    {
        if (skinIndex < 0 || CloudManager.Instance == null) return;
        if (CloudManager.Instance.ApplySkin(gameObject, skinIndex))
        {
            RefreshSpriteMetrics();
        }
    }

    void FixedUpdate()
    {
        // Only server moves clouds
        if (!IsServer) return;
        if (!IsSpawned) return;

        // Move right - through the body, so the step is a motion, not a teleport
        Vector2 movement = Vector2.right * _speed * Time.fixedDeltaTime;
        if (_rb != null && _rb.bodyType == RigidbodyType2D.Kinematic)
        {
            _rb.MovePosition(_rb.position + movement);
        }
        else
        {
            transform.position += (Vector3)movement;
        }

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
        }
        else
        {
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
        if (_rb != null) _rb.position = newPosition;
    }

    /// <summary>
    /// Teleport cloud to new position using NetworkTransform.Teleport (no interpolation)
    /// Server is owner of clouds, so server can call Teleport directly
    /// </summary>
    public void TeleportWithHide(Vector3 newPosition)
    {
        if (!IsServer) return;

        // Use NetworkTransform.Teleport to skip interpolation (same as PlayerController)
        if (_networkTransform != null)
        {
            _networkTransform.Teleport(newPosition, transform.rotation, transform.localScale);
        }
        else
        {
            transform.position = newPosition;
        }
        // The body must jump too, or the next MovePosition walks on from the
        // old spot and the cloud snaps back for a step.
        if (_rb != null) _rb.position = newPosition;
    }
}
