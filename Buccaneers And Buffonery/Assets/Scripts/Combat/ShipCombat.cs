using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ShipController))]
public class ShipCombat : NetworkBehaviour
{
    [Header("Cannons")]
    public Transform[] portMuzzles;       // left
    public Transform[] starboardMuzzles;  // right
    public GameObject cannonballPrefab;   // must have NetworkObject + Rigidbody + Cannonball (+ NetworkTransform)

    [Header("Muzzle VFX (index-aligned with muzzles)")]
    public ParticleSystem[] portMuzzleVFX;      // same length/order as portMuzzles
    public ParticleSystem[] starboardMuzzleVFX; // same length/order as starboardMuzzles

    [Header("Ballistics")]
    public float muzzleSpeed = 35f;
    [Tooltip("Random spread (degrees) applied around muzzle forward.")]
    public float spreadDegrees = 2.5f;
    [Tooltip("Extra kick inherited from ship's current velocity.")]
    public float inheritShipVelocity = 1.0f;

    [Header("Timing")]
    public float salvoCooldown = 1.25f;    // seconds between broadsides
    public float perBarrelStagger = 0.05f; // small stagger for nice feel

    [Header("Damage")]
    [Tooltip("Base damage dealt by each cannonball before multipliers.")]
    public float baseDamage = 20f;

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

        FireSideServerRpc(side == Side.Port);

        if (side == Side.Port) nextPortTime = Time.time + salvoCooldown;
        else nextStarboardTime = Time.time + salvoCooldown;
    }

    [ServerRpc]
    void FireSideServerRpc(bool isPort, ServerRpcParams _ = default)
    {
        var muzzles = isPort ? portMuzzles : starboardMuzzles;
        if (muzzles == null || muzzles.Length == 0 || cannonballPrefab == null) return;

        StartCoroutine(FireSalvo(muzzles, isPort));
    }

    System.Collections.IEnumerator FireSalvo(Transform[] muzzles, bool isPort)
    {
        var shipVel = shipRB ? shipRB.linearVelocity : Vector3.zero;
        float dmg = Mathf.Max(0f, baseDamage) * Mathf.Max(0.01f, shipCtrl ? shipCtrl.CannonDamageMultiplier : 1f);

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
            if (no) no.Spawn(true);

            var ball = go.GetComponent<Cannonball>();
            if (ball != null)
            {
                ball.Launch(m.position, vel, NetworkObjectId);
                TrySetBallDamage(ball, dmg);
            }
            else
            {
                TrySetDamageViaCommonPatterns(go, dmg);
            }

            // Tell all clients which exact muzzle index to flash
            MuzzleFlashClientRpc(isPort, i);

            yield return new WaitForSeconds(perBarrelStagger);
        }
    }

    // Damage helpers (same as before; keep if your Cannonball doesn’t expose a clear API)
    void TrySetBallDamage(Component ball, float dmg)
    {
        var mi = ball.GetType().GetMethod("SetDamage",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (mi != null) { mi.Invoke(ball, new object[] { dmg }); return; }

        var fi = ball.GetType().GetField("damage");
        if (fi != null && fi.FieldType == typeof(float)) { fi.SetValue(ball, dmg); return; }

        var pi = ball.GetType().GetProperty("Damage");
        if (pi != null && pi.PropertyType == typeof(float) && pi.CanWrite) { pi.SetValue(ball, dmg); return; }
    }
    void TrySetDamageViaCommonPatterns(GameObject go, float dmg)
    {
        foreach (var c in go.GetComponents<Component>()) TrySetBallDamage(c, dmg);
        foreach (var c in go.GetComponentsInChildren<Component>()) TrySetBallDamage(c, dmg);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MUZZLE FLASH: use pre-assigned per-muzzle ParticleSystems
    [ClientRpc]
    void MuzzleFlashClientRpc(bool isPort, int muzzleIndex)
    {
        var vfxArray = isPort ? portMuzzleVFX : starboardMuzzleVFX;
        if (vfxArray == null || muzzleIndex < 0 || muzzleIndex >= vfxArray.Length) return;

        var ps = vfxArray[muzzleIndex];
        if (!ps) return;

        // One-shot: clear then play to ensure it pops even if mid-sim
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play(true);
    }
}