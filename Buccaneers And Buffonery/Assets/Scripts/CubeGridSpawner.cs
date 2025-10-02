using UnityEngine;

public class CubeGridSpawner : MonoBehaviour
{
    [Header("Cube Prefab (10x10x10)")]
    public GameObject cubePrefab;

    [Header("Grid Settings")]
    public int gridSizeX = 100;
    public int gridSizeZ = 100;
    public float cubeSize = 10f;   // size of one cube edge

    [Header("Spawn Options")]
    public bool centerGrid = true; // center the whole grid on this object

    void Start()
    {
        if (cubePrefab == null)
        {
            Debug.LogWarning("[CubeGridSpawner] No prefab assigned!");
            return;
        }

        // optional offset so grid is centered on this object
        Vector3 origin = transform.position;
        if (centerGrid)
        {
            origin -= new Vector3(gridSizeX * cubeSize, 0f, gridSizeZ * cubeSize) * 0.5f;
        }

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                Vector3 pos = origin + new Vector3(x * cubeSize, 0f, z * cubeSize);
                Instantiate(cubePrefab, pos, Quaternion.identity, transform);
            }
        }

        Debug.Log($"Spawned {gridSizeX * gridSizeZ} cubes.");
    }
}