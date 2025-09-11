using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ShipController))]
public class ShipCombat : NetworkBehaviour
{
    [Header("Cannons")]
    public Transform[] portMuzzles;     // left side (X- to world-left when ship faces +Z)
    public Transform[] starboardMuzzles;// right side
    public GameObject cannonballPrefab; // must have NetworkObject + Rigidbody + Cannonball + NetworkTransform

    [Header("Ballistics")]
    public float muzzleSpeed = 35f;
    [Tooltip("Random spread (degrees) applied around muzzle forward.")]
    public float spreadDegrees = 2.5f;
    [Tooltip("Extra kick inherited from ship's current velocity.")]
    public float inheritShipVelocity = 1.0f;

    [Header("Timing")]
    public float salvoCooldown = 1.25f; // seconds between broadsides
    public float perBarrelStagger = 0.05f; // small stagger for nice feel

    float nextPortTime, nextStarboardTime;
    Rigidbody shipRB;
    ShipController shipCtrl;

    void Awake()
    {
        shipRB = GetComponent<Rigidbody>();
        shipCtrl = GetComponent<ShipController>();
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Q)) TryFireSide(Side.Port);       // left
        if (Input.GetKeyDown(KeyCode.E)) TryFireSide(Side.Starboard);  // right
    }

    enum Side { Port, Starboard }

    void TryFireSide(Side side)
    {
        if (side == Side.Port && Time.time < nextPortTime) return;
        if (side == Side.Starboard && Time.time < nextStarboardTime) return;

        // send request to server; server validates & spawns
        FireSideServerRpc(side == Side.Port);

        if (side == Side.Port) nextPortTime = Time.time + salvoCooldown;
        else nextStarboardTime = Time.time + salvoCooldown;
    }

    [ServerRpc]
    void FireSideServerRpc(bool isPort, ServerRpcParams _ = default)
    {
        var muzzles = isPort ? portMuzzles : starboardMuzzles;
        if (muzzles == null || muzzles.Length == 0 || cannonballPrefab == null) return;

        // optional: different damage for close/long range etc.

        // Stagger fire a bit for feel
        StartCoroutine(FireSalvo(muzzles, isPort));
    }

    System.Collections.IEnumerator FireSalvo(Transform[] muzzles, bool isPort)
    {
        var shipVel = shipRB ? shipRB.linearVelocity : Vector3.zero;

        for (int i = 0; i < muzzles.Length; i++)
        {
            var m = muzzles[i];
            if (!m) continue;

            // Direction with small random spread
            Vector3 dir = m.forward;
            if (spreadDegrees > 0f)
            {
                var rand = Random.insideUnitCircle * spreadDegrees;
                dir = Quaternion.Euler(rand.y, rand.x, 0f) * dir;
            }
            dir.Normalize();

            // Velocity = muzzle + a bit of inherited ship velocity
            Vector3 vel = dir * muzzleSpeed + shipVel * inheritShipVelocity;

            // Spawn & launch
            var go = Instantiate(cannonballPrefab, m.position, Quaternion.LookRotation(dir, Vector3.up));
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);

            var ball = go.GetComponent<Cannonball>();
            ball.Launch(m.position, vel, NetworkObjectId);

            // TODO: Muzzle flash SFX/VFX (ClientRpc) if you have VFX
            yield return new WaitForSeconds(perBarrelStagger);
        }
    }
}