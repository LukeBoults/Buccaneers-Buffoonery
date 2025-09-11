using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class ShipHealth : NetworkBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    // Server writes, everyone reads
    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public System.Action OnServerDeath;

    public override void OnNetworkSpawn()
    {
        if (IsServer) Health.Value = maxHealth;
        Health.OnValueChanged += (_, now) =>
        {
            if (IsServer && now <= 0f) HandleDeathServer();
        };
    }

    [ServerRpc(RequireOwnership = false)]
    public void HealServerRpc(float amount)
    {
        if (!IsServer) return;
        Health.Value = Mathf.Min(maxHealth, Health.Value + Mathf.Abs(amount));
    }

    [ServerRpc(RequireOwnership = false)]
    public void DamageServerRpc(float amount)
    {
        if (!IsServer) return;
        ApplyDamage(amount);
    }

    // Called by projectiles directly on server (no RPC required)
    public void ApplyDamage(float amount)
    {
        if (!IsServer) return;
        if (Health.Value <= 0f) return;
        Health.Value = Mathf.Max(0f, Health.Value - Mathf.Abs(amount));
    }

    void HandleDeathServer()
    {
        // Example: disable movement, start a sink/respawn, etc.
        var ctrl = GetComponent<ShipController>();
        if (ctrl) ctrl.enabled = false;

        OnServerDeath?.Invoke();

        // Optional: simple sink & despawn after 5s
        StartCoroutine(SinkAndDespawn());
    }

    System.Collections.IEnumerator SinkAndDespawn()
    {
        float t = 0f;
        var tr = transform;
        var start = tr.position;
        while (t < 5f)
        {
            t += Time.deltaTime;
            tr.position = start + Vector3.down * (t * 0.5f);
            yield return null;
        }
        if (IsServer && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }
}