using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class OceanGridSpawner : NetworkBehaviour
{
    [Header("Tile Prefab (10x10 units)")]
    public GameObject oceanTilePrefab;

    [Header("Grid Size (Tiles)")]
    public int tilesX = 100;
    public int tilesZ = 100;

    [Header("Tile Metrics")]
    public float tileSize = 10f;     // world units per tile edge
    public bool centerAtOrigin = true;

    [Header("Spawn Options")]
    public Transform parent;         // optional: container object
    public bool spawnOnAllClients = true; // recommended: true (local-only instancing)
    public int tilesPerFrame = 400;  // throttle to avoid frame hitching

    private bool _spawned;

    public override void OnNetworkSpawn()
    {
        // Spawn locally for everyone (server + clients) to avoid networking 10k tiles.
        if (spawnOnAllClients && !_spawned)
        {
            StartCoroutine(SpawnGridLocal());
            _spawned = true;
        }
        // If you *really* want server-driven spawning (not recommended), see alt. method below.
    }

    private IEnumerator SpawnGridLocal()
    {
        if (oceanTilePrefab == null)
        {
            Debug.LogError("[OceanGridSpawner] Missing oceanTilePrefab.");
            yield break;
        }

        // Calculate starting corner so grid is centered (or anchored at origin).
        Vector3 start = Vector3.zero;
        if (centerAtOrigin)
        {
            float width = tilesX * tileSize;
            float depth = tilesZ * tileSize;
            start = new Vector3(-width * 0.5f + tileSize * 0.5f, 0f, -depth * 0.5f + tileSize * 0.5f);
        }

        int countThisFrame = 0;

        for (int z = 0; z < tilesZ; z++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                Vector3 pos = start + new Vector3(x * tileSize, 0f, z * tileSize);
                var go = Instantiate(oceanTilePrefab, pos, Quaternion.identity, parent);

                // Optional: ensure no Rigidbody/Collider if your water is purely visual
                // var rb = go.GetComponent<Rigidbody>(); if (rb) Destroy(rb);
                // var col = go.GetComponent<Collider>(); if (col) Destroy(col);

                // Tip: For best performance, use a material with GPU instancing ON
                // (in the material inspector) and avoid unique per-instance changes.

                countThisFrame++;
                if (countThisFrame >= tilesPerFrame)
                {
                    countThisFrame = 0;
                    yield return null; // spread work across frames
                }
            }
        }
    }

    // ---------- OPTIONAL: server-driven network spawning (NOT RECOMMENDED for 10k tiles) ----------
    // If you insist, make sure the prefab has a NetworkObject and keep counts tiny (or chunked).
    // Call this from OnNetworkSpawn() but only on server: if (IsServer) StartCoroutine(SpawnGridNetworked());
    private IEnumerator SpawnGridNetworked()
    {
        if (!IsServer) yield break;
        if (oceanTilePrefab == null) yield break;

        int countThisFrame = 0;
        Vector3 start = centerAtOrigin
            ? new Vector3(-(tilesX * tileSize) * 0.5f + tileSize * 0.5f, 0f,
                          -(tilesZ * tileSize) * 0.5f + tileSize * 0.5f)
            : Vector3.zero;

        for (int z = 0; z < tilesZ; z++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                Vector3 pos = start + new Vector3(x * tileSize, 0f, z * tileSize);
                var go = Instantiate(oceanTilePrefab, pos, Quaternion.identity, parent);
                var no = go.GetComponent<NetworkObject>();
                if (no) no.Spawn(); // sync to clients

                countThisFrame++;
                if (countThisFrame >= tilesPerFrame)
                {
                    countThisFrame = 0;
                    yield return null;
                }
            }
        }
    }
}