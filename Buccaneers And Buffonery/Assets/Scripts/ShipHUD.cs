using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShipHUD : MonoBehaviour
{
    [Header("Bindings")]
    public ShipController ship;   // drag your local player's ShipController here (or call BindToShip)
    public Rigidbody shipRb;      // auto-filled from ship if empty

    [Header("Speed")]
    public TextMeshProUGUI speedLabel;      // "6.3 m/s"

    [Header("Sail % (Raise)")]
    public Slider sailLengthSlider;         // min=0, max=1
    public TextMeshProUGUI sailLengthLabel; // "Sail: 72%"

    [Header("Rudder / Wheel")]
    public Slider rudderSlider;             // min=-1, max=1
    public TextMeshProUGUI rudderLabel;     // "Wheel: L30%"

    public void BindToShip(ShipController controller)
    {
        ship = controller;
        shipRb = ship ? ship.GetComponent<Rigidbody>() : null;
        EnsureRanges();
        gameObject.SetActive(true);
    }

    void Awake() => EnsureRanges();

    void EnsureRanges()
    {
        if (sailLengthSlider) { sailLengthSlider.minValue = 0f; sailLengthSlider.maxValue = 1f; }
        if (rudderSlider) { rudderSlider.minValue = -1f; rudderSlider.maxValue = 1f; }
    }

    void Update()
    {
        if (!ship) return;
        if (!shipRb) shipRb = ship.GetComponent<Rigidbody>();

        // Speed
        float fwdSpeed = 0f;
        if (shipRb)
            fwdSpeed = Mathf.Max(0f, Vector3.Dot(shipRb.linearVelocity, ship.transform.forward)); // no reverse UI
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
    }
}