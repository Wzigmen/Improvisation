using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// The ability cards of one character (ten cards exist; the player puts up to four of them into the four slots).
// Input, cooldowns and the "start" of every card run on the owner's machine; everything that hurts somebody
// (or buffs a fighter) is decided by the host, like normal punches.
//
//   Slot keys 1-4 use whatever card sits in that slot; the left mouse button also plays the Heavy punch card.
//   Heavy punch  20 damage             Leap slam   jump forward, area hit on landing
//   Teleport     up to 50 m            Thorns      whoever hits you takes double damage for a few seconds
//   Fire dance   15 damage / 0.4 s     Lightning   instant bolt along the aim line
//   Heal         +30 health            Shield      no damage taken for a few seconds
//   Rage         double damage dealt   Vortex      pulls everybody close towards you
[RequireComponent(typeof(PlayerController))]
public class PlayerAbilities : NetworkBehaviour
{
    enum Fx : byte { Slam = 0, Teleport = 1, Thorns = 2, HeavyImpact = 3, Heal = 4, Shield = 5, Rage = 6, Vortex = 7 }

    // Written by the host, read by everybody (so all clients draw the spikes / fire / shield).
    readonly NetworkVariable<bool> thorns = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> dancing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> shield = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> rage = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    PlayerController player;
    readonly InputAction[] slotActions = new InputAction[AbilityLoadout.SlotCount];
    readonly float[] readyAt = new float[AbilityCatalog.All.Length];   // owner: when each card can be used again
    float danceLockUntil;                                              // owner: no moving while dancing
    int aimingSlot = -1;                                               // owner: slot of the teleport card while its key is held
    Transform marker;                                                  // owner: shows where the teleport lands
    Material markerMaterial;
    double thornsEnd, danceEnd, nextDanceTick, shieldEnd, rageEnd;     // host

    public bool ThornsActive => thorns.Value;
    public bool IsDancing => dancing.Value;
    public bool ShieldActive => shield.Value;
    public bool RageActive => rage.Value;
    public bool InputLocked => Time.time < danceLockUntil;

    public float CooldownLeft(AbilityId id) => Mathf.Max(0f, readyAt[(int)id] - Time.time);
    public bool IsActive(AbilityId id) =>
        (id == AbilityId.Thorns && thorns.Value) || (id == AbilityId.Dance && dancing.Value) ||
        (id == AbilityId.Shield && shield.Value) || (id == AbilityId.Rage && rage.Value);

    // Host: what a hit of this size becomes for this fighter (double while the rage lasts).
    public int ScaleDamage(int damage) => rage.Value ? damage * AbilityCatalog.RageMultiplier : damage;

    void Awake()
    {
        player = GetComponent<PlayerController>();

        slotActions[0] = Button("Slot1", "<Keyboard>/1", "<Gamepad>/leftTrigger");
        slotActions[1] = Button("Slot2", "<Keyboard>/2", "<Gamepad>/leftShoulder");
        slotActions[2] = Button("Slot3", "<Keyboard>/3", "<Gamepad>/rightShoulder");
        slotActions[3] = Button("Slot4", "<Keyboard>/4", "<Gamepad>/dpad/up");
    }

