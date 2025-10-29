using UnityEngine;

/// <summary>
/// Makes a GameObject bob up and down smoothly like a ship on gentle waves,
/// while preserving its current Y rotation (heading).
/// </summary>
public class ShipBob : MonoBehaviour
{
    [Header("Bob Motion")]
    [Tooltip("How high/low the object moves (units).")]
    public float amplitude = 0.25f;

    [Tooltip("How fast it bobs up and down.")]
    public float frequency = 1f;

    [Header("Sway (Tilt)")]
    [Tooltip("Enable gentle rotation sway (like rocking on waves).")]
    public bool sway = true;

    [Tooltip("Maximum tilt angle (degrees).")]
    public float tiltAngle = 3f;

    [Tooltip("Speed of tilt oscillation.")]
    public float tiltSpeed = 0.5f;

    [Header("Options")]
    [Tooltip("Randomize motion phase so multiple ships aren't synchronized.")]
    public bool randomizePhase = true;

    private Vector3 startPos;
    private Quaternion startRot;
    private float timeOffset;

    void Start()
    {
        startPos = transform.localPosition;
        startRot = transform.localRotation;
        timeOffset = randomizePhase ? Random.Range(0f, Mathf.PI * 2f) : 0f;
    }

    void Update()
    {
        float t = Time.time * frequency + timeOffset;

        // Vertical bobbing (up/down)
        float newY = startPos.y + Mathf.Sin(t) * amplitude;
        transform.localPosition = new Vector3(startPos.x, newY, startPos.z);

        if (sway)
        {
            float swayT = Time.time * tiltSpeed + timeOffset;
            float tiltX = Mathf.Sin(swayT) * tiltAngle;
            float tiltZ = Mathf.Cos(swayT * 0.8f) * tiltAngle;

            // Keep the Y rotation (heading) while adding local X/Z rocking
            Quaternion currentYaw = Quaternion.Euler(0f, transform.localEulerAngles.y, 0f);
            Quaternion rollPitch = Quaternion.Euler(tiltX, 0f, tiltZ);
            transform.localRotation = currentYaw * rollPitch;
        }
    }
}
