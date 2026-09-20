using UnityEngine;
using UnityEngine.UI;

// A hanging punching bag that can be hit forever. It is a small physics simulation instead of a canned
// animation: every punch adds a kick to a damped pendulum, so hits stack up and swings die out naturally.
// It is not a network object - every client runs the same simulation from the same hit events.
public class PunchingBag : MonoBehaviour
{
    public static PunchingBag Instance { get; private set; }

    [SerializeField] Transform swing;      // pivots at the hook
    [SerializeField] Transform visual;     // the bag itself, squashed on impact
    [SerializeField] Text counterText;
    [SerializeField] HitFlash flash;

    [Header("Swing")]
    [SerializeField] float swingFrequency = 4.2f;   // rad/s; ~1.5 s per swing
    [SerializeField] float swingDamping = 0.9f;     // per second: several visible swings before it settles
    [SerializeField] float hitKick = 3.4f;          // rad/s added per punch
    [SerializeField] float maxAngle = 75f;

    [Header("Twist and squash")]
    [SerializeField] float twistKick = 300f;        // deg/s
    [SerializeField] float squashKick = 9f;

    Vector2 angle;      // radians, towards +X / +Z
    Vector2 velocity;
    float twist, twistVelocity;
    float squash, squashVelocity;
    Vector3 visualScale;
    int hits;

    void Awake()
    {
        Instance = this;
        visualScale = visual.localScale;
        UpdateCounter();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f); // keep the spring stable on frame hitches

        // Damped pendulum on two axes.
        Vector2 accel = -swingFrequency * swingFrequency * angle - swingDamping * velocity;
        velocity += accel * dt;
        angle += velocity * dt;

        float max = maxAngle * Mathf.Deg2Rad;
        if (angle.magnitude > max)
        {
            angle = angle.normalized * max;
            velocity *= 0.5f; // soak up energy at the limit
        }

        // Slow twist around the chain, springs back with a wobble.
        twistVelocity += (-81f * twist - 1.4f * twistVelocity) * dt; // 9 rad/s spring, in degrees
        twist += twistVelocity * dt;

        // Fast squash spring: dents on impact, overshoots, settles.
        squashVelocity += (-22f * 22f * squash - 7f * squashVelocity) * dt;
        squash += squashVelocity * dt;

        // Displacement towards +X is a rotation about +Z; towards +Z is a rotation about -X.
        Quaternion tilt = Quaternion.AngleAxis(angle.x * Mathf.Rad2Deg, Vector3.forward)
                        * Quaternion.AngleAxis(-angle.y * Mathf.Rad2Deg, Vector3.right);
        swing.localRotation = tilt * Quaternion.AngleAxis(twist, Vector3.up);

        visual.localScale = new Vector3(
            visualScale.x * (1f + 0.5f * squash),
            visualScale.y * (1f - 0.7f * squash),
            visualScale.z * (1f + 0.5f * squash));
    }

    // Called on every client when somebody punches the bag.
    public void ApplyHit(Vector3 point, Vector3 direction, float power)
    {
        Vector3 d = direction;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) d = Vector3.forward;
        d.Normalize();

        velocity += new Vector2(d.x, d.z) * (hitKick * power);
        twistVelocity += Random.Range(-1f, 1f) * twistKick * power;
        squashVelocity += squashKick * power;

        hits++;
        UpdateCounter();
        if (flash != null) flash.Flash();

        var fx = HitEffects.Instance;
        if (fx != null)
            fx.Burst(point, new Color(1f, 0.85f, 0.2f), Mathf.RoundToInt(12 * power), 5f * power);
    }

    void UpdateCounter()
    {
        if (counterText != null) counterText.text = "Ударов: " + hits;
    }
}
