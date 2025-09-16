using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Collections;
using System.Collections;
using Steamworks;

[DisallowMultipleComponent]
public class PlayerUI : NetworkBehaviour
{
    [Header("UI refs (World Space Canvas)")]
    public Transform billboardPivot;         // the transform that will be rotated to face camera (usually Canvas root)
    public Slider healthSlider;              // Min=0, Max=1 (display-only)
    public TextMeshProUGUI nameText;
    public CanvasGroup canvasGroup;          // optional, used for smooth fading. If null, script will enable/disable root.

    [Header("Damage Flash (optional)")]
    public Image damageFlashImage;           // full-rect Image on the canvas used to flash damage (set color=red, alpha=0)
    public float flashDuration = 0.45f;
    public float flashPeakAlpha = 0.85f;
    public AnimationCurve flashCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Camera / LOD")]
    public float fadeStartDistance = 12f;    // start fading out
    public float fadeEndDistance = 18f;    // fully gone / LOD off
    public bool disableWhenFar = true;   // disable GameObject when beyond fadeEndDistance

    [Header("Health")]
    public float maxHealth = 100f;

    // Networked state
    private NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // locals
    Camera localCam;
    GameObject rootGO;
    float lastHealthLocal = -1f; // track for local flash logic (non-authoritative)

    Coroutine flashCoroutine;

    void Awake()
    {
        rootGO = (billboardPivot != null) ? billboardPivot.gameObject : this.gameObject;
        if (canvasGroup == null && billboardPivot != null)
        {
            canvasGroup = billboardPivot.GetComponent<CanvasGroup>();
        }
        // set up damage flash image initial state
        if (damageFlashImage) { var c = damageFlashImage.color; c.a = 0f; damageFlashImage.color = c; }
    }

    public override void OnNetworkSpawn()
    {
        // initialize health on server
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
        }

        // Owner sets their name (Steam hook below)
        if (IsOwner)
        {
            string myName = $"Player {OwnerClientId}";

            // === Steam integration (pick one) ===
            // Option A: Steamworks.NET
            // Uncomment the following line if you're using Steamworks.NET and added STEAMWORKS_NET to Scripting Define Symbols:
            myName = SteamFriends.GetPersonaName();

            // Option B: Facepunch.Steamworks
            // Uncomment the following line if you're using Facepunch.Steamworks and added USING_FACEPUNCH_STEAMWORKS to Scripting Define Symbols:
            // myName = SteamClient.Name;

            // If you'd rather not use defines, you can call one of the above directly (but that will fail compile if the lib isn't present).
            SetNameServerRpc(myName);
        }

        // subscribe
        currentHealth.OnValueChanged += OnHealthChanged;
        playerName.OnValueChanged += OnNameChanged;

        UpdateUiImmediate();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            localCam = Camera.main;

        // ensure damage flash image alpha is zero initially
        if (damageFlashImage)
        {
            var c = damageFlashImage.color; c.a = 0f; damageFlashImage.color = c;
        }
    }

    void OnDestroy()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        playerName.OnValueChanged -= OnNameChanged;
    }

    void LateUpdate()
    {
        // run only on clients
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsClient) return;
        if (localCam == null) localCam = Camera.main;
        if (localCam == null || billboardPivot == null) return;

        // Distance-based LOD / fade
        float dist = Vector3.Distance(localCam.transform.position, billboardPivot.position);

        if (disableWhenFar)
        {
            bool shouldBeActive = dist <= fadeEndDistance;
            if (rootGO.activeSelf != shouldBeActive)
                rootGO.SetActive(shouldBeActive);

            if (!shouldBeActive) return;
        }

        if (canvasGroup != null)
        {
            float alpha = 1f;
            if (dist > fadeStartDistance)
                alpha = Mathf.Clamp01(1f - (dist - fadeStartDistance) / Mathf.Max(0.001f, (fadeEndDistance - fadeStartDistance)));
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = alpha > 0.5f;
            canvasGroup.blocksRaycasts = alpha > 0.5f;
        }

        // Billboard: face camera, avoid mirrored/backwards text
        Vector3 dirToCam = (localCam.transform.position - billboardPivot.position).normalized;
        if (dirToCam.sqrMagnitude < 1e-6f) return;

        billboardPivot.LookAt(localCam.transform.position, Vector3.up);

        Vector3 pivotForward = billboardPivot.forward;
        Vector3 camForward = localCam.transform.forward;
        if (Vector3.Dot(pivotForward, camForward) < 0f)
        {
            billboardPivot.Rotate(0f, 180f, 0f, Space.Self);
        }

        // Keep upright (ignore camera roll)
        Vector3 euler = billboardPivot.eulerAngles;
        billboardPivot.eulerAngles = new Vector3(0f, euler.y, 0f);
    }

    // ========= Networked API =========

    [ServerRpc(RequireOwnership = false)]
    public void SetNameServerRpc(string newName, ServerRpcParams rpcParams = default)
    {
        playerName.Value = new FixedString32Bytes(newName);
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float dmg, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        float prev = currentHealth.Value;
        currentHealth.Value = Mathf.Clamp(currentHealth.Value - dmg, 0f, maxHealth);
        // optionally react server-side (death, etc.)
    }

    [ServerRpc(RequireOwnership = false)]
    public void HealServerRpc(float amount, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0f, maxHealth);
    }

    // Convenience for clients to request damage/heal
    public void RequestDamage(float dmg) { if (NetworkManager.Singleton?.IsClient == true) TakeDamageServerRpc(dmg); }
    public void RequestHeal(float amt) { if (NetworkManager.Singleton?.IsClient == true) HealServerRpc(amt); }

    // ========= Callbacks / UI updates =========
    void OnHealthChanged(float oldH, float newH)
    {
        UpdateHealthUI(newH);

        // Play damage flash on local client when someone took damage to this player.
        // We run the flash locally on all clients so each player's canvas shows it.
        // Only flash if health decreased.
        if (newH < oldH && damageFlashImage != null)
        {
            if (flashCoroutine != null) StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(PlayDamageFlash());
        }
    }

    void OnNameChanged(FixedString32Bytes oldN, FixedString32Bytes newN)
    {
        UpdateNameUI(newN.ToString());
    }

    void UpdateUiImmediate()
    {
        UpdateHealthUI(currentHealth.Value);
        UpdateNameUI(playerName.Value.ToString());
    }

    void UpdateHealthUI(float val)
    {
        if (healthSlider != null)
            healthSlider.value = Mathf.Clamp01(val / Mathf.Max(1f, maxHealth));
        lastHealthLocal = val;
    }

    void UpdateNameUI(string n)
    {
        if (nameText != null)
            nameText.text = n;
    }

    // Damage flash coroutine: uses flashCurve to shape alpha over flashDuration
    IEnumerator PlayDamageFlash()
    {
        if (damageFlashImage == null) yield break;

        float t = 0f;
        var c = damageFlashImage.color;
        while (t < flashDuration)
        {
            float p = t / Mathf.Max(0.0001f, flashDuration);
            float sample = flashCurve.Evaluate(p);
            c.a = Mathf.Clamp01(sample * flashPeakAlpha);
            damageFlashImage.color = c;
            t += Time.deltaTime;
            yield return null;
        }
        c.a = 0f;
        damageFlashImage.color = c;
    }
}