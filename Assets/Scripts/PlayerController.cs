using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Each player controls only their own character (IsOwner); other players' characters are moved by
// ClientNetworkTransform and just derive their walking/sprinting animation from the observed movement.
// Space jumps; jumping while sprinting is a forward flip. Right mouse button punches.
// Jump and punch states are synced so everyone sees them.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    const byte StateGrounded = 0;
    const byte StateJump = 1;
    const byte StateFlip = 2;
    const byte StateDash = 3;

    public const float DashCooldown = 3f;   // seconds between two dashes (Ctrl)

    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float sprintMultiplier = 1.8f;
    [SerializeField] float sprintAcceleration = 4f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] float gravity = -25f;
    [SerializeField] Transform cameraTransform;

    [Header("Jump")]
    [SerializeField] float jumpSpeed = 8f;
    [SerializeField] float flipJumpSpeed = 9.5f;
    [SerializeField] float flipSpeedBoost = 1.15f;
    [SerializeField] float coyoteTime = 0.1f;
    [SerializeField] float jumpBufferTime = 0.12f;

    [Header("Attack")]
    [SerializeField] float attackCooldown = 0.5f;
    [SerializeField] float attackDuration = 0.42f;   // length of the punch animation
    [SerializeField] float attackHitDelay = 0.14f;   // when the fist actually connects
    [SerializeField] float attackReach = 1.15f;      // sphere center in front of the character
    [SerializeField] float attackRadius = 1.1f;
    [SerializeField] float lungeSpeed = 3.5f;
    [SerializeField] float knockbackSpeed = 9f;
    [SerializeField] float stunTime = 0.35f;

    [Header("Ability cards: heavy punch and leap")]
    [SerializeField] float heavyDuration = 0.62f;    // the heavy punch winds up longer...
    [SerializeField] float heavyHitDelay = 0.26f;    // ...and lands later
    [SerializeField] float leapJumpSpeed = 10f;      // 2*10/25 = 0.8 s in the air
    [SerializeField] float leapSpeed = 11f;          // ~9 m forward

    [Header("Dash (Ctrl): a very fast hop in the running direction")]
    [SerializeField] float dashSpeed = 28f;
    [SerializeField] float dashDuration = 0.28f;
    [SerializeField] float dashHop = 4f;             // a little jump so it looks like a leap, not a slide

    CharacterController controller;
    InputAction moveAction;
    InputAction sprintAction;
    InputAction jumpAction;
    InputAction attackAction;
    InputAction dashAction;
    float verticalVelocity;
    float sprintFactor = 1f;
    Vector3 lastPosition;

    float coyoteTimer;
    float jumpBufferTimer;
    float airTime;
    bool flipping;
    Vector3 flipDirection;
    float flipStartTime;

    float attackStartTime = -10f;
    float nextAttackTime;
    int attackSide;
    bool attackHeavy;       // the punch playing right now is a heavy one (everybody sees this)
    bool pendingHeavy;      // owner only: the punch waiting for its hit check is a heavy one
    bool hitPending;

    bool leaping;
    Vector3 leapDirection;
    float leapStartTime;

    bool dashing;               // owner: a dash is in progress
    Vector3 dashDirection;
    float dashStartTime = -10f; // everybody: when the dash animation started
    float nextDashTime;         // owner

    float hitStartTime = -10f;
    Vector3 hitDirection = Vector3.forward;
    Vector3 knockback;
    float stunTimer;

    // Owner writes, everybody reads: what the character is doing in the air.
    readonly NetworkVariable<byte> motionState = new NetworkVariable<byte>(
        StateGrounded, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Bumped by the owner on every punch so everybody else can play the animation.
    readonly NetworkVariable<byte> attackCounter = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Fight state. The host is in charge of all of it, so nobody can cheat their own health.
    public const int MaxHealth = 100;
    public const int AttackDamage = 5;

    readonly NetworkVariable<int> health = new NetworkVariable<int>(
        MaxHealth, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> inMatch = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> ready = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Bots are characters like any other, but a BotBrain (host side) presses the "buttons" for them.
    readonly NetworkVariable<int> botNumber = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // The nickname typed in the start menu: the owner writes it once when the character spawns, everybody reads it.
    readonly NetworkVariable<FixedString64Bytes> nickname = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    string nicknameText = "";   // the same, cleaned up and kept as a string so it isn't rebuilt every frame

    // How the character is dressed (see CharacterStyle): six choices packed into one number, written by the owner.
    readonly NetworkVariable<uint> style = new NetworkVariable<uint>(
        0u, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // True while the owner is behind the curtain of the fitting room: everybody else doesn't see the character.
    readonly NetworkVariable<bool> customizing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public uint StyleValue => style.Value;
    public bool IsCustomizing => customizing.Value;
    public void SetStyle(uint value) { if (IsOwner && IsSpawned) style.Value = value; }
    public void SetCustomizing(bool value) { if (IsOwner && IsSpawned) customizing.Value = value; }

    // Owner: the fitting-room screen holds the character still.
    public bool Locked { get; set; }

    public BotBrain Brain { get; private set; }
    public bool IsBot => Brain != null;
    public int BotNumber => botNumber.Value;
    public string DisplayName => IsBot ? $"Бот {botNumber.Value}" : nicknameText.Length > 0 ? nicknameText : $"Игрок {OwnerClientId + 1}";
    // Feeds PlayerAppearance / the ready list: bots get colors from their own range, away from the players' ids.
    public ulong ColorId => IsBot ? 100UL + (ulong)Mathf.Max(0, botNumber.Value) : OwnerClientId;

    public int Health => health.Value;
    public bool InMatch => inMatch.Value;
    public bool IsReady => ready.Value;
    public bool IsDead => inMatch.Value && health.Value <= 0;   // knocked out of the current fight

    // 0..1, how fast the character is currently moving relative to the input magnitude.
    public float Speed01 { get; private set; }

    // 0..1, how much the character is sprinting right now (eases in and out).
    public float SprintAmount => Mathf.InverseLerp(1f, sprintMultiplier, sprintFactor);

    public bool Airborne => motionState.Value != StateGrounded;
    public bool IsFlipping => motionState.Value == StateFlip;

    // Dash: everybody sees it (motion state), only the owner has the cooldown.
    public bool IsDashing => motionState.Value == StateDash;
    public float DashProgress => IsDashing ? Mathf.Clamp01((Time.time - dashStartTime) / dashDuration) : 0f;
    public float DashCooldownLeft => Mathf.Max(0f, nextDashTime - Time.time);

    // How long a flip jump stays in the air (same physics on every machine, so the flip lines up).
    public float FlipDuration => 2f * flipJumpSpeed / Mathf.Abs(gravity);

    // 0..1 progress of the somersault, 0 when not flipping.
    public float FlipProgress => IsFlipping ? Mathf.Clamp01((Time.time - flipStartTime) / FlipDuration) : 0f;

    // Punch animation: seconds since the punch started, which hand (0 right, 1 left), and whether it is a heavy one.
    public float AttackTime => Time.time - attackStartTime;
    public float AttackDuration => attackHeavy ? heavyDuration : attackDuration;
    public float AttackHitDelay => attackHeavy ? heavyHitDelay : attackHitDelay;
    public bool IsAttacking => AttackTime < AttackDuration;
    public int AttackSide => attackSide;
    public bool AttackHeavy => attackHeavy;

    // The ability cards (a separate component) and what they need to know about us.
    public PlayerAbilities Abilities { get; private set; }
    public Transform CameraTransform => cameraTransform;
    public bool ControlledNow { get; private set; }   // owner: input is currently allowed (not paused / frozen / stunned)
    public bool IsLeaping => leaping;
    public bool CanStartAbility =>
        ControlledNow && !flipping && !leaping && !dashing && !IsAttacking && (Abilities == null || !Abilities.InputLocked);

    // Hit reaction: seconds since we were hit, and the direction the punch pushed us.
    public float HitTime => Time.time - hitStartTime;
    public Vector3 HitDirection => hitDirection;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Brain = GetComponent<BotBrain>();
        Abilities = GetComponent<PlayerAbilities>();

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

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding("<Mouse>/rightButton");
        attackAction.AddBinding("<Gamepad>/buttonWest");
        attackAction.AddBinding("<Gamepad>/rightTrigger");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/ctrl");   // either Ctrl key
        dashAction.AddBinding("<Gamepad>/buttonEast");
    }

    public override void OnNetworkSpawn()
    {
        motionState.OnValueChanged += OnMotionStateChanged;
        attackCounter.OnValueChanged += OnAttackCounterChanged;
        botNumber.OnValueChanged += OnBotNumberChanged;
        nickname.OnValueChanged += OnNicknameChanged;
        nicknameText = PlayerNickname.Clean(nickname.Value.ToString());

        ApplyColor();

        lastPosition = transform.position;
        // A bot lives on the host too, but it has no keyboard, no camera and its spawn point is set by the host.
        if (!IsOwner || IsBot) return;

        // Tell everybody who we are (the start menu doesn't let anybody in without a nickname).
        nickname.Value = PlayerNickname.ToNetwork(PlayerNickname.Current);
        style.Value = PlayerStyle.Saved;

        // Put every player on their own spot so characters don't spawn inside each other.
        controller.enabled = false;
        transform.SetPositionAndRotation(GetSpawnPosition(OwnerClientId), Quaternion.identity);
        controller.enabled = true;
        lastPosition = transform.position;

        moveAction.Enable();
        sprintAction.Enable();
        jumpAction.Enable();
        attackAction.Enable();
        dashAction.Enable();

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
        motionState.OnValueChanged -= OnMotionStateChanged;
        attackCounter.OnValueChanged -= OnAttackCounterChanged;
        botNumber.OnValueChanged -= OnBotNumberChanged;
        nickname.OnValueChanged -= OnNicknameChanged;
        moveAction.Disable();
        sprintAction.Disable();
        jumpAction.Disable();
        attackAction.Disable();
        dashAction.Disable();

        if (IsOwner && !IsBot && Camera.main != null)
        {
            var follow = Camera.main.GetComponent<CameraFollow>();
            if (follow != null) follow.SetTarget(null);
        }
    }

    void ApplyColor()
    {
        var appearance = GetComponent<PlayerAppearance>();
        if (appearance != null) appearance.SetPlayerColor(ColorId);

        // The gloves cover the hands the color was just given to.
        var characterStyle = GetComponent<CharacterStyle>();
        if (characterStyle != null) characterStyle.RefreshColors();
    }

    // A bot's number can arrive just after it spawns, so recolor when it does.
    void OnBotNumberChanged(int previous, int next) => ApplyColor();

    // The nickname can arrive just after the character appears on other machines.
    void OnNicknameChanged(FixedString64Bytes previous, FixedString64Bytes next) =>
        nicknameText = PlayerNickname.Clean(next.ToString());

    void OnMotionStateChanged(byte previous, byte next)
    {
        // Remote players start their somersault the moment the owner announces it.
        if (next == StateFlip) flipStartTime = Time.time;
        if (next == StateDash) dashStartTime = Time.time;
    }

    void OnAttackCounterChanged(byte previous, byte next)
    {
        // Everybody (including the owner) starts the punch animation from this one place.
        // The byte holds a 7-bit punch counter and, in the lowest bit, "this one is heavy".
        // Punches alternate hands, starting with the right one.
        attackHeavy = (next & 1) == 1;
        attackSide = ((next >> 1) + 1) & 1;
        attackStartTime = Time.time;
    }

    public static Vector3 GetSpawnPosition(ulong clientId)
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
        float dt = Time.deltaTime;

        // No input while the pause menu is open or we're not in a game, and none while stunned by a punch.
        stunTimer -= dt;
        // In a match fighters are frozen until "Fight" starts and once they're knocked out.
        bool matchAllows = MatchManager.Instance == null || MatchManager.Instance.CanAct(this);
        // Bots keep fighting when the host opens the pause menu; humans need to be in a game with no menu open.
        bool bot = Brain != null;
        // Dancing and leaping take over the character for a moment.
        bool abilityBusy = leaping || (Abilities != null && Abilities.InputLocked);
        bool controlled = stunTimer <= 0f && matchAllows && !abilityBusy && !Locked && (bot || (NetworkGame.InGame && !PauseMenu.IsPaused));
        ControlledNow = controlled && !bot;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }

        // The same "buttons" for everybody: where to go, where to aim, and whether to sprint / jump / punch.
        Vector3 direction;
        Vector3 aim = forward;
        bool sprintHeld, jumpPressed, attackPressed, dashPressed;
        if (bot)
        {
            Brain.Think(this, controlled, out direction, out aim, out attackPressed);
            sprintHeld = false;
            jumpPressed = false;
            dashPressed = false;
        }
        else
        {
            Vector2 input = controlled ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
            direction = forward * input.y + right * input.x;
            sprintHeld = controlled && sprintAction.IsPressed();
            jumpPressed = controlled && jumpAction.WasPressedThisFrame();
            // With the card panel open the mouse is busy with the cards, so the right button doesn't punch.
            attackPressed = controlled && !PauseMenu.PanelOpen && attackAction.WasPressedThisFrame();
            dashPressed = controlled && !PauseMenu.PanelOpen && dashAction.WasPressedThisFrame();
        }
        float inputMagnitude = Mathf.Min(1f, direction.magnitude);

        bool sprinting = sprintHeld && inputMagnitude > 0.1f;
        sprintFactor = Mathf.MoveTowards(sprintFactor, sprinting ? sprintMultiplier : 1f, sprintAcceleration * dt);

        // A short grace period after leaving the ground / before landing makes jumping feel forgiving.
        bool grounded = controller.isGrounded;
        if (grounded)
        {
            coyoteTimer = coyoteTime;
            airTime = 0f;
        }
        else
        {
            coyoteTimer -= dt;
            airTime += dt;
        }

        if (jumpPressed) jumpBufferTimer = jumpBufferTime;
        else jumpBufferTimer -= dt;

        if (dashPressed && Time.time >= nextDashTime && !dashing && !flipping && !leaping && !IsAttacking)
            StartDash(direction);

        // A dash lasts a fraction of a second; a punch that hit us (or a teleport) cuts it short.
        if (dashing && (Time.time - dashStartTime >= dashDuration || stunTimer > 0f)) dashing = false;

        if (jumpBufferTimer > 0f && coyoteTimer > 0f && !flipping && !dashing)
        {
            if (SprintAmount > 0.5f && inputMagnitude > 0.1f)
                StartFlip(direction);
            else
                verticalVelocity = jumpSpeed;

            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        if (controlled && !flipping && !dashing && Time.time >= nextAttackTime && attackPressed)
            StartAttack(aim);

        if (hitPending && AttackTime >= AttackHitDelay)
        {
            hitPending = false;
            PerformHitCheck();
        }

        // A flip commits to its direction and a punch keeps you facing the target;
        // otherwise the character turns towards where you steer.
        // A bot always faces whoever it is fighting, even while it strafes.
        Vector3 facing = bot ? aim : direction;
        if (!flipping && !leaping && !dashing && !IsAttacking && facing.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * dt;

        // The dash bursts out fast and slows a little towards its end.
        float dashSpeedNow = dashSpeed * Mathf.Lerp(1.2f, 0.6f, Mathf.Clamp01((Time.time - dashStartTime) / dashDuration));

        Vector3 horizontal = flipping
            ? flipDirection * (moveSpeed * sprintMultiplier * flipSpeedBoost)
            : leaping
                ? leapDirection * leapSpeed
                : dashing
                    ? dashDirection * dashSpeedNow
                    : direction * (moveSpeed * sprintFactor);

        // A small step forward into the punch, and whatever a punch that hit us added.
        if (AttackTime > 0.04f && AttackTime < 0.2f) horizontal += transform.forward * lungeSpeed;
        horizontal += knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 24f * dt);

        controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);

        if (flipping && controller.isGrounded && verticalVelocity <= 0f && Time.time - flipStartTime > 0.15f)
            flipping = false;

        // Landing from a leap: the slam happens right here.
        if (leaping && controller.isGrounded && verticalVelocity <= 0f && Time.time - leapStartTime > 0.15f)
        {
            leaping = false;
            if (Abilities != null) Abilities.OnLeapLanded(transform.position);
        }

        motionState.Value = dashing ? StateDash : flipping ? StateFlip : airTime > 0.1f ? StateJump : StateGrounded;
        Speed01 = Mathf.MoveTowards(Speed01, inputMagnitude, 8f * dt);
    }

    // Ctrl: a very fast hop in the direction we are running (or the way we face when standing still).
    void StartDash(Vector3 direction)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        dashDirection = flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
        dashing = true;
        dashStartTime = Time.time;
        nextDashTime = Time.time + DashCooldown;

        transform.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);
        if (controller.isGrounded) verticalVelocity = dashHop;
        hitPending = false;
        jumpBufferTimer = 0f;
        motionState.Value = StateDash;
    }

    void StartFlip(Vector3 direction)
    {
        flipping = true;
        flipStartTime = Time.time;
        flipDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        verticalVelocity = flipJumpSpeed;
        motionState.Value = StateFlip;
    }

    // ---- ability cards: things the card component asks the character to do ---------------------

    // Where the camera is looking, flattened: the direction punches, leaps and teleports go.
    public Vector3 AimDirection()
    {
        if (cameraTransform != null)
        {
            Vector3 f = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (f.sqrMagnitude > 0.001f) return f.normalized;
        }
        return transform.forward;
    }

    // Card 1: a slow, strong punch (20 damage).
    public void StartHeavyPunch(Vector3 aim) => StartAttack(aim, true);

    // Card 2: jump forward; the area hit happens when we land (see the leap landing in UpdateLocal).
    public void StartLeap(Vector3 aim)
    {
        leaping = true;
        leapStartTime = Time.time;
        leapDirection = aim.sqrMagnitude > 0.001f ? aim.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(leapDirection, Vector3.up);
        verticalVelocity = leapJumpSpeed;
        hitPending = false;
    }

    // Card 3: instant move, no fade (the fade is for changing locations).
    public void TeleportInstant(Vector3 position, float yaw) => TeleportNow(position, yaw);

    // ---- attacking -----------------------------------------------------------------------------

    void StartAttack(Vector3 cameraForward, bool heavy = false)
    {
        nextAttackTime = Time.time + (heavy ? heavyDuration + 0.05f : attackCooldown);

        // Punch where the camera is looking.
        if (cameraForward.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(cameraForward, Vector3.up);

        hitPending = true;
        pendingHeavy = heavy;

        // 7-bit counter in the upper bits, the "heavy" flag in the lowest one (also starts our own animation
        // through the change callback).
        int counter = ((attackCounter.Value >> 1) + 1) & 0x7F;
        attackCounter.Value = (byte)((counter << 1) | (heavy ? 1 : 0));
    }

    // Runs on the attacker's machine when the fist connects; the host confirms and applies each hit.
    void PerformHitCheck()
    {
        Vector3 center = transform.position + Vector3.up * 0.8f + transform.forward * attackReach;
        float power = 1f + SprintAmount * 0.6f; // sprinting punches hit harder
        bool heavy = pendingHeavy;
        pendingHeavy = false;
        if (heavy) power = 1.8f;
        int damage = heavy ? AbilityCatalog.HeavyPunchDamage : AttackDamage;

        var handled = new HashSet<Object>();
        bool anyHit = false;

        foreach (var col in Physics.OverlapSphere(center, attackRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(transform)) continue; // ourselves

            var otherPlayer = col.GetComponentInParent<PlayerController>();
            if (otherPlayer != null)
            {
                if (handled.Add(otherPlayer))
                {
                    HitRpc(otherPlayer.NetworkObject, FlatDirectionTo(otherPlayer.transform.position), power, damage);
                    anyHit = true;
                }
                continue;
            }

            var mob = col.GetComponentInParent<Mob>();
            if (mob != null)
            {
                if (handled.Add(mob))
                {
                    HitRpc(mob.NetworkObject, FlatDirectionTo(mob.transform.position), power, damage);
                    anyHit = true;
                }
                continue;
            }

            var bag = col.GetComponentInParent<PunchingBag>();
            if (bag != null && handled.Add(bag))
            {
                Vector3 point = col.ClosestPoint(center);
                HitBagRpc(point, transform.forward, power);
                anyHit = true;
            }
        }

        if (anyHit && cameraTransform != null)
        {
            var follow = cameraTransform.GetComponent<CameraFollow>();
            if (follow != null) follow.Shake(0.12f * power);
        }

        // A heavy punch lands with a small shockwave in front of the fist.
        if (heavy && Abilities != null) Abilities.HeavyPunchImpact(center);
    }

    Vector3 FlatDirectionTo(Vector3 position)
    {
        Vector3 d = position - transform.position;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
    }

    // The attacker asks the host to apply a hit; the host checks it's plausible and forwards it.
    const float MaxHitDistance = 4f;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void HitRpc(NetworkObjectReference target, Vector3 direction, float power, int damage)
    {
        if (!target.TryGet(out NetworkObject targetObject)) return;
        if (Vector3.Distance(transform.position, targetObject.transform.position) > MaxHitDistance) return;
        power = Mathf.Clamp(power, 0.5f, 2f);
        // The damage is decided by the host: a plain punch or a heavy one, nothing else.
        damage = damage >= AbilityCatalog.HeavyPunchDamage ? AbilityCatalog.HeavyPunchDamage : AttackDamage;

        var otherPlayer = targetObject.GetComponent<PlayerController>();
        if (otherPlayer != null && otherPlayer != this)
        {
            ServerStrike(otherPlayer, damage, direction, power);
            return;
        }

        var mob = targetObject.GetComponent<Mob>();
        if (mob != null) mob.ServerHit(direction, power, Abilities != null ? Abilities.ScaleDamage(damage) : damage);
    }

    // Host side: one player hurts another (`this` is the attacker). Used by punches and by every ability card.
    // Outside a fight players can shove each other around for fun, without harm.
    // Fighters only hurt each other while the fight is on, and only while both are still standing.
    // If the victim has the Thorns card active, the attacker takes double the damage they dealt.
    // A victim behind the Shield card takes no damage and isn't even pushed; an attacker in Rage deals double.
    public bool ServerStrike(PlayerController victim, int damage, Vector3 direction, float power)
    {
        if (victim == null || victim == this) return false;
        if (victim.Abilities != null && victim.Abilities.ShieldActive) return false;
        if (Abilities != null) damage = Abilities.ScaleDamage(damage);

        if (inMatch.Value || victim.inMatch.Value)
        {
            bool fair = MatchManager.Instance != null && MatchManager.Instance.IsFighting &&
                        inMatch.Value && victim.inMatch.Value &&
                        health.Value > 0 && victim.health.Value > 0;
            if (!fair) return false;

            victim.ServerDamage(damage);
            if (victim.Abilities != null && victim.Abilities.ThornsActive)
                ServerDamage(damage * AbilityCatalog.ThornsMultiplier);
        }

        victim.ServerReceiveHit(direction, power);
        return true;
    }

    // Host side: make the punching bag react (used by the area cards).
    public void ServerBagHit(Vector3 point, Vector3 direction, float power) => BagHitRpc(point, direction, power);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void HitBagRpc(Vector3 point, Vector3 direction, float power)
    {
        var bag = PunchingBag.Instance;
        if (bag == null || Vector3.Distance(transform.position, bag.transform.position) > MaxHitDistance + 2f) return;
        BagHitRpc(point, direction, Mathf.Clamp(power, 0.5f, 2f));
    }

    // Everybody sees the bag react.
    [Rpc(SendTo.Everyone)]
    void BagHitRpc(Vector3 point, Vector3 direction, float power)
    {
        if (PunchingBag.Instance != null) PunchingBag.Instance.ApplyHit(point, direction, power);
    }

    // ---- fights: health, ready check, moving between the field and the ring --------------------

    public void ServerDamage(int amount) => health.Value = Mathf.Max(0, health.Value - amount);

    // The Heal card: back up towards full health (a knocked-out fighter can't be healed).
    public void ServerHeal(int amount)
    {
        if (health.Value <= 0) return;
        health.Value = Mathf.Min(MaxHealth, health.Value + amount);
    }

    // Host side, before the bot is spawned: number it and put it straight into the match, already "ready".
    public void ServerInitBot(int number)
    {
        botNumber.Value = number;
        style.Value = CharacterStyleCatalog.RandomFor(number);   // every bot is dressed differently
        inMatch.Value = true;
        ready.Value = true;
        health.Value = MaxHealth;
    }

    // Host side: take this player into a match (full health, not ready yet) and move them to the ring.
    public void ServerJoinMatch(Vector3 position, float yaw)
    {
        inMatch.Value = true;
        ready.Value = false;
        health.Value = MaxHealth;
        TeleportRpc(position, yaw);
    }

    // Host side: back to the start field, everything reset.
    public void ServerLeaveMatch(Vector3 position, float yaw)
    {
        inMatch.Value = false;
        ready.Value = false;
        health.Value = MaxHealth;
        TeleportRpc(position, yaw);
    }

    // The "Ready" button. The host stores the answer, so it can't be changed outside the ready check.
    public void RequestReady(bool value) => SetReadyRpc(value);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void SetReadyRpc(bool value)
    {
        if (!inMatch.Value || MatchManager.Instance == null) return;
        if (MatchManager.Instance.CurrentPhase != MatchManager.Phase.Ready) return;
        ready.Value = value;
    }

    // Only the owner moves their character, so the host asks the owner to do the teleport,
    // behind a fade to black so it looks like loading another location.
    [Rpc(SendTo.Owner)]
    void TeleportRpc(Vector3 position, float yaw)
    {
        if (MatchUI.Instance != null) MatchUI.Instance.Transition(() => TeleportNow(position, yaw));
        else TeleportNow(position, yaw);
    }

    void TeleportNow(Vector3 position, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        // Tell everybody else the jump was instant, so they don't watch us slide across the map.
        var netTransform = GetComponent<ClientNetworkTransform>();
        if (netTransform != null) netTransform.Teleport(position, rotation, transform.localScale);

        verticalVelocity = 0f;
        knockback = Vector3.zero;
        stunTimer = 0f;
        flipping = false;
        leaping = false;
        dashing = false;
        jumpBufferTimer = 0f;
        hitPending = false;
        lastPosition = position;

        // Put the camera right behind the character again instead of swooping across the world.
        if (cameraTransform != null)
        {
            var follow = cameraTransform.GetComponent<CameraFollow>();
            if (follow != null) follow.SetTarget(transform);
        }
    }

    // ---- being hit -----------------------------------------------------------------------------

    // Host side only: tell the victim's machine to knock them back and everybody to play the reaction.
    public void ServerReceiveHit(Vector3 direction, float power)
    {
        KnockbackRpc(direction, power);
        HitReactionRpc(direction, power);
    }

    // Only the victim's own machine moves their character, so the shove is applied there.
    [Rpc(SendTo.Owner)]
    void KnockbackRpc(Vector3 direction, float power)
    {
        Vector3 d = direction;
        d.y = 0f;
        knockback = d.normalized * (knockbackSpeed * power);
        stunTimer = stunTime;
        verticalVelocity = Mathf.Max(verticalVelocity, 4f);
        flipping = false;
        jumpBufferTimer = 0f;
        hitPending = false;
    }

    [Rpc(SendTo.Everyone)]
    void HitReactionRpc(Vector3 direction, float power)
    {
        hitStartTime = Time.time;
        hitDirection = direction;

        var flash = GetComponent<HitFlash>();
        if (flash != null) flash.Flash();

        var fx = HitEffects.Instance;
        if (fx != null)
        {
            Vector3 point = transform.position + Vector3.up * 0.9f;
            fx.Burst(point, new Color(1f, 0.9f, 0.3f), Mathf.RoundToInt(12 * power), 4.5f * power);
        }

        if (IsOwner && cameraTransform != null)
        {
            var follow = cameraTransform.GetComponent<CameraFollow>();
            if (follow != null) follow.Shake(0.2f * power);
        }
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
