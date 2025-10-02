using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine; // CM 3.x

public class OwnerTpsMouseCam : NetworkBehaviour
{
    [Header("Camera Prefab (no NetworkObject)")]
    [SerializeField] private CinemachineCamera vcamPrefab;

    [Header("Targets")]
    [SerializeField] private Transform followTarget;   // usually a "CameraTarget" on your ship (position only)

    [Header("Mouse Look")]
    [SerializeField] private float mouseXSensitivity = 0.1f;  // deg per pixel
    [SerializeField] private float mouseYSensitivity = 0.08f; // deg per pixel
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitchDeg = -15f;
    [SerializeField] private float maxPitchDeg = 65f;

    [Header("Orbit/Zoom")]
    [SerializeField] private float defaultDistance = 12f;
    [SerializeField] private float minDistance = 5f;
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private float zoomStep = 2f;      // per wheel notch
    [SerializeField] private float defaultHeight = 3f; // base boom height above target
    [SerializeField] private Vector3 shoulderOffset = new Vector3(1.2f, 0f, 0f);

    [Header("Smoothing (lerp toward targets)")]
    [SerializeField] private float yawDamp = 15f;
    [SerializeField] private float pitchDamp = 15f;
    [SerializeField] private float distDamp = 12f;
    [SerializeField] private float heightDamp = 12f;
    [SerializeField] private float offsetDamp = 20f;
    [SerializeField] private float followPosDamp = 18f; // how snappy the rig tracks the ship position

    [Header("Cursor")]
    [SerializeField] private bool lockCursorAtStart = true;

    private CinemachineCamera myVcam;

    // World-space orbit rig (NOT parented to ship)
    private Transform pivot; // rotates yaw/pitch; also used as LookAt
    private Transform boom;  // child of pivot; positioned by (height, distance, shoulder)

    // targets
    private float tgtYaw, tgtPitch, tgtDist, tgtHeight;
    private Vector3 tgtShoulder;

    // smoothed
    private float yaw, pitch, dist, height;
    private Vector3 shoulder;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        if (!followTarget) followTarget = transform;

        CreateRig();
        SpawnVcam();
        SeedFromCurrent();

        if (lockCursorAtStart) SetCursorLocked(true);

        // Start rig at player position
        pivot.position = followTarget.position;
    }

    private void Update()
    {
        if (!IsOwner || myVcam == null || pivot == null || boom == null) return;

        // ESC toggles cursor lock/unlock
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool willLock = Cursor.lockState != CursorLockMode.Locked;
            SetCursorLocked(willLock);
        }

        // --- Always-on mouse look when locked ---
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float dx = Input.GetAxisRaw("Mouse X");
            float dy = Input.GetAxisRaw("Mouse Y");

            tgtYaw += dx * mouseXSensitivity * 100f;
            tgtPitch += (invertY ? dy : -dy) * mouseYSensitivity * 100f;
        }

        // Zoom
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
            tgtDist = Mathf.Clamp(tgtDist - wheel * zoomStep, minDistance, maxDistance);

        // Clamp pitch
        tgtPitch = Mathf.Clamp(tgtPitch, minPitchDeg, maxPitchDeg);

        // Smooth angular/boom params
        float dt = Time.unscaledDeltaTime;
        yaw = Mathf.LerpAngle(yaw, tgtYaw, 1f - Mathf.Exp(-yawDamp * dt));
        pitch = Mathf.Lerp(pitch, tgtPitch, 1f - Mathf.Exp(-pitchDamp * dt));
        dist = Mathf.Lerp(dist, tgtDist, 1f - Mathf.Exp(-distDamp * dt));
        height = Mathf.Lerp(height, tgtHeight, 1f - Mathf.Exp(-heightDamp * dt));
        shoulder = Vector3.Lerp(shoulder, tgtShoulder, 1f - Mathf.Exp(-offsetDamp * dt));
    }

    private void LateUpdate()
    {
        if (!IsOwner || pivot == null || boom == null || followTarget == null) return;

        float dt = Time.unscaledDeltaTime;

        // Track the ship position ONLY (no rotation inheritance)
        pivot.position = Vector3.Lerp(pivot.position, followTarget.position, 1f - Mathf.Exp(-followPosDamp * dt));

        // Apply yaw/pitch in world space (independent of ship rotation)
        pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Place boom relative to pivot
        boom.localPosition = new Vector3(shoulder.x, height + shoulder.y, -dist) + new Vector3(0f, 0f, shoulder.z);

        // Keep vcam hooked
        myVcam.Follow = boom;   // body follows boom
        myVcam.LookAt = pivot;  // aim at pivot (ship position), not ship rotation
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            if (myVcam) Destroy(myVcam.gameObject);
            if (pivot) Destroy(pivot.gameObject); // boom goes with it
        }
        myVcam = null; pivot = null; boom = null;
        SetCursorLocked(false);
    }

    private void OnDestroy()
    {
        if (myVcam) Destroy(myVcam.gameObject);
        if (pivot) Destroy(pivot.gameObject);
        SetCursorLocked(false);
    }

    // ----------------- Helpers -----------------

    private void CreateRig()
    {
        // World-space pivot
        var pivotGO = new GameObject("CamOrbitPivot (world)");
        pivot = pivotGO.transform;
        pivot.position = followTarget.position; // start at target
        pivot.rotation = Quaternion.identity;

        var boomGO = new GameObject("CamBoom (local)");
        boom = boomGO.transform;
        boom.SetParent(pivot, false);

        // init targets
        tgtYaw = 0f;
        tgtPitch = 10f;
        tgtDist = defaultDistance;
        tgtHeight = defaultHeight;
        tgtShoulder = shoulderOffset;

        yaw = tgtYaw; pitch = tgtPitch; dist = tgtDist; height = tgtHeight; shoulder = tgtShoulder;
        boom.localPosition = new Vector3(shoulder.x, height + shoulder.y, -dist) + new Vector3(0f, 0f, shoulder.z);
    }

    private void SpawnVcam()
    {
        if (vcamPrefab == null)
        {
            Debug.LogWarning("[OwnerTpsMouseCam] vcamPrefab not assigned.");
            return;
        }
        myVcam = Instantiate(vcamPrefab);
        DontDestroyOnLoad(myVcam.gameObject);
        myVcam.Priority = 20;

        myVcam.Follow = boom;
        myVcam.LookAt = pivot; // aim at pivot, not the rotating ship
    }

    private void SeedFromCurrent()
    {
        // Seed yaw/pitch from current camera direction (optional)
        Vector3 toCam = (myVcam.transform.position - followTarget.position);
        if (toCam.sqrMagnitude > 0.01f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(toCam, Vector3.up);
            float initYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float initPitch = Vector3.SignedAngle(flat.normalized, toCam.normalized, Vector3.Cross(flat, Vector3.up).normalized);

            tgtYaw = yaw = initYaw;
            tgtPitch = pitch = Mathf.Clamp(initPitch, minPitchDeg, maxPitchDeg);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}