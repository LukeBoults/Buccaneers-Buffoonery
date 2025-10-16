using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class ResourcePickup : NetworkBehaviour
{
    [Header("Config")]
    public MaterialType type = MaterialType.Wood;
    public int amount = 5;

    [Header("Float/Spin")]
    public float bobAmplitude = 0.25f;
    public float bobFrequency = 0.6f;
    public float spinSpeed = 50f;

    float startY;
    void Start()
    {
        startY = transform.position.y;
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Update()
    {
        // simple client visual
        transform.position = new Vector3(
            transform.position.x,
            startY + Mathf.Sin(Time.time * bobFrequency) * bobAmplitude,
            transform.position.z);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var inv = other.GetComponentInParent<PlayerInventory>();
        if (inv == null) return;

        inv.ServerAddResource(type, amount);   // <-- instead of AddResourceServerRpc
        NetworkObject.Despawn(true);
    }
}