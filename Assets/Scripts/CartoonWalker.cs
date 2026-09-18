using UnityEngine;

// Procedural walk animation for the round cartoon character:
// feet step, hands swing, body bobs and squashes a little. Sprinting makes it faster and bigger.
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

    [Header("Idle")]
    [SerializeField] float breathSpeed = 2f;
    [SerializeField] float breathAmount = 0.02f;

    Vector3 bodyPos, bodyScale, leftFootPos, rightFootPos, leftHandPos, rightHandPos;
    float phase;

    void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerController>();
        bodyPos = body.localPosition;
        bodyScale = body.localScale;
        leftFootPos = leftFoot.localPosition;
        rightFootPos = rightFoot.localPosition;
        leftHandPos = leftHand.localPosition;
        rightHandPos = rightHand.localPosition;
    }

    void Update()
    {
        float walk = player.Speed01;
        float sprint = player.SprintAmount;
        float stride = walk * (1f + sprint * sprintStride);
        phase += Time.deltaTime * stepsPerSecond * Mathf.PI * 2f * walk * (1f + sprint * sprintStepRate);

        float s = Mathf.Sin(phase);
        float c = Mathf.Cos(phase);

        // Feet: swing forward/back, lift while moving forward.
        leftFoot.localPosition = leftFootPos + new Vector3(0f, Mathf.Max(0f, c) * stepHeight * walk, s * stepLength * stride);
        rightFoot.localPosition = rightFootPos + new Vector3(0f, Mathf.Max(0f, -c) * stepHeight * walk, -s * stepLength * stride);

        // Hands swing opposite to the feet.
        leftHand.localPosition = leftHandPos + new Vector3(0f, 0f, -s * handSwing * stride);
        rightHand.localPosition = rightHandPos + new Vector3(0f, 0f, s * handSwing * stride);

        // Body bob + squash & stretch, plus a soft breathing idle.
        float bob = Mathf.Abs(s);
        float breath = Mathf.Sin(Time.time * breathSpeed) * breathAmount * (1f - walk);
        body.localPosition = bodyPos + Vector3.up * (bob * bobHeight * walk);
        float stretch = 1f + (bob - 0.5f) * 0.08f * walk + breath;
        body.localScale = new Vector3(bodyScale.x / Mathf.Sqrt(stretch), bodyScale.y * stretch, bodyScale.z / Mathf.Sqrt(stretch));

        // Lean into the walking direction, more when sprinting.
        body.localRotation = Quaternion.Euler(walk * (6f + sprint * sprintLean), 0f, s * 3f * walk);
    }
}
