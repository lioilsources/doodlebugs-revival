using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Collectible power-up that drops from destroyed planes.
/// Hovers like a balloon at the crash site (gentle bob) for HoverSeconds,
/// then slowly floats up and away like a hot-air balloon. Blinks while
/// ascending, despawns after Lifetime. Picked up on collision with Player.
/// Server moves it (velocity only), NetworkTransform syncs to clients.
/// </summary>
public class PowerUp : NetworkBehaviour, IDamagable
{
    // --- supply drop (Prompts/29 §5) ---
    // A crate on a parachute: sinks from the top edge to mid-height, hangs
    // there, and blows up when shot - so you can deny it to whoever is near.
    private const float DropSinkSpeed = 2f;
    private const float DropHangSeconds = 25f;
    private const float DropBlastRadius = 3f;
    private const float DropBlastKnockback = 12f;
    private const int DropBlastDamage = 2;

    public NetworkVariable<bool> NetIsSupplyDrop = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Extra data for the type: the special's WeaponType.</summary>
    public NetworkVariable<int> NetPayload = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _dropHangY;
    private float _dropArrivedAt = -1f;
    private ulong _lastShooterClient;
    private int _lastShooterIdx;
    private bool _hasShooter;
    private GameObject _chute;

    /// <summary>Server: fired when a supply drop is taken, shot or expires.</summary>
    public static event System.Action<string> OnSupplyDropResolved;

    private static void ResolveDrop(string outcome)
    {
        Telemetry.Log("supply_drop_end", ("outcome", outcome));
        OnSupplyDropResolved?.Invoke(outcome);
    }

    public void SetLastShooter(ulong clientId, int localIdx)
    {
        _lastShooterClient = clientId;
        _lastShooterIdx = localIdx;
        _hasShooter = true;
    }

    /// <summary>Server: make this a supply drop that hangs at `hangY`.</summary>
    public void ServerMakeSupplyDrop(PowerUpType type, int payload, float hangY)
    {
        if (!IsServer) return;
        NetPowerUpType.Value = (int)type;
        NetPayload.Value = payload;
        NetIsSupplyDrop.Value = true;
        _dropHangY = hangY;
        _spawnTime = Time.time;
    }

