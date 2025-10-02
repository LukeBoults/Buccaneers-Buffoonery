using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// Networked island spawner tailored for Kenney Pirate Kit.
/// - Server-only spawns; clients get replicated tiles.
/// - Allows overlap (with small jitter), 90°-snap rotation to suit modular pieces.
/// - Optional foliage/props pass on top of land tiles.
public class NetworkIslandSpawner : NetworkBehaviour
{
    [Header("Tile Variants (MUST have NetworkObject)")]
    [Tooltip("Drag in your sand/rock island chunks from Kenney Pirate Kit (with NetworkObject).")]
    public GameObject[] islandTiles;

    [Header("Optional Foliage/Props (MUST have NetworkObject)")]
    public GameObject[] decorPrefabs;

    [Header("Area / Grid")]
    public float tileSize = 10f;
    public Vector2 areaSize = new Vector2(1000, 1000);
    public bool centerOnOrigin = true;

    [Header("Islands")]
    public int islandCount = 5;
    public Vector2Int tilesPerIsland = new Vector2Int(80, 160);
    public int islandCenterSpacing = 6;

    [Header("Look (Kenney-friendly)")]
    public bool randomYRotation = true;               // snap to 90°
    public float yRotationStep = 90f;
    public float overlapJitter = 1.0f;                // small XY offset so overlaps interleave

    [Header("Decor pass")]
    [Range(0f, 1f)] public float decorChance = 0.35f;
    public Vector2 decorOffsetJitter = new Vector2(2f, 2f);

    [Header("Performance")]
    public bool spawnAsync = true;
    public int tilesPerFrame = 400;

    [Header("Randomness")]
    public int seed = -1;                             // -1 = random each run

    private System.Random rng;
    private bool _spawned;

    public override void OnNetworkSpawn()
    {
        if (!IsServer || _spawned) return;

        if (islandTiles == null || islandTiles.Length == 0)
        {
            Debug.LogError("[KenneyNetworkIslandSpawner] No island tiles assigned.");
            return;
        }

        if (seed < 0) seed = Random.Range(0, int.MaxValue);
        rng = new System.Random(seed);

        if (spawnAsync) StartCoroutine(SpawnIslands_Co());
        else SpawnIslands_Immediate();

        _spawned = true;
        Debug.Log($"[KenneyNetworkIslandSpawner] Server spawned islands (seed {seed}).");
    }

    // ---------------- spawning ----------------

    private (int cellsX, int cellsZ, Vector3 origin) GridSetup()
    {
        int cellsX = Mathf.Max(1, Mathf.RoundToInt(areaSize.x / tileSize));
        int cellsZ = Mathf.Max(1, Mathf.RoundToInt(areaSize.y / tileSize));
        Vector3 origin = transform.position;
        if (centerOnOrigin) origin -= new Vector3(cellsX * tileSize, 0f, cellsZ * tileSize) * 0.5f;
        return (cellsX, cellsZ, origin);
    }

    private void SpawnIslands_Immediate()
    {
        var (cellsX, cellsZ, origin) = GridSetup();
        var centers = new List<Vector2Int>();

        for (int i = 0; i < islandCount; i++)
        {
            if (!TryPickIslandCenter(cellsX, cellsZ, centers, islandCenterSpacing, out var center))
                break;

            centers.Add(center);
            RandomWalkIsland(origin, center, cellsX, cellsZ);
        }
    }

    private IEnumerator SpawnIslands_Co()
    {
        var (cellsX, cellsZ, origin) = GridSetup();
        var centers = new List<Vector2Int>();
        int budget = 0;

        for (int i = 0; i < islandCount; i++)
        {
            if (!TryPickIslandCenter(cellsX, cellsZ, centers, islandCenterSpacing, out var center))
                break;

            centers.Add(center);
            foreach (var _ in RandomWalkIslandEnum(origin, center, cellsX, cellsZ))
            {
                if (spawnAsync && ++budget >= tilesPerFrame) { budget = 0; yield return null; }
            }
        }
    }

