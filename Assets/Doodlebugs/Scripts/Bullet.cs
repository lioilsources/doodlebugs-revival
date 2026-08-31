using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Bullet Prefab script
public class Bullet : NetworkBehaviour
{
    // The bullet spawns at the fire point, which can still overlap the shooter's
    // own collider (or be caught by it in a tight turn) on the first physics
    // steps - ignore the shooter for this long after spawn.
    private const float SelfHitGracePeriod = 0.25f;

    public GameObject hitEffect;

    private float _spawnTime;

    // Optional lifetime for short-range weapons (0 = unlimited). Server-only.
    private float _lifetime;

    private Rigidbody2D _rb;
    private Vector3 _baseScale;
    private bool _exploded;

    // Which WeaponProfile drives this projectile (physics on the server,
    // scale/tint everywhere). Synced so late-arriving visuals stay correct.
    private NetworkVariable<int> _weaponId = new NetworkVariable<int>((int)WeaponType.MG,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private WeaponProfile Profile => WeaponProfile.Get(_weaponId.Value);

    /// <summary>False while a mine is still arming - it hits nothing yet.
    /// Runs on every client (ForegroundTile checks it locally).</summary>
    public bool IsArmed => Time.time - _spawnTime >= Profile.ArmDelay;

    /// <summary>The prefab's shared bullet sprite, restored when a pooled
    /// bullet switches from an override weapon back to a plain one.</summary>
    private Sprite _defaultSprite;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _baseScale = transform.localScale;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) _defaultSprite = sr.sprite;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _spawnTime = Time.time;

        _weaponId.OnValueChanged += OnWeaponChanged;
        ApplyVisual();

