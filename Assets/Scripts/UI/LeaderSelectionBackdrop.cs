using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The leader selection screen's background: the painting of the place where the chosen
/// flavour's campaign begins (its seat's card art), filling the screen behind a dark vignette,
/// slowly drifting and zooming, with embers rising through it. Turning the carousel crossfades
/// to the next flavour's place, so the choice reads as "this is where you will start".
/// </summary>
public class LeaderSelectionBackdrop : MonoBehaviour
{
    private const int EmberCount = 42;
    private const float FadeSeconds = 0.9f;
    private const float DriftSeconds = 24f;

    private RectTransform root;
    private readonly List<Ember> embers = new();
    // Two painting layers: the shown one, and the one fading in over it.
    private Image back, front;
    private float fade = 1f;
    private float shownSince;

    private sealed class Ember
    {
        public RectTransform rect;
        public Image image;
        public Vector2 position;
        public float speed, sway, phase, life, age, size;
    }

    private static Sprite vignetteSprite;
    private static Sprite glowSprite;

    public static LeaderSelectionBackdrop Attach(GameObject fullScreen)
    {
        if (fullScreen == null) return null;
        LeaderSelectionBackdrop backdrop = fullScreen.GetComponent<LeaderSelectionBackdrop>();
        if (backdrop == null) backdrop = fullScreen.AddComponent<LeaderSelectionBackdrop>();
        return backdrop;
    }

    // Built on first use: the selection screen may still be inactive (no Awake yet) when the
    // carousel already picks its first leader.
    private void Awake() => Build();

    private void Build()
    {
        if (root != null) return;
        root = transform as RectTransform;
        Image flat = GetComponent<Image>();
        if (flat != null) flat.color = new Color(0.03f, 0.028f, 0.03f, 1f);

        int layer = 0;
        back = Painting("PaintingBack", layer++);
        front = Painting("PaintingFront", layer++);
        AddLayer("Vignette", Vignette(), new Color(0.015f, 0.012f, 0.012f, 0.96f), Vector2.zero, Vector2.one, layer++);
        AddLayer("TopFade", HudSprites.FadeDown, new Color(0f, 0f, 0f, 0.75f), new Vector2(0f, 0.8f), Vector2.one, layer++);
        AddLayer("BottomFade", HudSprites.FadeUp, new Color(0.01f, 0.008f, 0.006f, 0.94f), Vector2.zero, new Vector2(1f, 0.5f), layer++);

        var emberLayer = new GameObject("Embers", typeof(RectTransform)).GetComponent<RectTransform>();
        emberLayer.SetParent(root, false);
        emberLayer.SetSiblingIndex(layer);
        Stretch(emberLayer);
        for (int i = 0; i < EmberCount; i++) embers.Add(NewEmber(emberLayer, true));
    }

    /// <summary>Crossfades to this painting (the flavour's starting place).</summary>
    public void Show(Sprite painting)
    {
        Build();
        if (painting == null || painting == front.sprite) return;
        // The one on screen goes to the back; the new one fades in on top.
        back.sprite = front.sprite;
        back.color = front.color;
        back.rectTransform.sizeDelta = front.rectTransform.sizeDelta;
        back.rectTransform.localScale = front.rectTransform.localScale;
        back.rectTransform.anchoredPosition = front.rectTransform.anchoredPosition;
        back.enabled = back.sprite != null;
        front.sprite = painting;
        front.enabled = true;
        Fit(front);
        fade = 0f;
        shownSince = Time.unscaledTime;
    }

    /// <summary>The leader is chosen: nothing to keep animating.</summary>
    public void Release() => enabled = false;

