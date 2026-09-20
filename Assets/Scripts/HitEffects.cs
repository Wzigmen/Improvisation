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
