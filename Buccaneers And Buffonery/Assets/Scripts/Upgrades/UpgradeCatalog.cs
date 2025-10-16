using UnityEngine;
using System.Linq;

[CreateAssetMenu(menuName = "BB/Upgrade Catalog", fileName = "UpgradeCatalog")]
public class UpgradeCatalog : ScriptableObject
{
    public UpgradeDef[] upgrades;

    public UpgradeDef Get(UpgradeType t) => upgrades.FirstOrDefault(u => u && u.type == t);
}