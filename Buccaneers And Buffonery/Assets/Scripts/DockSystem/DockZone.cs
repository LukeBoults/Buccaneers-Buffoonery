using UnityEngine;

public class DockZone : MonoBehaviour
{
    [Header("Points")]
    [Tooltip("Where the ship should be parked (position/rotation).")]
    public Transform shipDockPoint;

    [Tooltip("Where the player spawns as a person on the island (position/rotation).")]
    public Transform personSpawnPoint;

    [Header("Dock Settings")]
    [Tooltip("Max distance from player ship to allow docking (safety check).")]
    public float maxDockDistance = 6f;

    private void OnDrawGizmos()
    {
        if (shipDockPoint)
        {
            Gizmos.DrawWireCube(shipDockPoint.position, new Vector3(2, 2, 2));
            Gizmos.DrawLine(transform.position, shipDockPoint.position);
        }
        if (personSpawnPoint)
        {
            Gizmos.DrawSphere(personSpawnPoint.position, 0.4f);
        }
    }
}