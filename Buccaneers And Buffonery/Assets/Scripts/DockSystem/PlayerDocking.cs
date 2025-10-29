using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerDocking : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Root of the ship visuals/physics (can be the same as the Player root if you like).")]
    public GameObject shipBody;

    [Tooltip("Root of the on-foot character (enabled while docked).")]
    public GameObject personBody;

    [Tooltip("Ship movement scripts (your input + motor components). They will be enabled when NOT docked (owner-only).")]
    public MonoBehaviour[] shipMovementComponents;

    [Tooltip("Optional ship rigidbody to freeze/unfreeze at the dock.")]
    public Rigidbody shipRigidbody;

    [Tooltip("Person CharacterController (enabled when docked for the owner).")]
    public CharacterController personController;

    [Header("Cameras (owner-only)")]
    [Tooltip("Camera or rig following the SHIP. Enabled when sailing for the owner.")]
    public GameObject shipCameraRig;

    [Tooltip("Camera or rig following the PERSON. Enabled when docked for the owner.")]
    public GameObject personCameraRig;

    [Header("Search")]
    public LayerMask dockMask = ~0;
    public float searchRadius = 5f;

    [Header("State (read-only)")]
    public NetworkVariable<bool> IsDocked = new(writePerm: NetworkVariableWritePermission.Server);

    private DockZone _currentDockZone;
    private bool _wired;

    private void Awake()
    {
        // Default visuals: ship active, person hidden
        if (shipBody) shipBody.SetActive(true);
        if (personBody) personBody.SetActive(false);
        if (personController) personController.enabled = false;

        // Disable both cameras in editor; game logic will pick one for the owner
        if (shipCameraRig) shipCameraRig.SetActive(false);
        if (personCameraRig) personCameraRig.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        // Apply initial state for the local player (owner gets controls/camera)
        WireStateListener();
        ApplyStateVisualsAndControl(IsDocked.Value);
    }

    private void WireStateListener()
    {
        if (_wired) return;
        IsDocked.OnValueChanged += (_, now) => ApplyStateVisualsAndControl(now);
        _wired = true;
    }

    private void OnDestroy()
    {
        if (_wired) IsDocked.OnValueChanged -= (_, __) => { };
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.F))
        {
            if (IsDocked.Value) RequestReboardServerRpc();
            else RequestDockServerRpc();
        }
    }

    [ServerRpc]
    private void RequestDockServerRpc(ServerRpcParams _ = default)
    {
        if (IsDocked.Value) return;

        DockZone dock = FindNearbyDockZoneServer();
        if (dock == null || dock.shipDockPoint == null || dock.personSpawnPoint == null) return;

        float dist = Vector3.Distance(transform.position, dock.shipDockPoint.position);
        if (dist > dock.maxDockDistance + searchRadius) return;

        _currentDockZone = dock;

        // Park the ship server-authoritatively
        Vector3 shipPos = dock.shipDockPoint.position;
        Quaternion shipRot = dock.shipDockPoint.rotation;

        if (shipBody) shipBody.transform.SetPositionAndRotation(shipPos, shipRot);
        if (shipRigidbody)
        {
            shipRigidbody.linearVelocity = Vector3.zero;
            shipRigidbody.angularVelocity = Vector3.zero;
            shipRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Flip state → clients update controls/cameras locally via OnValueChanged
        IsDocked.Value = true;

        // Move the person body to island spawn (visual sync via ClientRpc)
        SetDockedClientRpc(true, shipPos, shipRot, dock.personSpawnPoint.position, dock.personSpawnPoint.rotation);
    }

    [ServerRpc]
    private void RequestReboardServerRpc(ServerRpcParams _ = default)
    {
        if (!IsDocked.Value) return;

        if (shipRigidbody)
        {
            // Typical boat: constrain X/Z rotation but allow Yaw + linear motion
            shipRigidbody.constraints = RigidbodyConstraints.None;
            shipRigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        Vector3 shipPos = shipBody ? shipBody.transform.position : transform.position;
        Quaternion shipRot = shipBody ? shipBody.transform.rotation : transform.rotation;

        IsDocked.Value = false;
        SetDockedClientRpc(false, shipPos, shipRot, Vector3.zero, Quaternion.identity);
        _currentDockZone = null;
    }

    [ClientRpc]
    private void SetDockedClientRpc(bool docked, Vector3 shipPos, Quaternion shipRot, Vector3 personPos, Quaternion personRot)
    {
        // Keep positions visually consistent for everyone
        if (shipBody) shipBody.transform.SetPositionAndRotation(shipPos, shipRot);

        if (docked)
        {
            if (personBody) personBody.transform.SetPositionAndRotation(personPos, personRot);
        }

        // Controls/cameras are handled by ApplyStateVisualsAndControl via OnValueChanged for owners.
        // But in case of race conditions (host), call it once more:
        if (IsOwner) ApplyStateVisualsAndControl(docked);
    }

    private void ApplyStateVisualsAndControl(bool docked)
    {
        // Visuals (everyone)
        if (personBody) personBody.SetActive(docked);
        if (shipBody) shipBody.SetActive(true); // ship always visible (parked or sailing)

        // Owner-only controls + cameras
        if (IsOwner)
        {
            // Ship controls when NOT docked
            ToggleShipMovement(!docked);

            if (personController) personController.enabled = docked;

            if (shipCameraRig) shipCameraRig.SetActive(!docked);
            if (personCameraRig) personCameraRig.SetActive(docked);

            // If undocking, snap the player root to ship to keep authority tight
            if (!docked && shipBody)
                transform.SetPositionAndRotation(shipBody.transform.position, shipBody.transform.rotation);
        }
        else
        {
            // Non-owners never control movement or see owner-only cameras
            ToggleShipMovement(false);
            if (personController) personController.enabled = false;
            if (shipCameraRig) shipCameraRig.SetActive(false);
            if (personCameraRig) personCameraRig.SetActive(false);
        }
    }

    private void ToggleShipMovement(bool enabled)
    {
        if (shipMovementComponents == null) return;
        foreach (var comp in shipMovementComponents.Where(c => c != null))
            comp.enabled = enabled;
    }

    private DockZone FindNearbyDockZoneServer()
    {
        var hits = Physics.OverlapSphere(transform.position, searchRadius, dockMask, QueryTriggerInteraction.Collide);
        DockZone best = null;
        float bestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var dz = h.GetComponentInParent<DockZone>();
            if (dz == null || dz.shipDockPoint == null || dz.personSpawnPoint == null) continue;
            float d = Vector3.Distance(transform.position, dz.shipDockPoint.position);
            if (d < bestDist) { bestDist = d; best = dz; }
        }
        return best;
    }
}