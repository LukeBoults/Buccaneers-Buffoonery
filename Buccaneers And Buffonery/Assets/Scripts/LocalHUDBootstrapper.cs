using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.EventSystems;

public class LocalHUDBootstrapper : MonoBehaviour
{
    [Header("Assign a NON-networked HUD prefab (Canvas + ShipHUD + TMP)")]
    public GameObject hudPrefab;

    [Header("Debug")]
    public bool verboseLogs = true;

    private GameObject hudInstance;
    private ShipHUD shipHUD;

    // Keep delegates so we can unsubscribe correctly
    private System.Action<ulong> _onClientConnected;

    private void Awake()
    {
        EnsureEventSystem();
    }

    private void OnEnable()
    {
        // Always try to start the routine (works offline too)
        StartCoroutine(BootstrapRoutine());

        if (NetworkManager.Singleton != null)
        {
            _onClientConnected = _ => StartCoroutine(BootstrapRoutine());
            NetworkManager.Singleton.OnClientConnectedCallback += _onClientConnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null && _onClientConnected != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= _onClientConnected;
    }

    private IEnumerator BootstrapRoutine()
    {
        // 0) Basic checks
        if (hudPrefab == null)
        {
            Debug.LogError("[LocalHUDBootstrapper] hudPrefab not assigned.");
            yield break;
        }

        // 1) Spawn HUD locally if needed
        if (hudInstance == null)
        {
            hudInstance = Instantiate(hudPrefab);
            DontDestroyOnLoad(hudInstance); // optional
            shipHUD = hudInstance.GetComponentInChildren<ShipHUD>(true);
            if (shipHUD == null)
            {
                Debug.LogError("[LocalHUDBootstrapper] ShipHUD component not found on prefab.");
                yield break;
            }
            if (verboseLogs) Debug.Log("[LocalHUDBootstrapper] HUD instantiated.");
        }

        // 2) Wait until networking is running OR we’re in offline play mode
        float t0 = Time.realtimeSinceStartup;
        while (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (Time.realtimeSinceStartup - t0 > 10f) break; // don’t hang forever if you’re offline
            yield return null;
        }

        // 3) Poll for a locally-owned ShipController (no timeout; keeps trying)
        if (verboseLogs) Debug.Log("[LocalHUDBootstrapper] Looking for locally-owned ShipController...");
        ShipController localShip = null;
        while (localShip == null)
        {
            localShip = FindLocalOwnedShip();
            if (localShip != null) break;
            yield return new WaitForSeconds(0.25f);
        }

        // 4) Bind HUD
        shipHUD.BindToShip(localShip);
        if (verboseLogs) Debug.Log("[LocalHUDBootstrapper] HUD bound to local ship.");
    }

    private ShipController FindLocalOwnedShip()
    {
        // If networking isn’t running (offline test), just grab any ShipController in scene.
        if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer))
        {
            var any = FindObjectOfType<ShipController>();
            if (any != null && verboseLogs) Debug.Log("[LocalHUDBootstrapper] Offline: bound to first ShipController found.");
            return any;
        }

        // Networked: find something owned by this client
        ulong localId = NetworkManager.Singleton.LocalClientId;
        var owned = NetworkManager.Singleton.SpawnManager.GetClientOwnedObjects(localId);
        foreach (var no in owned)
        {
            if (no != null && no.TryGetComponent(out ShipController sc))
                return sc;
        }

        // Also check children of owned objects (if ShipController is on a child)
        foreach (var no in owned)
        {
            if (no == null) continue;
            var sc = no.GetComponentInChildren<ShipController>(true);
            if (sc != null) return sc;
        }
        return null;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}