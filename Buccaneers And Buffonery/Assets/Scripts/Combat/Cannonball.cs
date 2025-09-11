using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class Cannonball : NetworkBehaviour
{
    [Header("Projectile")]
    public float damage = 25f;
    public float lifeSeconds = 8f;
    public float ownerIgnoreTime = 0.25f; // ignore hitting own ship right after launch
    public LayerMask hitMask = ~0;        // by default hit everything

    Rigidbody rb;
    float spawnTime;
    ulong ownerShipNetId;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();

        if (IsServer)
        {
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            spawnTime = Time.time;
            Invoke(nameof(Die), lifeSeconds);
        }
        else
        {
            // clients don't simulate physics
            rb.isKinematic = true;
        }
    }

    // Server-only: set initial state
    public void Launch(Vector3 position, Vector3 velocity, ulong ownerShipNetworkId)
    {
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity.normalized, Vector3.up));
        rb.linearVelocity = velocity;
        ownerShipNetId = ownerShipNetworkId;
    }

    void Die()
    {
        if (IsServer && NetworkObject && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    void OnCollisionEnter(Collision col)
    {
        if (!IsServer) return;
        HandleHit(col.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        HandleHit(other);
    }

    void HandleHit(Collider other)
    {
        // Ignore own ship for small grace period
        var targetNO = other.attachedRigidbody ? other.attachedRigidbody.GetComponentInParent<NetworkObject>()
                                               : other.GetComponentInParent<NetworkObject>();

        if (targetNO && targetNO.NetworkObjectId == ownerShipNetId && (Time.time - spawnTime) < ownerIgnoreTime)
            return;

        // Damage if ship
        var health = other.GetComponentInParent<ShipHealth>();
        if (health && health.IsSpawned)
            health.ApplyDamage(damage); // server-side call

        Die();
    }
}