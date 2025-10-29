using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimplePersonController : NetworkBehaviour
{
    public float moveSpeed = 4f;
    public float gravity = -20f;

    private CharacterController _cc;
    private Vector3 _vel;

    private void Awake() => _cc = GetComponent<CharacterController>();

    private void Update()
    {
        if (!IsOwner || !_cc.enabled) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v).normalized;

        Vector3 move = input * moveSpeed;
        _vel.y += gravity * Time.deltaTime;

        // Face move direction if moving
        if (input.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z), Vector3.up);

        _cc.Move((move + new Vector3(0f, _vel.y, 0f)) * Time.deltaTime);

        if (_cc.isGrounded && _vel.y < 0f) _vel.y = -2f;
    }
}
