using System.Collections.Generic;
using UnityEngine;
using System;

[CreateAssetMenu(menuName = "BB/Upgrade Catalog")]
public class UpgradeCatalog : ScriptableObject
{
    [Tooltip("Assign your UpgradeDef assets here.")]
    public List<UpgradeDef> upgrades = new List<UpgradeDef>();

    private Dictionary<UpgradeType, UpgradeDef> _map;

    private void OnEnable() { RebuildMap(); }
#if UNITY_EDITOR
    private void OnValidate() { RebuildMap(); }
#endif
    public UpgradeDef Get(UpgradeType t) => GetDef(t);
    private void RebuildMap()
    {
        if (_map == null) _map = new Dictionary<UpgradeType, UpgradeDef>();
        else _map.Clear();

        foreach (var def in upgrades)
        {
            if (!def) continue;
            _map[def.type] = def; // last one wins if duplicates exist
        }
    }

    public bool TryGet(UpgradeType t, out UpgradeDef def)
    {
        if (_map == null) RebuildMap();
        return _map.TryGetValue(t, out def);
    }

    /// <summary> Returns the UpgradeDef for type t, or null if not present. </summary>
    public UpgradeDef GetDef(UpgradeType t)
    {
        TryGet(t, out var def);
        return def;
    }
}