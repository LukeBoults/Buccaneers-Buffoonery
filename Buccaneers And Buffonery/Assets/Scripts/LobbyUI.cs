using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class LobbyUI : MonoBehaviour
{
    [Header("Manager")]
    public LobbyRelayManager manager;

    [Header("Main Menu Panel")]
    public GameObject panelMainMenu;
    public Button createButton;
    public TMP_InputField roomCodeInput;
    public Button joinButton;
    public TMP_Text statusText;

    [Header("Lobby Panel")]
    public GameObject panelLobby;
    public TMP_Text lobbyCodeLabel;
    public Button copyCodeButton;
    public Button leaveButton;
    public Button startButton;         // Host/Owner-only
    public TMP_Text lobbyStatusText;

    [Header("Players")]
    public Transform playerListContent;   // ScrollView/Viewport/Content
    public TMP_Text playerEntryPrefab;    // simple TMP_Text prefab as a row

    // Internal flags
    private bool _running;
    private bool _iAmLobbyOwner;

    private void Start()
    {
        // Panels
        panelMainMenu.SetActive(true);
        panelLobby.SetActive(false);

        // Buttons
        createButton.onClick.AddListener(OnCreateClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        leaveButton.onClick.AddListener(OnLeaveClicked);
        copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        startButton.onClick.AddListener(OnStartClicked);

        // Subscribe to manager events to hide the overlay in a single scene
        if (manager != null)
        {
            manager.OnLocalHostStarted += HideAllMenus;
            manager.OnLocalClientConnected += HideAllMenus;
            manager.OnLocalDisconnected += ShowMainMenu;   // <- show menu when we leave/shutdown
        }

        _running = true;
        _ = PlayerListPollAsync();
    }

    private void OnDestroy()
    {
        _running = false;
        if (manager != null)
        {
            manager.OnLocalHostStarted -= HideAllMenus;
            manager.OnLocalClientConnected -= HideAllMenus;
            manager.OnLocalDisconnected -= ShowMainMenu;
        }
    }

    private void OnDisable() { _running = false; }

    // ------------------- Button Handlers -------------------

    private void OnCreateClicked()
    {
        statusText.text = "Creating room...";
        manager.CreateRoom();

        panelMainMenu.SetActive(false);
        panelLobby.SetActive(true);

        _iAmLobbyOwner = true; // we just created it
        RefreshStartButtonVisibility();

        lobbyStatusText.text = "Room created. Share the code.";
    }

    private async void OnJoinClicked()
    {
        var code = (roomCodeInput ? roomCodeInput.text : "").Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            statusText.text = "Enter a room code.";
            return;
        }

        joinButton.interactable = false;
        statusText.text = $"Joining {code}...";
        manager.JoinRoomByCode(code);

        panelMainMenu.SetActive(false);
        panelLobby.SetActive(true);
        lobbyStatusText.text = "Joining room…";

        await Task.Delay(2000); // small UI debounce
        joinButton.interactable = true;
    }

    private void OnLeaveClicked()
    {
        manager.LeaveLobby();

        panelLobby.SetActive(false);
        panelMainMenu.SetActive(true);

        statusText.text = "Left lobby.";
        _iAmLobbyOwner = false;
        RefreshStartButtonVisibility();
        ClearPlayerList();
    }

    private void OnCopyCodeClicked()
    {
        GUIUtility.systemCopyBuffer = manager.currentLobbyCode ?? "";
        lobbyStatusText.text = "Copied room code!";
    }

    private void OnStartClicked()
    {
        // Allow if we are already the net host OR we are the lobby owner
        bool isNetHost = NetworkManager.Singleton && NetworkManager.Singleton.IsHost;

        if (!(isNetHost || _iAmLobbyOwner))
        {
            lobbyStatusText.text = "Only the host/room creator can start.";
            return;
        }

        lobbyStatusText.text = "Starting game…";
        manager.StartGameAsHost(""); // single-scene: scene name ignored
    }

    // ------------------- Async UI Loop -------------------

    private async Task PlayerListPollAsync()
    {
        while (_running)
        {
            // Update lobby code label
            lobbyCodeLabel.text = string.IsNullOrEmpty(manager.currentLobbyCode)
                ? "Room Code: —"
                : $"Room Code: {manager.currentLobbyCode}";

            // If in lobby and we have a lobby id, refresh player list + flags
            if (panelLobby.activeInHierarchy && !string.IsNullOrEmpty(manager.currentLobbyId))
            {
                try
                {
                    Lobby lobby = await LobbyService.Instance.GetLobbyAsync(manager.currentLobbyId);
                    UpdatePlayers(lobby);

                    // Determine if WE are the lobby owner (creator)
                    string myPlayerId = AuthenticationService.Instance.PlayerId;
                    _iAmLobbyOwner = (lobby != null && lobby.HostId == myPlayerId);

                    // Show Start if owner OR network host (after Start is pressed)
                    RefreshStartButtonVisibility();
                }
                catch
                {
                    // ignore transient errors
                }
            }

            await Task.Delay(1500);
        }
    }

    private void RefreshStartButtonVisibility()
    {
        if (!startButton) return;
        bool isNetHost = NetworkManager.Singleton && NetworkManager.Singleton.IsHost;
        startButton.gameObject.SetActive(_iAmLobbyOwner || isNetHost);
    }

    // ------------------- UI Helpers -------------------

    private void UpdatePlayers(Lobby lobby)
    {
        if (lobby == null) return;

        ClearPlayerList();

        if (lobby.Players != null)
        {
            foreach (var p in lobby.Players)
            {
                var row = Instantiate(playerEntryPrefab, playerListContent);
                if (row != null)
                {
                    string me = AuthenticationService.Instance.PlayerId;
                    bool isYou = (p.Id == me);
                    string shortId = !string.IsNullOrEmpty(p.Id) && p.Id.Length >= 6 ? p.Id.Substring(0, 6) : p.Id;
                    row.text = isYou ? $"• You ({shortId})" : $"• Player {shortId}";
                }
            }

            if (lobbyStatusText)
                lobbyStatusText.text = $"Players: {lobby.Players.Count}/{lobby.MaxPlayers}";
        }
    }

    private void ClearPlayerList()
    {
        if (!playerListContent) return;
        for (int i = playerListContent.childCount - 1; i >= 0; i--)
            Destroy(playerListContent.GetChild(i).gameObject);

        if (lobbyStatusText) lobbyStatusText.text = "";
    }

    // Single-scene helpers to hide/show overlays based on manager events
    private void HideAllMenus()
    {
        panelMainMenu.SetActive(false);
        panelLobby.SetActive(false);
    }

    private void ShowMainMenu()
    {
        panelMainMenu.SetActive(true);
        panelLobby.SetActive(false);
        // Optional: clear fields
        lobbyCodeLabel.text = "Room Code: —";
        lobbyStatusText.text = "";
    }
}