    private void Update()
    {
        if (root == null) return;
        float dt = Time.unscaledDeltaTime;

        // Crossfade, then a slow push in and drift across the painting.
        if (front.enabled)
        {
            fade = Mathf.MoveTowards(fade, 1f, dt / FadeSeconds);
            front.color = new Color(0.62f, 0.6f, 0.58f, fade);
            float p = Mathf.Repeat((Time.unscaledTime - shownSince) / DriftSeconds, 1f);
            float zoom = 1.06f + 0.08f * Mathf.SmoothStep(0f, 1f, Mathf.PingPong(p * 2f, 1f));
            front.rectTransform.localScale = Vector3.one * zoom;
            front.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(p * Mathf.PI * 2f) * 40f, Mathf.Cos(p * Mathf.PI * 2f) * 18f);
        }

        Vector2 size = root.rect.size;
        foreach (Ember e in embers)
        {
            e.age += dt;
            if (e.age >= e.life) { Respawn(e, false); continue; }
            e.position.y += e.speed * dt;
            float x = e.position.x + Mathf.Sin(e.phase + e.age * 0.9f) * e.sway;
            e.rect.anchoredPosition = new Vector2(x * size.x, e.position.y * size.y);
            float t = e.age / e.life;
            float flicker = Mathf.Sin(t * Mathf.PI) * (0.65f + 0.35f * Mathf.Sin(e.phase * 3f + e.age * 7f));
            Color c = e.image.color;
            c.a = Mathf.Clamp01(flicker) * 0.85f;
            e.image.color = c;
        }
    }

    // The painting fills the screen (cropped to it), whatever its proportions.
    private void Fit(Image image)
    {
        Vector2 screen = root.rect.size;
        if (screen.x <= 1f || screen.y <= 1f) screen = new Vector2(1920f, 1080f);
        Rect r = image.sprite.rect;
        float scale = Mathf.Max(screen.x / r.width, screen.y / r.height);
        image.rectTransform.sizeDelta = new Vector2(r.width * scale, r.height * scale);
    }

    private Image Painting(string name, int sibling)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.SetSiblingIndex(sibling);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    // ── Embers ──────────────────────────────────────────────────────────────────────────────

    private Ember NewEmber(RectTransform parent, bool anywhere)
    {
        var go = new GameObject("Ember", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.sprite = Glow();
        image.raycastTarget = false;
        var e = new Ember { rect = rect, image = image };
        Respawn(e, anywhere);
        return e;
    }

    private static void Respawn(Ember e, bool anywhere)
    {
        e.position = new Vector2(Random.value, anywhere ? Random.value * 0.9f : Random.Range(-0.05f, 0.25f));
        e.speed = Random.Range(0.012f, 0.04f);
        e.sway = Random.Range(0.002f, 0.012f);
        e.phase = Random.value * 10f;
        e.life = Random.Range(7f, 16f);
        e.age = anywhere ? Random.value * e.life : 0f;
        e.size = Random.Range(3f, 9f);
        e.rect.sizeDelta = new Vector2(e.size, e.size);
        e.image.color = Color.Lerp(new Color(1f, 0.52f, 0.2f, 0f), new Color(1f, 0.82f, 0.48f, 0f), Random.value);
    }

    // ── Layers and art ──────────────────────────────────────────────────────────────────────

    private void AddLayer(string name, Sprite sprite, Color color, Vector2 anchorMin, Vector2 anchorMax, int sibling)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.SetSiblingIndex(sibling);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // Clear in the middle (a little above centre, where the cards stand), dark toward the edges.
    private static Sprite Vignette()
    {
        if (vignetteSprite != null) return vignetteSprite;
        const int w = 256, h = 144;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SelectionVignette", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float v = ((y + 0.5f) / h * 2f - 1f) - 0.15f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1.15f, r));
                pixels[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return vignetteSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }

    private static Sprite Glow()
    {
        if (glowSprite != null) return glowSprite;
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "EmberGlow", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01(1f - r);
                pixels[y * n + x] = new Color(1f, 1f, 1f, a * a * (r < 0.25f ? 1f : 0.8f));
            }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return glowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }
}
