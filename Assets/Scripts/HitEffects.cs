using UnityEngine;

// Shared hit effect: a burst of sparks. One instance lives in the scene.
public class HitEffects : MonoBehaviour
{
    public static HitEffects Instance { get; private set; }

    [SerializeField] Material sparkMaterial;
    Mesh sphereMesh;

    void Awake()
    {
        Instance = this;

        // Grab the built-in sphere mesh so sparks are little round bits rather than flat squares.
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        Destroy(primitive);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Shared with the ability visuals (fire ring around a dancer).
    public Material SparkMaterial => sparkMaterial;
    public Mesh SphereMesh => sphereMesh;

    // A ring of sparks racing outwards along the ground, ending about `radius` metres from the middle.
    public void Shockwave(Vector3 position, float radius, Color color, int count = 48)
    {
        var go = new GameObject("Shockwave");
        go.transform.position = position + Vector3.up * 0.15f;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // lay the circle flat: its normal points up

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        const float life = 0.4f;
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius / life * 0.85f, radius / life);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        // A circle emits outwards from its edge, in its own plane.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.35f;
        shape.radiusThickness = 0f;

        var shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = sphereMesh;
        renderer.sharedMaterial = sparkMaterial;

        ps.Play();
        Destroy(go, 2f);
    }

    // The opposite of a shockwave: a ring of sparks that starts `radius` metres out and rushes in to the middle.
    public void Implosion(Vector3 position, float radius, Color color, int count = 64)
    {
        var go = new GameObject("Implosion");
        go.transform.position = position + Vector3.up * 0.2f;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        const float life = 0.55f;
        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = life;
        main.startSpeed = -radius / life; // a circle shoots its sparks outwards; a negative speed sends them inwards
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0f;

        var shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = sphereMesh;
        renderer.sharedMaterial = sparkMaterial;

        ps.Play();
        Destroy(go, 2f);
    }

    // A jagged flash of light between two points that thins out and vanishes.
    public void Bolt(Vector3 from, Vector3 to, Color color)
    {
        var go = new GameObject("Bolt");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = sparkMaterial;
        line.startColor = line.endColor = color;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 along = to - from;
        int segments = Mathf.Clamp(Mathf.RoundToInt(along.magnitude / 1.1f), 4, 26);
        Vector3 side = Vector3.Cross(along.normalized, Vector3.up);
        side = side.sqrMagnitude > 0.001f ? side.normalized : Vector3.right;

        line.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, i / (float)segments);
            if (i > 0 && i < segments) p += side * Random.Range(-0.45f, 0.45f) + Vector3.up * Random.Range(-0.35f, 0.35f);
            line.SetPosition(i, p);
        }

        StartCoroutine(FadeBolt(line));
    }

    System.Collections.IEnumerator FadeBolt(LineRenderer line)
    {
        const float duration = 0.3f;
        const float width = 0.5f;
        float t = 0f;
        while (t < duration && line != null)
        {
            t += Time.deltaTime;
            line.widthMultiplier = Mathf.Lerp(width, 0f, t / duration);
            yield return null;
        }
        if (line != null) Destroy(line.gameObject);
    }

    public void Burst(Vector3 position, Color color, int count = 14, float speed = 5f, float size = 0.13f)
    {
        var go = new GameObject("HitBurst");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // it auto-plays; configure while stopped

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 1.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        var shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = sphereMesh;
        renderer.sharedMaterial = sparkMaterial;

        ps.Play();
        Destroy(go, 2f);
    }
}
