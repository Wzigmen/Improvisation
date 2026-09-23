using System.Collections;
using Unity.Netcode;
using UnityEngine;

// A harmless wandering slime. The host runs its AI and movement (NetworkTransform syncs it),
// everybody plays the hop / hit / death animations locally.
[RequireComponent(typeof(CharacterController))]
public class Mob : NetworkBehaviour
{
    [SerializeField] Transform visual;
    [SerializeField] int maxHealth = 20;   // a punch does 5 damage, so a slime still goes down in 4 hits
    [SerializeField] float wanderSpeed = 1.6f;
    [SerializeField] float wanderRadius = 10f;
    [SerializeField] float gravity = -25f;

    // Where this slime lives (host only); it wanders around this point.
    public Vector3 Home { get; set; }

    CharacterController controller;
    HitFlash flash;

    // host-side state
    int health;
    Vector3 wanderTarget;
    float pauseTimer;
    float retargetTimer;
    Vector3 knockback;
    float stunTimer;
    float verticalVelocity;

    // shared animation state
    Vector3 lastPosition;
    float speedRatio;
    float hopPhase;
    float hitStartTime = -10f;
    Vector3 hitDirection;
    bool dead;
    Vector3 visualScale, visualPos;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        flash = GetComponent<HitFlash>();
        visualScale = visual.localScale;
        visualPos = visual.localPosition;
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;
        if (!IsServer) return;

        health = maxHealth;
        PickTarget();
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer && !dead) ServerUpdate();
        Animate();
    }

    // ---- host: AI -----------------------------------------------------------------------------

    void ServerUpdate()
    {
        float dt = Time.deltaTime;
        stunTimer -= dt;
        retargetTimer -= dt;

        Vector3 move = Vector3.zero;
        if (stunTimer <= 0f)
        {
            Vector3 toTarget = wanderTarget - transform.position;
            toTarget.y = 0f;

            if (pauseTimer > 0f)
            {
                pauseTimer -= dt;
            }
            else if (toTarget.magnitude < 0.4f || retargetTimer <= 0f)
            {
                // Arrived (or got stuck behind something): rest a moment, then pick a new spot.
                pauseTimer = Random.Range(1f, 3f);
                PickTarget();
            }
            else
            {
                Vector3 direction = toTarget.normalized;
                move = direction * wanderSpeed;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * dt);
            }
        }

        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 20f * dt);

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;

        controller.Move((move + knockback + Vector3.up * verticalVelocity) * dt);
    }

    void PickTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        Vector3 target = Home + new Vector3(offset.x, 0f, offset.y);
        // Stay well inside the fence (it stands 30 m from the middle).
        target.x = Mathf.Clamp(target.x, -27f, 27f);
        target.z = Mathf.Clamp(target.z, -27f, 27f);
        wanderTarget = target;
        retargetTimer = 8f;
    }

    // Called on the host when a player's punch or ability lands.
    public void ServerHit(Vector3 direction, float power, int damage, DamageType type = DamageType.Physical)
    {
        if (dead) return;

        health -= damage;
        Vector3 d = direction;
        d.y = 0f;
        d = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;

        knockback = d * (7f * power);
        stunTimer = 0.5f;
        verticalVelocity = 4f;
        HitReactionRpc(d, power, (byte)type);

        if (health <= 0)
        {
            dead = true;
            DeathRpc();
            StartCoroutine(DespawnAfterDelay(0.45f));
        }
    }

    IEnumerator DespawnAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (MobSpawner.Instance != null) MobSpawner.Instance.ScheduleRespawn(Home);
        NetworkObject.Despawn(true);
    }

    // ---- everyone: reactions ------------------------------------------------------------------

    [Rpc(SendTo.Everyone)]
    void HitReactionRpc(Vector3 direction, float power, byte type)
    {
        hitStartTime = Time.time;
        hitDirection = direction;
        if (flash != null) flash.Flash();

        var fx = HitEffects.Instance;
        if (fx == null) return;
        Vector3 point = transform.position + Vector3.up * 0.5f;
        // Physical hits spark green (like a squish), magical ones violet - the same split as on a player.
        Color color = (DamageType)type == DamageType.Magical ? new Color(0.7f, 0.5f, 1f) : new Color(0.5f, 1f, 0.5f);
        fx.Burst(point, color, Mathf.RoundToInt(10 * power), 4f * power);
    }

    [Rpc(SendTo.Everyone)]
    void DeathRpc()
    {
        dead = true;
        visual.gameObject.SetActive(false);
        controller.enabled = false;

        var fx = HitEffects.Instance;
        if (fx != null) fx.Burst(transform.position + Vector3.up * 0.4f, new Color(0.4f, 0.9f, 0.4f), 34, 6.5f, 0.18f);
    }

    // ---- everyone: animation ------------------------------------------------------------------

    void Animate()
    {
        if (dead) return;

        float dt = Time.deltaTime;
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        if (dt > 0f)
        {
            float ratio = delta.magnitude / dt / wanderSpeed;
            speedRatio = Mathf.MoveTowards(speedRatio, Mathf.Clamp01(ratio), 6f * dt);
        }

        // Hop while moving, breathe while resting.
        hopPhase += dt * 7f * speedRatio;
        float hop = Mathf.Abs(Mathf.Sin(hopPhase));
        float idle = Mathf.Sin(Time.time * 2f) * 0.03f * (1f - speedRatio);
        float stretch = 1f + idle + (hop - 0.5f) * 0.25f * speedRatio;

        // Hit: a damped squash wobble and a lean away from the punch.
        float t = Time.time - hitStartTime;
        float decay = t < 1f ? Mathf.Exp(-6f * t) : 0f;
        float wobble = Mathf.Cos(t * 32f);
        stretch -= 0.3f * decay * wobble;

        visual.localScale = new Vector3(visualScale.x / Mathf.Sqrt(stretch), visualScale.y * stretch, visualScale.z / Mathf.Sqrt(stretch));
        visual.localPosition = visualPos + Vector3.up * (hop * 0.18f * speedRatio);

        Vector3 local = transform.InverseTransformDirection(hitDirection);
        float lean = decay * (0.6f + 0.4f * wobble) * 35f;
        visual.localRotation = Quaternion.Euler(local.z * lean, 0f, -local.x * lean);
    }
}
