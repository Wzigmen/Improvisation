using UnityEngine;

// Gives every player a different color, derived from their client id, so nothing has to be synced.
public class PlayerAppearance : MonoBehaviour
{
    [SerializeField] Renderer[] tinted;

    // Golden-ratio hue steps keep neighbouring ids visually distinct; id 0 (the host) is a candy red.
    public static Color ColorFor(ulong clientId)
    {
        float hue = Mathf.Repeat(0.0f + clientId * 0.61803f, 1f);
        return Color.HSVToRGB(hue, 0.85f, 0.92f);
    }

    public void SetPlayerColor(ulong clientId)
    {
        Color color = ColorFor(clientId);
        foreach (var r in tinted)
        {
            if (r != null) r.material.SetColor("_BaseColor", color);
        }
    }
}
