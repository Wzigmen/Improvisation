using UnityEngine;

// Briefly tints every renderer of this object white when it gets hit, then restores the original colors.
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField] Color flashColor = Color.white;
    [SerializeField] float duration = 0.28f;

    Renderer[] renderers;
    Color[] baseColors;
    float timer;
    bool active;

    public void Flash()
    {
        if (!active)
        {
            // Read the colors at flash time: players get their color assigned after spawning.
            renderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                baseColors[i] = renderers[i].material.GetColor(BaseColor);
        }

        timer = duration;
        active = true;
    }

    void Update()
    {
        if (!active) return;

        timer -= Time.deltaTime;
        bool done = timer <= 0f;
        float t = done ? 0f : timer / duration;
        float strength = t * t; // strong at the moment of impact, fades out fast

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].material.SetColor(BaseColor, Color.Lerp(baseColors[i], flashColor, strength));
        }

        if (done) active = false;
    }
}
