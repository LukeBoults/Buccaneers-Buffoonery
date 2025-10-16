using Unity.Netcode;
using UnityEngine;

public struct UpgradeLevels : INetworkSerializable
{
    public int hullHP, moveSpeed, turnRate, cargo, cannon;
    public int Get(UpgradeType t) => t switch
    {
        UpgradeType.HullHP => hullHP,
        UpgradeType.MoveSpeed => moveSpeed,
        UpgradeType.TurnRate => turnRate,
        UpgradeType.CargoHold => cargo,
        UpgradeType.CannonDamage => cannon,
        _ => 0
    };
    public void Bump(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.HullHP: hullHP++; break;
            case UpgradeType.MoveSpeed: moveSpeed++; break;
            case UpgradeType.TurnRate: turnRate++; break;
            case UpgradeType.CargoHold: cargo++; break;
            case UpgradeType.CannonDamage: cannon++; break;
        }
    }
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref hullHP);
        s.SerializeValue(ref moveSpeed);
        s.SerializeValue(ref turnRate);
        s.SerializeValue(ref cargo);
        s.SerializeValue(ref cannon);
    }
}

[RequireComponent(typeof(NetworkObject))]
public class ShipUpgrades : NetworkBehaviour
{
    [Header("Data")]
    public UpgradeCatalog catalog;

    [Header("Bindings")]
    public ShipController ship; // assign or Find
    public PlayerInventory ownerInventory; // the owning player's inventory

    public NetworkVariable<UpgradeLevels> Levels = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Awake()
    {
        if (!ship) ship = GetComponent<ShipController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            ApplyToShip(); // ensure server state matches levels
        Levels.OnValueChanged += (_, __) => ApplyToShip(); // update on clients too
    }

    // Apply buffs to the ship based on levels (non-authoritative math is fine clientside for visuals)
    void ApplyToShip()
    {
        if (!ship || catalog == null) return;
        var lv = Levels.Value;

        // These assume ShipController exposes tunables; adjust names to your fields.
        // Safe defaults if not present.
        // Example multipliers / adds:
        if (catalog.Get(UpgradeType.MoveSpeed) != null)
        {
            float mult = 1f + lv.moveSpeed * catalog.Get(UpgradeType.MoveSpeed).moveSpeedMultStep;
            ship.MaxSpeedMultiplier = mult; // add this property if you don’t have one
        }
        if (catalog.Get(UpgradeType.TurnRate) != null)
        {
            float mult = 1f + lv.turnRate * catalog.Get(UpgradeType.TurnRate).turnRateMultStep;
            ship.TurnRateMultiplier = mult;
        }
        if (catalog.Get(UpgradeType.HullHP) != null)
        {
            float add = lv.hullHP * catalog.Get(UpgradeType.HullHP).hullHpPerLevel;
            ship.SetBonusHull(add); // you can implement SetBonusHull to raise max HP and optionally heal
        }
        if (catalog.Get(UpgradeType.CargoHold) != null)
        {
            int add = lv.cargo * catalog.Get(UpgradeType.CargoHold).cargoFlatPerLevel;
            ship.BonusCargo = add;
        }
        if (catalog.Get(UpgradeType.CannonDamage) != null)
        {
            float mult = 1f + lv.cannon * catalog.Get(UpgradeType.CannonDamage).cannonDmgMultStep;
            ship.CannonDamageMultiplier = mult;
        }
    }

    // Called by the shop server-side after validating payment
    public void Server_GrantLevel(UpgradeType type)
    {
        if (!IsServer) return;
        var lv = Levels.Value;
        lv.Bump(type);
        Levels.Value = lv; // triggers OnValueChanged -> ApplyToShip
    }
}