using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Robust NGO spawn manager:
/// - Lives in DDOL and late-binds to NetworkManager (Steam lobby friendly).
/// - Uses ConnectionApproval to set spawn position/rotation BEFORE Player prefab is created.
/// - Falls back to post-spawn placement for already-connected host.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [Header("Map Size (world units)")]
    [Tooltip("Set to 100 x 100 for your world.")]
    public float mapWidth = 100f;
    public float mapDepth = 100f;

    [Header("Placement")]
    [Tooltip("Keep players this far from edges.")]
    public float edgePadding = 5f;
    [Tooltip("Minimum spacing between spawns.")]
    public float minSeparation = 8f;
    [Tooltip("If true, map is centered around (0,0). If false, bottom-left is (0,0).")]
    public bool originAtCentre = true;
    [Tooltip("Optional: face this target (e.g., an empty at world origin).")]
    public Transform faceTarget;

    // Track used spawns to keep distance
    private readonly List<Vector3> usedSpawns = new();
    private NetworkManager nm;
    private bool wired;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        StartCoroutine(BindWhenNMExists());
    }

    void OnDisable()
    {
        Unwire();
    }

    private IEnumerator BindWhenNMExists()
    {
        while (NetworkManager.Singleton == null) yield return null;
        nm = NetworkManager.Singleton;
        Wire();
    }

    private void Wire()
    {
        if (wired || nm == null) return;

        // Enable connection approval
        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback += OnConnectionApproval;

        nm.OnServerStarted += OnServerStarted;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        wired = true;

        // If host already running (e.g., after lobby enter), do a pass
        if (nm.IsServer)
            OnServerStarted();
    }

    private void Unwire()
    {
        if (!wired || nm == null) return;
        nm.ConnectionApprovalCallback -= OnConnectionApproval;
        nm.OnServerStarted -= OnServerStarted;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        wired = false;
    }

    // ---------- Connection Approval (fires BEFORE PlayerObject is spawned) ----------
    private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                                      NetworkManager.ConnectionApprovalResponse response)
    {
        // Decide spawn now
        Vector3 spawn = FindSpacedSpawn();
        Quaternion rot = ComputeFacing(spawn);

        // Standard approval pathway
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = spawn;
        response.Rotation = rot;

        // Reserve the spot so the next player gets spaced
        usedSpawns.Add(spawn);
    }

    // ---------- Fallbacks / housekeeping ----------
    private void OnServerStarted()
    {
        if (!nm.IsServer) return;
        usedSpawns.Clear();

        // If host’s player already exists (rare but possible), ensure it’s validly placed.
        foreach (var kvp in nm.ConnectedClients)
            StartCoroutine(EnsurePlacedIfAtOrigin(kvp.Key));
    }

    private void OnClientConnected(ulong clientId)
    {
        // Usually not needed because ConnectionApproval handled it.
        // Keep as safety for edge races or if approval was toggled off accidentally.
        if (!nm.IsServer) return;
        StartCoroutine(EnsurePlacedIfAtOrigin(clientId));
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Optional: track and free their reserved spot if you keep a dictionary<clientId, spawn>
        // (Not critical for 8 players on a 100x100.)
    }

    private IEnumerator EnsurePlacedIfAtOrigin(ulong clientId)
    {
        // Wait for PlayerObject
        NetworkClient client;
        while (!nm.ConnectedClients.TryGetValue(clientId, out client) || client.PlayerObject == null)
            yield return null;

        Transform t = client.PlayerObject.transform;

        // If still at prefab origin (0,0,0) or near it, move to a valid spawn
        if ((t.position - Vector3.zero).sqrMagnitude < 0.01f)
        {
            Vector3 spawn = FindSpacedSpawn();
            Quaternion rot = ComputeFacing(spawn);
            t.SetPositionAndRotation(spawn, rot);
            usedSpawns.Add(spawn);
        }
    }

    // ---------- Spawn math ----------
    private Vector3 FindSpacedSpawn()
    {
        const int MAX_TRIES = 64;
        Vector3 candidate = GetRandomPointWithinBounds();
        int tries = 0;
        while (tries++ < MAX_TRIES && !IsFarEnough(candidate))
            candidate = GetRandomPointWithinBounds();
        return candidate;
    }

    private Vector3 GetRandomPointWithinBounds()
    {
        float halfW = mapWidth * 0.5f;
        float halfD = mapDepth * 0.5f;

        float minX, maxX, minZ, maxZ;
        if (originAtCentre)
        {
            minX = -halfW + edgePadding;
            maxX = halfW - edgePadding;
            minZ = -halfD + edgePadding;
            maxZ = halfD - edgePadding;
        }
        else
        {
            minX = 0f + edgePadding;
            maxX = mapWidth - edgePadding;
            minZ = 0f + edgePadding;
            maxZ = mapDepth - edgePadding;
        }

        float x = Random.Range(minX, maxX);
        float z = Random.Range(minZ, maxZ);
        return new Vector3(x, 0f, z);
    }

    private bool IsFarEnough(Vector3 pos)
    {
        float minSq = minSeparation * minSeparation;
        for (int i = 0; i < usedSpawns.Count; i++)
            if ((pos - usedSpawns[i]).sqrMagnitude < minSq)
                return false;
        return true;
    }

    private Quaternion ComputeFacing(Vector3 spawn)
    {
        if (faceTarget == null) return Quaternion.identity;
        Vector3 flatTarget = new Vector3(faceTarget.position.x, spawn.y, faceTarget.position.z);
        Vector3 dir = (flatTarget - spawn);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
