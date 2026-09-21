using UnityEngine;
using UnityEngine.InputSystem;

// Third-person orbit camera: the mouse (or right stick) rotates the camera around the target.
// With no target (main menu) it slowly circles the scene as a backdrop.
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float distance = 9.5f;
    [SerializeField] float pivotHeight = 1.4f;
    [SerializeField] float mouseSensitivity = 0.12f;
    [SerializeField] float stickSensitivity = 140f;
    [SerializeField] Vector2 pitchLimits = new Vector2(-10f, 70f);
    [SerializeField] float followSmoothTime = 0.06f;
    [SerializeField] float collisionRadius = 0.3f;

    [Header("Menu backdrop")]
    [SerializeField] Vector3 idleCenter = new Vector3(2.5f, 1.5f, 5f);
    [SerializeField] float idleDistance = 15f;
    [SerializeField] float idleSpeed = 6f;

    float yaw;
    float pitch = 20f;
    float shake;
    Vector3 pivot;
    Vector3 pivotVelocity;
    InputAction mouseLook;
    InputAction stickLook;

    // The fitting room pins the camera to a fixed spot in front of the character.
    Camera cam;
    float normalFov = 60f;
    bool fixedView;
    Vector3 fixedPosition;
    Quaternion fixedRotation;
    float fixedFov;

    void Awake()
    {
        mouseLook = new InputAction("MouseLook", InputActionType.Value, "<Mouse>/delta");
        stickLook = new InputAction("StickLook", InputActionType.Value, "<Gamepad>/rightStick");
        cam = GetComponent<Camera>();
        if (cam != null) normalFov = cam.fieldOfView;
    }

    // Pins the camera to this pose (and field of view) until ClearFixedView is called.
    public void SetFixedView(Vector3 position, Quaternion rotation, float fov)
    {
        fixedView = true;
        fixedPosition = position;
        fixedRotation = rotation;
        fixedFov = fov;
    }

    // Back to following the character, right behind it.
    public void ClearFixedView()
    {
        fixedView = false;
        if (cam != null) cam.fieldOfView = normalFov;
        if (target != null) SetTarget(target);
    }

    void OnEnable()
    {
        mouseLook.Enable();
        stickLook.Enable();
    }

    void OnDisable()
    {
        mouseLook.Disable();
        stickLook.Disable();
    }

    // Called when the local player's character spawns (or null when it goes away).
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        pivotVelocity = Vector3.zero;
        if (target == null) return;

        yaw = target.eulerAngles.y;
        pitch = 20f;
        pivot = target.position + Vector3.up * pivotHeight;
        Place(distance);
    }

    void LateUpdate()
    {
        if (fixedView)
        {
            transform.SetPositionAndRotation(fixedPosition, fixedRotation);
            if (cam != null) cam.fieldOfView = fixedFov;
            return;
        }

        if (target == null)
        {
            yaw += idleSpeed * Time.unscaledDeltaTime;
            pitch = 18f;
            pivot = idleCenter;
            Place(idleDistance);
            return;
        }

        // Only rotate while playing, with the cursor captured and the pause menu closed.
        if (NetworkGame.InGame && Cursor.lockState == CursorLockMode.Locked && !PauseMenu.IsPaused)
        {
            Vector2 mouse = mouseLook.ReadValue<Vector2>();
            Vector2 stick = stickLook.ReadValue<Vector2>();
            yaw += mouse.x * mouseSensitivity + stick.x * stickSensitivity * Time.deltaTime;
            pitch -= mouse.y * mouseSensitivity + stick.y * stickSensitivity * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        }

        Vector3 desiredPivot = target.position + Vector3.up * pivotHeight;
        pivot = Vector3.SmoothDamp(pivot, desiredPivot, ref pivotVelocity, followSmoothTime);
        Place(distance);

        // A short rumble when a punch lands or connects.
        if (shake > 0.001f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, 0.9f * Time.unscaledDeltaTime);
        }
    }

    public void Shake(float amount) => shake = Mathf.Max(shake, Mathf.Min(amount, 0.35f));

    void Place(float dist)
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = rotation * Vector3.back;

        // Pull the camera in when something (ground, house) is between it and the character.
        // The fence, stands and their steps sit on the "Ignore Raycast" layer so the camera passes through them
        // instead of being shoved into the character's back.
        const int ignoreRaycastLayer = 2;
        int mask = ~(1 << ignoreRaycastLayer);
        if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(hit.distance, 0.5f);

        transform.SetPositionAndRotation(pivot + back * dist, rotation);
    }
}
