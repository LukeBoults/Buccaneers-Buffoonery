using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.EventSystems;

public class AutoHUD : MonoBehaviour
{
    //Path is relative to Assets/Resources (no "Assets/Resources" prefix, no .prefab suffix)
    private const string kHudPath = "UI/ShipHUD";

    private GameObject hudInstance;
    private ShipHUD shipHUD;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("AutoHUD_Bootstrapper");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoHUD>();
    }

    private void Awake()
    {
        EnsureEventSystem();
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        // 1) Load HUD prefab from Resources/UI/ShipHUD.prefab
        var hudPrefab = Resources.Load<GameObject>(kHudPath);
        if (hudPrefab == null)
        {
            Debug.LogError($"[AutoHUD] Can't find HUD prefab at Resources/{kHudPath}. " +
                           "Make sure you have Assets/Resources/UI/ShipHUD.prefab");
            yield break;
        }

        // 2) Instantiate locally (non-networked)
        hudInstance = Instantiate(hudPrefab);
        DontDestroyOnLoad(hudInstance);
        shipHUD = hudInstance.GetComponentInChildren<ShipHUD>(true);
        if (!shipHUD)
        {
            Debug.LogError("[AutoHUD] ShipHUD component not found on the prefab or its children.");
            yield break;
        }
        Debug.Log("[AutoHUD] HUD instantiated.");

        // 3) Wait for networking (works for host or client; also okay offline)
        float t0 = Time.realtimeSinceStartup;
        while (NetworkManager.Singleton != null &&
              !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (Time.realtimeSinceStartup - t0 > 10f) break;
            yield return null;
        }

        // 4) Poll until this client owns a ShipController
        Debug.Log("[AutoHUD] Looking for locally-owned ShipController...");
        ShipController localShip = null;
        while (localShip == null)
        {
            localShip = FindLocalOwnedShip();
            if (localShip != null) break;
            yield return new WaitForSeconds(0.25f);
        }

        shipHUD.BindToShip(localShip);
        Debug.Log("[AutoHUD] HUD bound to local ship.");
    }

    private ShipController FindLocalOwnedShip()
    {
        // Offline fallback
        if (NetworkManager.Singleton == null ||
           (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer))
        {
            return FindObjectOfType<ShipController>();
        }

        // Networked: search objects owned by THIS client
        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (var no in NetworkManager.Singleton.SpawnManager.GetClientOwnedObjects(localId))
        {
            if (no && no.TryGetComponent(out ShipController sc)) return sc;
            if (no)
            {
                var scChild = no.GetComponentInChildren<ShipController>(true);
                if (scChild) return scChild;
            }
        }
        return null;
    }

    private void EnsureEventSystem()
    {
        if (!FindObjectOfType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}