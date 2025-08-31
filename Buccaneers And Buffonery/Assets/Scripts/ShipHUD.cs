using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ShipHUD : NetworkBehaviour
{
    [SerializeField]
    public Slider throttleBar;
    public Slider rudderBar;   // set to -1..+1 range

    void Update()
    {
        throttleBar.value = ShipController.throttle;
        rudderBar.value = controller.rudder;
    }
}