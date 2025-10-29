using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode; // <- for local player lookup

public class ShipHUD : MonoBehaviour
{
    [Header("Bindings")]
    public ShipController ship;   // you can set this via BindToShip at runtime
    public Rigidbody shipRb;      // auto-filled from ship if empty

    [Header("Speed")]
    public TextMeshProUGUI speedLabel;      // "6.3 m/s"

    [Header("Sail % (Raise)")]
    public Slider sailLengthSlider;         // min=0, max=1
    public TextMeshProUGUI sailLengthLabel; // "Sail: 72%"

    [Header("Rudder / Wheel")]
    public Slider rudderSlider;             // min=-1, max=1
    public TextMeshProUGUI rudderLabel;     // "Wheel: L30%"

    // ─────────────────────────────────────────────────────────────────────────────
    // Inventory UI
    [Header("Inventory")]
    [Tooltip("Optional: left empty. HUD will bind to the Local Player's inventory at runtime.")]
    public PlayerInventory inventory;
    [Tooltip("Label to display counts like W:12 S:3 M:1 C:5 P:0")]
    public TextMeshProUGUI inventoryLabel;

    [Header("Auto Bind")]
    [Tooltip("If true, HUD will auto-bind to the Local Player's Inventory after it spawns.")]
    public bool autoBindInventory = true;

    Coroutine autoBindCo;

    void Awake()
    {
        EnsureRanges();
    }

    void OnEnable()
    {
        // Kick off auto-binding if requested and not already bound
        if (autoBindInventory && inventory == null)
            autoBindCo = StartCoroutine(AutoBindInventoryRoutine());
    }

    void OnDisable()
    {
        if (autoBindCo != null)
        {
            StopCoroutine(autoBindCo);
            autoBindCo = null;
        }
    }

    // Allow your bootstrap to wire the ship explicitly
    public void BindToShip(ShipController controller)
    {
        ship = controller;
        shipRb = ship ? ship.GetComponent<Rigidbody>() : null;
        EnsureRanges();
        gameObject.SetActive(true);

        // If we still don't have an inventory and autobind is on, try again
        if (autoBindInventory && inventory == null && autoBindCo == null)
            autoBindCo = StartCoroutine(AutoBindInventoryRoutine());
    }

    public void BindToInventory(PlayerInventory inv)
    {
        inventory = inv;
    }

    void EnsureRanges()
    {
        if (sailLengthSlider) { sailLengthSlider.minValue = 0f; sailLengthSlider.maxValue = 1f; }
        if (rudderSlider) { rudderSlider.minValue = -1f; rudderSlider.maxValue = 1f; }
    }

    System.Collections.IEnumerator AutoBindInventoryRoutine()
    {
        // Wait for Netcode + local player object to exist
        while (true)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsClient && nm.SpawnManager != null)
            {
                var localPlayerObj = nm.SpawnManager.GetLocalPlayerObject();
                if (localPlayerObj != null && localPlayerObj.TryGetComponent(out PlayerInventory inv))
                {
                    BindToInventory(inv);
                    autoBindCo = null;
                    yield break;
                }
            }
            yield return null;
        }
    }

    void Update()
    {
        if (!ship) return;
        if (!shipRb) shipRb = ship.GetComponent<Rigidbody>();

        // Speed (keeps your original linearVelocity usage)
        float fwdSpeed = 0f;
        if (shipRb)
            fwdSpeed = Mathf.Max(0f, Vector3.Dot(shipRb.linearVelocity, ship.transform.forward));
        if (speedLabel) speedLabel.text = $"{fwdSpeed:0.0} m/s";

        // Sail %
        float sail01 = ship.CurrentThrottle; // 0..1
        if (sailLengthSlider) sailLengthSlider.value = sail01;
        if (sailLengthLabel) sailLengthLabel.text = $"Sail: {(sail01 * 100f):0}%";

        // Rudder
        float wheel = ship.CurrentSteer; // -1..1
        if (rudderSlider) rudderSlider.value = wheel;
        if (rudderLabel)
        {
            string side = wheel < -0.001f ? "L" : (wheel > 0.001f ? "R" : "C");
            rudderLabel.text = $"Wheel: {side}{Mathf.Abs(wheel) * 100f:0}%";
        }

        // Inventory (only if bound + label assigned)
        if (inventory && inventoryLabel)
        {
            var c = inventory.Counts.Value; // network-synced snapshot
            inventoryLabel.text = $"Wood:{c.wood}  Stone:{c.stone}  Metal:{c.metal}  Cloth:{c.cloth}  Powder:{c.powder}";
        }
    }
}