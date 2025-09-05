using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public class LobbyRelayManager : MonoBehaviour
{
    [Header("Lobby Settings")]
    public int maxPlayers = 8;
    [Tooltip("How often to ping the lobby (host only).")]
    public float lobbyHeartbeatInterval = 15f;
    public float lobbyPollInterval = 2f;

    [Header("Debug")]
    public string currentLobbyCode;
    public string currentLobbyId;

    [Header("Player Spawning")]
    [Tooltip("Optional: override the player prefab used for SpawnAsPlayerObject. If null, uses NetworkConfig.PlayerPrefab.")]
    [SerializeField] private GameObject playerPrefabOverride;

    // ---- Map / spawn settings ----
    [Header("Spawn Area")]
    public float tileWorldSize = 10f;     // your tile is 10x10
    public int tilesX = 100;              // 100 tiles wide
    public int tilesZ = 100;              // 100 tiles tall
    public float edgeMargin = 20f;        // keep away from the map edge
    public float seaLevelY = 0f;          // water height
    public float minSeparation = 15f;     // keep players apart
    public int maxSpawnTries = 24;        // attempts to find a clear spot

    [Tooltip("Optional: layers to avoid when picking a spawn (boats, obstacles).")]
    public LayerMask spawnCollisionMask;  // optional; set to 'Default' or your boat layer

    [Tooltip("Radius used when checking Physics.CheckSphere to avoid overlaps.")]
    public float spawnClearRadius = 4f;

    // Track used spawns per session (server-side)
    private static readonly List<Vector3> s_usedSpawns = new();

    // UI events (single-scene): let UI hide/show panels when networking changes
    public Action OnLocalHostStarted;       // fired on the host process after StartHost succeeds
    public Action OnLocalClientConnected;   // fired on the client process when OUR client connects
    public Action OnLocalDisconnected;      // fired when the local process disconnects/shuts down

    private Lobby _lobby;
    private Coroutine _heartbeatCo;
    private Coroutine _pollCo;

    // Join throttling / watcher
    private bool _isJoiningLobby;
    private Coroutine _clientWatchCo;
    private bool _clientConnecting;

    private bool _gameStarting; // guard so Start isn’t pressed twice
    private bool _callbacksHooked;

    private async void Awake()
    {
        await InitUGS();
    }

    private void OnEnable()
    {
        HookNetworkCallbacks(true);
    }

    private void OnDisable()
    {
        HookNetworkCallbacks(false);
    }

    private void HookNetworkCallbacks(bool enable)
    {
        if (NetworkManager.Singleton == null) return;

        if (enable && !_callbacksHooked)
        {
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            _callbacksHooked = true;
        }
        else if (!enable && _callbacksHooked)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            _callbacksHooked = false;
        }
    }

    private void HandleServerStarted()
    {
        // Host process: notify UI and ensure host player exists (manual-safe)
        OnLocalHostStarted?.Invoke();
        MaybeSpawnPlayer(NetworkManager.Singleton.LocalClientId);
        StartCoroutine(EnsurePlayerSpawnRoutine(NetworkManager.Singleton.LocalClientId));
    }

    private void HandleClientConnected(ulong clientId)
    {
        // Client-local UI hide
        if (!NetworkManager.Singleton.IsServer &&
            clientId == NetworkManager.Singleton.LocalClientId)
        {
            OnLocalClientConnected?.Invoke();
        }

        // Server: ensure a player object exists for the connecting client (manual-safe)
        if (NetworkManager.Singleton.IsServer)
        {
            MaybeSpawnPlayer(clientId);
            Debug.Log($"[Spawn] server picking spawn for client {clientId}. used={s_usedSpawns.Count}");
            StartCoroutine(EnsurePlayerSpawnRoutine(clientId));
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // If WE are the one who disconnected, notify UI
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            OnLocalDisconnected?.Invoke();
        }
    }

    // ---------------- PUBLIC UI HOOKS ----------------

    // Host creates the lobby (no Relay allocation here)
    public async void CreateRoom()
    {
        s_usedSpawns.Clear();
        _clientConnecting = false; // reset session flags
        try
        {
            _lobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName: "B&B Room",
                maxPlayers: maxPlayers,
                options: new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>()
                    {
                        { "relayCode",   new DataObject(DataObject.VisibilityOptions.Member, "") },
                        { "gameStarted", new DataObject(DataObject.VisibilityOptions.Public,  "false") }
                    }
                });

            currentLobbyId = _lobby.Id;
            currentLobbyCode = _lobby.LobbyCode;
            Debug.Log($"[Lobby] Created. Code: {currentLobbyCode}");

            // Keep the lobby alive (host only)
            _heartbeatCo = StartCoroutine(LobbyHeartbeat());
            _pollCo = StartCoroutine(LobbyPoll());
        }
        catch (Exception e)
        {
            Debug.LogError($"CreateRoom failed: {e}");
        }
    }

    // Clients join by room code; they will auto-connect when the host starts
    public async void JoinRoomByCode(string lobbyCode)
    {
        if (_isJoiningLobby) return; // debounce
        _isJoiningLobby = true;

        _clientConnecting = false; // reset session flags

        try
        {
            if (string.IsNullOrWhiteSpace(lobbyCode))
                throw new ArgumentException("lobbyCode empty");

            int attempt = 0;
            while (true)
            {
                try
                {
                    _lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode.Trim().ToUpper());
                    currentLobbyId = _lobby.Id;
                    currentLobbyCode = _lobby.LobbyCode;
                    Debug.Log($"[Lobby] Joined {currentLobbyCode}");

                    if (_pollCo == null) _pollCo = StartCoroutine(LobbyPoll());
                    StartClientWatch(); // manager-side watcher to connect when host starts
                    break;
                }
                catch (LobbyServiceException e)
                {
                    // Retry on rate limit (429)
                    if (attempt < 3 && e.Message.Contains("Rate limit"))
                    {
                        int delayMs = (int)Mathf.Pow(2, attempt) * 1500; // 1500, 3000, 6000
                        Debug.LogWarning($"[Lobby] Rate limited joining. Retrying in {delayMs}ms…");
                        await Task.Delay(delayMs);
                        attempt++;
                        continue;
                    }

                    Debug.LogError($"JoinRoomByCode failed: {e}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"JoinRoomByCode failed: {ex}");
                    break;
                }
            }
        }
        finally
        {
            _isJoiningLobby = false;
        }
    }

    // Host presses Start in a single scene: allocate Relay, publish join code, start host
    public async void StartGameAsHost(string _/*ignored scene name in single-scene flow*/)
    {
        if (_lobby == null) { Debug.LogWarning("No lobby."); return; }
        s_usedSpawns.Clear();
        if (_gameStarting) { Debug.Log("[Lobby] Start ignored; already starting."); return; }
        _gameStarting = true;

        // Optional: stop background lobby loops while we’re gaming
        if (_heartbeatCo != null) { StopCoroutine(_heartbeatCo); _heartbeatCo = null; }
        if (_pollCo != null) { StopCoroutine(_pollCo); _pollCo = null; }

        try
        {
            // Fresh Relay allocation
            var alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            Debug.Log($"[Relay] JoinCode (host): {joinCode}");

            // Configure transport (HOST)
            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(
                alloc.RelayServer.IpV4,
                (ushort)alloc.RelayServer.Port,
                alloc.AllocationIdBytes,
                alloc.Key,
                alloc.ConnectionData
            );

            // Publish to lobby: set relayCode + gameStarted=true
            await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>()
                {
                    { "relayCode",   new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                    { "gameStarted", new DataObject(DataObject.VisibilityOptions.Public,  "true") }
                }
            });

            if (playerPrefabOverride != null)
            {
                NetworkManager.Singleton.NetworkConfig.PlayerPrefab = playerPrefabOverride;
            }
            // Start host
            if (!NetworkManager.Singleton.IsHost)
                NetworkManager.Singleton.StartHost();

            Debug.Log("[Lobby] Host started (single-scene).");
        }
        catch (Exception e)
        {
            _gameStarting = false; // allow retry
            Debug.LogError($"StartGameAsHost failed: {e}");
        }
    }

    private IEnumerator EnsurePlayerSpawnRoutine(ulong clientId, float timeout = 2f)
    {
        float t = 0f;
        while (t < timeout)
        {
            // If already has a player, done
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var cd) && cd.PlayerObject != null)
                yield break;

            // Try spawning
            MaybeSpawnPlayer(clientId);

            yield return null;
            t += Time.unscaledDeltaTime;
        }

        Debug.LogWarning($"[Spawn] Failed to confirm player spawn for client {clientId} within {timeout}s.");
    }

    // Clients auto-join when the host flips the flag
    public async Task ConnectClientFromLobbyDataAndStart()
    {
        if (_lobby == null) return;

        // Wait up to ~8s for the relayCode to propagate
        string relayJoinCode = "";
        for (int tries = 0; tries < 8 && string.IsNullOrEmpty(relayJoinCode); tries++)
        {
            try { _lobby = await LobbyService.Instance.GetLobbyAsync(_lobby.Id); } catch { }
            if (_lobby?.Data != null && _lobby.Data.TryGetValue("relayCode", out var d))
                relayJoinCode = d.Value;

            if (string.IsNullOrEmpty(relayJoinCode))
                await Task.Delay(800);
        }

        if (string.IsNullOrEmpty(relayJoinCode))
        {
            Debug.LogWarning("[Client] relayCode not available yet.");
            return;
        }

        try
        {
            // Join relay allocation (CLIENT)
            JoinAllocation j = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(
                j.RelayServer.IpV4,
                (ushort)j.RelayServer.Port,
                j.AllocationIdBytes,
                j.Key,
                j.ConnectionData,
                j.HostConnectionData
            );

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
                NetworkManager.Singleton.StartClient();

            Debug.Log("[Client] Started client via Relay.");
        }
        catch (Exception e)
        {
            Debug.LogError($"ConnectClientFromLobbyDataAndStart failed: {e}");
        }
    }

    public async void LeaveLobby()
    {
        try
        {
            if (!string.IsNullOrEmpty(currentLobbyId))
                await LobbyService.Instance.RemovePlayerAsync(currentLobbyId, AuthenticationService.Instance.PlayerId);
        }
        catch { /* ignore */ }

        currentLobbyId = currentLobbyCode = null;
        _lobby = null;
        s_usedSpawns.Clear();

        await ShutdownNetworkCleanAsync();
        OnLocalDisconnected?.Invoke();
    }

    private async Task ShutdownNetworkCleanAsync()
    {
        // stop our loops/watchers
        if (_heartbeatCo != null) StopCoroutine(_heartbeatCo);
        if (_pollCo != null) StopCoroutine(_pollCo);
        if (_clientWatchCo != null) StopCoroutine(_clientWatchCo);
        _heartbeatCo = _pollCo = _clientWatchCo = null;

        _clientConnecting = false;
        _isJoiningLobby = false;
        _gameStarting = false;

        // Unhook callbacks before shutdown to prevent ghost events
        HookNetworkCallbacks(false);

        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsListening))
        {
            NetworkManager.Singleton.Shutdown();
            // wait until it’s really down
            for (int i = 0; i < 40; i++) // ~4s worst case
            {
                if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsListening)
                    break;
                await Task.Delay(100);
            }
        }

        // Rehook for next session
        HookNetworkCallbacks(true);
    }

    // ---------------- INTERNALS ----------------

    private void StartClientWatch()
    {
        if (_clientWatchCo != null) StopCoroutine(_clientWatchCo);
        _clientWatchCo = StartCoroutine(WatchForGameStartAndConnect());
    }

    private IEnumerator WatchForGameStartAndConnect()
    {
        var wait = new WaitForSeconds(1f);
        while (_lobby != null && !_clientConnecting && !(NetworkManager.Singleton?.IsClient == true))
        {
            var task = LobbyService.Instance.GetLobbyAsync(_lobby.Id);
            while (!task.IsCompleted) yield return null;

            if (!task.IsFaulted && task.Result != null)
            {
                _lobby = task.Result;

                var data = _lobby.Data;
                bool started = data != null && data.ContainsKey("gameStarted") && data["gameStarted"].Value == "true";
                string relayCode = (data != null && data.ContainsKey("relayCode")) ? data["relayCode"].Value : "";

                if (started && !string.IsNullOrEmpty(relayCode))
                {
                    _clientConnecting = true;
                    _ = ConnectClientFromLobbyDataAndStart(); // fire-and-forget
                    yield break;
                }
            }
            yield return wait;
        }
    }

    private IEnumerator LobbyHeartbeat()
    {
        while (_lobby != null)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            yield return new WaitForSeconds(lobbyHeartbeatInterval);
        }
    }

    private IEnumerator LobbyPoll()
    {
        while (_lobby != null)
        {
            var t = LobbyService.Instance.GetLobbyAsync(_lobby.Id);
            while (!t.IsCompleted) yield return null;
            if (!t.IsFaulted && t.Result != null)
                _lobby = t.Result; // refresh cached lobby (players, data, etc.)
            yield return new WaitForSeconds(lobbyPollInterval);
        }
    }

    private void MaybeSpawnPlayer(ulong clientId)
    {
        // Already has a player?
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var cd) && cd.PlayerObject != null)
            return;

        GameObject prefabGO = playerPrefabOverride != null
            ? playerPrefabOverride
            : NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

        if (prefabGO == null)
        {
            Debug.LogError("[Spawn] No player prefab set (override & NetworkConfig both null).");
            return;
        }

        var go = Instantiate(prefabGO, GetSpawnPointFor(clientId), Quaternion.identity);
        var no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[Spawn] Player prefab missing NetworkObject.");
            Destroy(go);
            return;
        }

        no.SpawnAsPlayerObject(clientId);
        Debug.Log($"[Spawn] Spawned player for client {clientId}.");

        int rotSeed = unchecked(
            (currentLobbyId ?? "nolobby").GetHashCode()
            ^ (int)clientId
            ^ 0x5F3759DF
        );

        var rotRng = new System.Random(rotSeed);
        float yaw = (float)(rotRng.NextDouble() * 360.0);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    }

    private Vector3 GetSpawnPointFor(ulong clientId)
    {
        // ---- Sanity defaults (prevents 0s from inspector) ----
        int tx = tilesX > 0 ? tilesX : 100;
        int tz = tilesZ > 0 ? tilesZ : 100;
        float tw = tileWorldSize > 0f ? tileWorldSize : 10f;
        int tries = Mathf.Max(1, maxSpawnTries);
        float sep = Mathf.Max(0f, minSeparation);

        // Map half extents
        float halfX = tx * tw * 0.5f;
        float halfZ = tz * tw * 0.5f;

        // Bounds inside edge margin
        float minX = -halfX + edgeMargin;
        float maxX = halfX - edgeMargin;
        float minZ = -halfZ + edgeMargin;
        float maxZ = halfZ - edgeMargin;

        // Deterministic RNG per lobby+client
        int seed = unchecked((currentLobbyId ?? "nolobby").GetHashCode() ^ (int)clientId ^ 0x5F3759DF);
        var rng = new System.Random(seed);

        bool Accept(Vector3 c, bool doPhysics)
        {
            // separation from already-used spawns
            for (int k = 0; k < s_usedSpawns.Count; k++)
                if ((s_usedSpawns[k] - c).sqrMagnitude < sep * sep)
                    return false;

            // optional physics clearance
            if (doPhysics && spawnCollisionMask.value != 0)
                if (Physics.CheckSphere(c, spawnClearRadius, spawnCollisionMask))
                    return false;

            return true;
        }

        // Phase 1: random with physics (if mask set)
        bool doPhys = spawnCollisionMask.value != 0;
        for (int i = 0; i < tries; i++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
            float z = Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble());
            var cand = new Vector3(x, seaLevelY, z);
            if (Accept(cand, doPhys))
            {
                s_usedSpawns.Add(cand);
                Debug.Log($"[Spawn] RANDOM ok (phys={doPhys}) for {clientId} -> {cand}");
                return cand;
            }
        }

        // Phase 2: random without physics (guarantee some variety)
        for (int i = 0; i < tries; i++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
            float z = Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble());
            var cand = new Vector3(x, seaLevelY, z);
            if (Accept(cand, false))
            {
                s_usedSpawns.Add(cand);
                Debug.Log($"[Spawn] RANDOM ok (no phys) for {clientId} -> {cand}");
                return cand;
            }
        }

        // Phase 3: spiral fallback (never stacks)
        // Place each new player on an expanding golden-angle spiral around center.
        int n = s_usedSpawns.Count;                // number already picked this session
        float angle = n * 137.50776405f * Mathf.Deg2Rad;
        float radius = sep * 1.25f * (1 + 0.35f * n);  // expand a bit each slot
        var spiral = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        var center = new Vector3(Mathf.Clamp(0f, minX, maxX), seaLevelY, Mathf.Clamp(0f, minZ, maxZ));
        var fallback = center + spiral;

        // Clamp to bounds/margins
        fallback.x = Mathf.Clamp(fallback.x, minX, maxX);
        fallback.z = Mathf.Clamp(fallback.z, minZ, maxZ);

        s_usedSpawns.Add(fallback);
        Debug.LogWarning($"[Spawn] SPIRAL fallback for {clientId} -> {fallback}");
        return fallback;
    }

    private async Task InitUGS()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return;

            var options = new InitializationOptions();
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"UGS signed in. PlayerID={AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"UGS init failed: {e}");
        }
    }
}
