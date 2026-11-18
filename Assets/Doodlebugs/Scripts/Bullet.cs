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

    /// <summary>Seconds since spawn (local clock).</summary>
    public float Age => Time.time - _spawnTime;

    // Server-only behaviour state (Prompts/29)
    private bool _pierce;                // fully charged Lance Mk II
    private readonly HashSet<PlayerController> _pierced = new();
    private float _detonateAt = -1f;     // remote detonation scheduled
    private bool _thrustKilled;          // EMP
    private bool _homingInit;
    private PlayerController _homingTarget;

    // Client visuals
    private bool _spawnFxPending;
    private SpriteRenderer _radiusRing;
    private SpriteRenderer _impactMarker;
    private float _nextMarkerAt, _nextBeepAt;
    private Vector3 _prevPos;

    // Optional lifetime for short-range weapons (0 = unlimited). Server-only.
    private float _lifetime;

    private Rigidbody2D _rb;
    private Vector3 _baseScale;
    private bool _exploded;

    // One terrain bite per projectile. A bullet overlapping two or three
    // tiles in its contact step used to fire every one of their 2x2 bites,
    // so a "2x2 hole" was really 3x3 or 4x3; the first tile to see the
    // bullet now claims it. Local per instance, so every peer bites once.
    private bool _terrainBitten;

    // The hitbox. The prefab's box is sized for the legacy tracer texture at
    // the prefab's own (tiny) scale; authored art is scaled UP by
    // ApplyArtScale, and a box that just rode along with it was 19 x 7.5
    // world units for a tracer - a third of the screen width. That is what
    // bit a quarter of the terrain out of every foreground hit and stopped
    // bullets in the empty air ten units short of a cloud.
    private BoxCollider2D _box;
    private Vector2 _baseBoxSize;
    private Vector2 _baseBoxOffset;

    // Opaque rect of each projectile sprite in sprite-local units, computed
    // once from its pixels - the generated art has transparent padding and a
    // tracer's stroke is 3 of its 16 rows, so the sprite bounds would still
    // be several times too tall.
    private static readonly Dictionary<Sprite, Rect> _opaqueRectCache = new();

    // Floor for the hitbox in world units: a 3 px tracer stroke scaled to a
    // tenth of a unit would slip between a plane's collider and the
    // physics step. The legacy box was 0.53 x 0.24.
    private const float MinHitboxWorld = 0.25f;

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

    /// <summary>ForegroundTile: true for the first tile this projectile
    /// touches, false for every other one in the same contact step.</summary>
    public bool TryClaimTerrainBite()
    {
        if (_terrainBitten) return false;
        _terrainBitten = true;
        return true;
    }

    /// <summary>The prefab's shared bullet sprite, restored when a pooled
    /// bullet switches from an override weapon back to a plain one.</summary>
    private Sprite _defaultSprite;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _baseScale = transform.localScale;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) _defaultSprite = sr.sprite;
        _box = GetComponent<BoxCollider2D>();
        if (_box != null)
        {
            _baseBoxSize = _box.size;
            _baseBoxOffset = _box.offset;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _spawnTime = Time.time;
        _terrainBitten = false;

        _pierced.Clear();
        _detonateAt = -1f;
        _thrustKilled = false;
        _homingInit = false;
        _homingTarget = null;
        _prevPos = transform.position;

        _weaponId.OnValueChanged += OnWeaponChanged;
        _elementId.OnValueChanged += OnElementChanged;
        _shooterClientId.OnValueChanged += OnShooterChanged;
        _shooterLocalPlayerIndex.OnValueChanged += OnShooterIdxChanged;
        ApplyVisual();
        ApplyTrail();

        // Sound + muzzle flash wait one frame: weapon, element and shooter
        // are set right after Spawn and arrive with the next delta.
        _spawnFxPending = true;
    }

    private void OnShooterChanged(ulong prev, ulong next) => ApplyTrail();
    private void OnShooterIdxChanged(int prev, int next) => ApplyTrail();

    private void PlaySpawnFx()
    {
        var profile = Profile;
        // Bullet replicates to every client - this doubles as the shot sound
        SfxManager.PlayShoot(Element, profile.Type);
        if (profile.ForceMultiplier > 0f && profile.Category != WeaponCategory.Child)
        {
            WorldFx.Muzzle(transform.position, ElementProfile.Get(Element).Tint);
        }
        if (profile.Type == WeaponType.BigOne) SfxManager.PlayNamed("siren", 0.7f, 0f);
        else if (profile.GravityScale > 0f && profile.Category != WeaponCategory.Child) SfxManager.PlayNamed("whistle", 0.35f, 0.05f);

        // Radius you can read: mine trigger once armed, airburst blast all the way
        if (IsMineLike(profile) || profile.RemoteDetonate)
        {
            _radiusRing = WorldFx.AttachRing(transform, profile.ExplosionRadius,
                new Color(1f, 0.35f, 0.25f, profile.RemoteDetonate ? 0.45f : 0f), 0.08f);
        }
    }

    private static bool IsMineLike(WeaponProfile p) => p.LinearDrag > 0f && p.ArmDelay > 0f;

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _weaponId.OnValueChanged -= OnWeaponChanged;
        _elementId.OnValueChanged -= OnElementChanged;
        _shooterClientId.OnValueChanged -= OnShooterChanged;
        _shooterLocalPlayerIndex.OnValueChanged -= OnShooterIdxChanged;
        ReleaseTrail();
        if (_radiusRing != null) Destroy(_radiusRing.gameObject);
        if (_impactMarker != null) Destroy(_impactMarker.gameObject);
        _radiusRing = null;
        _impactMarker = null;
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
        var preset = ElementProfile.Get(Element).Trail;
        if (preset != null)
        {
            // Whose shot is it (Prompts/29 D9): a white core for yours, the
            // shooter's colour for everyone else's. The element keeps its sprite.
            preset = preset.Clone();
            var nm = NetworkManager.Singleton;
            bool mine = nm != null && _shooterClientId.Value == nm.LocalClientId &&
                        _shooterLocalPlayerIndex.Value != PlayerController.BotLocalPlayerIndex;
            Color c = mine ? Color.white
                : PlayerColorManager.Instance != null
                    ? PlayerColorManager.Instance.GetColor(_shooterClientId.Value, Mathf.Max(0, _shooterLocalPlayerIndex.Value))
                    : preset.ColorStart;
            preset.ColorStart = Color.Lerp(preset.ColorStart, c, 0.7f);
        }
        _trail = EffectAssets.CreateTrailSystem(transform, preset, order);
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
                RestoreLegacyHitbox();
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
        FitHitbox(sprite, scale);
    }

    /// <summary>
    /// Hitbox = the opaque part of the drawn sprite. Set in sprite-local
    /// units, so the object's scale carries it to exactly the size on
    /// screen; only a floor keeps a hairline stroke from being skipped over.
    /// </summary>
    private void FitHitbox(Sprite sprite, float scale)
    {
        if (_box == null) return;

        Rect r = OpaqueRect(sprite);
        float minLocal = MinHitboxWorld / Mathf.Max(scale, 0.0001f);
        _box.size = new Vector2(Mathf.Max(r.width, minLocal), Mathf.Max(r.height, minLocal));
        _box.offset = r.center;
    }

    /// <summary>The prefab's box again, for the legacy shared tracer the
    /// prefab's scale was calibrated for.</summary>
    private void RestoreLegacyHitbox()
    {
        if (_box == null) return;
        _box.size = _baseBoxSize;
        _box.offset = _baseBoxOffset;
    }

    /// <summary>
    /// Bounding rect of the pixels with alpha above a quarter, relative to
    /// the sprite pivot and in world units at scale 1. Falls back to the
    /// sprite bounds when the texture is not readable or fully transparent.
    /// The projectile textures are at most 48 x 32 px, so reading them once
    /// is nothing; the result is cached per sprite.
    /// </summary>
    private static Rect OpaqueRect(Sprite sprite)
    {
        if (_opaqueRectCache.TryGetValue(sprite, out var cached)) return cached;

        var b = sprite.bounds;
        var result = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);

        var tex = sprite.texture;
        if (tex != null && tex.isReadable)
        {
            var rect = sprite.rect;
            int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            var px = tex.GetPixels32();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * tex.width + x0;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a <= 64) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX >= 0)
            {
                float ppu = sprite.pixelsPerUnit > 0.01f ? sprite.pixelsPerUnit : 100f;
                var pivot = sprite.pivot;   // pixels, relative to the sprite rect
                result = new Rect(
                    (minX - pivot.x) / ppu,
                    (minY - pivot.y) / ppu,
                    (maxX + 1 - minX) / ppu,
                    (maxY + 1 - minY) / ppu);
            }
        }

        _opaqueRectCache[sprite] = result;
        return result;
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

    /// <summary>Server: pierce the first plane and clouds (full-charge Lance Mk II).</summary>
    public void SetPierce(bool pierce)
    {
        if (IsServer) _pierce = pierce;
    }

    public bool IsFrom(ulong clientId, int localPlayerIndex) =>
        _shooterClientId.Value == clientId && _shooterLocalPlayerIndex.Value == localPlayerIndex;

    /// <summary>Server: burst now (remote detonation).</summary>
    public void ServerDetonate()
    {
        if (!IsServer || _exploded || !IsSpawned) return;
        if (Profile.ExplosionRadius > 0f) Explode();
        NetworkObject.Despawn();
    }

    /// <summary>Server: burst at a given time (a tap still flies the minimum fuse).</summary>
    public void DetonateAt(float time)
    {
        if (IsServer) _detonateAt = time;
    }

    /// <summary>Server: removed without a blast (Thunderclap wipes it).</summary>
    public void ServerFizzle()
    {
        if (!IsServer || !IsSpawned) return;
        FizzleClientRpc(transform.position);
        NetworkObject.Despawn();
    }

    [ClientRpc]
    private void FizzleClientRpc(Vector3 pos) => WorldFx.Puff(pos, new Color(0.8f, 0.9f, 1f), 5, 0.8f);

    /// <summary>Server: EMP - rockets and seekers lose thrust and homing.</summary>
    public void ServerKillThrust()
    {
        if (!IsServer) return;
        _thrustKilled = true;
        _homingTarget = null;
        if (_rb != null) _rb.gravityScale = Mathf.Max(_rb.gravityScale, 1f);
    }

    private void Update()
    {
        if (_spawnFxPending)
        {
            _spawnFxPending = false;
            PlaySpawnFx();
        }
        UpdateClientVisuals();

        if (!IsServer || !IsSpawned) return;

        if (_detonateAt > 0f && Time.time >= _detonateAt)
        {
            ServerDetonate();
            return;
        }

        if (_lifetime > 0f && Time.time - _spawnTime >= _lifetime)
        {
            if (Profile.ExplodeOnExpire && Profile.ExplosionRadius > 0f) Explode();
            NetworkObject.Despawn();
        }
    }

    // --- client-side readability (Prompts/29 §6) -------------------------------

    private void UpdateClientVisuals()
    {
        var profile = Profile;
        Vector3 pos = transform.position;
        Vector3 vel = Time.deltaTime > 0f ? (pos - _prevPos) / Time.deltaTime : Vector3.zero;
        _prevPos = pos;

        if (_radiusRing != null)
        {
            if (IsMineLike(profile))
            {
                // Armed mine: pulsing trigger radius; beeps faster as YOUR plane nears
                if (IsArmed)
                {
                    float pulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 6f);
                    _radiusRing.color = new Color(1f, 0.3f, 0.2f, pulse);
                    BeepIfLocalPlaneNear(pos, profile);
                }
            }
            WorldFx.SetRadius(_radiusRing, profile.ExplosionRadius);
        }

        // Where the bomb will land, for everyone
        if (profile.GravityScale > 0f && profile.ArmDelay <= 0f && Time.time >= _nextMarkerAt)
        {
            _nextMarkerAt = Time.time + 0.1f;
            UpdateImpactMarker(pos, IsServer && _rb != null ? (Vector3)_rb.linearVelocity : vel, profile);
        }
    }

    private void BeepIfLocalPlaneNear(Vector3 pos, WeaponProfile profile)
    {
        if (Time.time < _nextBeepAt) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        float best = float.MaxValue;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (!p.IsOwner || p.IsBot || p.InHangar) continue;
            if (IsShootersOwnPlane(p)) continue;
            best = Mathf.Min(best, Vector2.Distance(p.transform.position, pos));
        }
        if (best > 4f) return;
        SfxManager.PlayNamed("beep", 0.4f, 0f);
        _nextBeepAt = Time.time + Mathf.Lerp(0.12f, 0.6f, best / 4f);
    }

    private void UpdateImpactMarker(Vector3 pos, Vector3 vel, WeaponProfile profile)
    {
        float g = Physics2D.gravity.y * profile.GravityScale;
        int mask = 1 << LayerMask.NameToLayer("Foreground");
        float groundY = ArenaBounds.Ground != null ? ArenaBounds.Ground.bounds.max.y : -100f;
        Vector2 p = pos, v = vel;
        Vector2? hit = null;
        const float step = 0.06f;
        for (int i = 0; i < 60 && hit == null; i++)
        {
            Vector2 next = p + v * step + 0.5f * g * step * step * Vector2.up;
            var h = Physics2D.Linecast(p, next, mask);
            if (h.collider != null) hit = h.point;
            else if (next.y <= groundY) hit = new Vector2(next.x, groundY);
            v += g * step * Vector2.up;
            p = next;
        }
        if (hit == null)
        {
            if (_impactMarker != null) _impactMarker.enabled = false;
            return;
        }
        if (_impactMarker == null)
        {
            var go = new GameObject("ImpactMarker");
            _impactMarker = go.AddComponent<SpriteRenderer>();
            _impactMarker.sprite = UiSprites.Ring(0.05f);
            _impactMarker.sortingOrder = WorldFx.Order - 6;
        }
        _impactMarker.enabled = true;
        _impactMarker.transform.position = hit.Value;
        float r = profile.ExplosionRadius * (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
        _impactMarker.transform.localScale = new Vector3(r * 2f / 1.28f, r * 0.7f / 1.28f, 1f);
        _impactMarker.color = new Color(1f, 0.3f, 0.2f, 0.75f);
    }

    private void FixedUpdate()
    {
        if (!IsServer || _rb == null) return;

        var profile = Profile;

        // Homing: lock once at launch, steer at the profile's turn rate; a
        // target hiding in a cloud breaks the lock for good.
        if (profile.HomingTurnRate > 0f && !_thrustKilled)
        {
            if (!_homingInit) { _homingInit = true; AcquireTarget(profile); }
            if (_homingTarget != null)
            {
                if (Age > profile.HomingSeconds || !_homingTarget.IsSpawned || _homingTarget.InHangar ||
                    InsideCloud(_homingTarget.transform.position))
                {
                    _homingTarget = null;
                }
                else
                {
                    Vector2 v = _rb.linearVelocity;
                    Vector2 want = ((Vector2)_homingTarget.transform.position - _rb.position).normalized;
                    Vector2 turned = Vector3.RotateTowards(v.sqrMagnitude > 0.01f ? v.normalized : (Vector2)transform.right,
                        want, profile.HomingTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                    _rb.linearVelocity = turned * v.magnitude;
                }
            }
        }

        // Rocket thrust along the projectile's facing
        if (profile.Acceleration > 0f && !_thrustKilled)
        {
            _rb.AddForce((Vector2)transform.right * profile.Acceleration * Time.fixedDeltaTime,
                ForceMode2D.Impulse);
        }

        if (profile.MaxSpeed > 0f && _rb.linearVelocity.sqrMagnitude > profile.MaxSpeed * profile.MaxSpeed)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * profile.MaxSpeed;
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

        if (ArenaBounds.IsSpace(other)) return;

        // Clouds block bullets (cover), but a falling bomb drops through and
        // a mine drifts inside one - that's where it hides.
        if ((Profile.GravityScale > 0f || Profile.ArmDelay > 0f || _pierce) &&
            other.GetComponent<Cloud>() != null)
        {
            return;
        }

        // Supply crate: remember who shot it - its blast credits them
        var crate = other.GetComponent<PowerUp>();
        if (crate != null) crate.SetLastShooter(_shooterClientId.Value, _shooterLocalPlayerIndex.Value);

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
                // explosion damages it, no separate direct hit. Anything
                // damagable that is not a plane (supply crate) takes the hit.
                if (targetPlayer == null) damagable.Hit(_damage.Value);
                Explode();
            }
            else
            {
                if (targetPlayer != null && _pierced.Contains(targetPlayer)) return;
                if (targetPlayer != null && !IsShootersOwnPlane(targetPlayer))
                {
                    targetPlayer.SetLastAttacker(_shooterClientId.Value, _shooterLocalPlayerIndex.Value);
                }
                damagable.Hit(_damage.Value);
                // A plain hit on a plane used to draw nothing - the victim's
                // own shield/hull flash said something was hit, never by what.
                bool shield = targetPlayer != null && targetPlayer.PlaneStats != null &&
                              targetPlayer.PlaneStats.LastDamageAbsorbedByShield;
                PlayHitFxClientRpc(transform.position, _elementId.Value, _shooterClientId.Value,
                    _shooterLocalPlayerIndex.Value, _damage.Value, shield, targetPlayer != null);

                // A pierce round goes through its first plane
                if (_pierce && targetPlayer != null && _pierced.Count == 0)
                {
                    _pierced.Add(targetPlayer);
                    return;
                }
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
                PlayHitFxClientRpc(transform.position, _elementId.Value, 0, 0, 0, false, false);
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

        var profile = Profile;
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
            bool shield = player.PlaneStats != null && player.PlaneStats.LastDamageAbsorbedByShield;
            if (!ownPlane)
            {
                PlayHitFxClientRpc(player.transform.position, -1, _shooterClientId.Value,
                    _shooterLocalPlayerIndex.Value, _damage.Value, shield, true);
            }
        }

        // Knockback (Prompts/29 D3): reaches 1.6 x the blast, falls off
        // linearly; the planes it throws into the ground are kills.
        if (profile.Knockback > 0f)
        {
            float reach = radius * 1.6f;
            foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (player.InHangar) continue;
                Vector2 d = (Vector2)player.transform.position - (Vector2)pos;
                float dist = d.magnitude;
                if (dist > reach) continue;
                bool ownPlane = IsShootersOwnPlane(player);
                if (ownPlane && Time.time - _spawnTime < SelfHitGracePeriod) continue;
                Vector2 dir = dist > 0.01f ? d / dist : Vector2.up;
                float k = 1f - dist / reach;
                float spin = Mathf.Sign(Vector3.Cross(player.transform.right, dir).z) * profile.Knockback * 1.5f * k;
                player.ServerApplyImpulse(dir * profile.Knockback * k, spin,
                    _shooterClientId.Value, _shooterLocalPlayerIndex.Value, !ownPlane);
            }
        }

        // Cluster: bomblets fan out of the blast
        if (profile.SplitInto.HasValue && profile.SplitCount > 0 && Shooting.SharedBulletPrefab != null)
        {
            var child = WeaponProfile.Get(profile.SplitInto.Value);
            for (int i = 0; i < profile.SplitCount; i++)
            {
                float a = Mathf.Lerp(55f, 125f, profile.SplitCount > 1 ? (float)i / (profile.SplitCount - 1) : 0.5f);
                Vector2 v = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * 6f;
                Shooting.SpawnProjectile(Shooting.SharedBulletPrefab, pos + Vector3.up * 0.4f,
                    Quaternion.Euler(0f, 0f, a), v, 0f, _shooterClientId.Value, _shooterLocalPlayerIndex.Value,
                    child.Damage, child, _elementId.Value);
            }
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

        // The blast's true reach, readable (Prompts/29 §6)
        WorldFx.Ring(position, radius, new Color(1f, 0.95f, 0.8f, 0.9f), 0.25f, 0.14f);
    }

    [ClientRpc]
    private void PlayHitFxClientRpc(Vector3 position, int elementId, ulong shooterClientId,
        int shooterLocalIdx, int damage, bool shieldHit, bool hitPlane)
    {
        if (elementId >= 0)
        {
            var element = (ProjectileElement)elementId;
            SfxManager.PlayImpact(element);
            EffectLibrary.SpawnImpact(element, position, hitEffect);
        }

        // Hit confirm for the shooter only: a white cross + the damage dealt
        // (blue = shield, orange = hull).
        var nm = NetworkManager.Singleton;
        if (!hitPlane || nm == null || shooterClientId != nm.LocalClientId ||
            shooterLocalIdx == PlayerController.BotLocalPlayerIndex) return;
        WorldFx.HitMarker(position);
        WorldFx.Text(position + Vector3.up * 0.6f, "-" + damage,
            shieldHit ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.6f, 0.2f), 0.55f);
    }

    // --- homing ----------------------------------------------------------------

    private void AcquireTarget(WeaponProfile profile)
    {
        Vector2 from = transform.position;
        Vector2 dir = _rb != null && _rb.linearVelocity.sqrMagnitude > 0.01f ? _rb.linearVelocity.normalized : (Vector2)transform.right;
        float best = float.MaxValue;
        PlayerController target = null;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p.InHangar || IsShootersOwnPlane(p)) continue;
            Vector2 to = (Vector2)p.transform.position - from;
            float d = to.magnitude;
            if (d > profile.HomingRange || Vector2.Angle(dir, to) > profile.HomingConeDeg) continue;
            if (d < best) { best = d; target = p; }
        }
        _homingTarget = target;
        if (target != null) LockWarnClientRpc(target.NetworkObjectId);
    }

    private static readonly Collider2D[] _pointHits = new Collider2D[8];

    private static bool InsideCloud(Vector2 point)
    {
        int n = Physics2D.OverlapPoint(point, new ContactFilter2D().NoFilter(), _pointHits);
        for (int i = 0; i < n; i++)
        {
            if (_pointHits[i] != null && _pointHits[i].GetComponent<Cloud>() != null) return true;
        }
        return false;
    }

    [ClientRpc]
    private void LockWarnClientRpc(ulong targetObjectId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(targetObjectId, out var obj)) return;
        var target = obj.GetComponent<PlayerController>();
        if (target == null) return;
        if (target.IsOwner && !target.IsBot)
        {
            // You are being hunted
            SfxManager.PlayNamed("lock", 0.8f, 0f);
            SfxManager.Haptic(HapticKind.Medium);
            WorldFx.Text(target.transform.position + Vector3.up * 1.4f, "!", new Color(1f, 0.3f, 0.2f), 0.9f, 1f);
        }
        else if (_shooterClientId.Value == nm.LocalClientId &&
                 _shooterLocalPlayerIndex.Value != PlayerController.BotLocalPlayerIndex)
        {
            WorldFx.Ring(target.transform.position, 1.6f, new Color(0.6f, 1f, 0.6f), 0.35f, 0.12f);
        }
    }
}
