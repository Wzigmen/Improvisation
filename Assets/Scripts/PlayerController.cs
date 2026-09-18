using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Each player controls only their own character (IsOwner); other players' characters are moved by
// ClientNetworkTransform and just derive their walking/sprinting animation from the observed movement.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float sprintMultiplier = 1.8f;
    [SerializeField] float sprintAcceleration = 4f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] float gravity = -25f;
    [SerializeField] Transform cameraTransform;

    CharacterController controller;
    InputAction moveAction;
    InputAction sprintAction;
    float verticalVelocity;
    float sprintFactor = 1f;
    Vector3 lastPosition;

    // 0..1, how fast the character is currently moving relative to the input magnitude.
    public float Speed01 { get; private set; }

    // 0..1, how much the character is sprinting right now (eases in and out).
    public float SprintAmount => Mathf.InverseLerp(1f, sprintMultiplier, sprintFactor);

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Keyboard>/rightShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");
    }

    public override void OnNetworkSpawn()
    {
        var appearance = GetComponent<PlayerAppearance>();
        if (appearance != null) appearance.SetPlayerColor(OwnerClientId);

        lastPosition = transform.position;
        if (!IsOwner) return;

        // Put every player on their own spot so characters don't spawn inside each other.
        controller.enabled = false;
        transform.SetPositionAndRotation(GetSpawnPosition(OwnerClientId), Quaternion.identity);
        controller.enabled = true;
        lastPosition = transform.position;

        moveAction.Enable();
        sprintAction.Enable();

        var cam = Camera.main;
        if (cam != null)
        {
            cameraTransform = cam.transform;
            var follow = cam.GetComponent<CameraFollow>();
            if (follow != null) follow.SetTarget(transform);
        }
    }

    public override void OnNetworkDespawn()
    {
        moveAction.Disable();
        sprintAction.Disable();

        if (IsOwner && Camera.main != null)
        {
            var follow = Camera.main.GetComponent<CameraFollow>();
            if (follow != null) follow.SetTarget(null);
        }
    }

    static Vector3 GetSpawnPosition(ulong clientId)
    {
        if (clientId == 0) return new Vector3(0f, 0.1f, 0f);
        float angle = clientId * 60f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle) * 2.5f, 0.1f, Mathf.Sin(angle) * 2.5f);
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner) UpdateLocal();
        else UpdateRemote();
    }

    void UpdateLocal()
    {
        // No input while the pause menu is open or we're not in a game.
        bool active = NetworkGame.InGame && !PauseMenu.IsPaused;
        Vector2 input = active ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }

        Vector3 direction = forward * input.y + right * input.x;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        bool sprinting = active && sprintAction.IsPressed() && input.sqrMagnitude > 0.01f;
        sprintFactor = Mathf.MoveTowards(sprintFactor, sprinting ? sprintMultiplier : 1f, sprintAcceleration * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * (moveSpeed * sprintFactor) + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        Speed01 = Mathf.MoveTowards(Speed01, input.magnitude, 8f * Time.deltaTime);
    }

    // Other players: work out how fast they're moving from where they actually are on screen.
    void UpdateRemote()
    {
        if (Time.deltaTime <= 0f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        float ratio = delta.magnitude / Time.deltaTime / moveSpeed; // 1 = walking, sprintMultiplier = sprinting
        Speed01 = Mathf.MoveTowards(Speed01, Mathf.Clamp01(ratio), 8f * Time.deltaTime);
        sprintFactor = Mathf.MoveTowards(sprintFactor, Mathf.Clamp(ratio, 1f, sprintMultiplier), sprintAcceleration * Time.deltaTime);
    }
}
