using System.Net;
using Unity.Netcode.Transports.UTP;
using Unity.Netcode;
using UnityEngine;

using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;

[DefaultExecutionOrder(-1000)]
public class P2PBootstrap : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject playerPrefab;

    [Header("Transport Defaults")]
    public string defaultAddress = "127.0.0.1";
    public ushort defaultPort = 7777;

    public static P2PBootstrap Instance { get; private set; }
    public UnityTransport Transport { get; private set; }
    NetworkManager nm;

    // NEW: track what we set
    public string CurrentAddress { get; private set; } = "";
    public ushort CurrentPort { get; private set; } = 0;
    public string ServerBindAddress { get; private set; } = "0.0.0.0";
    public string LastDisconnectReason { get; private set; } = "";

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureNetworkManager();
        ApplyDefaults();

        // Hook some events for diagnostics
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        nm.OnServerStarted += OnServerStarted;
        nm.OnServerStopped += OnServerStopped;
    }

    void OnDestroy()
    {
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        nm.OnServerStarted -= OnServerStarted;
        nm.OnServerStopped -= OnServerStopped;
    }

    void EnsureNetworkManager()
    {
        nm = NetworkManager.Singleton ?? FindObjectOfType<NetworkManager>();
        if (nm == null)
        {
            var go = new GameObject("NetworkManager (Auto)");
            DontDestroyOnLoad(go);
            nm = go.AddComponent<NetworkManager>();
        }

        Transport = nm.GetComponent<UnityTransport>();
        if (Transport == null) Transport = nm.gameObject.AddComponent<UnityTransport>();

        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = Transport;

        nm.NetworkConfig.ConnectionApproval = false;
        nm.NetworkConfig.EnableSceneManagement = false;
        nm.NetworkConfig.TickRate = 60;

        if (playerPrefab)
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
        else
            Debug.LogWarning("[P2P] No Player Prefab assigned on P2PBootstrap.");
    }

    void ApplyDefaults()
    {
        SetAddressAndPort(defaultAddress, defaultPort, ServerBindAddress);
    }

    // OVERLOAD keeps bindAddr explicit; for clients it’s ignored by UTP
    public void SetAddressAndPort(string address, ushort port, string bindAddr = "0.0.0.0")
    {
        if (!Transport)
        {
            Debug.LogError("[P2P] UnityTransport missing.");
            return;
        }

        CurrentAddress = address;
        CurrentPort = port;
        ServerBindAddress = bindAddr;

        try
        {
            Transport.SetConnectionData(address, port, bindAddr);
        }
        catch
        {
            // older UTP
            Transport.SetConnectionData(address, port);
        }
    }

    public bool StartHost()
    {
        if (!nm || nm.NetworkConfig.NetworkTransport == null)
        {
            Debug.LogError("[P2P] Cannot start Host: NetworkManager or Transport missing.");
            return false;
        }
        // Host should listen on all interfaces
        if (CurrentAddress != "0.0.0.0") SetAddressAndPort("0.0.0.0", CurrentPort == 0 ? defaultPort : CurrentPort, "0.0.0.0");

        LastDisconnectReason = "";
        var ok = nm.StartHost();
        Debug.Log(ok ? "[P2P] Host started." : "[P2P] StartHost failed.");
        return ok;
    }

    public bool StartClient()
    {
        if (!nm || nm.NetworkConfig.NetworkTransport == null)
        {
            Debug.LogError("[P2P] Cannot start Client: NetworkManager or Transport missing.");
            return false;
        }

        LastDisconnectReason = "";
        var ok = nm.StartClient();
        Debug.Log(ok ? "[P2P] Client starting (connecting...)." : "[P2P] StartClient failed (pre-check).");
        return ok;
    }

    public void StopAll()
    {
        if (nm && (nm.IsServer || nm.IsClient))
            nm.Shutdown();
        Debug.Log("[P2P] Network shutdown.");
    }

    // ===== Diagnostics =====
    void OnServerStarted()
    {
        Debug.Log("[P2P] Server started.");
        DumpHostInfoToConsole();
    }

    void OnServerStopped(bool _)
    {
        Debug.Log("[P2P] Server stopped.");
    }

    void OnClientConnected(ulong clientId)
    {
        if (nm.IsServer)
            Debug.Log($"[P2P] Client {clientId} connected.");
        else if (clientId == nm.LocalClientId)
            Debug.Log("[P2P] Connected to host.");
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (!nm) return;

        if (nm.IsServer)
        {
            Debug.Log($"[P2P] Client {clientId} disconnected.");
        }
        else if (clientId == nm.LocalClientId)
        {
            // NGO exposes a DisconnectReason string in newer versions; if empty, it’s likely timeout/refused.
            var reason = nm.DisconnectReason;
            LastDisconnectReason = string.IsNullOrEmpty(reason.ToString()) ? "Unknown / Timeout / Refused" : reason.ToString();
            Debug.LogWarning($"[P2P] Disconnected from host. Reason: {LastDisconnectReason}");
        }
    }

    public string[] GetLocalIPv4()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                      .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                      .Select(ip => ip.ToString())
                      .Distinct()
                      .ToArray();
        }
        catch
        {
            return new string[0];
        }
    }

    public string[] GetRoomCodes()
    {
        var ips = GetLocalIPv4();
        if (ips == null || ips.Length == 0) return Array.Empty<string>();
        var list = new System.Collections.Generic.List<string>(ips.Length);
        foreach (var ip in ips)
        {
            try { list.Add(RoomCodeUtil.MakeCodeIPv4Port(ip, CurrentPort == 0 ? defaultPort : CurrentPort)); }
            catch { /* skip invalid */ }
        }
        return list.ToArray();
    }

    public void DumpHostInfoToConsole()
    {
        var ips = GetLocalIPv4();
        if (ips.Length == 0) Debug.LogWarning("[P2P] No LAN IPv4 detected. Are you offline or using only IPv6?");
        foreach (var ip in ips) Debug.Log($"[P2P] LAN IP: {ip}:{CurrentPort}");
        Debug.Log($"[P2P] Bind: {ServerBindAddress}, Transport Addr (client connect target): {CurrentAddress}, Port: {CurrentPort}");
    }
}