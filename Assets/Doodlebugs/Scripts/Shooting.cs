using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// PlayerHolder Prefab script.
//
// Slot 0 = the drafted weapon (or a supply-drop special while it has ammo),
// slot 1 = the gadget (Prompts/29 §4) - or a second weapon if one is ever
// set through NetWeaponId2 and no gadget is equipped.
public class Shooting : NetworkBehaviour
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float baseBulletForce = 20f;

    /// <summary>The bullet prefab, for projectiles spawned by other
    /// projectiles (cluster bomblets). Set by the first spawned plane.</summary>
    public static GameObject SharedBulletPrefab { get; private set; }

    // --- Weapon state (server-write) ---
    // Current weapon (may be crate-boosted); resets to the selected weapon on death.
    public NetworkVariable<int> NetWeaponId = new NetworkVariable<int>((int)WeaponType.MG,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Second weapon slot (Prompts/26 D10): -1 = none. Unused while a gadget is equipped.
    public NetworkVariable<int> NetWeaponId2 = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Gadget in slot 1 (Prompts/29): -1 = none.
    public NetworkVariable<int> NetGadgetId = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Supply-drop special replacing slot 0 for SpecialAmmo shots: -1 = none.
    public NetworkVariable<int> NetSpecialId = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetSpecialAmmo = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // A charge weapon (Lance) is being held - every client draws the beam.
    public NetworkVariable<bool> NetCharging = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Hangar choices - survive deaths, reset only by a new pick.
    private int _selectedWeaponId = (int)WeaponType.MG;
    private int _selectedWeaponId2 = -1;
    private int _selectedGadgetId = (int)GadgetProfile.Default;

    private const int MaxSlots = 2;
    private const float MinRemoteFuse = 0.15f;   // a tap still flies a little

    public WeaponProfile CurrentWeapon => GetWeapon(0);

    public bool HasGadget => NetGadgetId.Value >= 0;
    public bool HasSpecial => NetSpecialId.Value >= 0 && NetSpecialAmmo.Value > 0;
    public int SpecialAmmo => HasSpecial ? NetSpecialAmmo.Value : 0;

    /// <summary>Trigger slots the plane carries: 2 with a gadget or a second weapon.</summary>
    public int SlotCount => HasGadget || NetWeaponId2.Value >= 0 ? 2 : 1;

    public bool IsGadgetSlot(int slot) => slot == 1 && HasGadget;

    public int GetWeaponId(int slot)
    {
        if (slot == 0) return HasSpecial ? NetSpecialId.Value : NetWeaponId.Value;
        return NetWeaponId2.Value >= 0 ? NetWeaponId2.Value : NetWeaponId.Value;
    }

    public WeaponProfile GetWeapon(int slot) => WeaponProfile.Get(GetWeaponId(slot));

    public GadgetProfile Gadget => GadgetProfile.Get(NetGadgetId.Value);

    /// <summary>The drafted weapon (never a crate tier or a special).</summary>
    public int DraftedWeaponId => (int)WeaponProfile.BaseOf((WeaponType)NetWeaponId.Value);

    /// <summary>A key that changes whenever a trigger's icon must change.</summary>
    public int GetSlotKey(int slot) => IsGadgetSlot(slot) ? 1000 + NetGadgetId.Value : GetWeaponId(slot);

    public Sprite GetSlotIcon(int slot, out Color tint, out string label)
    {
        if (IsGadgetSlot(slot))
        {
            tint = Color.white;
            label = Gadget.DisplayName;
            return Gadget.LoadIcon();
        }
        var w = GetWeapon(slot);
        label = w.DisplayName;
        return w.LoadIcon(out tint);
    }

    // Run-upgrade FIRE RATE levels shorten the cooldown. Pilot maturity no
    // longer scales firing (Prompts/28 D2).
    private float runRofMultiplier => playerController?.PlaneStats?.RofMultiplier ?? 1f;

    private float SlotCooldown(int slot) =>
        IsGadgetSlot(slot) ? Gadget.Cooldown : GetWeapon(slot).Cooldown / runRofMultiplier;

    private PlayerController playerController;
    private Rigidbody2D planeRb;

    // Owner-side clocks, per slot
    private readonly float[] _lastFireTime = { -999f, -999f };
    private readonly bool[] _charging = new bool[MaxSlots];
    private readonly float[] _chargeStart = new float[MaxSlots];
    private readonly bool[] _awaitRelease = new bool[MaxSlots];

    // Server: live remote-detonation projectiles of this plane
    private readonly List<Bullet> _remote = new();
    private float _serverGadgetReadyAt;

    /// <summary>0..1 for the trigger ring: recharge, or the charge level while
    /// a Lance is held. Owner-side clock - the same one the firing gate uses.</summary>
    public float GetCharge01(int slot)
    {
        if ((uint)slot >= MaxSlots) return 1f;
        if (_charging[slot]) return ChargeFraction(slot);
        float cooldown = SlotCooldown(slot);
        if (cooldown <= 0f) return 1f;
        return Mathf.Clamp01((Time.time - _lastFireTime[slot]) / cooldown);
    }

    public bool IsChargingSlot(int slot) => (uint)slot < MaxSlots && _charging[slot];

    private float ChargeFraction(int slot)
    {
        var w = GetWeapon(slot);
        return w.ChargeSeconds > 0f ? Mathf.Clamp01((Time.time - _chargeStart[slot]) / w.ChargeSeconds) : 1f;
    }

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerController>();
        if (bulletPrefab != null) SharedBulletPrefab = bulletPrefab;
        if (IsServer) NetGadgetId.Value = _selectedGadgetId;
    }

    void Start()
    {
        planeRb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        bool isLocalPlayer = playerController != null && playerController.IsLocalPlayer;
        bool canProcessInput = IsOwner || (isLocalPlayer && NetworkManager.Singleton.IsHost);
        if (!canProcessInput) return;

        if (playerController == null || playerController.InHangar)
        {
            CancelCharges();
            return;
        }

        var provider = playerController.GetInputProvider();
        if (provider == null) return;

        int slots = SlotCount;
        for (int slot = 0; slot < slots; slot++)
        {
            bool ready = Time.time >= _lastFireTime[slot] + SlotCooldown(slot);

            if (IsGadgetSlot(slot))
            {
                if (ready && provider.GetShootInput(slot))
                {
                    _lastFireTime[slot] = Time.time;
                    UseGadgetServerRpc();
                    SfxManager.Haptic(HapticKind.Light);
                }
                continue;
            }

            var weapon = GetWeapon(slot);

            // Remote detonation: releasing the trigger bursts the projectile
            if (_awaitRelease[slot] && !provider.GetShootHeld(slot))
            {
                _awaitRelease[slot] = false;
                DetonateServerRpc();
            }

            // Charge weapon: press starts, release fires
            if (weapon.ChargeSeconds > 0f)
            {
                if (!_charging[slot])
                {
                    if (ready && provider.GetShootInput(slot))
                    {
                        _charging[slot] = true;
                        _chargeStart[slot] = Time.time;
                        SetChargingServerRpc(true);
                    }
                }
                else if (!provider.GetShootHeld(slot))
                {
                    float charge = ChargeFraction(slot);
                    _charging[slot] = false;
                    SetChargingServerRpc(false);
                    Fire(slot, charge);
                }
                continue;
            }
            if (_charging[slot]) { _charging[slot] = false; SetChargingServerRpc(false); }

            if (!ready || !provider.GetShootInput(slot)) continue;
            Fire(slot, 0f);
            if (weapon.RemoteDetonate) _awaitRelease[slot] = true;
        }
    }

    private void CancelCharges()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_charging[i])
            {
                _charging[i] = false;
                SetChargingServerRpc(false);
            }
            _awaitRelease[i] = false;
        }
    }

    private void Fire(int slot, float charge)
    {
        _lastFireTime[slot] = Time.time;
        float planeSpeed = planeRb != null ? planeRb.linearVelocity.magnitude : 0f;
        int localPlayerIndex = playerController.LocalPlayerIndex >= 0 ? playerController.LocalPlayerIndex : 0;
        ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed, localPlayerIndex, slot, charge);
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 position, Quaternion rotation, float planeSpeed, int localPlayerIndex,
        int slot, float charge, ServerRpcParams rpcParams = default)
    {
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        // Server-side guard: parked planes don't shoot, whatever the client says
        if (playerController != null && playerController.InHangar) return;

        // The server resolves the weapon from its own slot state - a client
        // can name a slot, never a weapon.
        slot = (uint)slot < MaxSlots ? slot : 0;
        if (IsGadgetSlot(slot)) return;
        bool special = slot == 0 && HasSpecial;
        var weapon = GetWeapon(slot);

        if (weapon.Type == WeaponType.Emp)
        {
            ServerEmp();
        }
        else
        {
            charge = Mathf.Clamp01(charge);
            int baseDamage = weapon.ChargeSeconds > 0f
                ? Mathf.RoundToInt(Mathf.Lerp(weapon.Damage, weapon.ChargeDamageMax, charge))
                : weapon.Damage;
            int damage = baseDamage;
            var planeStats = playerController?.PlaneStats;
            if (planeStats != null)
            {
                damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * planeStats.DamageMultiplier));
            }
            float force = weapon.ChargeSeconds > 0f
                ? Mathf.Lerp(weapon.ForceMultiplier, weapon.ChargeForceMax, charge)
                : weapon.ForceMultiplier;

            // Out of the tail for backward weapons: from behind the plane
            if (Mathf.Abs(weapon.FireAngleOffset) > 90f)
            {
                position = transform.position - transform.right * 1.3f;
            }
            Quaternion aim = rotation * Quaternion.Euler(0f, 0f, weapon.FireAngleOffset);
            Vector2 inherit = (Vector2)transform.right * planeSpeed;
            int element = (int)ShooterElement();

            for (int i = 0; i < weapon.PelletCount; i++)
            {
                float offset = 0f;
                if (weapon.PelletCount > 1)
                {
                    float t = (float)i / (weapon.PelletCount - 1);
                    offset = Mathf.Lerp(-weapon.SpreadDegrees / 2f, weapon.SpreadDegrees / 2f, t);
                }
                var b = SpawnProjectile(bulletPrefab, position, aim * Quaternion.Euler(0f, 0f, offset), inherit,
                    baseBulletForce * force, shooterClientId, localPlayerIndex, damage, weapon, element,
                    charge >= 0.99f);
                if (b != null && weapon.RemoteDetonate) _remote.Add(b);
            }
        }

        if (special)
        {
            NetSpecialAmmo.Value = Mathf.Max(0, NetSpecialAmmo.Value - 1);
            if (NetSpecialAmmo.Value == 0) NetSpecialId.Value = -1;
        }
    }

    /// <summary>Server: spawn one projectile. Also used for cluster bomblets.</summary>
    public static Bullet SpawnProjectile(GameObject prefab, Vector3 position, Quaternion rotation,
        Vector2 inheritVelocity, float force, ulong shooterClientId, int localPlayerIndex, int damage,
        WeaponProfile weapon, int element, bool fullCharge = false)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, position, rotation);
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn(true);

        var bullet = go.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.SetShooter(shooterClientId, localPlayerIndex);
            bullet.SetDamage(damage);
            bullet.SetWeapon((int)weapon.Type);
            // The shape decides what the projectile is made of (plan 24, D1).
            bullet.SetElement(element);
            if (weapon.BulletLifetime > 0f) bullet.SetLifetime(weapon.BulletLifetime);
            bullet.SetPierce(weapon.Pierce && fullCharge);
        }

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = weapon.GravityScale;
            rb.linearDamping = weapon.LinearDrag;
            // Muzzle speed along the aim + the plane's own velocity (forward,
            // whatever the aim - a tail gun's bullet must not inherit +speed
            // along its own direction).
            rb.linearVelocity = (Vector2)(rotation * Vector3.right) * force + inheritVelocity;
        }
        return bullet;
    }

    /// <summary>This plane's projectile element, from its current shape.</summary>
    private ProjectileElement ShooterElement()
    {
        var appearance = GetComponent<PlaneAppearance>();
        if (appearance == null) return ProjectileElement.Metal;
        return PlaneModelCatalog.ElementOf(appearance.NetModelId.Value);
    }

    [ServerRpc]
    private void SetChargingServerRpc(bool charging) => NetCharging.Value = charging;

    [ServerRpc]
    private void DetonateServerRpc()
    {
        for (int i = _remote.Count - 1; i >= 0; i--)
        {
            var b = _remote[i];
            if (b == null || !b.IsSpawned) { _remote.RemoveAt(i); continue; }
            if (b.Age < MinRemoteFuse) { b.DetonateAt(Time.time + MinRemoteFuse - b.Age); continue; }
            b.ServerDetonate();
            _remote.RemoveAt(i);
        }
    }

    // --- gadgets (server) --------------------------------------------------------

    [ServerRpc]
    private void UseGadgetServerRpc()
    {
        if (!HasGadget || playerController == null || playerController.InHangar) return;
        var g = Gadget;
        // The owner gates on its own clock; the server only refuses spam.
        if (Time.time < _serverGadgetReadyAt) return;
        _serverGadgetReadyAt = Time.time + g.Cooldown - 0.4f;

        switch (g.Type)
        {
            case GadgetType.Nitro:
                playerController.ServerBoost(g.Duration);
                break;
            case GadgetType.BarrelRoll:
                playerController.PlaneStats?.BeginDodge(g.Duration);
                break;
            case GadgetType.Thunderclap:
                ServerThunderclap(g);
                break;
        }
        GadgetFxClientRpc((int)g.Type, transform.position);
        Telemetry.Log("gadget_use", ("gadget", g.Type));
    }

    private void ServerThunderclap(GadgetProfile g)
    {
        Vector2 me = transform.position;
        int myIdx = playerController.LocalPlayerIndex >= 0 ? playerController.LocalPlayerIndex : 0;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p == playerController || p.InHangar) continue;
            Vector2 d = (Vector2)p.transform.position - me;
            float dist = d.magnitude;
            if (dist > g.Radius) continue;
            Vector2 dir = dist > 0.01f ? d / dist : Vector2.up;
            float spin = (Random.value < 0.5f ? -1f : 1f) * 40f;
            p.ServerApplyImpulse(dir * g.Knockback, spin, OwnerClientId, myIdx, !playerController.IsBot);
        }
        foreach (var b in FindObjectsByType<Bullet>(FindObjectsSortMode.None))
        {
            if (!b.IsSpawned || b.IsFrom(OwnerClientId, myIdx)) continue;
            if (((Vector2)b.transform.position - me).sqrMagnitude <= g.Radius * g.Radius) b.ServerFizzle();
        }
    }

    private void ServerEmp()
    {
        var w = WeaponProfile.Get(WeaponType.Emp);
        Vector2 me = transform.position;
        foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (p == playerController || p.InHangar) continue;
            if (((Vector2)p.transform.position - me).sqrMagnitude > w.ExplosionRadius * w.ExplosionRadius) continue;
            p.ServerForceEngineOff(0.8f);
        }
        foreach (var b in FindObjectsByType<Bullet>(FindObjectsSortMode.None))
        {
            if (b.IsSpawned && ((Vector2)b.transform.position - me).sqrMagnitude <= w.ExplosionRadius * w.ExplosionRadius)
                b.ServerKillThrust();
        }
        EmpFxClientRpc(transform.position, w.ExplosionRadius);
    }

    [ClientRpc]
    private void GadgetFxClientRpc(int gadget, Vector3 position)
    {
        var g = GadgetProfile.Get(gadget);
        SfxManager.PlayNamed(g.Sound, 0.8f, 0.04f);
        switch (g.Type)
        {
            case GadgetType.Thunderclap:
                WorldFx.Ring(position, g.Radius, UiTheme.Gadget, 0.3f, 0.25f);
                WorldFx.Ring(position, g.Radius * 0.6f, Color.white, 0.2f, 0.15f);
                break;
            case GadgetType.Nitro:
                WorldFx.Puff(position - transform.right * 0.8f, new Color(0.6f, 0.95f, 1f), 10);
                break;
        }
    }

    [ClientRpc]
    private void EmpFxClientRpc(Vector3 position, float radius)
    {
        SfxManager.PlayNamed("emp", 0.9f, 0f);
        WorldFx.Ring(position, radius, new Color(0.55f, 0.85f, 1f), 0.45f, 0.35f);
    }

    // --- Weapon management (server-side) ---

    /// <summary>
    /// Weapon crate: climb one tier of the drafted weapon. Already on the top
    /// tier: +1 shield and a recharged gadget instead - a crate is never empty
    /// (Prompts/29 §5). Returns the pickup line.
    /// </summary>
    public string UpgradeWeaponTier()
    {
        if (!IsServer) return "";
        var next = WeaponProfile.Get(NetWeaponId.Value).UpgradesTo;
        if (next.HasValue)
        {
            NetWeaponId.Value = (int)next.Value;
            return WeaponProfile.Get(next.Value).DisplayName;
        }
        playerController?.PlaneStats?.ServerAddShield(1);
        ServerRechargeGadget();
        return "+SHIELD +GADGET";
    }

    /// <summary>Supply-drop special: replaces slot 0 for its ammo. Server-only.</summary>
    public void GiveSpecial(WeaponType type)
    {
        if (!IsServer) return;
        var w = WeaponProfile.Get(type);
        NetSpecialId.Value = (int)type;
        NetSpecialAmmo.Value = Mathf.Max(1, w.SpecialAmmo);
    }

    /// <summary>Server: gadget ready now - the server gate and the owner's ring.</summary>
    public void ServerRechargeGadget()
    {
        if (!IsServer) return;
        _serverGadgetReadyAt = 0f;
        RechargeGadgetClientRpc();
    }

    [ClientRpc]
    private void RechargeGadgetClientRpc()
    {
        _lastFireTime[1] = -999f;
    }

    /// <summary>Reset the crate-boosted weapon back to the hangar pick. Server-only.</summary>
    public void ResetWeaponToSelected()
    {
        if (!IsServer) return;
        NetWeaponId.Value = _selectedWeaponId;
        NetWeaponId2.Value = _selectedWeaponId2;
        NetGadgetId.Value = _selectedGadgetId;
        NetSpecialId.Value = -1;
        NetSpecialAmmo.Value = 0;
        NetCharging.Value = false;
        _remote.Clear();
    }

    /// <summary>Server-side: apply a validated hangar selection.</summary>
    public void ServerSetSelectedWeapon(int weaponId, int slot = 0)
    {
        if (!IsServer) return;

        if (slot == 1)
        {
            if (weaponId < -1 || weaponId >= WeaponProfile.Count) return;
            if (weaponId >= 0 && !WeaponProfile.Get(weaponId).IsDraftable) return;
            _selectedWeaponId2 = weaponId;
            NetWeaponId2.Value = weaponId;
            return;
        }

        // Only draftable weapons - never a crate tier or a special by RPC
        if (weaponId < 0 || weaponId >= WeaponProfile.Count) return;
        if (!WeaponProfile.Get(weaponId).IsDraftable) return;

        _selectedWeaponId = weaponId;
        NetWeaponId.Value = weaponId;
        Telemetry.Log("weapon_pick", ("weapon", (WeaponType)weaponId));
    }

    public void ServerSetSelectedGadget(int gadgetId)
    {
        if (!IsServer) return;
        if (gadgetId < -1 || gadgetId >= GadgetProfile.Count) return;
        _selectedGadgetId = gadgetId;
        NetGadgetId.Value = gadgetId;
        Telemetry.Log("gadget_pick", ("gadget", gadgetId < 0 ? "none" : ((GadgetType)gadgetId).ToString()));
    }

    /// <summary>Owner asks the server to equip a weapon picked in the hangar.</summary>
    [ServerRpc]
    public void RequestSelectWeaponServerRpc(int weaponId)
    {
        ServerSetSelectedWeapon(weaponId);
    }

    [ServerRpc]
    public void RequestSelectGadgetServerRpc(int gadgetId)
    {
        ServerSetSelectedGadget(gadgetId);
    }
}