    // IDamagable: only a supply crate reacts to being shot
    public void Hit(int damage)
    {
        if (!IsServer || _collected || !NetIsSupplyDrop.Value) return;
        _collected = true;
        Vector3 pos = transform.position;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.InHangar) continue;
            Vector2 d = (Vector2)p.transform.position - (Vector2)pos;
            float dist = d.magnitude;
            // The shooter's own plane is hurt and pushed, but never credits itself
            bool own = _hasShooter && p.OwnerClientId == _lastShooterClient &&
                       Mathf.Max(0, p.LocalPlayerIndex) == _lastShooterIdx;
            bool credit = _hasShooter && !own;
            if (dist <= DropBlastRadius)
            {
                if (credit) p.SetLastAttacker(_lastShooterClient, _lastShooterIdx);
                p.Hit(DropBlastDamage);
            }
            if (dist <= DropBlastRadius * 1.6f)
            {
                Vector2 dir = dist > 0.01f ? d / dist : Vector2.up;
                p.ServerApplyImpulse(dir * DropBlastKnockback * (1f - dist / (DropBlastRadius * 1.6f)), 30f,
                    _lastShooterClient, _lastShooterIdx, credit);
            }
        }
        CrateBlastClientRpc(pos);
        ResolveDrop("shot");
        DespawnSelf();
    }

    [ClientRpc]
    private void CrateBlastClientRpc(Vector3 pos)
    {
        SfxManager.PlayExplosion();
        EffectLibrary.SpawnExplosion(ProjectileElement.Fire, pos, DropBlastRadius, null);
        WorldFx.Ring(pos, DropBlastRadius, new Color(1f, 0.85f, 0.4f), 0.3f, 0.15f);
        ForegroundScroller.Instance?.DestroyTilesInRadius(pos, DropBlastRadius);
    }

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

    /// <summary>Crates in the sky on this peer (bot sensing, no per-tick scene scan).</summary>
    public static readonly System.Collections.Generic.List<PowerUp> Active =
        new System.Collections.Generic.List<PowerUp>();

    public PowerUpType Type => (PowerUpType)NetPowerUpType.Value;

    /// <summary>Still up for grabs (not picked, not despawning).</summary>
    public bool IsAvailable => !_collected;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!Active.Contains(this)) Active.Add(this);

        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _spawnTime = Time.time;

        // Apply visual based on type
        NetPowerUpType.OnValueChanged += OnTypeChanged;
        NetIsSupplyDrop.OnValueChanged += OnDropChanged;
        ApplyVisual((PowerUpType)NetPowerUpType.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        Active.Remove(this);
        NetPowerUpType.OnValueChanged -= OnTypeChanged;
        NetIsSupplyDrop.OnValueChanged -= OnDropChanged;
    }

    private void OnTypeChanged(int prev, int next)
    {
        ApplyVisual((PowerUpType)next);
    }

    private void ApplyVisual(PowerUpType type)
    {
        if (_spriteRenderer == null) return;

        int idx = (int)type;

        var named = idx >= (typeSprites?.Length ?? 0) ? LoadIcon(type) : null;
        if (named != null)
        {
            _spriteRenderer.sprite = named;
            _spriteRenderer.color = Color.white;
        }
        else if (typeSprites != null && idx >= 0 && idx < typeSprites.Length && typeSprites[idx] != null)
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
        ApplyChute();
    }

    private static readonly System.Collections.Generic.Dictionary<PowerUpType, Sprite> _icons = new();

    private static Sprite LoadIcon(PowerUpType type)
    {
        if (_icons.TryGetValue(type, out var s)) return s;
        string key = type == PowerUpType.Nitro ? "powerup_nitro"
                   : type == PowerUpType.FullRepair ? "powerup_repair"
                   : type == PowerUpType.Special ? "powerup_supply"
                   : null;
        s = key != null ? Resources.Load<Sprite>("Sprites/PowerUps/" + key) : null;
        _icons[type] = s;
        return s;
    }

    private void ApplyChute()
    {
        bool drop = NetIsSupplyDrop.Value;
        if (drop && _chute == null)
        {
            // A canopy made of the UI disc, squashed - no art asset
            _chute = new GameObject("Chute");
            _chute.transform.SetParent(transform, false);
            _chute.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            _chute.transform.localScale = new Vector3(1.6f, 0.7f, 1f);
            var sr = _chute.AddComponent<SpriteRenderer>();
            sr.sprite = UiSprites.Disc;
            sr.color = new Color(1f, 0.95f, 0.85f);
            sr.sortingOrder = 49;
            transform.localScale = Vector3.one * 1.5f;
        }
        if (_chute != null) _chute.SetActive(drop);
    }

    private void OnDropChanged(bool prev, bool next)
    {
        ApplyChute();
        if (next)
        {
            // Four channels: banner + siren + haptic + kill-feed line
            SfxManager.PlayNamed("supply", 0.9f, 0f);
            SfxManager.Haptic(HapticKind.Medium);
            BannerOverlay.Show("SUPPLY DROP", UiTheme.Highlight, transform.position.x);
            GameHUD.Instance?.PushFeedLine("SUPPLY DROP");
        }
    }

    private void FixedUpdate()
    {
        // Balloon movement is server-authoritative (NetworkTransform syncs)
        if (!IsServer || _rb == null || _collected) return;

        float elapsed = Time.time - _spawnTime;
        if (NetIsSupplyDrop.Value)
        {
            // Sink to the hang height, then sway gently
            if (_rb.position.y > _dropHangY)
            {
                _rb.linearVelocity = new Vector2(0f, -DropSinkSpeed);
            }
            else
            {
                if (_dropArrivedAt < 0f) _dropArrivedAt = Time.time;
                _rb.linearVelocity = new Vector2(0.4f * Mathf.Sin(elapsed * 0.9f), BobSpeed * Mathf.Sin(elapsed * BobAngularFreq));
            }
            return;
        }
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

        bool drop = NetIsSupplyDrop.Value;

        // Self-destruct
        if (IsServer && !_collected)
        {
            bool expired = drop ? _dropArrivedAt > 0f && Time.time - _dropArrivedAt >= DropHangSeconds
                                : elapsed >= Lifetime;
            if (expired)
            {
                if (drop) ResolveDrop("expired");
                DespawnSelf();
                return;
            }
        }

        // Blink effect near end of life
        if (_spriteRenderer != null && !drop && elapsed >= BlinkStartTime)
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
            var shooting = other.gameObject.GetComponent<Shooting>();
            var pc = other.gameObject.GetComponent<PlayerController>();
            string line;
            switch (type)
            {
                case PowerUpType.Weapon:
                    // Weapon crate: climb one tier (until death); maxed = shield + gadget
                    line = shooting != null ? shooting.UpgradeWeaponTier() : "";
                    break;
                case PowerUpType.Nitro:
                    shooting?.ServerRechargeGadget();
                    pc?.ServerBoost(1.5f);
                    line = "NITRO";
                    break;
                case PowerUpType.Special:
                    var special = (WeaponType)Mathf.Max(0, NetPayload.Value);
                    shooting?.GiveSpecial(special);
                    line = WeaponProfile.Get(special).DisplayName + " x" + Mathf.Max(1, WeaponProfile.Get(special).SpecialAmmo);
                    break;
                case PowerUpType.FullRepair:
                    planeStats.ServerFullRepair();
                    line = "FULL REPAIR";
                    break;
                default:
                    planeStats.ApplyPowerUp(type);
                    line = type == PowerUpType.Health ? "+HULL" : type == PowerUpType.Shield ? "+SHIELD"
                         : type == PowerUpType.Repair ? "+HANDLING" : "DAMAGE x1.5";
                    break;
            }
            if (NetIsSupplyDrop.Value) ResolveDrop("taken");
            Telemetry.Log("pickup", ("type", type));

            ulong owner = other.GetComponent<NetworkObject>() != null ? other.GetComponent<NetworkObject>().OwnerClientId : 0;
            bool isBot = pc != null && pc.IsBot;
            PlayPickupFxClientRpc(transform.position, NetPowerUpType.Value, new Unity.Collections.FixedString64Bytes(line ?? ""), owner, isBot);
            DespawnSelf();
        }
    }

    private static readonly Color[] PickupColors =
    {
        new Color(0.3f, 0.95f, 0.4f), new Color(0.4f, 0.65f, 1f), new Color(1f, 0.85f, 0.2f),
        new Color(1f, 0.35f, 0.3f), new Color(1f, 0.6f, 0.2f), new Color(0.4f, 0.9f, 1f),
        new Color(0.9f, 0.3f, 0.65f), new Color(0.3f, 0.95f, 0.4f),
    };

    /// <summary>Four channels (ROADMAP §3.3): flash + sound + haptic (taker) + floating text.</summary>
    [ClientRpc]
    private void PlayPickupFxClientRpc(Vector3 position, int type, Unity.Collections.FixedString64Bytes line, ulong takerClientId, bool takerIsBot)
    {
        Color c = type >= 0 && type < PickupColors.Length ? PickupColors[type] : Color.white;
        SfxManager.PlayPowerUp();
        if ((PowerUpType)type == PowerUpType.Special) SfxManager.PlayNamed("special", 0.8f, 0f);
        WorldFx.Ring(position, 1.4f, c, 0.25f, 0.2f);
        WorldFx.Puff(position, c, 8);
        WorldFx.Text(position + Vector3.up * 0.8f, line.ToString(), c, 0.55f, 1f);
        var nm = NetworkManager.Singleton;
        if (nm != null && takerClientId == nm.LocalClientId && !takerIsBot)
        {
            SfxManager.Haptic(HapticKind.Light);
        }
    }

    private void DespawnSelf()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}
