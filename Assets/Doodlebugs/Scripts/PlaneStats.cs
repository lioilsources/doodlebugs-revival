using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages plane statistics: shield, health, handling, damage multiplier.
/// Server-authoritative - all writes go through server.
/// Attach to PlaneHolder prefab alongside PlayerController.
/// </summary>
public class PlaneStats : NetworkBehaviour
{
    // --- Constants ---
    private const int MaxShield = 3;
    private const int MaxHealth = 3;
    private const float MaxHandling = 1.0f;
    private const float MinHandling = 0.3f;
    private const float MaxDamageMultiplier = 2.0f;
    private const float ShieldRegenDelay = 5f;
    private const float ShieldRegenInterval = 3f;
    private const float DamageBoostDuration = 10f;
    private const float HandlingLossPerDamage = 0.1f;

    // --- Network Variables (server-write) ---
    public NetworkVariable<int> NetShield = new NetworkVariable<int>(MaxShield,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> NetHealth = new NetworkVariable<int>(MaxHealth,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> NetHandling = new NetworkVariable<float>(MaxHandling,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> NetDamageMultiplier = new NetworkVariable<float>(1.0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Remaining damage-boost seconds, quantized to 0.5s so it syncs at most
    // twice a second (for the HUD countdown on clients).
    public NetworkVariable<float> NetDamageBoostRemaining = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- Public accessors ---
    public int Shield => NetShield.Value;
    public int Health => NetHealth.Value;
    public float Handling => NetHandling.Value;
    public float DamageMultiplier => NetDamageMultiplier.Value;

    // --- Server-side timers (not networked) ---
    private float _shieldRegenTimer;
    private float _shieldRegenCooldown;
    private float _damageBoostTimer;

    // --- Invulnerability ---
    private const float InvulnerabilityDuration = 2f;
    private float _invulnerabilityTimer;

    /// <summary>True while invulnerable after respawn (networked for visual blink).</summary>
    public NetworkVariable<bool> NetInvulnerable = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsInvulnerable => NetInvulnerable.Value;

    private void Update()
    {
        if (!IsServer) return;

        float dt = Time.deltaTime;

        // Shield regeneration
        if (NetShield.Value < MaxShield)
        {
            _shieldRegenCooldown -= dt;
            if (_shieldRegenCooldown <= 0f)
            {
                _shieldRegenTimer -= dt;
                if (_shieldRegenTimer <= 0f)
                {
                    NetShield.Value = Mathf.Min(NetShield.Value + 1, MaxShield);
                    _shieldRegenTimer = ShieldRegenInterval;
                }
            }
        }

        // Damage boost countdown
        if (_damageBoostTimer > 0f)
        {
            _damageBoostTimer -= dt;
            if (_damageBoostTimer <= 0f)
            {
                NetDamageMultiplier.Value = 1.0f;
                _damageBoostTimer = 0f;
            }
        }

        // Sync quantized boost time for HUD (only writes on 0.5s steps)
        float quantized = Mathf.Ceil(Mathf.Max(0f, _damageBoostTimer) * 2f) / 2f;
        if (!Mathf.Approximately(NetDamageBoostRemaining.Value, quantized))
        {
            NetDamageBoostRemaining.Value = quantized;
        }

        // Invulnerability countdown
        if (_invulnerabilityTimer > 0f)
        {
            _invulnerabilityTimer -= dt;
            if (_invulnerabilityTimer <= 0f)
            {
                NetInvulnerable.Value = false;
                _invulnerabilityTimer = 0f;
            }
        }
    }

    /// <summary>True when the most recent TakeDamage was fully absorbed by shield (server-only).</summary>
    public bool LastDamageAbsorbedByShield { get; private set; }

    /// <summary>
    /// Apply damage to this plane. Returns true if the plane is dead.
    /// Server-only.
    /// </summary>
    public bool TakeDamage(int damage)
    {
        if (!IsServer) return false;
        if (NetInvulnerable.Value) return false;

        int remaining = damage;

        // Shield absorbs first
        int shieldAbsorbed = Mathf.Min(remaining, NetShield.Value);
        NetShield.Value -= shieldAbsorbed;
        remaining -= shieldAbsorbed;
        LastDamageAbsorbedByShield = remaining <= 0;

        // Health takes the rest
        NetHealth.Value = Mathf.Max(0, NetHealth.Value - remaining);

        // Handling degrades with total damage taken
        float handlingLoss = HandlingLossPerDamage * damage;
        NetHandling.Value = Mathf.Max(MinHandling, NetHandling.Value - handlingLoss);

        // Reset shield regen timer
        _shieldRegenCooldown = ShieldRegenDelay;
        _shieldRegenTimer = ShieldRegenInterval;

        return NetHealth.Value <= 0;
    }

    /// <summary>
    /// Apply a power-up effect. Server-only.
    /// </summary>
    public void ApplyPowerUp(PowerUpType type)
    {
        if (!IsServer) return;

        switch (type)
        {
            case PowerUpType.Health:
                NetHealth.Value = Mathf.Min(NetHealth.Value + 2, MaxHealth);
                break;

            case PowerUpType.Shield:
                NetShield.Value = Mathf.Min(NetShield.Value + 2, MaxShield);
                break;

            case PowerUpType.Repair:
                NetHandling.Value = Mathf.Min(NetHandling.Value + 0.3f, MaxHandling);
                break;

            case PowerUpType.Damage:
                NetDamageMultiplier.Value = Mathf.Min(NetDamageMultiplier.Value + 0.5f, MaxDamageMultiplier);
                _damageBoostTimer = DamageBoostDuration;
                break;
        }

        Debug.Log($"[PlaneStats] Applied {type} power-up to player {OwnerClientId}");
    }

    /// <summary>
    /// Reset all stats to defaults (on respawn). Server-only.
    /// </summary>
    public void ResetStats()
    {
        if (!IsServer) return;

        NetShield.Value = MaxShield;
        NetHealth.Value = MaxHealth;
        NetHandling.Value = MaxHandling;
        NetDamageMultiplier.Value = 1.0f;

        _shieldRegenCooldown = 0f;
        _shieldRegenTimer = ShieldRegenInterval;
        _damageBoostTimer = 0f;

        // Start invulnerability
        _invulnerabilityTimer = InvulnerabilityDuration;
        NetInvulnerable.Value = true;
    }

    /// <summary>
    /// Get remaining damage boost time (for HUD). Quantized to 0.5s on clients.
    /// </summary>
    public float DamageBoostTimeRemaining => IsServer ? _damageBoostTimer : NetDamageBoostRemaining.Value;
}
