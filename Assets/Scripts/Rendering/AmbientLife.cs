using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small signs of life on the board (tuned in BoardLookSettings), started by BoardLook.Apply:
///  • chimney smoke over every settlement in sight — more plumes for bigger places, dark smoke
///    over orc, troll and undead holds;
///  • now and then a small flock of birds crossing the view, with faint shadows on the ground;
///  • dust puffs at a miniature's base when it sets off or stops (Dust, from Character3DView).
/// Water glints are drawn by the fog overlay (FogOfWar.shader), which already covers the board.
/// </summary>
public class AmbientLife : MonoBehaviour
{
    private const float RefreshInterval = 1f;
    private const int SortingOrder = 1;        // with the units on the Hexes layer
    private const int BirdSortingOrder = 200;  // above units and labels
    private const int BirdShadowOrder = 0;

    private static AmbientLife instance;

    private Material particleMaterial;
    private ParticleSystem dust;
    private readonly Dictionary<Hex, ParticleSystem> smokeByHex = new();
    private readonly List<Hex> scratch = new();
    private float nextRefresh;

    private class Bird
    {
        public Transform Body;
        public Transform Shadow;
        public Vector3 Velocity;
        public float Phase;
        public float Age;
    }

    private readonly List<Bird> birds = new();
    private Sprite birdSprite;
    private float nextFlock;

    public static void Ensure()
    {
        if (instance != null) return;
        Shader shader = BoardLookSettings.Instance.particleShader;
        if (shader == null) return;
        var go = new GameObject("AmbientLife");
        instance = go.AddComponent<AmbientLife>();
        instance.particleMaterial = new Material(shader) { name = "AmbientParticle (runtime)" };
        instance.nextFlock = Time.time + Random.Range(4f, 10f);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (particleMaterial != null) Destroy(particleMaterial);
    }

