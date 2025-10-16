using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class ShipHealth : NetworkBehaviour
{
    [Header("Health")]
    public float baseMaxHealth = 100f;   // renamed for clarity
    public float maxHealth;              // runtime computed

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public System.Action OnServerDeath;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            float bonus = 0f;
            var ctrl = GetComponent<ShipController>();
            if (ctrl) bonus = ctrl.GetBonusHull();
            maxHealth = baseMaxHealth + Mathf.Max(0f, bonus);

            Health.Value = maxHealth;
        }

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

    public void ApplyDamage(float amount)
    {
        if (!IsServer || Health.Value <= 0f) return;
        Health.Value = Mathf.Max(0f, Health.Value - Mathf.Abs(amount));
    }

    void HandleDeathServer()
    {
        var ctrl = GetComponent<ShipController>();
        if (ctrl) ctrl.enabled = false;

        OnServerDeath?.Invoke();
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