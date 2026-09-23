using UnityEngine;

// A thin arm or leg: a cylinder that is stretched between two points (the shoulder or hip on the body and the
// hand or shoe that CartoonWalker animates) every frame, so the limb always joins the two however they move.
public class LimbLinker : MonoBehaviour
{
    [SerializeField] Transform from;
    [SerializeField] Transform to;
    [SerializeField] float thickness = 0.14f;

    void LateUpdate()
    {
        if (from == null || to == null) return;

        Vector3 a = from.position, b = to.position;
        Vector3 direction = b - a;
        float length = direction.magnitude;
        if (length < 0.001f) return;

        // A Unity cylinder is 2 units tall along its Y axis and its parent might be scaled, so work in local units.
        float parentScale = transform.parent != null ? Mathf.Max(0.0001f, transform.parent.lossyScale.y) : 1f;
        transform.position = (a + b) * 0.5f;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
        transform.localScale = new Vector3(thickness, length * 0.5f / parentScale, thickness);
    }
}
