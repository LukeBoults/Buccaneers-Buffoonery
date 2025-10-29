using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using Steamworks;
using Netcode.Transports; // SteamNetworkingSocketsTransport

public class Menu : MonoBehaviour
{
    [Header("UI (TMP)")]
    public TMP_InputField joinCodeInput;     // enter 6-char code, LobbyID, or "steam:STEAMID64"
    public Button hostButton;
    public Button joinByCodeButton;
    public Button shutdownButton;
    public Button copyCodeButton;            // copies SHORT CODE
    public TextMeshProUGUI statusText;       // status line
    public TextMeshProUGUI hostInfoText;     // shows code/lobby info
    public GameObject menuPanel;

    [Header("Steam Init")]
    public bool initSteamHere = true;
    public uint appIdForTesting = 480;

    // Steam state
    bool steamReady;
    CSteamID currentLobby = CSteamID.Nil;
    bool isOwner;
    string currentShortCode = "";   // <- 6-char human code
    string pendingCodeSearch = "";  // <- when joining by short code

    // Transport + callbacks
    SteamNetworkingSocketsTransport Transport =>
        NetworkManager.Singleton ? NetworkManager.Singleton.GetComponent<SteamNetworkingSocketsTransport>() : null;

    Callback<LobbyCreated_t> cbLobbyCreated;
    Callback<LobbyEnter_t> cbLobbyEnter;
    Callback<LobbyDataUpdate_t> cbLobbyDataUpdate;
    Callback<GameLobbyJoinRequested_t> cbLobbyJoinRequested;
    Callback<LobbyMatchList_t> cbLobbyMatchList;

