using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
public class ShipController : NetworkBehaviour
{
    public ShipController Instance { get; private set; }

    [Header("Speed Settings")]
    public float maxForwardSpeed = 12f;
    public float acceleration = 2f;   // how quickly throttle changes
    public float deceleration = 2f;

    [Header("Turning Settings")]
    public float maxRudderAngle = 30f;   // degrees left/right
    public float rudderTurnRate = 25f;   // how fast the wheel turns
    public float turnResponse = 0.5f;    // how quickly the ship responds to rudder input

    private Rigidbody rb;

    // Persistent controls
    private float throttle = 0f;   // 0 → 1 (fraction of max speed)
    private float rudder = 0f;     // -1 → 1 (left/right wheel position)

    private float targetThrottle = 0f;
    private float targetRudder = 0f;

    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        rb.isKinematic = !IsServer;
    }

    void Update()
    {
        if (!IsOwner) return;

        // Adjust target throttle
        if (Input.GetKey(KeyCode.W))
            targetThrottle = Mathf.Clamp01(targetThrottle + Time.deltaTime * (1f / acceleration));
        if (Input.GetKey(KeyCode.S))
            targetThrottle = Mathf.Clamp01(targetThrottle - Time.deltaTime * (1f / deceleration));

        // Adjust target rudder
        if (Input.GetKey(KeyCode.A))
            targetRudder = Mathf.Clamp(targetRudder - Time.deltaTime * (1f / rudderTurnRate), -1f, 1f);
        if (Input.GetKey(KeyCode.D))
            targetRudder = Mathf.Clamp(targetRudder + Time.deltaTime * (1f / rudderTurnRate), -1f, 1f);

        // Send state to server
        SendInputServerRpc(targetThrottle, targetRudder);
    }

    [ServerRpc]
    void SendInputServerRpc(float throttleInput, float rudderInput)
    {
        targetThrottle = throttleInput;
        targetRudder = rudderInput;
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        // Smoothly apply throttle
        throttle = Mathf.Lerp(throttle, targetThrottle, Time.fixedDeltaTime * acceleration);
        float speed = throttle * maxForwardSpeed;

        // Smoothly apply rudder
        rudder = Mathf.Lerp(rudder, targetRudder, Time.fixedDeltaTime * rudderTurnRate);
        float rudderAngle = rudder * maxRudderAngle;

        // Forward force
        rb.AddForce(transform.forward * speed, ForceMode.Acceleration);

        // Turn based on rudder
        Quaternion deltaRot = Quaternion.Euler(0f, rudderAngle * turnResponse * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * deltaRot);
    }

    // Accessors for UI
    public float GetThrottle01() => throttle;
    public float GetRudder01() => (rudder + 1f) * 0.5f; // 0 = left, 0.5 = center, 1 = right
}