using UnityEngine;
using UnityEngine.UI;

// A glowing pad on the ground whose tiles shimmer through the rainbow in rolling waves.
// It counts the players standing on it (shown as a big number floating above) and, as soon as at least
// one player is inside, everything turns glowing green. Every client counts from where the players really
// are, so nothing has to be synced.
public class ColorZone : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField] Renderer[] tiles;
    [SerializeField] Vector2 halfSize = new Vector2(3f, 3f);   // half width (X) and depth (Z) of the pad
    [SerializeField] Transform countAnchor;                    // billboard root holding the number
    [SerializeField] Text countText;
    [SerializeField] Light glow;

    [Header("What pressing Play here does")]
    [SerializeField] MatchMode mode = MatchMode.Players;
    [Tooltip("If set, this text floats above the pad instead of the player count.")]
    [SerializeField] string caption = "";

    [Header("Look")]
    [SerializeField] float rainbowSpeed = 0.12f;   // hue cycles per second
    [SerializeField] float rainbowSpread = 0.07f;  // hue difference per metre, gives the rolling waves
    [SerializeField] Color idleNumberColor = Color.white;
    [SerializeField] Color activeNumberColor = new Color(0.5f, 1f, 0.55f);

    MaterialPropertyBlock block;
    Vector3[] tileOffsets;
    Vector3 anchorBaseScale;   // the canvas is scaled down (px -> m); the "pop" must multiply this, not replace it
    float active;        // 0..1 blend from rainbow to green
    float nextCount;
    int count = -1;
    float countChangeTime = -10f;

    void Awake()
    {
        block = new MaterialPropertyBlock();
        anchorBaseScale = countAnchor.localScale;
        tileOffsets = new Vector3[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
            tileOffsets[i] = tiles[i].transform.position - transform.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (Time.time >= nextCount)
        {
            nextCount = Time.time + 0.1f;
            int now = CountPlayers();
            if (now != count)
            {
                count = now;
                if (!HasCaption)
                {
                    countChangeTime = Time.time;
                    countText.text = count.ToString();
                }
            }
            if (HasCaption && countText.text != caption) countText.text = caption;
        }

        active = Mathf.MoveTowards(active, count > 0 ? 1f : 0f, 4f * dt);
        float t = Time.time;

        for (int i = 0; i < tiles.Length; i++)
        {
            Vector3 p = tileOffsets[i];

            // Idle: hue drifts over time and across the pad, so colors roll over the tiles.
            float hue = Mathf.Repeat(t * rainbowSpeed + (p.x + p.z) * rainbowSpread, 1f);
            Color rainbow = Color.HSVToRGB(hue, 0.85f, 1f);

            // Occupied: green with ripples spreading out from the middle.
            float ripple = 0.75f + 0.25f * Mathf.Sin(t * 5f - (Mathf.Abs(p.x) + Mathf.Abs(p.z)) * 1.2f);
            Color green = Color.HSVToRGB(0.33f, 0.9f, ripple);

            block.SetColor(BaseColor, Color.Lerp(rainbow, green, active));
            tiles[i].SetPropertyBlock(block);
        }

        // The light spilling onto the surroundings follows the same colors.
        if (glow != null)
        {
            Color idleGlow = Color.HSVToRGB(Mathf.Repeat(t * rainbowSpeed, 1f), 0.8f, 1f);
            glow.color = Color.Lerp(idleGlow, new Color(0.2f, 1f, 0.3f), active);
            glow.intensity = Mathf.Lerp(2f, 6f, active) * (0.92f + 0.08f * Mathf.Sin(t * 6f));
        }

        // The number: green when someone is inside, with a little pop whenever it changes; always faces the camera.
        countText.color = Color.Lerp(idleNumberColor, activeNumberColor, active);
        float pop = Mathf.Clamp01((Time.time - countChangeTime) / 0.25f);
        countAnchor.localScale = anchorBaseScale * Mathf.Lerp(1.35f, 1f, pop * pop);

        var cam = Camera.main;
        if (cam != null)
            countAnchor.rotation = Quaternion.LookRotation(countAnchor.position - cam.transform.position);
    }

    // True while the player on THIS machine stands on the pad (used for the "Play" prompt).
    public bool LocalPlayerInside { get; private set; }

    public MatchMode Mode => mode;
    bool HasCaption => !string.IsNullOrEmpty(caption);

    int CountPlayers()
    {
        int inside = 0;
        bool localInside = false;
        Vector3 center = transform.position;
        foreach (var player in FindObjectsByType<PlayerController>())
        {
            if (!player.IsSpawned || player.IsBot) continue;

            Vector3 d = player.transform.position - center;
            // A jump or a flip over the pad still counts, but flying far above it doesn't.
            if (Mathf.Abs(d.x) <= halfSize.x && Mathf.Abs(d.z) <= halfSize.y && d.y < 4f)
            {
                inside++;
                if (player.IsOwner) localInside = true;
            }
        }
        LocalPlayerInside = localInside;
        return inside;
    }
}