    // ── Dust ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A puff of dust around a miniature's base; radius = the base's world radius.</summary>
    public static void Dust(Vector3 position, float radius, bool landing)
    {
        if (instance == null || !BoardLookSettings.Instance.dust || radius <= 0f) return;
        ParticleSystem system = instance.EnsureDust();
        int count = landing ? 10 : 6;
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            // The base is an ellipse on screen (its disc is tilted toward the camera).
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.45f, 0f);
            emit.position = position + dir * radius * 0.9f;
            emit.velocity = dir * radius * Random.Range(0.8f, 1.6f) + Vector3.up * radius * 0.3f;
            emit.startSize = radius * Random.Range(0.45f, 0.8f) * (landing ? 1.2f : 1f);
            emit.startLifetime = Random.Range(0.5f, 0.9f);
            emit.startColor = new Color(0.64f, 0.57f, 0.45f, landing ? 0.5f : 0.38f);
            system.Emit(emit, 1);
        }
    }

    // ── Selection motes ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gold motes drifting up from a selected miniature's base, called every frame while it has
    /// focus (amount = 0..1: hovered units get fewer). Shares the dust system's particles.
    /// </summary>
    public static void Motes(Vector3 position, float radius, float amount)
    {
        if (instance == null || radius <= 0f || amount <= 0.01f) return;
        instance.moteBudget += Time.deltaTime * 14f * amount;
        if (instance.moteBudget < 1f) return;
        ParticleSystem system = instance.EnsureDust();
        var emit = new ParticleSystem.EmitParams();
        while (instance.moteBudget >= 1f)
        {
            instance.moteBudget -= 1f;
            float a = Random.value * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Random.value) * radius * 0.85f;
            emit.position = position + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.45f, -0.01f);
            emit.velocity = new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.25f, 0.45f), 0f) * radius * 3f;
            emit.startSize = radius * Random.Range(0.06f, 0.12f);
            emit.startLifetime = Random.Range(0.8f, 1.4f);
            emit.startColor = new Color(1f, 0.85f, 0.45f, 0.9f);
            system.Emit(emit, 1);
        }
    }

    private float moteBudget;

    private ParticleSystem EnsureDust()
    {
        if (dust != null) return dust;
        dust = CreateSystem("Dust", transform);
        ParticleSystem.MainModule main = dust.main;
        main.loop = false;
        main.maxParticles = 300;
        ParticleSystem.EmissionModule emission = dust.emission;
        emission.enabled = false;
        ParticleSystem.SizeOverLifetimeModule size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.4f));
        ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.1f);
        ParticleSystem.LimitVelocityOverLifetimeModule limit = dust.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0f;
        limit.dampen = 0.08f;
        dust.Play();
        return dust;
    }

    // ── Smoke ───────────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        UpdateBirds();
        ShowSmokeForZoom();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        RefreshSmoke();
    }

    private void RefreshSmoke()
    {
        Board board = Board.Instance;
        bool enabled = BoardLookSettings.Instance.smoke;
        scratch.Clear();
        foreach (KeyValuePair<Hex, ParticleSystem> pair in smokeByHex)
        {
            Hex hex = pair.Key;
            if (!enabled || hex == null || !WantsSmoke(hex)) scratch.Add(hex);
        }
        foreach (Hex hex in scratch)
        {
            if (smokeByHex.TryGetValue(hex, out ParticleSystem system) && system != null) Destroy(system.gameObject);
            smokeByHex.Remove(hex);
        }
        if (!enabled || board == null || board.hexes == null) return;

        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null || smokeByHex.ContainsKey(hex) || !WantsSmoke(hex)) continue;
            smokeByHex[hex] = CreateSmoke(hex, board);
        }
    }

    // Smoke is a close-up detail: zoomed out, the puffs read as stray clouds over the map.
    private const float SmokeMaxZoom = 9f;
    private bool smokeShown = true;

    private void ShowSmokeForZoom()
    {
        Camera cam = Camera.main;
        bool show = cam != null && cam.orthographicSize <= SmokeMaxZoom;
        if (show == smokeShown) return;
        smokeShown = show;
        foreach (ParticleSystem system in smokeByHex.Values)
            if (system != null) system.GetComponent<ParticleSystemRenderer>().enabled = show;
    }

    private static bool WantsSmoke(Hex hex) => hex.HasAnyPC() && hex.ShouldShowPcVisual();

    private ParticleSystem CreateSmoke(Hex hex, Board board)
    {
        PC pc = hex.GetPCData();
        int plumes = pc.citySize switch
        {
            PCSizeEnum.camp => 1,
            PCSizeEnum.village => 1,
            PCSizeEnum.town => 2,
            PCSizeEnum.majorTown => 3,
            _ => 4,
        };
        RacesEnum race = pc.owner != null ? pc.owner.race : RacesEnum.Common;
        bool dark = race is RacesEnum.Orc or RacesEnum.Goblin or RacesEnum.Troll or RacesEnum.Nazgul or RacesEnum.Undead or RacesEnum.Balrog;
        Color tint = dark ? new Color(0.14f, 0.13f, 0.13f, 0.3f) : new Color(0.72f, 0.71f, 0.68f, 0.2f);

        ParticleSystem system = CreateSystem($"Smoke {pc.pcName}", transform);
        system.transform.position = hex.transform.position + new Vector3(0f, board.hexSize.y * 0.15f, 0f);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(board.hexSize.x * 0.03f, board.hexSize.x * 0.05f);
        main.startColor = tint;
        main.prewarm = true;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0.7f * plumes;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(board.hexSize.x * 0.45f, board.hexSize.y * 0.3f, 0f);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.6f));

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.2f);

        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.03f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.2f;
        system.GetComponent<ParticleSystemRenderer>().enabled = smokeShown;

        system.Play();
        return system;
    }

    // ── Birds ───────────────────────────────────────────────────────────────────────────────

    private void UpdateBirds()
    {
        Camera cam = Camera.main;
        float dt = Time.deltaTime;
        for (int i = birds.Count - 1; i >= 0; i--)
        {
            Bird bird = birds[i];
            bird.Age += dt;
            bird.Phase += dt * 11f;
            bird.Body.position += bird.Velocity * dt;
            float flap = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(bird.Phase));
            float size = bird.Body.localScale.x;
            bird.Body.localScale = new Vector3(size, size * flap, 1f);
            bird.Shadow.position = bird.Body.position + new Vector3(0.35f, -0.45f, 0f);
            bird.Shadow.localScale = bird.Body.localScale;
            if (bird.Age > 45f || (cam != null && OutsideView(cam, bird.Body.position, 1.5f) && bird.Age > 3f))
            {
                Destroy(bird.Body.gameObject);
                Destroy(bird.Shadow.gameObject);
                birds.RemoveAt(i);
            }
        }

        BoardLookSettings settings = BoardLookSettings.Instance;
        if (!settings.birds || cam == null || Time.time < nextFlock) return;
        nextFlock = Time.time + Random.Range(settings.birdInterval.x, settings.birdInterval.y);
        SpawnFlock(cam);
    }

    // The board point at the centre of the view, and how much more board a tilted camera shows
    // than its orthographicSize says (the far edge of a perspective view reaches further).
    private static Vector3 ViewCentre(Camera cam) => BoardNavigator.Instance != null ? BoardNavigator.Instance.Pivot : cam.transform.position;
    private static float ViewStretch => BoardNavigator.CurrentTilt > 0f ? 1.35f : 1f;

    private static bool OutsideView(Camera cam, Vector3 position, float margin)
    {
        Vector3 c = ViewCentre(cam);
        float h = cam.orthographicSize * ViewStretch + margin;
        float w = cam.orthographicSize * ViewStretch * cam.aspect + margin;
        return Mathf.Abs(position.x - c.x) > w || Mathf.Abs(position.y - c.y) > h;
    }

    private void SpawnFlock(Camera cam)
    {
        Vector3 c = ViewCentre(cam);
        float h = cam.orthographicSize * ViewStretch, w = h * cam.aspect;
        bool fromLeft = Random.value < 0.5f;
        var start = new Vector3(c.x + (fromLeft ? -w - 0.8f : w + 0.8f), c.y + Random.Range(-h * 0.6f, h * 0.6f), 0f);
        float angle = Random.Range(-20f, 20f) * Mathf.Deg2Rad;
        var heading = new Vector3((fromLeft ? 1f : -1f) * Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        float speed = Random.Range(0.7f, 1.0f);
        var side = new Vector3(-heading.y, heading.x, 0f);
        int count = Random.Range(3, 8);
        for (int i = 0; i < count; i++)
        {
            // A loose V behind the leader.
            int rank = (i + 1) / 2;
            float wing = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
            Vector3 offset = -heading * rank * 0.22f + side * wing * rank * 0.16f
                + new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(-0.04f, 0.04f), 0f);
            birds.Add(CreateBird(start + offset, heading * speed * Random.Range(0.95f, 1.05f)));
        }
    }

    private Bird CreateBird(Vector3 position, Vector3 velocity)
    {
        float size = Random.Range(0.08f, 0.11f);
        var body = new GameObject("Bird", typeof(SpriteRenderer));
        body.transform.SetParent(transform, false);
        body.transform.position = position;
        body.transform.localScale = new Vector3(size, size, 1f);
        var sr = body.GetComponent<SpriteRenderer>();
        sr.sprite = BirdSprite();
        sr.color = new Color(0.1f, 0.09f, 0.08f, 0.85f);
        sr.flipX = velocity.x < 0f;
        sr.sortingLayerName = "Hexes";
        sr.sortingOrder = BirdSortingOrder;

        var shadow = new GameObject("BirdShadow", typeof(SpriteRenderer));
        shadow.transform.SetParent(transform, false);
        var ssr = shadow.GetComponent<SpriteRenderer>();
        ssr.sprite = sr.sprite;
        ssr.color = new Color(0f, 0f, 0f, 0.16f);
        ssr.flipX = sr.flipX;
        ssr.sortingLayerName = "Hexes";
        ssr.sortingOrder = BirdShadowOrder;

        return new Bird { Body = body.transform, Shadow = shadow.transform, Velocity = velocity, Phase = Random.value * 6f };
    }

    // A small "v" silhouette, generated once.
    private Sprite BirdSprite()
    {
        if (birdSprite != null) return birdSprite;
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Bird" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Two wings: arcs rising from the body at the centre, thinning toward the tips.
                float u = (x + 0.5f) / w * 2f - 1f;          // -1..1
                float v = (y + 0.5f) / h;                     // 0..1
                float wingY = 0.35f + 0.5f * Mathf.Pow(Mathf.Abs(u), 0.7f) - 0.25f * u * u;
                float thickness = Mathf.Lerp(0.16f, 0.03f, Mathf.Abs(u));
                float a = Mathf.Clamp01(1f - Mathf.Abs(v - wingY) / thickness);
                if (Mathf.Abs(u) > 0.97f) a = 0f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        birdSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        birdSprite.name = "Bird";
        return birdSprite;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private ParticleSystem CreateSystem(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.sortingLayerName = "Hexes";
        renderer.sortingOrder = SortingOrder;
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
}
