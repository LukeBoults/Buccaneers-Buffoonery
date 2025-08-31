using UnityEngine;
using Unity.Netcode;

public class PlayerMovement : NetworkBehaviour
{
    public float speed = 5f;

    void Start()
    {
        if (IsOwner)
        {
            GetComponent<Renderer>().material.color = Color.red;
        }
    }


    void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        transform.Translate(direction * speed * Time.deltaTime);
    }
}