    void Awake()
    {
        if (initSteamHere)
        {
            try { steamReady = SteamAPI.Init(); }
            catch { steamReady = false; }
        }
        else steamReady = true;

        cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        cbLobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        cbLobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        cbLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyInviteJoinRequested);
        cbLobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);

        if (hostButton) hostButton.onClick.AddListener(OnHost);
        if (joinByCodeButton) joinByCodeButton.onClick.AddListener(OnJoinByCode);
        if (shutdownButton) shutdownButton.onClick.AddListener(OnShutdown);
        if (copyCodeButton) copyCodeButton.onClick.AddListener(CopyCode);

        InvokeRepeating(nameof(RefreshUI), 0f, 0.25f);
        RefreshUI();
    }

    void OnDestroy()
    {
        if (hostButton) hostButton.onClick.RemoveListener(OnHost);
        if (joinByCodeButton) joinByCodeButton.onClick.RemoveListener(OnJoinByCode);
        if (shutdownButton) shutdownButton.onClick.RemoveListener(OnShutdown);
        if (copyCodeButton) copyCodeButton.onClick.RemoveListener(CopyCode);
        CancelInvoke(nameof(RefreshUI));

        if (initSteamHere && steamReady)
        {
            SteamAPI.Shutdown();
            steamReady = false;
        }
    }

    void Update()
    {
        if (steamReady) SteamAPI.RunCallbacks();
    }

    // ===== Buttons =====

    void OnHost()
    {
        if (!CheckSteamAndTransport()) return;

        // Public so code search can find it. (Use FriendsOnly + invites if you prefer.)
        const ELobbyType type = ELobbyType.k_ELobbyTypePublic;
        int maxPlayers = 8;

        LogStatus("Creating lobby…");
        SteamMatchmaking.CreateLobby(type, maxPlayers);
    }

    void OnJoinByCode()
    {
        if (!CheckSteamAndTransport()) return;

        var raw = joinCodeInput ? joinCodeInput.text.Trim() : "";
        if (string.IsNullOrEmpty(raw)) { LogStatus("Enter code / LobbyID / steam:ID"); return; }

        // Direct by SteamID: "steam:7656119..."
        if (raw.StartsWith("steam:", System.StringComparison.OrdinalIgnoreCase))
        {
            var idTxt = raw.Substring(6).Trim();
            if (ulong.TryParse(idTxt, out var hostId))
            {
                StartClientNGO(hostId);
                return;
            }
            LogStatus("Invalid SteamID64 after 'steam:'.");
            return;
        }

        // Numeric LobbyID
        if (ulong.TryParse(raw, out var lid))
        {
            SteamMatchmaking.JoinLobby(new CSteamID(lid));
            return;
        }

        // Short human code (e.g., ABC123)
        if (!IsLikelyShortCode(raw))
        {
            LogStatus("Code must be 4–8 letters/numbers.");
            return;
        }

        // Search by lobby data: code == raw
        pendingCodeSearch = raw.ToUpperInvariant();
        SteamMatchmaking.AddRequestLobbyListStringFilter("code", pendingCodeSearch, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.RequestLobbyList(); // -> OnLobbyMatchList
        LogStatus($"Searching for code {pendingCodeSearch}…");
    }

    void OnShutdown()
    {
        if (currentLobby.IsValid()) SteamMatchmaking.LeaveLobby(currentLobby);
        currentLobby = CSteamID.Nil;
        currentShortCode = "";

        if (NetworkManager.Singleton && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            NetworkManager.Singleton.Shutdown();

        isOwner = false;
        LogStatus("Shutdown complete.");
        RefreshUI();
    }

    void CopyCode()
    {
        if (!currentLobby.IsValid())
        {
            LogStatus("No lobby yet.");
            return;
        }
        var toCopy = string.IsNullOrEmpty(currentShortCode) ? currentLobby.m_SteamID.ToString() : currentShortCode;
        GUIUtility.systemCopyBuffer = toCopy;
        LogStatus($"Copied: {toCopy}");
    }

    // ===== Steam callbacks =====

    void OnLobbyCreated(LobbyCreated_t e)
    {
        if (e.m_eResult != EResult.k_EResultOK) { LogStatus("Create failed: " + e.m_eResult); return; }

        currentLobby = new CSteamID(e.m_ulSteamIDLobby);
        isOwner = true;

        // Generate & set a 6-char code (A-Z, 2-9 without 0/1/I/O/L)
        currentShortCode = MakeShortCode(6);
        SteamMatchmaking.SetLobbyData(currentLobby, "name", $"{SteamFriends.GetPersonaName()}'s Lobby");
        SteamMatchmaking.SetLobbyData(currentLobby, "code", currentShortCode);
        SteamMatchmaking.SetLobbyData(currentLobby, "host_steamid", SteamUser.GetSteamID().m_SteamID.ToString());
        SteamMatchmaking.SetLobbyData(currentLobby, "max", "8");
        SteamMatchmaking.SetLobbyJoinable(currentLobby, true);

        LogStatus($"Lobby created. Code: {currentShortCode}  (ID {currentLobby.m_SteamID})");
    }

    void OnLobbyEnter(LobbyEnter_t e)
    {
        currentLobby = new CSteamID(e.m_ulSteamIDLobby);
        isOwner = SteamMatchmaking.GetLobbyOwner(currentLobby) == SteamUser.GetSteamID();

        if (isOwner)
        {
            StartHostNGO();
        }
        else
        {
            var hostStr = SteamMatchmaking.GetLobbyData(currentLobby, "host_steamid");
            if (ulong.TryParse(hostStr, out var hostId)) StartClientNGO(hostId);
            else LogStatus("Lobby missing host_steamid.");
        }
        RefreshUI();
    }

    void OnLobbyDataUpdate(LobbyDataUpdate_t e)
    {
        if (!currentLobby.IsValid() || e.m_ulSteamIDLobby != currentLobby.m_SteamID) return;

        var name = SteamMatchmaking.GetLobbyData(currentLobby, "name");
        var mem = SteamMatchmaking.GetNumLobbyMembers(currentLobby);
        var max = SteamMatchmaking.GetLobbyData(currentLobby, "max");
        currentShortCode = SteamMatchmaking.GetLobbyData(currentLobby, "code");

        if (hostInfoText)
        {
            UpdateLobbyInfoPanel();
        }
    }

    // Result of RequestLobbyList (used when joining by short code)
    void OnLobbyMatchList(LobbyMatchList_t e)
    {
        if (string.IsNullOrEmpty(pendingCodeSearch)) return;

        if (e.m_nLobbiesMatching <= 0)
        {
            LogStatus($"No lobby found for code {pendingCodeSearch}.");
            pendingCodeSearch = "";
            return;
        }

        // Join the first matching lobby
        var lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
        LogStatus($"Found code {pendingCodeSearch}. Joining lobby {lobbyId.m_SteamID}…");
        pendingCodeSearch = "";
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    void OnLobbyInviteJoinRequested(GameLobbyJoinRequested_t e)
    {
        SteamMatchmaking.JoinLobby(e.m_steamIDLobby);
    }

    // ===== NGO helpers =====

    void StartHostNGO()
    {
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;
        NetworkManager.Singleton.StartHost();
        LogStatus($"Hosting as {SteamFriends.GetPersonaName()}");
    }

    void StartClientNGO(ulong hostSteamId)
    {
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;
        if (Transport == null) { LogStatus("Steam transport missing."); return; }
        Transport.ConnectToSteamID = hostSteamId;
        NetworkManager.Singleton.StartClient();
        LogStatus($"Connecting to {hostSteamId}…");
    }

    // ===== Utils/UI =====

    void UpdateLobbyInfoPanel()
    {
        if (!hostInfoText || !currentLobby.IsValid()) return;

        string code = SteamMatchmaking.GetLobbyData(currentLobby, "code");
        if (string.IsNullOrEmpty(code))
            code = currentShortCode;

        hostInfoText.text = $"Room Code: {code}";
    }
    void RefreshUI()
    {
        var nm = NetworkManager.Singleton;
        bool running = nm && (nm.IsServer || nm.IsClient);

        if (joinByCodeButton) joinByCodeButton.interactable = !running;
        if (hostButton) hostButton.interactable = !running;
        if (shutdownButton) shutdownButton.interactable = currentLobby.IsValid() || running;
        if (joinCodeInput) joinCodeInput.interactable = !running;

        if (!statusText) return;

        if (!steamReady)
        {
            statusText.text = $"Steam not ready (AppID {appIdForTesting})";
            if (hostInfoText) hostInfoText.text = "";
            return;
        }

        if (!nm) { statusText.text = "Status: Idle (no NetworkManager)"; return; }

        if (nm.IsServer && nm.IsClient)
        {
            statusText.text = $"Status: Host | Clients: {nm.ConnectedClientsList.Count - 1}";
        }
        else if (nm.IsClient)
        {
            statusText.text = $"Status: Client | Connected: {nm.IsConnectedClient}";
        }
        else
        {
            statusText.text = "Status: Idle";
        }

        if (currentLobby.IsValid())
            UpdateLobbyInfoPanel();
    }

    bool CheckSteamAndTransport()
    {
        if (!steamReady) { LogStatus("Steam not initialized or Steam not running."); return false; }
        if (!NetworkManager.Singleton) { LogStatus("No NetworkManager in scene."); return false; }
        if (!Transport) { LogStatus("Steam transport not set on NetworkManager."); return false; }
        return true;
    }

    void LogStatus(string s)
    {
        if (statusText) statusText.text = s;
        Debug.Log("[SteamMenu] " + s);
    }

    // 6-char code, easy to read (no 0/1/I/O/L)
    static readonly char[] CODE_ALPH = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    string MakeShortCode(int len)
    {
        var rng = new System.Security.Cryptography.RNGCryptoServiceProvider();
        var bytes = new byte[len];
        rng.GetBytes(bytes);
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++) sb.Append(CODE_ALPH[bytes[i] % CODE_ALPH.Length]);
        return sb.ToString();
    }

    bool IsLikelyShortCode(string s)
    {
        if (s.Length < 4 || s.Length > 8) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsLetterOrDigit(c)) return false;
        }
        return true;
    }

    public void ShowMenu()
    {
        menuPanel.SetActive(true);
    }

    public void HideMenu()
    {
        menuPanel.SetActive(false);
    }
}