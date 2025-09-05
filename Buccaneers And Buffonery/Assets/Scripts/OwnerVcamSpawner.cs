using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;  // new Cinemachine namespace in CM 3.x

public class OwnerVcamSpawner : NetworkBehaviour
{
    [Header("Camera Prefab (no NetworkObject)")]
    [SerializeField] private CinemachineCamera vcamPrefab;

    [Header("Targets")]
    [SerializeField] private Transform followTarget; // e.g. ship root or a child "CameraTarget"
    [SerializeField] private Transform lookAtTarget; // often same as followTarget

    private CinemachineCamera myVcam;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        if (vcamPrefab == null)
        {
            Debug.LogWarning("[OwnerVcamSpawner] vcamPrefab not assigned.");
            return;
        }

        // Instantiate a local-only vcam
        myVcam = Instantiate(vcamPrefab);

        // Make sure it survives scene changes if NGO SceneManager loads new scenes
        DontDestroyOnLoad(myVcam.gameObject);

        // Configure follow/lookAt
        myVcam.Follow = followTarget != null ? followTarget : transform;
        myVcam.LookAt = lookAtTarget != null ? lookAtTarget : transform;

        // Raise priority so this vcam takes over
        myVcam.Priority = 20;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && myVcam != null)
        {
            Destroy(myVcam.gameObject);
            myVcam = null;
        }
    }

    private void OnDestroy()
    {
        // Safety cleanup if despawn wasn't called
        if (myVcam != null)
        {
            Destroy(myVcam.gameObject);
            myVcam = null;
        }
    }
}