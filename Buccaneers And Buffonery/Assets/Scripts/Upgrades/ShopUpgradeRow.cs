using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopUpgradeRow : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private Slider progress;
    [SerializeField] private Button buyButton;

    [Serializable]
    public class CostPill
    {
        public GameObject root;             // enable/disable if 0
        public Image icon;
        public TextMeshProUGUI amount;
    }

    [Header("Cost Pills in MaterialType order (Wood, Stone, Metal, Cloth, Gunpowder)")]
    [SerializeField] private CostPill[] costPills = new CostPill[5];

    // Data/state
    private UpgradeDef def;
    private ShipUpgrades shipUpgrades;   // assumed component on the player's ship
    private PlayerInventory inventory;   // local player's inventory (netvar)
    private Action<UpgradeType> onBuy;

    public UpgradeType Type => def ? def.type : default;

    public void Setup(UpgradeDef def, ShipUpgrades shipUpgrades, PlayerInventory inventory,
                      Sprite[] materialIcons, Action<UpgradeType> onBuy)
    {
        this.def = def;
        this.shipUpgrades = shipUpgrades;
        this.inventory = inventory;
        this.onBuy = onBuy;

        if (titleLabel) titleLabel.text = NiceTitle(def.type);

        // Assign icons (index matches MaterialType enum order)
        for (int i = 0; i < costPills.Length && i < materialIcons.Length; i++)
        {
            if (costPills[i]?.icon) costPills[i].icon.sprite = materialIcons[i];
        }

        // Wire button
        if (buyButton)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => this.onBuy?.Invoke(def.type));
        }

        RefreshView();
    }

    public void RefreshView()
    {
        if (!def) return;

        int level = GetCurrentLevel(def.type);
        level = Mathf.Clamp(level, 0, def.maxLevel);

        if (progress)
        {
            progress.minValue = 0;
            progress.maxValue = def.maxLevel;
            progress.wholeNumbers = true;
            progress.value = level;
        }
        if (levelLabel) levelLabel.text = $"Lv {level} / {def.maxLevel}";

        // If maxed, gray out costs + disable buy
        if (level >= def.maxLevel)
        {
            ShowCosts((0, 0, 0, 0, 0));
            if (buyButton) buyButton.interactable = false;
            return;
        }

        int nextLevel = level + 1;
        var (w, s, me, c, p) = def.CostForLevel(nextLevel);
        ShowCosts((w, s, me, c, p));

        bool canAfford = HasEnough(w, s, me, c, p);
        if (buyButton) buyButton.interactable = canAfford;
    }

    private void ShowCosts((int w, int s, int me, int c, int p) cost)
    {
        int[] amounts = new[] { cost.w, cost.s, cost.me, cost.c, cost.p };
        for (int i = 0; i < costPills.Length && i < amounts.Length; i++)
        {
            var pill = costPills[i];
            if (pill == null) continue;

            int amt = amounts[i];
            if (pill.amount) pill.amount.text = amt.ToString();
            if (pill.root) pill.root.SetActive(amt > 0);

            // Optional: tint amount red if unaffordable
            bool enough = HasEnoughIndex(i, amt);
            if (pill.amount) pill.amount.color = enough ? Color.white : new Color(1f, 0.35f, 0.35f);
        }
    }

    private bool HasEnough(int w, int s, int me, int c, int p)
    {
        if (!inventory) return false;
        var counts = inventory.Counts.Value;
        return counts.wood >= w &&
               counts.stone >= s &&
               counts.metal >= me &&
               counts.cloth >= c &&
               counts.powder >= p;
    }

    private bool HasEnoughIndex(int materialIndex, int needed)
    {
        if (!inventory) return false;
        var counts = inventory.Counts.Value;
        return materialIndex switch
        {
            0 => counts.wood >= needed,
            1 => counts.stone >= needed,
            2 => counts.metal >= needed,
            3 => counts.cloth >= needed,
            4 => counts.powder >= needed,
            _ => false
        };
    }

    private int GetCurrentLevel(UpgradeType t)
    {
        if (!shipUpgrades) return 0;
        // Assumes ShipUpgrades exposes GetLevel(UpgradeType). If yours is named differently,
        // change this call to your existing method.
        return shipUpgrades.GetLevel(t);
    }

    private static string NiceTitle(UpgradeType t)
    {
        // Optional pretty-print
        return t switch
        {
            UpgradeType.HullHP => "Hull HP",
            UpgradeType.MoveSpeed => "Move Speed",
            UpgradeType.TurnRate => "Turn Rate",
            UpgradeType.CargoHold => "Cargo Hold",
            UpgradeType.CannonDamage => "Cannon Damage",
            _ => t.ToString()
        };
    }
}