    private void RandomWalkIsland(Vector3 origin, Vector2Int center, int cellsX, int cellsZ)
    {
        int steps = rng.Next(tilesPerIsland.x, tilesPerIsland.y + 1);
        Vector2Int pos = center;

        for (int s = 0; s < steps; s++)
        {
            SpawnOneTileAtCell(origin, pos);
            Step(ref pos, cellsX, cellsZ);
        }
    }

    private IEnumerable<object> RandomWalkIslandEnum(Vector3 origin, Vector2Int center, int cellsX, int cellsZ)
    {
        int steps = rng.Next(tilesPerIsland.x, tilesPerIsland.y + 1);
        Vector2Int pos = center;

        for (int s = 0; s < steps; s++)
        {
            SpawnOneTileAtCell(origin, pos);
            yield return null;
            Step(ref pos, cellsX, cellsZ);
        }
    }

    private void Step(ref Vector2Int pos, int cellsX, int cellsZ)
    {
        Vector2Int dir = RandomCardinal();
        if (rng.NextDouble() < 0.25) dir += RandomCardinal();
        pos = new Vector2Int(
            Mathf.Clamp(pos.x + Mathf.Clamp(dir.x, -1, 1), 0, cellsX - 1),
            Mathf.Clamp(pos.y + Mathf.Clamp(dir.y, -1, 1), 0, cellsZ - 1)
        );
    }

    private void SpawnOneTileAtCell(Vector3 origin, Vector2Int cell)
    {
        var prefab = islandTiles[rng.Next(islandTiles.Length)];
        if (!prefab) return;

        Vector3 basePos = origin + new Vector3(cell.x * tileSize, 0f, cell.y * tileSize);

        float jx = ((float)rng.NextDouble() * 2f - 1f) * overlapJitter;
        float jz = ((float)rng.NextDouble() * 2f - 1f) * overlapJitter;

        float yaw = 0f;
        if (randomYRotation)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(360f / yRotationStep));
            yaw = Mathf.Round((float)rng.NextDouble() * (steps - 1)) * yRotationStep;
        }

        var go = Instantiate(prefab, basePos + new Vector3(jx, 0f, jz), Quaternion.Euler(0f, yaw, 0f));
        var no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError($"[KenneyNetworkIslandSpawner] '{prefab.name}' missing NetworkObject.");
            Destroy(go);
            return;
        }

        no.Spawn(true);

        // decor pass
        if (decorPrefabs != null && decorPrefabs.Length > 0 && (float)rng.NextDouble() < decorChance)
        {
            var decor = decorPrefabs[rng.Next(decorPrefabs.Length)];
            if (decor)
            {
                float dx = ((float)rng.NextDouble() * 2f - 1f) * decorOffsetJitter.x;
                float dz = ((float)rng.NextDouble() * 2f - 1f) * decorOffsetJitter.y;
                var deco = Instantiate(decor, go.transform.position + new Vector3(dx, 0f, dz), Quaternion.Euler(0f, yaw, 0f));
                var decoNO = deco.GetComponent<NetworkObject>();
                if (decoNO) decoNO.Spawn(true); else Destroy(deco);
            }
        }
    }

    // ---------- helpers ----------

    private bool TryPickIslandCenter(int cellsX, int cellsZ, List<Vector2Int> centers, int minSpacing, out Vector2Int center)
    {
        for (int attempts = 0; attempts < 64; attempts++)
        {
            int x = rng.Next(2, Mathf.Max(3, cellsX - 2));
            int z = rng.Next(2, Mathf.Max(3, cellsZ - 2));
            var c = new Vector2Int(x, z);

            bool farEnough = true;
            foreach (var o in centers)
                if (Mathf.Abs(o.x - c.x) < minSpacing && Mathf.Abs(o.y - c.y) < minSpacing) { farEnough = false; break; }

            if (farEnough) { center = c; return true; }
        }
        center = default;
        return false;
    }

    private Vector2Int RandomCardinal()
    {
        switch (rng.Next(4))
        {
            case 0: return new Vector2Int(1, 0);
            case 1: return new Vector2Int(-1, 0);
            case 2: return new Vector2Int(0, 1);
            default: return new Vector2Int(0, -1);
        }
    }
}