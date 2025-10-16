using UnityEngine;

[CreateAssetMenu(menuName = "BB/Upgrade Def", fileName = "UpgradeDef")]
public class UpgradeDef : ScriptableObject
{
    public UpgradeType type;
    [Range(1, 10)] public int maxLevel = 5;

    [Header("Cost per level (base amounts)")]
    public int baseWood = 10;
    public int baseStone = 4;
    public int baseMetal = 3;
    public int baseCloth = 2;
    public int basePowder = 0;

    [Header("Scaling")]
    [Tooltip("Final cost = base * (1 + scale)^(level-1)")]
    public float costScale = 0.35f;

    [Header("Effect per level (as multiplier add or flat)")]
    public float hullHpPerLevel = 50f;
    public float moveSpeedMultStep = 0.08f;   // +8% per level
    public float turnRateMultStep = 0.10f;   // +10% per level
    public int cargoFlatPerLevel = 10;
    public float cannonDmgMultStep = 0.12f;

    // Helper: compute integer cost at target nextLevel (1..maxLevel)
    public (int wood, int stone, int metal, int cloth, int powder) CostForLevel(int nextLevel)
    {
        float m = Mathf.Pow(1f + Mathf.Max(0f, costScale), Mathf.Max(0, nextLevel - 1));
        int w = Mathf.CeilToInt(baseWood * m);
        int s = Mathf.CeilToInt(baseStone * m);
        int me = Mathf.CeilToInt(baseMetal * m);
        int c = Mathf.CeilToInt(baseCloth * m);
        int p = Mathf.CeilToInt(basePowder * m);
        return (w, s, me, c, p);
    }
}