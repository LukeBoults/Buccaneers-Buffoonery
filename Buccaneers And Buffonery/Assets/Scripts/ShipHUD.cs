using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShipHUD : MonoBehaviour
{
    [Header("UI")]
    public Slider throttleBar;        // Min=-1, Max=1
    public Slider rudderBar;          // Min=-1, Max=1
    public TextMeshProUGUI speedLabel;

    private ShipController ship;
    private Rigidbody shipRb;

    public void BindToShip(ShipController controller)
    {
        ship = controller;
        shipRb = ship ? ship.GetComponent<Rigidbody>() : null;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!ship || !shipRb) return;

        // Actual forward speed
        float fwdSpeed = Vector3.Dot(shipRb.linearVelocity, ship.transform.forward);

        // Map to -1..1 using ship caps
        float normalized =
            fwdSpeed >= 0f
            ? Mathf.Clamp(fwdSpeed / ship.maxForwardSpeed, 0f, 1f)
            : -Mathf.Clamp(-fwdSpeed / ship.maxReverseSpeed, 0f, 1f);

        if (throttleBar) throttleBar.value = normalized;
        if (rudderBar) rudderBar.value = ship.CurrentSteer; // expose CurrentSteer in ShipController
        if (speedLabel) speedLabel.text = $"{Mathf.Abs(fwdSpeed):0.0} m/s"; // abs: always positive
    }
}