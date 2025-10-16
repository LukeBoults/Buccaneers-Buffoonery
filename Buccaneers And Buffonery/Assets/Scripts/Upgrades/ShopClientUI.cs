using UnityEngine;
using Unity.Netcode;
using TMPro;

public class ShopClientUI : MonoBehaviour
{
    public static ShopClientUI Instance { get; private set; }

    [Header("Bindings")]
    public GameObject root;
    public TextMeshProUGUI infoLabel;

    ShopStation currentShop;

    void Awake() { Instance = this; Close(); }

    public void Open(ShopStation shop)
    {
        currentShop = shop;
        root.SetActive(true);
        infoLabel.text = "Dockyard Upgrades";
    }
    public void Close()
    {
        root.SetActive(false);
        currentShop = null;
    }

    public void ShowToast(string msg)
    {
        if (infoLabel) infoLabel.text = msg;
    }

    // Helper to resolve the local player’s ship (owner)
    bool TryGetMyShip(out NetworkObject shipNo)
    {
        shipNo = null;
        var nm = NetworkManager.Singleton;
        if (nm == null) return false;
        var local = nm.SpawnManager?.GetLocalPlayerObject();
        if (!local) return false;

        // Your ship might be a child/companion of the player. Adjust if needed:
        // Option A: player holds a reference to their ship
        // Option B: find the first ShipUpgrades owned by me
        foreach (var no in FindObjectsOfType<NetworkObject>())
        {
            if (no.OwnerClientId == nm.LocalClientId && no.TryGetComponent<ShipUpgrades>(out _))
            {
                shipNo = no;
                return true;
            }
        }
        return false;
    }

    // Button hooks
    public void OnBuy_HullHP() => Buy(UpgradeType.HullHP);
    public void OnBuy_MoveSpeed() => Buy(UpgradeType.MoveSpeed);
    public void OnBuy_TurnRate() => Buy(UpgradeType.TurnRate);
    public void OnBuy_Cargo() => Buy(UpgradeType.CargoHold);
    public void OnBuy_CannonDmg() => Buy(UpgradeType.CannonDamage);

    void Buy(UpgradeType t)
    {
        if (currentShop == null) { ShowToast("No shop in range"); return; }
        if (!TryGetMyShip(out var shipNo)) { ShowToast("No ship found"); return; }

        var nm = NetworkManager.Singleton;
        currentShop.PurchaseServerRpc(nm.LocalClientId, shipNo, t);
    }
}