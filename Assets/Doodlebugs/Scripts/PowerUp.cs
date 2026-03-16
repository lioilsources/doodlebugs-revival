using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Collectible power-up that drops from destroyed planes.
/// Falls with light gravity, picked up on collision with Player.
/// Self-destructs after 15s (blinks from 10s).
/// </summary>
public class PowerUp : NetworkBehaviour
{
    private const float Lifetime = 15f;
    private const float BlinkStartTime = 10f;
    private const float BlinkRate = 8f; // blinks per second

    public NetworkVariable<int> NetPowerUpType = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private SpriteRenderer _spriteRenderer;
    private float _spawnTime;
    private bool _collected;

    // Colors per power-up type
    private static readonly Color[] TypeColors = {
        new Color(0.2f, 0.9f, 0.2f), // Health - green
        new Color(0.3f, 0.5f, 1.0f), // Shield - blue
        new Color(1.0f, 0.8f, 0.0f), // Repair - yellow
        new Color(1.0f, 0.2f, 0.2f), // Damage - red
    };

    // Type labels for sprite text
    private static readonly string[] TypeLabels = { "H", "S", "R", "D" };

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _spriteRenderer = GetComponent<SpriteRenderer>();
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
        if (idx >= 0 && idx < TypeColors.Length)
        {
            _spriteRenderer.color = TypeColors[idx];
        }

        // Set sorting order high so power-ups render above background
        _spriteRenderer.sortingOrder = 50;
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
        if (!IsServer) return;
        if (_collected) return;

        if (other.gameObject.CompareTag("Player"))
        {
            var planeStats = other.gameObject.GetComponent<PlaneStats>();
            if (planeStats != null)
            {
                _collected = true;
                planeStats.ApplyPowerUp((PowerUpType)NetPowerUpType.Value);
                PlayPickupFxClientRpc(transform.position, NetPowerUpType.Value);
                DespawnSelf();
            }
        }
    }

    [ClientRpc]
    private void PlayPickupFxClientRpc(Vector3 position, int type)
    {
        // Simple scale-up + fade effect (no extra prefab needed)
        // The object is being despawned, so this runs on existing instances
    }

    private void DespawnSelf()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}
