using Unity.Netcode;
using UnityEngine;

public struct ResourceCounts : INetworkSerializable
{
    public int wood, stone, metal, cloth, powder;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref wood);
        s.SerializeValue(ref stone);
        s.SerializeValue(ref metal);
        s.SerializeValue(ref cloth);
        s.SerializeValue(ref powder);
    }

    public void Add(MaterialType t, int amt)
    {
        switch (t)
        {
            case MaterialType.Wood: wood += amt; break;
            case MaterialType.Stone: stone += amt; break;
            case MaterialType.Metal: metal += amt; break;
            case MaterialType.Cloth: cloth += amt; break;
            case MaterialType.Gunpowder: powder += amt; break;
        }
    }
}

[RequireComponent(typeof(NetworkObject))]
public class PlayerInventory : NetworkBehaviour
{
    public NetworkVariable<ResourceCounts> Counts =
        new NetworkVariable<ResourceCounts>(writePerm: NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────
    // ADDED: direct server-side add (use this from server code like pickups)
    public void ServerAddResource(MaterialType type, int amount)
    {
        if (!IsServer) return; // safety
        var c = Counts.Value;
        c.Add(type, Mathf.Max(0, amount));
        Counts.Value = c;      // <-- critical: assign back so netvar notifies clients
    }

    // RENAMED: keep an RPC wrapper only for client→server requests (optional)
    [ServerRpc(RequireOwnership = false)]
    public void RequestAddResourceServerRpc(MaterialType type, int amount)
    {
        ServerAddResource(type, amount);
    }
    // ─────────────────────────────────────────────────────────────
}