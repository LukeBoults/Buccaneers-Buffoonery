using UnityEngine;
using Unity.Netcode;

/// Smooth, arcadey boat controller (NO WIND).
/// - Owner sets sail % (0..1) and wheel (-1..1); server simulates.
/// - Stable buoyancy, anti-flip, handbrake, lateral damping.
/// - Exposes CurrentThrottle (sail %) and CurrentSteer (wheel) for HUD.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class ShipController : NetworkBehaviour
{
    // -------- Speed / Sails --------
    [Header("Speed / Sails")]
    [Tooltip("Max forward speed (m/s).")]
    public float maxForwardSpeed = 12f;
    [Tooltip("Max accel toward target speed (m/s^2).")]
    public float acceleration = 10f;
    [Tooltip("Max decel when target speed drops (m/s^2).")]
    public float decelAcceleration = 8f;
    [Tooltip("Sail raise/lower rate per second (W/S).")]
    public float sailRaiseRate = 0.6f;

    // -------- Wheel / Steering --------
    [Header("Wheel / Steering")]
    [Tooltip("Wheel change rate when A/D held (per second).")]
    public float wheelChangeRate = 1.25f;
    [Tooltip("Auto-center per second when no A/D (0 = off).")]
    public float wheelAutoCenterRate = 0.25f;
    [Tooltip("Yaw torque scale from wheel.")]
    public float steerTorque = 16f;
    [Tooltip("Steer scales with speed: 0=no scale, 1=needs speed.")]
    [Range(0f, 1f)] public float steerSpeedScale = 0.6f;
    [Tooltip("Extra steering authority at low sails (half-sail tighter turns).")]
    public float lowSailSteerBoost = 0.35f;
    [Tooltip("Tiny cosmetic roll while turning (deg). 0 = off.")]
    public float bankVisualRollDeg = 6f;

    // -------- Drag / Damping --------
    [Header("Drag / Damping")]
    public float baseLinearDrag = 0.45f;
    public float baseAngularDrag = 1.5f;
    public float brakeDragMultiplier = 5f;
    public float lateralDamping = 6f;

    // -------- Buoyancy --------
    [Header("Water / Buoyancy")]
    public float waterLevelY = 1f;
    public float buoyancyStrength = 60f;
    public float buoyancyDamping = 10f;
    public float maxUpAccel = 20f;
    public bool addBob = true;
    public float bobAmplitude = 0.1f;
    public float bobFrequency = 0.25f;

    // -------- Stability --------
    [Header("Stability / Anti-Flip")]
    public float uprightTorque = 22f;
    public float uprightDamping = 7f;
    public float maxRollPitchRate = 1.2f;
    public float gyroTorque = 100f;
    public float flipAssistAngle = 55f;
    public float flipAssistTorque = 150f;
    public float hardRightingTorque = 200f;

    [Header("Impact Settle")]
    public float impactDampWindow = 0.25f;
    public float maxAngularVelNoImpact = 3f, maxAngularVelImpact = 2f;

    // -------- Input / Net --------
    [Header("Input / Net")]
    public float inputSendInterval = 1f / 30f;

    // Runtime
    Rigidbody rb;
    float bobPhase;
    float impactCooldown;
    int contactCount;

    // Authoritative telegraphs (server)
    float srvSail01; // 0..1
    float srvWheel;  // -1..1
    bool srvBrake;

    // Owner mirrors
    float cliSail01;
    float cliWheel;
    float nextSend;

    // HUD-friendly NVs (optional)
    public NetworkVariable<float> ThrottleNV = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<float> SteerNV = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // HUD aliases
    public float CurrentThrottle => IsOwner ? cliSail01 : ThrottleNV.Value; // 0..1
    public float CurrentSteer => IsOwner ? cliWheel : SteerNV.Value;    // -1..1

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();

        unchecked
        {
            int seed = (int)NetworkObjectId;
            Random.InitState(seed);
            bobPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        if (IsServer)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearDamping = baseLinearDrag;
            rb.angularDamping = baseAngularDrag;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            rb.mass = 1500f;
            rb.centerOfMass = new Vector3(0f, -0.9f, 0f);
            rb.maxAngularVelocity = maxAngularVelNoImpact;
#if UNITY_2021_2_OR_NEWER
            rb.maxDepenetrationVelocity = 2.0f;
#endif
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 12;
        }
        else
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // Sail %
        if (Input.GetKey(KeyCode.W)) cliSail01 += sailRaiseRate * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) cliSail01 -= sailRaiseRate * Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.Z)) cliSail01 = 0.5f; // Half
        if (Input.GetKeyDown(KeyCode.X)) cliSail01 = 1.0f; // Full
        if (Input.GetKeyDown(KeyCode.C)) cliSail01 = 0.0f; // Strike
        cliSail01 = Mathf.Clamp01(cliSail01);

        // Wheel
        bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        if (left) cliWheel -= wheelChangeRate * Time.deltaTime;
        if (right) cliWheel += wheelChangeRate * Time.deltaTime;
        if (!left && !right && wheelAutoCenterRate > 0f)
            cliWheel = Mathf.MoveTowards(cliWheel, 0f, wheelAutoCenterRate * Time.deltaTime);
        if (Input.GetKeyDown(KeyCode.R)) cliWheel = 0f;
        cliWheel = Mathf.Clamp(cliWheel, -1f, 1f);

        bool brake = Input.GetKey(KeyCode.Space);

        if (Time.unscaledTime >= nextSend)
        {
            nextSend = Time.unscaledTime + inputSendInterval;
            SubmitInputServerRpc(cliSail01, cliWheel, brake);

            ThrottleNV.Value = cliSail01;
            SteerNV.Value = cliWheel;
        }
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        Vector3 v = rb.linearVelocity;
        Vector3 vLocal = transform.InverseTransformDirection(v);
        float fwd = Mathf.Max(0f, vLocal.z); // no commanded reverse
        float targetSpeed = srvSail01 * maxForwardSpeed;

        // PD-ish speed control
        float speedErr = targetSpeed - fwd;
        float accelCmd = speedErr * 2.2f; // Kp
        if (accelCmd > 0f) accelCmd = Mathf.Min(accelCmd, acceleration);
        else accelCmd = Mathf.Max(accelCmd, -decelAcceleration);

        if (Mathf.Abs(speedErr) > 0.05f)
            rb.AddForce(transform.forward * accelCmd, ForceMode.Acceleration);

        // Lateral damping
        Vector3 lateral = transform.right * vLocal.x;
        rb.AddForce(-lateral * lateralDamping, ForceMode.Acceleration);

        // Steering (speed-scaled, extra at low sails)
        float speed01 = Mathf.Clamp01(fwd / Mathf.Max(1f, maxForwardSpeed));
        float steerScale = Mathf.Lerp(1f - steerSpeedScale, 1f, speed01);
        float sailBoost = Mathf.Lerp(1f + lowSailSteerBoost, 1f, srvSail01);
        float yawTorque = srvWheel * steerTorque * steerScale * sailBoost;
        rb.AddTorque(Vector3.up * yawTorque, ForceMode.Acceleration);

        // Cosmetic bank
        if (bankVisualRollDeg > 0.01f)
        {
            float bankSign = -Mathf.Sign(srvWheel);
            float bankTorque = bankSign * (bankVisualRollDeg / 90f) * steerTorque * 0.5f;
            rb.AddTorque(transform.forward * bankTorque, ForceMode.Acceleration);
        }

        // Brake
        rb.linearDamping = srvBrake ? baseLinearDrag * brakeDragMultiplier : baseLinearDrag;

        // Buoyancy + Upright
        ApplyBuoyancy(v);
        ApplyUprightAntiFlip();

        // Impact settle
        if (impactCooldown > 0f)
        {
            impactCooldown -= Time.fixedDeltaTime;
            rb.maxAngularVelocity = maxAngularVelImpact;

            Vector3 w = transform.InverseTransformDirection(rb.angularVelocity);
            w.x *= 0.6f; w.z *= 0.6f;
            rb.angularVelocity = transform.TransformDirection(w);

            rb.angularDamping = baseAngularDrag * 2.2f;
        }
        else
        {
            rb.maxAngularVelocity = maxAngularVelNoImpact;
            rb.angularDamping = baseAngularDrag;
        }
    }

    // ----- Buoyancy -----
    void ApplyBuoyancy(Vector3 velWorld)
    {
        float targetY = waterLevelY;
        if (addBob)
        {
            float t = Time.time;
            targetY += Mathf.Sin((t * Mathf.PI * 2f * bobFrequency) + bobPhase) * bobAmplitude;
        }

        float depth = targetY - rb.position.y;
        float vy = velWorld.y;
        float upAccel = depth * buoyancyStrength - vy * buoyancyDamping;
        upAccel = Mathf.Clamp(upAccel, -maxUpAccel, maxUpAccel);
        rb.AddForce(Vector3.up * upAccel, ForceMode.Acceleration);
    }

    // ----- Upright / Anti-Flip -----
    void ApplyUprightAntiFlip()
    {
        Vector3 up = transform.up;
        Vector3 axis = Vector3.Cross(up, Vector3.up);
        float sinA = axis.magnitude;
        if (sinA > 1e-5f) axis /= sinA;
        float angle = (sinA <= 1e-5f) ? 0f : Mathf.Asin(Mathf.Clamp(sinA, 0f, 1f));

        Vector3 wWorld = rb.angularVelocity;
        Vector3 wLocal = transform.InverseTransformDirection(wWorld);
        Vector2 rp = new Vector2(wLocal.x, wLocal.z);
        float rpMag = rp.magnitude;
        if (rpMag > maxRollPitchRate)
        {
            Vector2 excess = rp * (rpMag - maxRollPitchRate) / Mathf.Max(rpMag, 1e-3f);
            Vector3 counterLocal = new Vector3(-excess.x, 0f, -excess.y) * gyroTorque;
            rb.AddTorque(transform.TransformDirection(counterLocal), ForceMode.Acceleration);
        }

        float kp = uprightTorque, kd = uprightDamping;
        Vector3 wNoYaw = wWorld; wNoYaw.y = 0f;
        Vector3 torque = axis * (kp * angle) - wNoYaw * kd;

        float deg = angle * Mathf.Rad2Deg;
        if (deg > flipAssistAngle && deg < 120f) torque += axis * flipAssistTorque;
        if (Vector3.Dot(up, Vector3.up) < 0f) torque += axis * hardRightingTorque;

        const float maxAccelTorque = 90f;
        if (torque.magnitude > maxAccelTorque) torque = torque.normalized * maxAccelTorque;

        rb.AddTorque(torque, ForceMode.Acceleration);
    }

    // ----- Collisions -----
    void OnCollisionEnter(Collision c)
    {
        if (!IsServer) return;
        contactCount++;
        impactCooldown = impactDampWindow;
    }
    void OnCollisionExit(Collision c)
    {
        if (!IsServer) return;
        contactCount = Mathf.Max(0, contactCount - 1);
    }

    // ----- RPC -----
    [ServerRpc]
    void SubmitInputServerRpc(float sail01, float wheel, bool brake)
    {
        srvSail01 = Mathf.Clamp01(sail01);
        srvWheel = Mathf.Clamp(wheel, -1f, 1f);
        srvBrake = brake;
    }
}