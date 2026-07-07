using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Collectible power-up that drops from destroyed planes.
/// Hovers like a balloon at the crash site (gentle bob) for HoverSeconds,
/// then slowly floats up and away like a hot-air balloon. Blinks while
/// ascending, despawns after Lifetime. Picked up on collision with Player.
/// Server moves it (velocity only), NetworkTransform syncs to clients.
/// </summary>
public class PowerUp : NetworkBehaviour
{
    private const float HoverSeconds = 10f;   // balloon waits at the crash site
    private const float Lifetime = 20f;       // hover + ascent, then gone
    private const float BlinkStartTime = HoverSeconds; // blinks = leaving soon
    private const float BlinkRate = 8f; // blinks per second

    // Gentle bob while hovering (velocity wave integrates to ~±0.15 units)
    private const float BobSpeed = 0.3f;
    private const float BobAngularFreq = 2.1f;

    // Hot-air-balloon ascent: eases from 0 up to max climb speed
    private const float AscendMaxSpeed = 2.5f;
    private const float AscendRampSeconds = 4f;

    // The power-up spawns exactly where the victim died, while the victim's
    // collider is still there (respawn teleport lands a frame+ later on the
    // owner). Without this delay the dying plane instantly eats its own drop.
    private const float PickupDelay = 0.5f;

    public NetworkVariable<int> NetPowerUpType = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Tooltip("Icon per PowerUpType, in enum order: Health, Shield, Repair, Damage")]
    [SerializeField] private Sprite[] typeSprites;

    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D _rb;
    private float _spawnTime;
    private bool _collected;

    // Fallback tint colors per power-up type (used only when no sprite assigned)
    private static readonly Color[] TypeColors = {
        new Color(0.2f, 0.9f, 0.2f), // Health - green
        new Color(0.3f, 0.5f, 1.0f), // Shield - blue
        new Color(1.0f, 0.8f, 0.0f), // Repair - yellow
        new Color(1.0f, 0.2f, 0.2f), // Damage - red
        new Color(0.9f, 0.55f, 0.15f), // Weapon crate - orange
    };

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _spawnTime = Time.time;

        // Apply visual based on type
        NetPowerUpType.OnValueChanged += OnTypeChanged;
        ApplyVisual((PowerUpType)NetPowerUpType.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        NetPowerUpType.OnValueChanged -= OnTypeChanged;
    }

    private void OnTypeChanged(int prev, int next)
    {
        ApplyVisual((PowerUpType)next);
    }

    private void ApplyVisual(PowerUpType type)
    {
        if (_spriteRenderer == null) return;

        int idx = (int)type;

        if (typeSprites != null && idx >= 0 && idx < typeSprites.Length && typeSprites[idx] != null)
        {
            // Real icon - already colored, no tint
            _spriteRenderer.sprite = typeSprites[idx];
            _spriteRenderer.color = Color.white;
        }
        else if (idx >= 0 && idx < TypeColors.Length)
        {
            // Fallback: tint whatever sprite the prefab carries
            _spriteRenderer.color = TypeColors[idx];
        }

        // Set sorting order high so power-ups render above background
        _spriteRenderer.sortingOrder = 50;
    }

    private void FixedUpdate()
    {
        // Balloon movement is server-authoritative (NetworkTransform syncs)
        if (!IsServer || _rb == null || _collected) return;

        float elapsed = Time.time - _spawnTime;
        if (elapsed < HoverSeconds)
        {
            // Bob in place at the crash site
            _rb.linearVelocity = new Vector2(0f, BobSpeed * Mathf.Sin(elapsed * BobAngularFreq));
        }
        else
        {
            // Ease into a slow climb - off to space
            float t = (elapsed - HoverSeconds) / AscendRampSeconds;
            _rb.linearVelocity = new Vector2(0f, Mathf.Min(1f, t) * AscendMaxSpeed);
        }
    }

    private void Update()
    {
        float elapsed = Time.time - _spawnTime;

        // Self-destruct
        if (IsServer && elapsed >= Lifetime && !_collected)
        {
            DespawnSelf();
            return;
        }

        // Blink effect near end of life
        if (_spriteRenderer != null && elapsed >= BlinkStartTime)
        {
            float blink = Mathf.Sin(elapsed * BlinkRate * Mathf.PI * 2f);
            _spriteRenderer.enabled = blink > 0f;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryPickup(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // A plane already overlapping when the pickup delay ends (or while
        // invulnerable) never re-enters the trigger - Stay covers that case.
        TryPickup(other);
    }

    private void TryPickup(Collider2D other)
    {
        if (!IsServer) return;
        if (_collected) return;
        if (Time.time - _spawnTime < PickupDelay) return;

        if (other.gameObject.CompareTag("Player"))
        {
            var planeStats = other.gameObject.GetComponent<PlaneStats>();
            if (planeStats == null) return;

            // Freshly (re)spawned planes are invulnerable - they also shouldn't
            // hoover up drops (typically their own, at the death position).
            if (planeStats.IsInvulnerable) return;

            _collected = true;

            var type = (PowerUpType)NetPowerUpType.Value;
            if (type == PowerUpType.Weapon)
            {
                // Weapon crate: climb one tier of the current weapon (until death)
                other.gameObject.GetComponent<Shooting>()?.UpgradeWeaponTier();
            }
            else
            {
                planeStats.ApplyPowerUp(type);
            }

            PlayPickupFxClientRpc(transform.position, NetPowerUpType.Value);
            DespawnSelf();
        }
    }

    [ClientRpc]
    private void PlayPickupFxClientRpc(Vector3 position, int type)
    {
        SfxManager.PlayPowerUp();
    }

    private void DespawnSelf()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}