        // Bullet replicates to every client - this doubles as the shot sound
        SfxManager.PlayShoot();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _weaponId.OnValueChanged -= OnWeaponChanged;
    }

    /// <summary>Assign the weapon profile driving this projectile. Server-only.</summary>
    public void SetWeapon(int weaponId)
    {
        if (IsServer)
        {
            _weaponId.Value = weaponId;
            ApplyVisual();
        }
    }

    private void OnWeaponChanged(int prev, int next) => ApplyVisual();

    private void ApplyVisual()
    {
        var profile = Profile;
        transform.localScale = _baseScale * profile.ProjectileScale;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileSpriteName))
            {
                var overrideSprite = Resources.Load<Sprite>(
                    "Sprites/Projectiles/" + profile.ProjectileSpriteName);
                if (overrideSprite != null)
                {
                    sr.sprite = overrideSprite;
                    sr.color = Color.white; // the art carries its own colours
                }
                else
                {
                    sr.color = profile.ProjectileTint;
                }
            }
            else
            {
                if (_defaultSprite != null) sr.sprite = _defaultSprite;
                sr.color = profile.ProjectileTint;
            }
            // Bullets render below clouds (order 10), so a lurking mine is
            // naturally hidden while it drifts inside one.
        }
    }

    /// <summary>Despawn the bullet after this many seconds (short-range weapons). Server-only.</summary>
    public void SetLifetime(float seconds)
    {
        if (IsServer)
        {
            _lifetime = seconds;
        }
    }

    private void Update()
    {
        if (!IsServer || _lifetime <= 0f) return;

        if (Time.time - _spawnTime >= _lifetime && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || _rb == null) return;

        var profile = Profile;

        // Rocket thrust along the projectile's facing
        if (profile.Acceleration > 0f)
        {
            _rb.AddForce((Vector2)transform.right * profile.Acceleration * Time.fixedDeltaTime,
                ForceMode2D.Impulse);
        }

        // Bombs tip nose-down, rockets keep facing their flight path
        if ((profile.GravityScale > 0f || profile.Acceleration > 0f) &&
            _rb.linearVelocity.sqrMagnitude > 0.5f)
        {
            transform.right = _rb.linearVelocity.normalized;
        }
    }

    // Track who shot this bullet for scoring
    private NetworkVariable<ulong> _shooterClientId = new NetworkVariable<ulong>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _shooterLocalPlayerIndex = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variable damage (affected by shooter's DamageMultiplier)
    private NetworkVariable<int> _damage = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Set bullet damage. Call from server after spawning.
    /// </summary>
    public void SetDamage(int damage)
    {
        if (IsServer)
        {
            _damage.Value = damage;
        }
    }

    /// <summary>
    /// Set the shooter's client ID and local player index. Call from server after spawning.
    /// </summary>
    public void SetShooter(ulong shooterClientId, int localPlayerIndex = 0)
    {
        if (IsServer)
        {
            _shooterClientId.Value = shooterClientId;
            _shooterLocalPlayerIndex.Value = localPlayerIndex;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        HandleContact(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        // A mine arms while a collider already overlaps it (or a plane parks on
        // top of a drifting one) - Enter alone would never fire.
        if (Profile.ArmDelay > 0f)
        {
            HandleContact(other);
        }
    }

    private void HandleContact(Collider2D other)
    {
        if (!IsServer || _exploded) return;

        // Not armed yet (mine) - pass through everything
        if (Time.time - _spawnTime < Profile.ArmDelay) return;

        if (other.gameObject.name == "Space") return;

        // Clouds block bullets (cover), but a falling bomb drops through and
        // a mine drifts inside one - that's where it hides.
        if ((Profile.GravityScale > 0f || Profile.ArmDelay > 0f) &&
            other.GetComponent<Cloud>() != null)
        {
            return;
        }

        var damagable = other.gameObject.GetComponent<IDamagable>();
        if (damagable != null)
        {
            var targetPlayer = other.gameObject.GetComponent<PlayerController>();
            if (targetPlayer != null && IsShootersOwnPlane(targetPlayer) &&
                Time.time - _spawnTime < SelfHitGracePeriod)
            {
                // Fresh bullet still leaving the shooter's own plane -
                // pass through without damage or despawn.
                return;
            }

            if (Profile.ExplosionRadius > 0f)
            {
                // The direct target sits inside the blast radius - the
                // explosion damages it, no separate direct hit.
                Explode();
            }
            else
            {
                if (targetPlayer != null && !IsShootersOwnPlane(targetPlayer))
                {
                    targetPlayer.SetLastAttacker(_shooterClientId.Value, _shooterLocalPlayerIndex.Value);
                }
                damagable.Hit(_damage.Value);
            }
        }
        else
        {
            // Non-damagable object (wall, foreground tile, ...) - explode or spark
            if (Profile.ExplosionRadius > 0f)
            {
                Explode();
            }
            else
            {
                PlayHitFxClientRpc(transform.position);
            }
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }

    private bool IsShootersOwnPlane(PlayerController player)
    {
        int localIdx = player.LocalPlayerIndex >= 0 ? player.LocalPlayerIndex : 0;
        return player.OwnerClientId == _shooterClientId.Value &&
               localIdx == _shooterLocalPlayerIndex.Value;
    }

    /// <summary>
    /// Server-side AoE: damage every plane in the blast radius, then blow away
    /// foreground tiles and play the boom on all clients. Own planes are only
    /// spared during the spawn grace period - a badly dropped bomb hurts.
    /// </summary>
    private void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        float radius = Profile.ExplosionRadius;
        Vector3 pos = transform.position;

        var hitPlanes = new HashSet<PlayerController>();
        foreach (var col in Physics2D.OverlapCircleAll(pos, radius))
        {
            var player = col.GetComponent<PlayerController>();
            if (player == null || !hitPlanes.Add(player)) continue;

            bool ownPlane = IsShootersOwnPlane(player);
            if (ownPlane && Time.time - _spawnTime < SelfHitGracePeriod) continue;

            if (!ownPlane)
            {
                player.SetLastAttacker(_shooterClientId.Value, _shooterLocalPlayerIndex.Value);
            }
            player.Hit(_damage.Value);
        }

        ExplodeClientRpc(pos, radius);
    }

    [ClientRpc]
    private void ExplodeClientRpc(Vector3 position, float radius)
    {
        SfxManager.PlayExplosion();

        // Terrain destruction is local-visual, same as single-tile bullet hits;
        // position+radius come from the server, so every client digs the
        // same crater.
        ForegroundScroller.Instance?.DestroyTilesInRadius(position, radius);

        if (hitEffect != null)
        {
            var effect = Instantiate(hitEffect, position, Quaternion.identity);
            effect.transform.localScale *= Mathf.Max(1f, radius);
            Destroy(effect, 0.8f);
        }
    }

    [ClientRpc]
    private void PlayHitFxClientRpc(Vector3 position)
    {
        if (hitEffect == null) return;
        var effect = Instantiate(hitEffect, position, Quaternion.identity);
        Destroy(effect, 0.8f);
    }
}
