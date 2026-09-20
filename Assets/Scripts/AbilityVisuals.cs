using System.Collections.Generic;
using UnityEngine;

// What the ability cards look like on a character, for everybody to see:
//  - Thorns: a ring of grey spikes sticking out of the body while the buff is on
//  - Fire dance: a ring of flames rising around the dancer
// Everything is built in code the first time it's needed.
[RequireComponent(typeof(PlayerAbilities))]
public class AbilityVisuals : MonoBehaviour
{
    [SerializeField] Material spikeMaterial;

    static Mesh coneMesh;

    PlayerAbilities abilities;
    Transform spikeRoot;
    ParticleSystem fire;

    void Awake() => abilities = GetComponent<PlayerAbilities>();

    void Update()
    {
        UpdateSpikes(abilities.ThornsActive);
        UpdateFire(abilities.IsDancing);
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
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f), new Color(1f, 0.25f, 0.05f));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        var emission = fire.emission;
        emission.rateOverTime = 90f;
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
