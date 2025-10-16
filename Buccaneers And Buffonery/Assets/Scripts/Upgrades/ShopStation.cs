using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ShopStation : NetworkBehaviour
{
    public UpgradeCatalog catalog;

    // Simple proximity gating: local UI opens when the local player enters
    void OnTriggerEnter(Collider other)
    {
        if (!IsClient) return;
        if (other.TryGetComponent(out NetworkObject no) && no.IsLocalPlayer)
        {
            ShopClientUI.Instance?.Open(this); // simple singleton UI
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsClient) return;
        if (other.TryGetComponent(out NetworkObject no) && no.IsLocalPlayer)
        {
            ShopClientUI.Instance?.Close();
        }
    }

    // Client → Server purchase request
    [ServerRpc(RequireOwnership = false)]
    public void PurchaseServerRpc(ulong playerId, NetworkObjectReference shipRef, UpgradeType type)
    {
        if (catalog == null) return;
        var def = catalog.Get(type);
        if (def == null) return;

        // Resolve ship + inventory
        if (!shipRef.TryGet(out var shipNO)) return;
        var shipUpg = shipNO.GetComponent<ShipUpgrades>();
        if (shipUpg == null) return;

        // Basic ownership check: only the owning client can buy for their ship
        if (shipNO.OwnerClientId != playerId) return;

        // Ensure inventory reference
        var inv = shipUpg.ownerInventory;
        if (inv == null)
        {
            // fallback: try find on owner player object
            var nm = NetworkManager.Singleton;
            if (nm.ConnectedClients.TryGetValue(playerId, out var cc) && cc.PlayerObject)
                cc.PlayerObject.TryGetComponent(out inv);
            shipUpg.ownerInventory = inv;
        }
        if (inv == null) return;

        // Validate level cap
        var lv = shipUpg.Levels.Value.Get(type);
        if (lv >= def.maxLevel) return;

        // Cost for next level
        var cost = def.CostForLevel(lv + 1);

        // Check resources
        var c = inv.Counts.Value;
        if (c.wood < cost.wood || c.stone < cost.stone ||
            c.metal < cost.metal || c.cloth < cost.cloth ||
            c.powder < cost.powder) return;

        // Deduct & grant
        c.wood -= cost.wood;
        c.stone -= cost.stone;
        c.metal -= cost.metal;
        c.cloth -= cost.cloth;
        c.powder -= cost.powder;
        inv.Counts.Value = c;

        shipUpg.Server_GrantLevel(type);

        // (Optional) notify clients they succeeded
        PurchaseResultClientRpc(playerId, type, true);
    }

    [ClientRpc]
    void PurchaseResultClientRpc(ulong who, UpgradeType type, bool ok)
    {
        if (!IsClient) return;
        if (NetworkManager.Singleton.LocalClientId != who) return;
        // You can flash a small toast here via UI singleton
        ShopClientUI.Instance?.ShowToast(ok ? $"Purchased {type}!" : $"Purchase failed: {type}");
    }
}