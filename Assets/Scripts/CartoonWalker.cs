using UnityEngine;

// Procedural animation for the round cartoon character:
// feet step, hands swing, body bobs and squashes a little. Sprinting makes it faster and bigger,
// jumping raises the hands and stretches the body, a flip jump does a full forward somersault,
// punches wind up and thrust a fist, and taking a hit whips the body away from the blow.
public class CartoonWalker : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField] Transform body;
    [SerializeField] Transform leftFoot;
    [SerializeField] Transform rightFoot;
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;

    [Header("Walk")]
    [SerializeField] float stepsPerSecond = 2.4f;
    [SerializeField] float stepLength = 0.35f;
    [SerializeField] float stepHeight = 0.18f;
    [SerializeField] float handSwing = 0.3f;
    [SerializeField] float bobHeight = 0.07f;

    [Header("Sprint")]
    [SerializeField] float sprintStepRate = 0.7f;
    [SerializeField] float sprintStride = 0.3f;
    [SerializeField] float sprintLean = 8f;

    [Header("Air")]
    [SerializeField] float landingSquashTime = 0.18f;
    [SerializeField] float landingSquash = 0.22f;
    [SerializeField] Vector3 flipCenter = new Vector3(0f, 0.75f, 0f); // somersault pivots around the body's middle

    [Header("Punch")]
    [SerializeField] Vector3 punchWindup = new Vector3(0.13f, -0.05f, -0.35f);  // hand offset while winding up (right hand)
    [SerializeField] Vector3 punchStrike = new Vector3(-0.47f, 0.15f, 1.15f);   // hand offset at full extension (right hand)
    [SerializeField] float punchFistGrowth = 0.55f;

    [Header("Hit reaction")]
    [SerializeField] float hitLean = 24f;
    [SerializeField] float hitDecay = 5f;
    [SerializeField] float hitWobbleSpeed = 28f;

    [Header("Idle")]
    [SerializeField] float breathSpeed = 2f;
    [SerializeField] float breathAmount = 0.02f;

    Transform model;
    Vector3 modelPos, bodyPos, bodyScale, leftFootPos, rightFootPos, leftHandPos, rightHandPos, handScale;
    float phase;
    float air;          // 0..1 blend into the airborne pose
    float tuck;         // 0..1 blend into the curled-up somersault pose
    float landTimer;
    float dance;        // 0..1 blend into the dance
    float dancePhase;
    float knockedOut;   // 0..1 blend into the lying-down pose
    float dash;         // 0..1 blend into the dash pose (leaning into it, arms and feet trailing behind)
    float flipAngle;
    bool wasAirborne;

    void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerController>();
        model = body.parent; // parent of Body, Feet and Hands
        modelPos = model.localPosition;
        bodyPos = body.localPosition;
        bodyScale = body.localScale;
        leftFootPos = leftFoot.localPosition;
        rightFootPos = rightFoot.localPosition;
        leftHandPos = leftHand.localPosition;
        rightHandPos = rightHand.localPosition;
        handScale = rightHand.localScale;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool airborne = player.Airborne;
        bool flipping = player.IsFlipping;

        air = Mathf.MoveTowards(air, airborne ? 1f : 0f, 10f * dt);
        tuck = Mathf.MoveTowards(tuck, flipping ? 1f : 0f, 12f * dt);
        dash = Mathf.MoveTowards(dash, player.IsDashing ? 1f : 0f, 14f * dt);
        if (wasAirborne && !airborne) landTimer = landingSquashTime;
        wasAirborne = airborne;
        landTimer = Mathf.Max(0f, landTimer - dt);

        float walk = player.Speed01 * (1f - air); // legs stop pedalling in the air
        float sprint = player.SprintAmount;
        float stride = walk * (1f + sprint * sprintStride);
        phase += dt * stepsPerSecond * Mathf.PI * 2f * walk * (1f + sprint * sprintStepRate);

        float s = Mathf.Sin(phase);
        float c = Mathf.Cos(phase);

        // Fire dance: sway, bounce, stomp and wave the arms in turn.
        bool dancing = player.Abilities != null && player.Abilities.IsDancing;
        dance = Mathf.MoveTowards(dance, dancing ? 1f : 0f, 6f * dt);
        dancePhase += dt * 9f * dance;
        float dSin = Mathf.Sin(dancePhase);
        Vector3 danceFootL = new Vector3(0f, Mathf.Max(0f, dSin) * 0.22f * dance, 0f);
        Vector3 danceFootR = new Vector3(0f, Mathf.Max(0f, -dSin) * 0.22f * dance, 0f);

        // Feet: swing forward/back, lift while moving forward; dangle up in the air.
        // (In a dash the whole model tips forward around its middle, see UpdateModelRotation, so the feet trail behind.)
        Vector3 feetAir = new Vector3(0f, 0.2f * air * (1f - dash) + 0.12f * tuck, -0.12f * dash);
        leftFoot.localPosition = leftFootPos + feetAir + danceFootL + new Vector3(0f, Mathf.Max(0f, c) * stepHeight * walk, s * stepLength * stride);
        rightFoot.localPosition = rightFootPos + feetAir + danceFootR + new Vector3(0f, Mathf.Max(0f, -c) * stepHeight * walk, -s * stepLength * stride);

        // Hands swing opposite to the feet; fly up when jumping, hug the body when somersaulting.
        float handsUp = 0.3f * air * (1f - tuck) * (1f - dash);
        Vector3 leftHandOffset = new Vector3(0.28f * tuck, handsUp, -s * handSwing * stride);
        Vector3 rightHandOffset = new Vector3(-0.28f * tuck, handsUp, s * handSwing * stride);
        // Dash: both arms swept back like a diver's.
        leftHandOffset += new Vector3(0.1f * dash, 0f, -0.5f * dash);
        rightHandOffset += new Vector3(-0.1f * dash, 0f, -0.5f * dash);
        leftHandOffset += new Vector3(-0.1f * dance, (0.5f + 0.5f * dSin) * 0.85f * dance, 0f);
        rightHandOffset += new Vector3(0.1f * dance, (0.5f - 0.5f * dSin) * 0.85f * dance, 0f);

        // Punch pose (offsets for the punching hand, fist size, torso twist and lean).
        GetPunchPose(out Vector3 punchOffset, out Vector3 guardOffset, out float fist, out float torsoYaw, out float punchLean);
        bool rightPunches = player.AttackSide == 0;
        leftHandOffset += rightPunches ? guardOffset : punchOffset;
        rightHandOffset += rightPunches ? punchOffset : guardOffset;
        float fistGrowth = punchFistGrowth * (player.AttackHeavy ? 1.7f : 1f);
        leftHand.localScale = handScale * (1f + (rightPunches ? 0f : fistGrowth * fist));
        rightHand.localScale = handScale * (1f + (rightPunches ? fistGrowth * fist : 0f));

        // Hit reaction: whipped away from the blow, arms flung up, body wobbling.
        float hitTime = player.HitTime;
        float hitAmount = hitTime < 1f ? Mathf.Exp(-hitDecay * hitTime) : 0f;
        float hitWobble = Mathf.Cos(hitTime * hitWobbleSpeed);
        Vector3 hitLocal = transform.InverseTransformDirection(player.HitDirection);
        leftHandOffset += new Vector3(0f, 0.25f * hitAmount, -hitLocal.z * 0.3f * hitAmount);
        rightHandOffset += new Vector3(0f, 0.25f * hitAmount, -hitLocal.z * 0.3f * hitAmount);

        leftHand.localPosition = leftHandPos + leftHandOffset;
        rightHand.localPosition = rightHandPos + rightHandOffset;

        // Body bob + squash & stretch, plus a soft breathing idle.
        float bob = Mathf.Abs(s);
        float breath = Mathf.Sin(Time.time * breathSpeed) * breathAmount * (1f - walk) * (1f - air);
        body.localPosition = bodyPos + Vector3.up * (bob * bobHeight * walk + Mathf.Abs(dSin) * 0.12f * dance);
        float landing = landingSquashTime > 0f ? landTimer / landingSquashTime : 0f;
        float stretch = 1f + (bob - 0.5f) * 0.08f * walk + breath + 0.1f * air * (1f - tuck) - 0.08f * tuck
                        - landingSquash * landing - 0.22f * hitAmount * hitWobble + 0.1f * Mathf.Abs(dSin) * dance
                        + 0.3f * dash; // stretched out along the direction of the dash
        body.localScale = new Vector3(bodyScale.x / Mathf.Sqrt(stretch), bodyScale.y * stretch, bodyScale.z / Mathf.Sqrt(stretch));

        // Lean into the walking direction (more when sprinting) and the punch; tip away from a hit.
        Quaternion lean = Quaternion.Euler(walk * (6f + sprint * sprintLean) + punchLean, 0f, s * 3f * walk);
        float hitTilt = hitAmount * (0.6f + 0.4f * hitWobble) * hitLean;
        body.localRotation = lean * Quaternion.Euler(hitLocal.z * hitTilt, 0f, -hitLocal.x * hitTilt)
                                  * Quaternion.Euler(0f, 0f, dSin * 16f * dance);

        torsoYaw += Mathf.Sin(dancePhase * 0.5f) * 55f * dance; // the dancer twirls back and forth
        UpdateModelRotation(flipping, torsoYaw, dt);
    }

    // Wind-up -> fast strike -> hold -> recover, as a pose for whichever hand is punching.
    void GetPunchPose(out Vector3 punchOffset, out Vector3 guardOffset, out float fist, out float torsoYaw, out float punchLean)
    {
        punchOffset = Vector3.zero;
        guardOffset = Vector3.zero;
        fist = 0f;
        torsoYaw = 0f;
        punchLean = 0f;

        float duration = Mathf.Max(0.01f, player.AttackDuration);
        float u = player.AttackTime / duration;
        if (u < 0f || u >= 1f) return;

        const float windupEnd = 0.24f, strikeEnd = 0.43f, holdEnd = 0.62f;
        if (u < windupEnd)
        {
            float p = 1f - (1f - u / windupEnd) * (1f - u / windupEnd);
            punchOffset = punchWindup * p;
            torsoYaw = 18f * p;
            punchLean = -6f * p;
        }
        else if (u < strikeEnd)
        {
            float q = (u - windupEnd) / (strikeEnd - windupEnd);
            float p = 1f - (1f - q) * (1f - q) * (1f - q); // explosive start
            punchOffset = Vector3.Lerp(punchWindup, punchStrike, p);
            torsoYaw = Mathf.Lerp(18f, -28f, p);
            punchLean = Mathf.Lerp(-6f, 14f, p);
            fist = p;
        }
        else if (u < holdEnd)
        {
            punchOffset = punchStrike;
            torsoYaw = -28f;
            punchLean = 14f;
            fist = 1f;
        }
        else
        {
            float p = Mathf.SmoothStep(0f, 1f, (u - holdEnd) / (1f - holdEnd));
            punchOffset = Vector3.Lerp(punchStrike, Vector3.zero, p);
            torsoYaw = Mathf.Lerp(-28f, 0f, p);
            punchLean = Mathf.Lerp(14f, 0f, p);
            fist = 1f - p;
        }

        // The idle hand is pulled up towards the chest as a guard.
        guardOffset = new Vector3(0.2f, 0.12f, 0.3f) * Mathf.Sin(u * Mathf.PI);

        // The offsets above are for a right-hand punch (+X hand extends, the left guard hand moves inward
        // towards +X); a left-hand punch is the mirror image.
        if (player.AttackSide == 1)
        {
            punchOffset.x = -punchOffset.x;
            guardOffset.x = -guardOffset.x;
            torsoYaw = -torsoYaw;
        }

        // The heavy punch (ability card 1): a bigger wind-up, a longer reach and a harder twist.
        if (player.AttackHeavy)
        {
            punchOffset *= 1.25f;
            torsoYaw *= 1.5f;
            punchLean *= 1.6f;
        }
    }

    // Twists the torso for punches and rotates the whole model forward around its middle for a flip.
    void UpdateModelRotation(bool flipping, float torsoYaw, float dt)
    {
        if (flipping)
        {
            flipAngle = 360f * player.FlipProgress;
        }
        else if (flipAngle > 0f)
        {
            // Finish the turn quickly instead of snapping if we land a hair early.
            float target = flipAngle > 180f ? 360f : 0f;
            flipAngle = Mathf.MoveTowards(flipAngle, target, 900f * dt);
            if (flipAngle >= 360f) flipAngle = 0f;
        }

        // Knocked out of a fight: keel over forwards and stay down.
        knockedOut = Mathf.MoveTowards(knockedOut, player.IsDead ? 1f : 0f, 3f * dt);

        // A dash dives forward: the whole character tips over around its middle.
        Quaternion rotation = Quaternion.Euler(flipAngle + 80f * knockedOut + 58f * dash, torsoYaw, 0f);
        model.localRotation = rotation;
        model.localPosition = modelPos + flipCenter - rotation * flipCenter + Vector3.down * (0.15f * knockedOut);
    }
}
