using UnityEngine;

// Gives every player a different color, derived from their client id, so nothing has to be synced.
public class PlayerAppearance : MonoBehaviour
{
    [SerializeField] Renderer[] tinted;

    public void SetPlayerColor(ulong clientId)
    {
        // Golden-ratio hue steps keep neighbouring ids visually distinct; id 0 (the host) stays orange.
        float hue = Mathf.Repeat(0.07f + clientId * 0.61803f, 1f);
        Color color = Color.HSVToRGB(hue, 0.75f, 1f);
        foreach (var r in tinted)
        {
            if (r != null) r.material.SetColor("_BaseColor", color);
        }
    }
}
