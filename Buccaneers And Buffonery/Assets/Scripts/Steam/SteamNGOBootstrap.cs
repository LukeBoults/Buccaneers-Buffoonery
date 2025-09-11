using UnityEngine;
using Unity.Netcode;
using Netcode.Transports;                // SteamNetworkingSocketsTransport (community package)
using Steamworks;                        // Steamworks.NET

[DefaultExecutionOrder(-1000)]
public class SteamNGOBootstrap : MonoBehaviour
{
    [Header("Player Prefab")]
    public GameObject playerPrefab;      // root must have NetworkObject

    [Header("Steam Init")]
    public bool initSteamHere = true;    // set false if you init Steam elsewhere
    public uint appIdForTesting = 480;   // Spacewar for local testing; replace with your appid later

    private NetworkManager nm;
    private SteamNetworkingSocketsTransport transport;
    private bool steamReady;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);

        // --- Steamworks.NET init ---
        if (initSteamHere)
        {
            try { steamReady = SteamAPI.Init(); }
            catch (System.Exception e) { Debug.LogError("[Steam] Init failed: " + e); steamReady = false; }
        }
        else
        {
            steamReady = true; // assume some other bootstrap did it
        }

        if (steamReady)
        {
            Debug.Log($"[Steam] Logged in as: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})");
        }
        else
        {
            Debug.LogWarning("[Steam] Not initialized. Ensure Steam client is running and steam_appid.txt exists.");
        }

        // --- Ensure NetworkManager ---
        nm = NetworkManager.Singleton ?? FindObjectOfType<NetworkManager>();
        if (!nm)
        {
            var go = new GameObject("NetworkManager (Steam)");
            DontDestroyOnLoad(go);
            nm = go.AddComponent<NetworkManager>();
        }

        // --- Ensure Steam transport on same GO ---
        transport = nm.GetComponent<SteamNetworkingSocketsTransport>();
        if (!transport) transport = nm.gameObject.AddComponent<SteamNetworkingSocketsTransport>();

        // --- Wire transport and config ---
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.ConnectionApproval = false;
        nm.NetworkConfig.EnableSceneManagement = false;
        nm.NetworkConfig.TickRate = 60;
        if (playerPrefab) nm.NetworkConfig.PlayerPrefab = playerPrefab;
    }

    void Update()
    {
        if (steamReady) SteamAPI.RunCallbacks();
    }

    public bool StartHost()
    {
        var ok = nm.StartHost();
        Debug.Log(ok
            ? $"[SteamNGO] Hosting as {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})"
            : "[SteamNGO] StartHost failed");
        return ok;
    }

    public bool StartClient(ulong hostSteamId)
    {
        if (!transport)
        {
            Debug.LogError("[SteamNGO] Steam transport missing on NetworkManager.");
            return false;
        }
        transport.ConnectToSteamID = hostSteamId;   // <-- key difference vs UTP
        var ok = nm.StartClient();
        Debug.Log(ok ? $"[SteamNGO] Joining host {hostSteamId}" : "[SteamNGO] StartClient failed");
        return ok;
    }

    void OnDestroy()
    {
        if (initSteamHere && steamReady)
        {
            SteamAPI.Shutdown();
            steamReady = false;
        }
    }
}