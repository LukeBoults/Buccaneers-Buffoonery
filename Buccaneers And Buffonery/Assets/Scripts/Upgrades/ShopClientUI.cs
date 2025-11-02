using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

public class ShopClientUI : MonoBehaviour
{
    public static ShopClientUI Instance { get; private set; }

    [Header("Root")]
    public GameObject root;
    public TextMeshProUGUI infoLabel;

    [Header("Catalog + Row")]
    [Tooltip("List of upgrades to show (assign your UpgradeDef assets).")]
    [SerializeField] private List<UpgradeDef> upgradeCatalog = new List<UpgradeDef>();
    [SerializeField] private ShopUpgradeRow rowPrefab;
    [SerializeField] private Transform rowsParent;

    [Header("Icons by MaterialType order (Wood, Stone, Metal, Cloth, Gunpowder)")]
    [SerializeField] private Sprite[] materialIcons = new Sprite[5];

    private readonly List<ShopUpgradeRow> liveRows = new();
    private ShopStation currentShop;

    // Resolved at open:
    private NetworkObject myShipNo;
    private ShipUpgrades myShipUpgrades;
    private PlayerInventory myInventory;

    void Awake()
    {
        Instance = this;
        Close();
    }

    public void Open(ShopStation shop)
    {
        currentShop = shop;
        root.SetActive(true);
        if (infoLabel) infoLabel.text = "Dockyard Upgrades";

        ResolvePlayerRefs();

        BuildRows();

        // Listen for inventory changes to update affordability tint + interactable
        if (myInventory != null)
        {
            myInventory.Counts.OnValueChanged += OnInventoryChanged;
        }

        // Initial refresh
        RefreshAllRows();
    }

    public void Close()
    {
        // Unsub
        if (myInventory != null)
        {
            myInventory.Counts.OnValueChanged -= OnInventoryChanged;
        }

        ClearRows();
        root.SetActive(false);
        currentShop = null;
        myShipNo = null;
        myShipUpgrades = null;
        myInventory = null;
    }

    public void ShowToast(string msg)
    {
        if (infoLabel) infoLabel.text = msg;
    }

    // -------------------- Build UI --------------------
    private void BuildRows()
    {
        ClearRows();
        if (!rowPrefab || !rowsParent) return;

        foreach (var def in upgradeCatalog)
        {
            if (!def) continue;
            var row = Instantiate(rowPrefab, rowsParent);
            row.Setup(def, myShipUpgrades, myInventory, materialIcons, OnBuyRequest);
            liveRows.Add(row);
        }
    }

    private void ClearRows()
    {
        for (int i = liveRows.Count - 1; i >= 0; i--)
        {
            if (liveRows[i]) Destroy(liveRows[i].gameObject);
        }
        liveRows.Clear();
    }

    private void RefreshAllRows()
    {
        foreach (var r in liveRows) if (r) r.RefreshView();
    }

    private void OnInventoryChanged(ResourceCounts oldV, ResourceCounts newV)
    {
        RefreshAllRows();
    }

    // -------------------- Buying --------------------
    private void OnBuyRequest(UpgradeType t)
    {
        Buy(t);
    }

    public void OnBuy_HullHP() => Buy(UpgradeType.HullHP);
    public void OnBuy_MoveSpeed() => Buy(UpgradeType.MoveSpeed);
    public void OnBuy_TurnRate() => Buy(UpgradeType.TurnRate);
    public void OnBuy_Cargo() => Buy(UpgradeType.CargoHold);
    public void OnBuy_CannonDmg() => Buy(UpgradeType.CannonDamage);

    private void Buy(UpgradeType t)
    {
        if (currentShop == null) { ShowToast("No shop in range"); return; }
        if (!TryGetMyShip(out var shipNo)) { ShowToast("No ship found"); return; }

        var nm = NetworkManager.Singleton;
        currentShop.PurchaseServerRpc(nm.LocalClientId, shipNo, t);

        // Immediate optimistic refresh (server will correct shortly)
        RefreshAllRows();
    }

    // -------------------- Resolve local player ship + inv --------------------
    private void ResolvePlayerRefs()
    {
        TryGetMyShip(out myShipNo);
        myShipUpgrades = myShipNo ? myShipNo.GetComponent<ShipUpgrades>() : null;
        myInventory = FindMyInventory();
    }

    // Your original helper (kept)
    private bool TryGetMyShip(out NetworkObject shipNo)
    {
        shipNo = null;
        var nm = NetworkManager.Singleton;
        if (nm == null) return false;
        var local = nm.SpawnManager?.GetLocalPlayerObject();
        if (!local) return false;

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

    // Tries to find a PlayerInventory owned by me (can be on player or ship)
    private PlayerInventory FindMyInventory()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return null;

        foreach (var inv in FindObjectsOfType<PlayerInventory>())
        {
            if (inv.TryGetComponent<NetworkObject>(out var no) &&
                no.OwnerClientId == nm.LocalClientId)
            {
                return inv;
            }
        }
        // Fallback: try on the ship object
        if (myShipNo) return myShipNo.GetComponent<PlayerInventory>();
        return null;
    }
}