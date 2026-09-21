using System.Collections.Generic;
using UnityEngine;

// What the ability cards look like on a character, for everybody to see:
//  - Thorns: a ring of grey spikes sticking out of the body while the buff is on
//  - Fire dance: a ring of flames rising around the dancer
//  - Shield: a sparkling bubble around the character
//  - Rage: red flames streaming off the character
// Everything is built in code the first time it's needed.
[RequireComponent(typeof(PlayerAbilities))]
public class AbilityVisuals : MonoBehaviour
{
    [SerializeField] Material spikeMaterial;

    static Mesh coneMesh;

    PlayerAbilities abilities;
    Transform spikeRoot;
    PlayerController player;
    ParticleSystem fire, shieldSparkles, rageFlames, dashTrail;

    void Awake()
    {
        abilities = GetComponent<PlayerAbilities>();
        player = GetComponent<PlayerController>();
    }

    void Update()
    {
        UpdateSpikes(abilities.ThornsActive);
        UpdateFire(abilities.IsDancing);
        UpdateAura(ref shieldSparkles, abilities.ShieldActive, BuildShield);
        UpdateAura(ref rageFlames, abilities.RageActive, BuildRage);
        UpdateAura(ref dashTrail, player.IsDashing, BuildDashTrail);   // the Ctrl dash isn't a card but looks the same to everybody
    }

