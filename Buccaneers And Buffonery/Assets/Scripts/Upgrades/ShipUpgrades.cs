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

    // NEW: simple accessor used by ShopUpgradeRow
    public int GetLevel(UpgradeType t) => Levels.Value.Get(t);

    void ApplyToShip()
    {
        if (!ship || catalog == null) return;
        var lv = Levels.Value;

        var defMove = catalog.GetDef(UpgradeType.MoveSpeed);
        var defTurn = catalog.GetDef(UpgradeType.TurnRate);
        var defHull = catalog.GetDef(UpgradeType.HullHP);
        var defCargo = catalog.GetDef(UpgradeType.CargoHold);
        var defCannon = catalog.GetDef(UpgradeType.CannonDamage);

        if (defMove != null)
        {
            float mult = 1f + lv.moveSpeed * defMove.moveSpeedMultStep;
            ship.MaxSpeedMultiplier = mult;
        }
        if (defTurn != null)
        {
            float mult = 1f + lv.turnRate * defTurn.turnRateMultStep;
            ship.TurnRateMultiplier = mult;
        }
        if (defHull != null)
        {
            float add = lv.hullHP * defHull.hullHpPerLevel;
            ship.SetBonusHull(add);
        }
        if (defCargo != null)
        {
            int add = lv.cargo * defCargo.cargoFlatPerLevel;
            ship.BonusCargo = add;
        }
        if (defCannon != null)
        {
            float mult = 1f + lv.cannon * defCannon.cannonDmgMultStep;
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