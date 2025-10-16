using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ResourceSpawnManager : NetworkBehaviour
{
    [Header("Prefabs (NetworkObject)")]
    public ResourcePickup[] pickupPrefabs; // assign variants (wood crate, barrel, ore, etc.)

    [Header("Counts & Timing")]
    public int targetAlive = 60;
    public float spawnInterval = 2.5f;   // seconds between spawn attempts
    public float respawnHeightY = 0.4f;  // sea level + offset (match your water)

    [Header("Area (world units)")]
    public Vector2 areaMin = new(-450f, -450f);
    public Vector2 areaMax = new(450f, 450f);
    public float edgeMargin = 20f;

    [Header("Placement")]
    public LayerMask solidGroundMask; // if you want island placement; leave 0 to float on sea
    public bool preferIslands = false;

    private readonly HashSet<ulong> aliveIds = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnerLoop());
    }

    private IEnumerator SpawnerLoop()
    {
        // Warmup: top-up to target
        while (IsServer)
        {
            // Clean dead refs
            aliveIds.RemoveWhere(id => !NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(id));

            if (aliveIds.Count < targetAlive)
            {
                int toSpawn = Mathf.Min(6, targetAlive - aliveIds.Count);
                for (int i = 0; i < toSpawn; i++)
                {
                    SpawnOne();
                    yield return null; // spread work
                }
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private void SpawnOne()
    {
        if (pickupPrefabs == null || pickupPrefabs.Length == 0) return;

        // pick a random prefab
        var prefab = pickupPrefabs[Random.Range(0, pickupPrefabs.Length)];

        // pick a position
        Vector3 pos = RandomPointInArea();
        if (preferIslands && solidGroundMask.value != 0)
        {
            // raycast down from a safe height to find island tops
            Vector3 rayStart = new Vector3(pos.x, 200f, pos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out var hit, 500f, solidGroundMask))
            {
                pos = hit.point + Vector3.up * 0.2f;
            }
            else
            {
                // fallback to sea
                pos = new Vector3(pos.x, respawnHeightY, pos.z);
            }
        }
        else
        {
            pos = new Vector3(pos.x, respawnHeightY, pos.z);
        }

        var go = Instantiate(prefab.gameObject, pos, Quaternion.identity);
        var no = go.GetComponent<NetworkObject>();
        no.Spawn(true);
        aliveIds.Add(no.NetworkObjectId);
    }

    private Vector3 RandomPointInArea()
    {
        float minX = Mathf.Min(areaMin.x, areaMax.x) + edgeMargin;
        float maxX = Mathf.Max(areaMin.x, areaMax.x) - edgeMargin;
        float minZ = Mathf.Min(areaMin.y, areaMax.y) + edgeMargin;
        float maxZ = Mathf.Max(areaMin.y, areaMax.y) - edgeMargin;

        float x = Random.Range(minX, maxX);
        float z = Random.Range(minZ, maxZ);
        return new Vector3(x, 0f, z);
    }
}