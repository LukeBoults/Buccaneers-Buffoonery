using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class OwnerVcamSpawner : NetworkBehaviour
{
    [SerializeField] CinemachineCamera vcamPrefab;
    [SerializeField] Transform followTarget;   // e.g. ship root or a child "CameraTarget"
    [SerializeField] Transform lookAtTarget;   // often same as followTarget

    CinemachineCamera myVcam;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        // Spawn a local-only vcam
        myVcam = Instantiate(vcamPrefab);
        myVcam.Follow = followTarget != null ? followTarget : transform;
        myVcam.LookAt = lookAtTarget != null ? lookAtTarget : transform;

        // Optional: raise priority so it wins
        myVcam.Priority = 20;
    }

    void OnDestroy()
    {
        if (IsOwner && myVcam != null)
            Destroy(myVcam.gameObject);
    }
}