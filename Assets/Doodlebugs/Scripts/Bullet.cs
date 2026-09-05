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

    // What the projectile is MADE of - comes from the shooter's plane shape
    // (PlaneModelCatalog.ElementOf). Synced for the same reason the weapon
    // is: a client that joins mid-flight has to draw the right thing, and
    // the impact/explosion RPCs carry it so the splash matches the shot.
    private NetworkVariable<int> _elementId = new NetworkVariable<int>((int)ProjectileElement.Metal,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private WeaponProfile Profile => WeaponProfile.Get(_weaponId.Value);
    private ProjectileElement Element => (ProjectileElement)_elementId.Value;

    // Sprites are shared and immutable - loading per shot would allocate on
    // every trigger pull. Key is the full Resources path.
    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    // The trail is detached on despawn and left to finish, so it needs to
    // outlive this component.
    private ParticleSystem _trail;

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
        _elementId.OnValueChanged += OnElementChanged;
        ApplyVisual();
        ApplyTrail();

        // Bullet replicates to every client - this doubles as the shot sound
        SfxManager.PlayShoot(Element, Profile.Type);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _weaponId.OnValueChanged -= OnWeaponChanged;
        _elementId.OnValueChanged -= OnElementChanged;
        ReleaseTrail();
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

    /// <summary>Assign the projectile element (shooter's plane shape). Server-only.</summary>
    public void SetElement(int elementId)
    {
        if (IsServer)
        {
            _elementId.Value = elementId;
            ApplyVisual();
            ApplyTrail();
        }
    }

    private void OnWeaponChanged(int prev, int next) => ApplyVisual();

    private void OnElementChanged(int prev, int next)
    {
        ApplyVisual();
        ApplyTrail();
    }

    /// <summary>Hang the element's trail off the projectile. Runs on every
    /// client; re-entrant because the element can land after the spawn.</summary>
    private void ApplyTrail()
    {
        if (_trail != null) Destroy(_trail.gameObject);
        var sr = GetComponent<SpriteRenderer>();
        int order = sr != null ? sr.sortingOrder - 1 : 0;   // behind the projectile
        _trail = EffectAssets.CreateTrailSystem(transform, ElementProfile.Get(Element).Trail, order);
    }

    /// <summary>
    /// Let the trail finish on its own. Parented to the bullet it would be
    /// destroyed with it, and the last half-second of puffs would pop out of
    /// existence mid-air.
    /// </summary>
    private void ReleaseTrail()
    {
        if (_trail == null) return;

        var go = _trail.gameObject;
        go.transform.SetParent(null, true);
        var emission = _trail.emission;
        emission.rateOverTime = 0f;
        Destroy(go, _trail.main.startLifetime.constantMax + 0.1f);
        _trail = null;
    }

    private void ApplyVisual()
    {
        var profile = Profile;
        // Legacy default: the prefab's own scale, which exists to shrink the
        // 1289x974 shared tracer texture. Authored art overrides this below -
        // inheriting it would render a 32 px sprite at 0.03 world units.
        transform.localScale = _baseScale * profile.ProjectileScale;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            // Element art first, then the Metal set, then the weapon's own
            // override, then the shared tracer tinted. Every step is optional
            // so the game runs with no generated art at all.
            var element = ElementProfile.Get(Element);
            var sprite = CachedSprite(element.ProjectilePath(profile.Type));

            if (sprite == null && Element != ProjectileElement.Metal)
            {
                sprite = CachedSprite(
                    ElementProfile.Get(ProjectileElement.Metal).ProjectilePath(profile.Type));
            }
            if (sprite == null && !string.IsNullOrEmpty(profile.ProjectileSpriteName))
            {
                sprite = CachedSprite("Sprites/Projectiles/" + profile.ProjectileSpriteName);
            }

            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.color = Color.white;   // the art carries its own colours
                ApplyArtScale(sprite, profile);
            }
            else
            {
                if (_defaultSprite != null) sr.sprite = _defaultSprite;
                // No art for this pair - the element still shows as a tint.
                sr.color = Element == ProjectileElement.Metal
                    ? profile.ProjectileTint
                    : element.Tint;
            }
            // Bullets render below clouds (order 10), so a lurking mine is
            // naturally hidden while it drifts inside one.
        }
    }

    /// <summary>
    /// Size authored art by MEASURING it: scale the sprite so its long axis
    /// lands on ElementProfile.WorldLength for this weapon. The prefab's
    /// localScale is calibrated for one specific legacy texture and means
    /// nothing to a sprite drawn at its own pixel size, so art that opts in
    /// here must not inherit it. Uniform, so nothing is stretched - the
    /// legacy scale is non-uniform (0.094 x 0.111) to correct that one
    /// texture's aspect.
    /// </summary>
    private void ApplyArtScale(Sprite sprite, WeaponProfile profile)
    {
        float ppu = sprite.pixelsPerUnit > 0.01f ? sprite.pixelsPerUnit : 100f;
        float spriteLong = Mathf.Max(sprite.rect.width, sprite.rect.height) / ppu;
        if (spriteLong <= 0.0001f) return;

        float target = ElementProfile.WorldLength(profile.Type) * profile.ProjectileScale;
        float scale = target / spriteLong;
        transform.localScale = new Vector3(scale, scale, _baseScale.z);
    }

    private static Sprite CachedSprite(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_spriteCache.TryGetValue(path, out var cached)) return cached;

        var sprite = Resources.Load<Sprite>(path);
        _spriteCache[path] = sprite;   // null is cached too - a miss is permanent
        return sprite;
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
                // A plain hit on a plane used to draw nothing - the victim's
                // own shield/hull flash said something was hit, never by what.
                PlayHitFxClientRpc(transform.position, _elementId.Value);
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
                PlayHitFxClientRpc(transform.position, _elementId.Value);
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

        ExplodeClientRpc(pos, radius, _elementId.Value);
    }

    [ClientRpc]
    private void ExplodeClientRpc(Vector3 position, float radius, int elementId)
    {
        var element = (ProjectileElement)elementId;
        SfxManager.PlayExplosion(element);

        // Terrain destruction is local-visual, same as single-tile bullet hits;
        // position+radius come from the server, so every client digs the
        // same crater.
        ForegroundScroller.Instance?.DestroyTilesInRadius(position, radius);

        // Element art if it exists, the old explosion prefab if it does not.
        EffectLibrary.SpawnExplosion(element, position, radius, hitEffect);
    }

    [ClientRpc]
    private void PlayHitFxClientRpc(Vector3 position, int elementId)
    {
        var element = (ProjectileElement)elementId;
        SfxManager.PlayImpact(element);
        EffectLibrary.SpawnImpact(element, position, hitEffect);
    }
}