    // White-blue speed sparks left hanging in the air behind a dashing character.
    ParticleSystem BuildDashTrail()
    {
        var ps = NewAura("DashTrail", new Vector3(0f, 0.7f, 0f), Quaternion.identity,
            ParticleSystemSimulationSpace.World, 0.25f, 0.45f, 0.1f, 0.26f,
            new Color(0.6f, 0.9f, 1f), Color.white, 400f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;
        return ps;
    }

    // ---- shield and rage: particle auras, built the first time they are needed --------------------

    void UpdateAura(ref ParticleSystem aura, bool active, System.Func<ParticleSystem> build)
    {
        if (aura == null)
        {
            if (!active || HitEffects.Instance == null) return;
            aura = build();
        }

        var emission = aura.emission;
        if (emission.enabled != active)
        {
            emission.enabled = active;
            if (active && !aura.isPlaying) aura.Play();
        }
    }

    // A sparkling bubble of light hugging the character.
    ParticleSystem BuildShield()
    {
        var ps = NewAura("ShieldSparkles", new Vector3(0f, 0.9f, 0f), Quaternion.identity,
            ParticleSystemSimulationSpace.Local, 0.5f, 0.8f, 0.13f, 0.24f,
            new Color(0.35f, 0.85f, 1f), Color.white, 200f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.2f;
        shape.radiusThickness = 0f; // only the surface: a hollow bubble
        return ps;
    }

    // Red flames streaming up off the character.
    ParticleSystem BuildRage()
    {
        var ps = NewAura("RageFlames", new Vector3(0f, 0.15f, 0f), Quaternion.Euler(-90f, 0f, 0f),
            ParticleSystemSimulationSpace.World, 0.5f, 0.9f, 0.18f, 0.34f,
            new Color(1f, 0.15f, 0.1f), new Color(1f, 0.55f, 0.1f), 80f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.6f;
        shape.radiusThickness = 1f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        return ps;
    }

    ParticleSystem NewAura(string objectName, Vector3 localPosition, Quaternion localRotation,
        ParticleSystemSimulationSpace space, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
        Color colorA, Color colorB, float rate)
    {
        var fx = HitEffects.Instance;
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.gravityModifier = 0f;
        main.simulationSpace = space;
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.enabled = false;

        var shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = fx.SphereMesh;
        renderer.sharedMaterial = fx.SparkMaterial;
        return ps;
    }

    // ---- thorns -------------------------------------------------------------------------------

    void UpdateSpikes(bool active)
    {
        if (active && spikeRoot == null) BuildSpikes();
        if (spikeRoot == null) return;

        if (spikeRoot.gameObject.activeSelf != active) spikeRoot.gameObject.SetActive(active);
        if (active)
        {
            // They breathe in and out a little so the buff feels alive.
            float s = 1f + 0.08f * Mathf.Sin(Time.time * 8f);
            spikeRoot.localScale = Vector3.one * s;
        }
    }

    void BuildSpikes()
    {
        var body = transform.Find("Model/Body");
        if (body == null) return;

        spikeRoot = new GameObject("Spikes").transform;
        spikeRoot.SetParent(body, false);
        spikeRoot.localPosition = new Vector3(0f, 0.75f, 0f); // the middle of the round body

        Vector3 radii = new Vector3(0.66f, 0.61f, 0.61f);
        const int count = 30;
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));

        for (int i = 0; i < count; i++)
        {
            // Evenly spread directions over a sphere.
            float y = 1f - i / (float)(count - 1) * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            Vector3 d = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);

            // Keep the face (eyes) clear.
            if (d.z > 0.4f && d.y > -0.35f && d.y < 0.65f) continue;

            var spike = new GameObject("Spike");
            spike.transform.SetParent(spikeRoot, false);
            spike.AddComponent<MeshFilter>().sharedMesh = GetConeMesh();
            spike.AddComponent<MeshRenderer>().sharedMaterial = spikeMaterial;
            spike.transform.localPosition = Vector3.Scale(d, radii) - d * 0.06f; // base sunk into the body
            spike.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d);
            spike.transform.localScale = new Vector3(0.16f, 0.36f, 0.16f);
        }
    }

    // ---- fire dance ---------------------------------------------------------------------------

    void UpdateFire(bool dancing)
    {
        if (fire == null)
        {
            if (!dancing || HitEffects.Instance == null) return;
            BuildFire();
        }

        var emission = fire.emission;
        if (emission.enabled != dancing)
        {
            emission.enabled = dancing;
            if (dancing && !fire.isPlaying) fire.Play();
        }
    }

    void BuildFire()
    {
        var fx = HitEffects.Instance;
        var go = new GameObject("DanceFire");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.1f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // circle lies flat

        fire = go.AddComponent<ParticleSystem>();
        fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = fire.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);   // a wide ring needs bigger flames
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f), new Color(1f, 0.25f, 0.05f));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 900;

        // The ring is about 3 times longer than it used to be, so it needs about 3 times as many flames.
        var emission = fire.emission;
        emission.rateOverTime = 320f;
        emission.enabled = false;

        // A ring on the ground, a bit inside the damage radius so what burns looks like what hurts.
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = AbilityCatalog.DanceRadius - 0.4f;
        shape.radiusThickness = 0.15f;

        // Flames rise. (All three axes must use the same curve mode.)
        var velocity = fire.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var shrink = fire.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = fx.SphereMesh;
        renderer.sharedMaterial = fx.SparkMaterial;
    }

    // ---- a little cone mesh for the spikes (base on y=0, tip at y=1, radius 0.5) ---------------

    static Mesh GetConeMesh()
    {
        if (coneMesh != null) return coneMesh;

        const int segments = 12;
        const float r = 0.5f, h = 1f;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector3 n = new Vector3(c * h, r, s * h).normalized;
            verts.Add(new Vector3(c * r, 0f, s * r)); norms.Add(n);
            verts.Add(new Vector3(0f, h, 0f)); norms.Add(n);
        }
        for (int i = 0; i < segments; i++)
        {
            tris.Add(i * 2); tris.Add(i * 2 + 1); tris.Add((i + 1) * 2);
        }

        coneMesh = new Mesh { name = "SpikeCone" };
        coneMesh.SetVertices(verts);
        coneMesh.SetNormals(norms);
        coneMesh.SetTriangles(tris, 0);
        coneMesh.RecalculateBounds();
        return coneMesh;
    }
}
