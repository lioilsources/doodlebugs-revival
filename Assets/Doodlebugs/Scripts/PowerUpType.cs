/// <summary>Synced as int - append only.</summary>
public enum PowerUpType
{
    Health,
    Shield,
    Repair,
    Damage,
    Weapon,      // crate: +1 tier of the current weapon, lost on death
    Nitro,       // canister: recharged gadget + a short boost (Prompts/29)
    Special,     // supply drop: a special weapon (payload = WeaponType)
    FullRepair,  // supply drop: shield, hull and handling to full
}