    static InputAction Button(string name, params string[] bindings)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (var b in bindings) action.AddBinding(b);
        return action;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner || player.IsBot) return;
        foreach (var action in slotActions) action.Enable();
    }

    public override void OnNetworkDespawn()
    {
        if (marker != null) Destroy(marker.gameObject);
        foreach (var action in slotActions) action.Disable();
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerTick();
        if (IsOwner && !player.IsBot) OwnerInput();
    }

    // ---- owner: pressing cards ----------------------------------------------------------------

    void OwnerInput()
    {
        // Teleport is "hold to aim, let go to jump", so while aiming nothing else is read.
        if (aimingSlot >= 0)
        {
            UpdateTeleportAim();
            return;
        }

        // With the card panel open the mouse is busy with the cards and the number keys are not "fire".
        if (!player.CanStartAbility || PauseMenu.PanelOpen) return;

        // The left mouse button only counts while the cursor is captured (otherwise it's for clicking buttons).
        bool mouseHeavy = Mouse.current != null && Cursor.lockState == CursorLockMode.Locked &&
                          Mouse.current.leftButton.wasPressedThisFrame;
        if (mouseHeavy)
        {
            if (AbilityLoadout.SlotOf(AbilityId.HeavyPunch) >= 0) Use(AbilityId.HeavyPunch);
            return;
        }

        for (int slot = 0; slot < slotActions.Length; slot++)
        {
            if (!slotActions[slot].WasPressedThisFrame()) continue;
            PressSlot(slot);
            break;
        }
    }

    void PressSlot(int slot)
    {
        int ability = AbilityLoadout.Get(slot);
        if (ability < 0) return; // an empty slot does nothing

        var id = (AbilityId)ability;
        if (id == AbilityId.Teleport)
        {
            if (CooldownLeft(id) <= 0f) aimingSlot = slot;
            return;
        }
        Use(id);
    }

    void Use(AbilityId id)
    {
        if (CooldownLeft(id) > 0f) return;

        bool started = false;
        switch (id)
        {
            case AbilityId.HeavyPunch:
                player.StartHeavyPunch(player.AimDirection());
                started = true;
                break;
            case AbilityId.LeapSlam:
                player.StartLeap(player.AimDirection());
                started = true;
                break;
            case AbilityId.Thorns:
                ThornsRpc();
                started = true;
                break;
            case AbilityId.Dance:
                danceLockUntil = Time.time + AbilityCatalog.DanceDuration;
                DanceRpc();
                started = true;
                break;
            case AbilityId.Lightning:
            {
                Vector3 aim = player.AimDirection();
                transform.rotation = Quaternion.LookRotation(aim, Vector3.up);
                LightningRpc(transform.position + Vector3.up, aim);
                started = true;
                break;
            }
            case AbilityId.Heal:
                if (player.Health >= PlayerController.MaxHealth) return; // nothing to heal: keep the card ready
                HealRpc();
                started = true;
                break;
            case AbilityId.Shield:
                ShieldRpc();
                started = true;
                break;
            case AbilityId.Rage:
                RageRpc();
                started = true;
                break;
            case AbilityId.Vortex:
                VortexRpc();
                started = true;
                break;
        }

        if (started) readyAt[(int)id] = Time.time + AbilityCatalog.Get(id).Cooldown;
    }

    // Called by the character when a leap touches the ground.
    public void OnLeapLanded(Vector3 position) => SlamRpc(position);

    // Called by the character when a heavy punch lands: a small shockwave in front of the fist.
    public void HeavyPunchImpact(Vector3 position) => EffectRpc((byte)Fx.HeavyImpact, position);

    // ---- owner: teleport ----------------------------------------------------------------------

    // While the key is held a marker shows where we'd land; letting go jumps there (Esc or a pause cancels).
    void UpdateTeleportAim()
    {
        bool cancel = !player.ControlledNow || PauseMenu.PanelOpen ||
                      (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
        if (cancel)
        {
            StopAiming();
            return;
        }

        bool valid = FindTeleportSpot(out Vector3 destination, out float yaw);
        ShowMarker(valid, destination);

        if (slotActions[aimingSlot].WasReleasedThisFrame())
        {
            StopAiming();
            if (!valid) return;

            EffectRpc((byte)Fx.Teleport, transform.position);
            player.TeleportInstant(destination, yaw);
            EffectRpc((byte)Fx.Teleport, destination);
            readyAt[(int)AbilityId.Teleport] = Time.time + AbilityCatalog.Get(AbilityId.Teleport).Cooldown;
        }
    }

    void StopAiming()
    {
        aimingSlot = -1;
        if (marker != null) marker.gameObject.SetActive(false);
    }

    void ShowMarker(bool valid, Vector3 position)
    {
        if (marker == null) BuildMarker();
        if (marker == null) return;

        marker.gameObject.SetActive(valid);
        if (!valid) return;

        marker.position = position;
        marker.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(Time.time * 10f));
    }

    // A flat glowing disc with a thin beam of light above it.
    void BuildMarker()
    {
        var fx = HitEffects.Instance;
        if (fx == null) return;

        markerMaterial = new Material(fx.SparkMaterial);
        markerMaterial.SetColor("_BaseColor", new Color(0.72f, 0.5f, 1f));

        marker = new GameObject("TeleportMarker").transform;
        AddMarkerPart("Disc", new Vector3(0f, 0.04f, 0f), new Vector3(1.6f, 0.02f, 1.6f));
        AddMarkerPart("Beam", new Vector3(0f, 1.2f, 0f), new Vector3(0.12f, 1.2f, 0.12f));
    }

    void AddMarkerPart(string partName, Vector3 localPosition, Vector3 localScale)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        part.name = partName;
        Destroy(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        part.transform.SetParent(marker, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
    }

    // Where the camera is looking, limited to 50 m and to the arena we are in. False if there is no room to stand.
    bool FindTeleportSpot(out Vector3 target, out float yaw)
    {
        target = transform.position;
        yaw = transform.eulerAngles.y;

        var cam = player.CameraTransform;
        if (cam == null) return false;

        Vector3 position = transform.position;
        Vector3 flatDirection = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        flatDirection = flatDirection.sqrMagnitude > 0.001f ? flatDirection.normalized : transform.forward;

        // Where the camera is looking: the first thing its ray hits (never ourselves), else 50 m straight ahead.
        target = position + flatDirection * AbilityCatalog.TeleportRange;
        var hits = Physics.RaycastAll(cam.position, cam.forward, 150f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            target = hit.point;
            break;
        }

        // At most 50 m away from where we stand, and never out of the arena we are in.
        Vector3 offset = target - position;
        offset.y = 0f;
        if (offset.magnitude > AbilityCatalog.TeleportRange)
            target = position + offset.normalized * AbilityCatalog.TeleportRange;
        target = ClampToArena(target);
        target.y = GroundHeight(target) + 0.1f;

        // Step back towards ourselves until there is room to stand (walls, fences, other characters).
        for (int i = 0; i < 8 && Blocked(target); i++)
        {
            target -= flatDirection * 0.6f;
            target.y = GroundHeight(target) + 0.1f;
        }
        if (Blocked(target)) return false;

        yaw = Quaternion.LookRotation(flatDirection).eulerAngles.y;
        return true;
    }

    // In a match you can't teleport out of the ring; on the start field you can't leave the ground.
    Vector3 ClampToArena(Vector3 p)
    {
        if (player.InMatch)
        {
            Vector3 c = GameLayout.RingCenter;
            float h = GameLayout.RingHalfSize - 1f;
            p.x = Mathf.Clamp(p.x, c.x - h, c.x + h);
            p.z = Mathf.Clamp(p.z, c.z - h, c.z + h);
        }
        else
        {
            p.x = Mathf.Clamp(p.x, -38f, 38f);
            p.z = Mathf.Clamp(p.z, -38f, 38f);
        }
        return p;
    }

    float GroundHeight(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.name == "FenceCollider") continue; // the invisible fence walls are not floor
            return hit.point.y;
        }
        return 0f;
    }

    static bool Blocked(Vector3 feet) =>
        Physics.CheckCapsule(feet + Vector3.up * 0.55f, feet + Vector3.up * 1.0f, 0.45f, ~0, QueryTriggerInteraction.Ignore);

    // ---- host: what the cards do --------------------------------------------------------------

    bool ServerMayUse() =>
        IsSpawned && (MatchManager.Instance == null || MatchManager.Instance.CanAct(player));

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void SlamRpc(Vector3 center)
    {
        if (!ServerMayUse()) return;
        if (Vector3.Distance(center, transform.position) > 4f) return; // must be where the character really is

        AreaStrike(center, AbilityCatalog.SlamRadius, AbilityCatalog.SlamDamage, 1.7f);
        EffectRpc((byte)Fx.Slam, center);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ThornsRpc()
    {
        if (!ServerMayUse()) return;
        thorns.Value = true;
        thornsEnd = NetworkManager.ServerTime.Time + AbilityCatalog.ThornsDuration;
        EffectRpc((byte)Fx.Thorns, transform.position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void DanceRpc()
    {
        if (!ServerMayUse()) return;
        dancing.Value = true;
        danceEnd = NetworkManager.ServerTime.Time + AbilityCatalog.DanceDuration;
        nextDanceTick = NetworkManager.ServerTime.Time + AbilityCatalog.DanceTickInterval;
    }

    // A bolt along the aim line: it hits the nearest target within reach (a player, a slime or the punching bag).
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void LightningRpc(Vector3 origin, Vector3 direction)
    {
        if (!ServerMayUse()) return;
        if (Vector3.Distance(origin, transform.position + Vector3.up) > 3f) return; // must start at the character

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        direction.Normalize();

        float nearest = AbilityCatalog.LightningRange;
        PlayerController hitPlayer = null;
        Mob hitMob = null;
        bool hitBag = false;
        Vector3 hitPoint = origin + direction * nearest;

        foreach (var victim in MatchManager.ActivePlayers(false))
        {
            if (victim == player || victim.IsDead) continue;
            if (OnLine(origin, direction, victim.transform.position, out float along) && along < nearest)
            {
                nearest = along;
                hitPlayer = victim;
                hitMob = null;
                hitBag = false;
                hitPoint = victim.transform.position + Vector3.up * 0.9f;
            }
        }

        foreach (var mob in FindObjectsByType<Mob>())
        {
            if (OnLine(origin, direction, mob.transform.position, out float along) && along < nearest)
            {
                nearest = along;
                hitPlayer = null;
                hitMob = mob;
                hitBag = false;
                hitPoint = mob.transform.position + Vector3.up * 0.4f;
            }
        }

        var bag = PunchingBag.Instance;
        if (bag != null && OnLine(origin, direction, bag.transform.position, out float bagAlong) && bagAlong < nearest)
        {
            nearest = bagAlong;
            hitPlayer = null;
            hitMob = null;
            hitBag = true;
            hitPoint = bag.transform.position + Vector3.down * 0.6f;
        }

        if (hitPlayer != null) player.ServerStrike(hitPlayer, AbilityCatalog.LightningDamage, direction, 1.4f);
        else if (hitMob != null) hitMob.ServerHit(direction, 1.4f, ScaleDamage(AbilityCatalog.LightningDamage));
        else if (hitBag) player.ServerBagHit(hitPoint, direction, 1.4f);

        LightningFxRpc(origin, hitPoint);
    }

    // Is `point` within a bolt's width of the line that starts at `origin` (flat, along `direction`)?
    static bool OnLine(Vector3 origin, Vector3 direction, Vector3 point, out float along)
    {
        Vector3 to = point - origin;
        to.y = 0f;
        along = Vector3.Dot(to, direction);
        if (along < 0f || along > AbilityCatalog.LightningRange) return false;
        return (to - direction * along).magnitude <= AbilityCatalog.LightningWidth;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void HealRpc()
    {
        if (!ServerMayUse()) return;
        player.ServerHeal(AbilityCatalog.HealAmount);
        EffectRpc((byte)Fx.Heal, transform.position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ShieldRpc()
    {
        if (!ServerMayUse()) return;
        shield.Value = true;
        shieldEnd = NetworkManager.ServerTime.Time + AbilityCatalog.ShieldDuration;
        EffectRpc((byte)Fx.Shield, transform.position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void RageRpc()
    {
        if (!ServerMayUse()) return;
        rage.Value = true;
        rageEnd = NetworkManager.ServerTime.Time + AbilityCatalog.RageDuration;
        EffectRpc((byte)Fx.Rage, transform.position);
    }

    // Pulls everybody within reach towards us and hurts them a little.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void VortexRpc()
    {
        if (!ServerMayUse()) return;

        Vector3 center = transform.position;
        foreach (var victim in MatchManager.ActivePlayers(false))
        {
            if (victim == player) continue;
            Vector3 toUs = center - victim.transform.position;
            toUs.y = 0f;
            if (toUs.magnitude > AbilityCatalog.VortexRadius) continue;
            // A shove of speed v carries a player v*v/48 metres, so pick the speed that brings them (almost) to us.
            float pull = Mathf.Clamp(toUs.magnitude - 1.5f, 0.5f, 5f);
            player.ServerStrike(victim, AbilityCatalog.VortexDamage, Outward(toUs), Mathf.Sqrt(48f * pull) / 9f);
        }

        foreach (var mob in FindObjectsByType<Mob>())
        {
            Vector3 toUs = center - mob.transform.position;
            toUs.y = 0f;
            if (toUs.magnitude > AbilityCatalog.VortexRadius) continue;
            // Slimes slide v*v/40 metres after a shove of speed 7 * power.
            float pull = Mathf.Clamp(toUs.magnitude - 1.2f, 0.5f, 8f);
            mob.ServerHit(Outward(toUs), Mathf.Sqrt(40f * pull) / 7f, ScaleDamage(AbilityCatalog.VortexDamage));
        }

        EffectRpc((byte)Fx.Vortex, center);
    }

    void ServerTick()
    {
        double now = NetworkManager.ServerTime.Time;

        if (thorns.Value && now >= thornsEnd) thorns.Value = false;
        if (shield.Value && now >= shieldEnd) shield.Value = false;
        if (rage.Value && now >= rageEnd) rage.Value = false;

        if (dancing.Value)
        {
            // The fire only burns while the dancer is still in the fight.
            if (!ServerMayUse()) { dancing.Value = false; return; }

            while (now >= nextDanceTick && nextDanceTick < danceEnd)
            {
                AreaStrike(transform.position, AbilityCatalog.DanceRadius, AbilityCatalog.DanceTickDamage, 0.5f);
                nextDanceTick += AbilityCatalog.DanceTickInterval;
            }
            if (now >= danceEnd) dancing.Value = false;
        }
    }

    // Hurts everybody within `radius` of `center` (except us): players (with all the usual fight rules),
    // slimes and the punching bag.
    void AreaStrike(Vector3 center, float radius, int damage, float power)
    {
        foreach (var victim in MatchManager.ActivePlayers(false))
        {
            if (victim == player) continue;
            Vector3 d = victim.transform.position - center;
            d.y = 0f;
            if (d.magnitude > radius) continue;
            player.ServerStrike(victim, damage, Outward(d), power);
        }

        foreach (var mob in FindObjectsByType<Mob>())
        {
            Vector3 d = mob.transform.position - center;
            d.y = 0f;
            if (d.magnitude > radius) continue;
            mob.ServerHit(Outward(d), power, ScaleDamage(damage));
        }

        var bag = PunchingBag.Instance;
        if (bag != null)
        {
            Vector3 d = bag.transform.position - center;
            d.y = 0f;
            if (d.magnitude <= radius + 0.5f)
                player.ServerBagHit(bag.transform.position + Vector3.down * 1.2f, Outward(d), power);
        }
    }

    Vector3 Outward(Vector3 d) => d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;

    // ---- everybody: effects -------------------------------------------------------------------

    [Rpc(SendTo.Everyone)]
    void EffectRpc(byte kind, Vector3 position)
    {
        var fx = HitEffects.Instance;
        if (fx == null) return;

        switch ((Fx)kind)
        {
            case Fx.Slam:
                fx.Shockwave(position, AbilityCatalog.SlamRadius, new Color(1f, 0.65f, 0.2f), 64);
                fx.Burst(position + Vector3.up * 0.3f, new Color(1f, 0.7f, 0.25f), 30, 7f, 0.18f);
                Shake(0.35f);
                break;
            case Fx.HeavyImpact:
                fx.Shockwave(position, 1.7f, new Color(1f, 0.4f, 0.3f), 32);
                break;
            case Fx.Teleport:
                fx.Burst(position + Vector3.up * 0.9f, new Color(0.7f, 0.45f, 1f), 36, 6f, 0.16f);
                fx.Shockwave(position, 1.4f, new Color(0.75f, 0.55f, 1f), 30);
                break;
            case Fx.Thorns:
                fx.Burst(position + Vector3.up * 0.9f, new Color(0.5f, 0.9f, 0.55f), 24, 5f, 0.14f);
                break;
            case Fx.Heal:
                fx.Burst(position + Vector3.up * 0.9f, new Color(1f, 0.6f, 0.75f), 34, 4f, 0.15f);
                fx.Shockwave(position, 1.7f, new Color(1f, 0.65f, 0.8f), 30);
                break;
            case Fx.Shield:
                fx.Burst(position + Vector3.up * 0.9f, new Color(0.4f, 0.9f, 1f), 30, 6f, 0.14f);
                fx.Shockwave(position, 1.9f, new Color(0.45f, 0.9f, 1f), 36);
                break;
            case Fx.Rage:
                fx.Burst(position + Vector3.up * 0.9f, new Color(1f, 0.3f, 0.2f), 36, 6.5f, 0.17f);
                fx.Shockwave(position, 2.2f, new Color(1f, 0.25f, 0.2f), 40);
                Shake(0.2f);
                break;
            case Fx.Vortex:
                fx.Implosion(position, AbilityCatalog.VortexRadius, new Color(0.55f, 0.6f, 1f), 90);
                fx.Shockwave(position, 2f, new Color(0.6f, 0.65f, 1f), 30);
                Shake(0.25f);
                break;
        }
    }

    // Everybody sees the bolt from the caster to whatever it hit.
    [Rpc(SendTo.Everyone)]
    void LightningFxRpc(Vector3 from, Vector3 to)
    {
        var fx = HitEffects.Instance;
        if (fx == null) return;

        fx.Bolt(from, to, new Color(1f, 0.95f, 0.45f));
        fx.Burst(to, new Color(1f, 0.95f, 0.5f), 26, 7f, 0.15f);
        Shake(0.2f);
    }

    void Shake(float amount)
    {
        if (!IsOwner || player.CameraTransform == null) return;
        var follow = player.CameraTransform.GetComponent<CameraFollow>();
        if (follow != null) follow.Shake(amount);
    }
}
