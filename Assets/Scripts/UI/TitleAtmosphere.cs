using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The title diorama's weather and signs of life (TitleDiorama), in the board's own particle look
/// (BoardLookSettings.particleShader, as AmbientLife): chimney smoke over the towns (dark over the
/// Shadow's holds), embers rising off Mordor's lava, and now and then a flock of birds crossing
/// the board.
/// </summary>
public sealed class TitleAtmosphere : MonoBehaviour
{
    // Draw order on the board's sorting layer: over the miniatures (towns at 2, figures at 3).
    private const int ParticleOrder = 5;
    private const int BirdOrder = 200;

    private int sortingLayer;
    private Vector2 hex;
    private Vector3 centre;
    private Vector2 half;
    private Material particleMaterial;
    private ParticleSystem embers;
    private readonly List<Vector3> lava = new();
    private float emberBudget;

    private readonly List<(Transform body, Vector3 velocity, float phase, float age)> birds = new();
    private float nextFlock;

    private static Sprite birdSprite;

    public static TitleAtmosphere Create(Transform parent, int sortingLayer, Vector2 hexSize, Vector3 centre, Vector2 halfSize)
    {
        var go = new GameObject("TitleAtmosphere");
        go.transform.SetParent(parent, false);
        var atmosphere = go.AddComponent<TitleAtmosphere>();
        atmosphere.sortingLayer = sortingLayer;
        atmosphere.hex = hexSize;
        atmosphere.centre = centre;
        atmosphere.half = halfSize;
        Shader shader = BoardLookSettings.Instance.particleShader;
        if (shader != null) atmosphere.particleMaterial = new Material(shader) { name = "TitleParticle (runtime)" };
        atmosphere.nextFlock = Time.unscaledTime + 3f;
        return atmosphere;
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
    }

    // ── Sources ─────────────────────────────────────────────────────────────────────────────

    public void AddLava(Vector3 at) => lava.Add(at);

    /// <summary>Chimney smoke over a settlement, as AmbientLife: more plumes for bigger places.</summary>
    public void AddSmoke(Vector3 at, int plumes, bool dark)
    {
        if (particleMaterial == null) return;
        ParticleSystem system = CreateSystem("Smoke", ParticleOrder);
        system.transform.position = at + new Vector3(0f, hex.y * 0.15f, -0.01f);
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(hex.x * 0.05f, hex.x * 0.08f);
        main.startColor = dark ? new Color(0.12f, 0.11f, 0.11f, 0.38f) : new Color(0.74f, 0.72f, 0.68f, 0.26f);
        main.prewarm = true;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 0.8f * plumes;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(hex.x * 0.4f, hex.y * 0.25f, 0f);
        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.08f, 0.13f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.8f));
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.2f);
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.04f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.2f;
        system.Play();
    }

    // ── Running ─────────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
        EmitEmbers(dt);
        UpdateBirds(dt, now);
    }

    private void EmitEmbers(float dt)
    {
        if (lava.Count == 0 || particleMaterial == null) return;
        if (embers == null) embers = LooseSystem("Embers", 400);
        var emit = new ParticleSystem.EmitParams();
        emberBudget += dt * 26f;
        while (emberBudget >= 1f)
        {
            emberBudget -= 1f;
            Vector3 at = lava[Random.Range(0, lava.Count)];
            emit.position = at + new Vector3(Random.Range(-0.45f, 0.45f) * hex.x, Random.Range(-0.35f, 0.35f) * hex.y, -0.02f);
            emit.velocity = new Vector3(Random.Range(-0.05f, 0.08f), Random.Range(0.18f, 0.4f), 0f);
            emit.startSize = Random.Range(0.018f, 0.04f);
            emit.startLifetime = Random.Range(1.4f, 3f);
            emit.startColor = Color.Lerp(new Color(1f, 0.36f, 0.06f, 0.95f), new Color(1f, 0.75f, 0.3f, 1f), Random.value);
            embers.Emit(emit, 1);
        }
    }

    private void UpdateBirds(float dt, float now)
    {
        for (int i = birds.Count - 1; i >= 0; i--)
        {
            (Transform body, Vector3 velocity, float phase, float age) = birds[i];
            age += dt;
            phase += dt * 11f;
            body.position += velocity * dt;
            float size = body.localScale.x;
            body.localScale = new Vector3(size, size * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(phase))), 1f);
            if (age > 60f || Mathf.Abs(body.position.x - centre.x) > half.x + 3f)
            {
                Destroy(body.gameObject);
                birds.RemoveAt(i);
                continue;
            }
            birds[i] = (body, velocity, phase, age);
        }
        if (now < nextFlock) return;
        nextFlock = now + Random.Range(9f, 16f);
        bool fromLeft = Random.value < 0.5f;
        var start = new Vector3(centre.x + (fromLeft ? -half.x - 1f : half.x + 1f), centre.y + Random.Range(-half.y, half.y) * 0.7f, -0.06f);
        var heading = new Vector3(fromLeft ? 1f : -1f, Random.Range(-0.25f, 0.25f), 0f).normalized;
        var side = new Vector3(-heading.y, heading.x, 0f);
        int count = Random.Range(4, 9);
        for (int i = 0; i < count; i++)
        {
            int rank = (i + 1) / 2;
            float wing = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
            Vector3 at = start - heading * rank * 0.24f + side * wing * rank * 0.17f;
            var go = new GameObject("Bird", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            float s = Random.Range(0.09f, 0.12f);
            go.transform.localScale = new Vector3(s, s, 1f);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = BirdSprite();
            sr.color = new Color(0.08f, 0.07f, 0.06f, 0.85f);
            sr.sortingLayerID = sortingLayer;
            sr.sortingOrder = BirdOrder;
            birds.Add((go.transform, heading * Random.Range(0.75f, 0.95f), Random.value * 6f, 0f));
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private ParticleSystem LooseSystem(string name, int max)
    {
        ParticleSystem system = CreateSystem(name, ParticleOrder);
        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.maxParticles = max;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.25f);
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.4f));
        system.Play();
        return system;
    }

    private ParticleSystem CreateSystem(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = order;
        return system;
    }

    // Alpha in quickly, out slowly.
    private static ParticleSystem.MinMaxGradient Fade(float fadeIn)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient);
    }

    // A small "v" silhouette (as AmbientLife's birds).
    private static Sprite BirdSprite()
    {
        if (birdSprite != null) return birdSprite;
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TitleBird" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float wingY = 0.35f + 0.5f * Mathf.Pow(Mathf.Abs(u), 0.7f) - 0.25f * u * u;
                float thickness = Mathf.Lerp(0.16f, 0.03f, Mathf.Abs(u));
                float a = Mathf.Abs(u) > 0.97f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(v - wingY) / thickness);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        birdSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        return birdSprite;
    }
}
