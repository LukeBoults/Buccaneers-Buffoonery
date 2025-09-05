using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative physics ship controller for Unity + NGO.
/// - Owner client reads input, sends to server via ServerRpc
/// - Server applies forces/torques in FixedUpdate
/// - Use NetworkTransform to replicate motion to all clients
/// - Includes lightweight buoyancy + optional sine bobbing
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class ShipController : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("Forward acceleration in m/s^2 when throttle fully pressed.")]
    public float acceleration = 8f;

    [Tooltip("Reverse acceleration in m/s^2 when reversing.")]
    public float reverseAcceleration = 5f;

    [Tooltip("Max forward speed in m/s.")]
    public float maxForwardSpeed = 10f;

    [Tooltip("Max reverse speed in m/s (positive number).")]
    public float maxReverseSpeed = 4f;

    [Header("Steering")]
    [Tooltip("Base yaw torque applied for steering.")]
    public float steerTorque = 12f;

    [Tooltip("How much steering scales with forward speed (0–1). Higher = more speed needed to turn.")]
    [Range(0f, 1f)] public float steerSpeedScale = 0.6f;

    [Header("Water Feel")]
    [Tooltip("Damps sideways slide; higher = less drift.")]
    public float lateralDamping = 4f;

    [Tooltip("Extra drag while braking (Space).")]
    public float brakeDragMultiplier = 4f;

    [Tooltip("Baseline linear drag on the Rigidbody.")]
    public float baseLinearDrag = 0.5f;

    [Tooltip("Baseline angular drag on the Rigidbody.")]
    public float baseAngularDrag = 1.5f;

    [Header("Upright Stabilization")]
    [Tooltip("Strength of the auto-upright torque.")]
    public float uprightTorque = 8f;

    [Tooltip("How quickly the ship rights itself (bigger = snappier).")]
    public float uprightDamping = 2.5f;

    [Header("Buoyancy / Bobbing")]
    [Tooltip("Enable spring-damper buoyancy to keep the ship at water height.")]
    public bool useBuoyancy = true;

    [Tooltip("Flat sea level (Y). Replace with a water height sampler for waves).")]
    public float waterLevelY = 0f;

    [Tooltip("Spring strength toward water surface (higher = stiffer).")]
    public float buoyancyStrength = 50f;

    [Tooltip("Damping against vertical velocity (prevents oscillation).")]
    public float buoyancyDamping = 8f;

    [Tooltip("Add a gentle sine bob to the water height target.")]
    public bool addBob = true;

    [Tooltip("Vertical bob amplitude in meters.")]
    public float bobAmplitude = 0.15f;

    [Tooltip("Bob frequency in Hz (cycles per second).")]
    public float bobFrequency = 0.3f;

    [Tooltip("If true, gravity stays on and buoyancy pushes up. If false, gravity is off and we hard-clamp Y (arcade).")]
    public bool buoyancyUsesGravity = true;

    [Header("Input (client)")]
    [Tooltip("How often the owning client sends inputs (seconds).")]
    public float inputSendInterval = 1f / 30f;

    private Rigidbody rb;

    // Last inputs received by the server for this ship
    private float srvThrottle; // -1..1 (negative = reverse)
    private float srvSteer;    // -1..1  (A/D or left/right)
    private bool srvBrake;

    // Client-side input packing
    private float cliNextSendTime;

    // Cache for lateral damping
    private Vector3 vel;
    private Vector3 localVel;

    // Per-ship bob phase so they don't all bob in sync
    private float bobPhase;

    public NetworkVariable<float> ThrottleNV = new NetworkVariable<float>(
    0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public NetworkVariable<float> SteerNV = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public float CurrentThrottle => ThrottleNV.Value; // -1..1
    public float CurrentSteer => SteerNV.Value;    // -1..1
    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();

        // Unique-ish bob phase per ship
        unchecked
        {
            int seed = (int)NetworkObjectId;
            Random.InitState(seed);
            bobPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        if (IsServer)
        {
            rb.isKinematic = false;
            rb.useGravity = buoyancyUsesGravity;   // gravity ON when using buoyancy, OFF for hard clamp style
            rb.linearDamping = baseLinearDrag;
            rb.angularDamping = baseAngularDrag;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        else
        {
            // Prevent client-side physics fighting the server
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Gather local input
        float throttle = Input.GetAxisRaw("Vertical");   // W/S or Up/Down
        float steer = Input.GetAxisRaw("Horizontal"); // A/D or Left/Right
        bool brake = Input.GetKey(KeyCode.Space);

        // Deadzones
        if (Mathf.Abs(throttle) < 0.05f) throttle = 0f;
        if (Mathf.Abs(steer) < 0.05f) steer = 0f;

        // Throttle input sending to reduce bandwidth
        if (Time.unscaledTime >= cliNextSendTime)
        {
            cliNextSendTime = Time.unscaledTime + inputSendInterval;
            SubmitInputServerRpc(throttle, steer, brake);
        }

        if (IsOwner)
        {
            // ... you already computed throttle, steer, brake ...
            ThrottleNV.Value = throttle;
            SteerNV.Value = steer;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Calculate local velocity
        vel = rb.linearVelocity;
        localVel = transform.InverseTransformDirection(vel);

        // 1) Apply forward / reverse acceleration with speed limits
        float desiredMaxSpeed = srvThrottle >= 0f ? maxForwardSpeed : maxReverseSpeed;
        float accel = srvThrottle >= 0f ? acceleration : reverseAcceleration;

        // Current forward speed in local Z
        float fwdSpeed = localVel.z;

        // Desired acceleration along forward axis
        float targetAccel = srvThrottle * accel;

        // Clamp so we don’t exceed speed caps
        if (srvThrottle > 0f && fwdSpeed >= desiredMaxSpeed) targetAccel = 0f;
        else if (srvThrottle < 0f && -fwdSpeed >= desiredMaxSpeed) targetAccel = 0f;

        // Apply forward force (Acceleration mode = m/s^2)
        if (Mathf.Abs(targetAccel) > 0.001f)
            rb.AddForce(transform.forward * targetAccel, ForceMode.Acceleration);

        // 2) Lateral damping to reduce side-slipping (boat “grip”)
        Vector3 lateral = transform.right * localVel.x; // sideways component
        rb.AddForce(-lateral * lateralDamping, ForceMode.Acceleration);

        // 3) Steering: torque scales with forward speed (so you don’t spin in place)
        float speed01 = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / Mathf.Max(1f, maxForwardSpeed));
        float steerScale = Mathf.Lerp(1f - steerSpeedScale, 1f, speed01); // 0.4..1 for steerSpeedScale=0.6
        float yawSign = Mathf.Sign(fwdSpeed == 0f ? 1f : fwdSpeed); // reverse steering when moving backward feels intuitive
        if (Mathf.Abs(srvSteer) > 0.001f)
        {
            float torque = srvSteer * steerTorque * steerScale * yawSign;
            rb.AddTorque(Vector3.up * torque, ForceMode.Acceleration);
        }

        // 4) Brake: temporarily increase drag to slow quickly
        rb.linearDamping = srvBrake ? baseLinearDrag * brakeDragMultiplier : baseLinearDrag;

        // 5) Buoyancy / Bobbing
        if (useBuoyancy)
        {
            ApplyBuoyancy();
        }

        // 6) Keep upright (optional but nice on waves/collisions)
        ApplyUprightTorque();
    }

    private void ApplyBuoyancy()
    {
        // Target water surface at this (x,z)
        float targetWaterY = SampleWaterHeight(transform.position.x, transform.position.z);

        // Optional sine bob so even a flat sea feels alive
        if (addBob)
        {
            float t = Time.time;
            targetWaterY += Mathf.Sin((t * Mathf.PI * 2f * bobFrequency) + bobPhase) * bobAmplitude;
        }

        // Spring-damper toward target water height
        float y = rb.position.y;
        float depth = targetWaterY - y; // positive if below surface target
        float verticalVel = rb.linearVelocity.y;

        // Acceleration upward
        float upAccel = depth * buoyancyStrength - verticalVel * buoyancyDamping;

        // If gravity is disabled, clamp Y hard (arcade style)
        if (!buoyancyUsesGravity)
        {
            // Teleport Y to water and null vertical velocity; still add a tiny up force for feel
            Vector3 p = rb.position;
            p.y = Mathf.Lerp(p.y, targetWaterY, 0.8f); // smooth snap
            rb.MovePosition(p);

            Vector3 v = rb.linearVelocity;
            v.y = 0f;
            rb.linearVelocity = v;

            // tiny stabilizing force so it doesn't feel dead
            rb.AddForce(Vector3.up * (upAccel * 0.1f), ForceMode.Acceleration);
        }
        else
        {
            // Gravity ON: true buoyancy force
            rb.AddForce(Vector3.up * upAccel, ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// Replace this with your wave sampler later.
    /// For now, a flat plane at waterLevelY.
    /// </summary>
    private float SampleWaterHeight(float x, float z)
    {
        // Hook point for future: return WaterSystem.GetHeight(x,z);
        return waterLevelY;
    }

    private void ApplyUprightTorque()
    {
        // Target upright = world up
        Vector3 up = transform.up;
        Vector3 torqueAxis = Vector3.Cross(up, Vector3.up); // axis to rotate around
        float angle = Vector3.SignedAngle(up, Vector3.up, torqueAxis);

        // PD-like torque: proportional + a bit of damping
        Vector3 corrective =
            -torqueAxis.normalized * angle * uprightTorque
            - rb.angularVelocity * uprightDamping;

        if (!float.IsNaN(corrective.x))
            rb.AddTorque(corrective, ForceMode.Acceleration);
    }

    // ----------------- RPCs -----------------
    [ServerRpc]
    private void SubmitInputServerRpc(float throttle, float steer, bool brake)
    {
        // Lightweight validation/clamping
        srvThrottle = Mathf.Clamp(throttle, -1f, 1f);
        srvSteer = Mathf.Clamp(steer, -1f, 1f);
        srvBrake = brake;
    }
}