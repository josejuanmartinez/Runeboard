using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A short (about two seconds) burst of weather across the top of the screen when an
/// environmental card comes into play: rain streaks for Rain, flakes for Snow, embers for fire,
/// twinkling motes for the stars and moons, drifting mist for the shadows, and so on. Drawn on
/// its own overlay canvas just under the turn banner, then gone.
/// </summary>
public class EnvironmentBurst : MonoBehaviour
{
    private const float Duration = 1.8f;
    private const int Count = 90;
    private const float Band = 0.42f; // share of the screen, from the top, the weather fills

    private enum Style { Rain, Storm, Snow, Wind, Embers, Dust, Stars, Light, Petals, Mist, Ravens }

    private sealed class Mote
    {
        public RectTransform rect;
        public Image image;
        public Vector2 position, velocity;
        public float phase, size, alpha;
    }

    private static Sprite dot;
    private static Sprite soft;

    public static void Play(CardData card)
    {
        if (card == null) return;
        Game game = Game.Instance;
        if (game == null || !game.started) return;
        var go = new GameObject("EnvironmentBurst", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 190; // under the turn banner (200)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        go.AddComponent<EnvironmentBurst>().Run(StyleOf(card.name));
    }

    private static Style StyleOf(string name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        bool Has(params string[] words) { foreach (string w in words) if (n.Contains(w)) return true; return false; }
        if (Has("lightning")) return Style.Storm;
        if (Has("rain", "ulmo", "drown")) return Style.Rain;
        if (Has("snow", "winter", "frozen", "caradhras", "chill")) return Style.Snow;
        if (Has("wind")) return Style.Wind;
        if (Has("fire", "ash", "red sun", "pollution", "wildfire")) return Style.Embers;
        if (Has("sand", "drought", "muddy")) return Style.Dust;
        if (Has("star", "moon", "night", "earendil", "faded")) return Style.Stars;
        if (Has("sun", "dawn", "day", "light", "morning", "west")) return Style.Light;
        if (Has("flower", "butterfl", "willow")) return Style.Petals;
        if (Has("raven")) return Style.Ravens;
        return Style.Mist; // fog, clouds, gloom, shadows, the dead, the Nazgûl unleashed
    }

    private RectTransform root;
    private readonly List<Mote> motes = new();
    private Style style;
    private Image glow, flash;

    private void Run(Style chosen)
    {
        style = chosen;
        root = (RectTransform)transform;
        EnsureSprites();

        // A tinted haze along the top edge, under the particles.
        glow = NewImage("Glow", HudSprites.FadeDown);
        var gr = glow.rectTransform;
        gr.anchorMin = new Vector2(0f, 1f - Band);
        gr.anchorMax = Vector2.one;
        gr.offsetMin = gr.offsetMax = Vector2.zero;
        glow.color = Tint(0f);

        for (int i = 0; i < Count; i++) motes.Add(NewMote());

        if (style == Style.Storm)
        {
            flash = NewImage("Flash", null);
            var fr = flash.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            flash.color = new Color(0.85f, 0.9f, 1f, 0f);
        }
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        float t = 0f;
        Vector2 size = root.rect.size;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            // Quick in, a held middle, a soft out.
            float envelope = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((Duration - t) / 0.6f);
            glow.color = Tint(0.55f * envelope);
            if (flash != null)
            {
                float strike = Mathf.Max(Spike(t, 0.35f), 0.7f * Spike(t, 0.55f));
                flash.color = new Color(0.85f, 0.9f, 1f, 0.35f * strike);
            }
            foreach (Mote m in motes) Step(m, t, envelope, size);
            yield return null;
        }
        Destroy(gameObject);
    }

    private static float Spike(float t, float at) => Mathf.Clamp01(1f - Mathf.Abs(t - at) / 0.06f);

    private void Step(Mote m, float t, float envelope, Vector2 size)
    {
        float dt = Time.unscaledDeltaTime;
        m.position += m.velocity * dt;
        float sway = style is Style.Snow or Style.Petals or Style.Ravens ? Mathf.Sin(m.phase + t * 3f) * 0.004f : 0f;
        // Wrap round so the band stays full for the whole burst.
        if (m.position.x > 1.05f) m.position.x -= 1.1f;
        if (m.position.x < -0.05f) m.position.x += 1.1f;
        if (m.position.y < 1f - Band) m.position.y += Band;
        if (m.position.y > 1.02f) m.position.y -= Band;
        m.rect.anchoredPosition = new Vector2((m.position.x + sway) * size.x, m.position.y * size.y);
        // Fade toward the bottom of the band, so the weather melts into the board.
        float depth = Mathf.InverseLerp(1f - Band, 1f - Band * 0.4f, m.position.y);
        float twinkle = style == Style.Stars ? 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(m.phase + t * 5f)) : 1f;
        Color c = m.image.color;
        c.a = m.alpha * envelope * depth * twinkle;
        m.image.color = c;
    }

    private Mote NewMote()
    {
        Image image = NewImage("Mote", style is Style.Mist ? soft : dot);
        var m = new Mote { rect = image.rectTransform, image = image, phase = Random.value * 10f };
        m.rect.anchorMin = m.rect.anchorMax = Vector2.zero;
        m.position = new Vector2(Random.value * 1.1f - 0.05f, 1f - Random.value * Band);
        Color colour;
        switch (style)
        {
            case Style.Rain:
            case Style.Storm:
                m.rect.sizeDelta = new Vector2(2f, Random.Range(16f, 30f));
                m.rect.localEulerAngles = new Vector3(0f, 0f, 12f);
                m.velocity = new Vector2(0.06f, -Random.Range(0.55f, 0.8f));
                colour = new Color(0.7f, 0.8f, 0.95f);
                m.alpha = Random.Range(0.35f, 0.6f);
                break;
            case Style.Snow:
                m.size = Random.Range(4f, 9f);
                m.velocity = new Vector2(Random.Range(-0.01f, 0.02f), -Random.Range(0.04f, 0.1f));
                colour = Color.white;
                m.alpha = Random.Range(0.6f, 0.95f);
                break;
            case Style.Wind:
                m.rect.sizeDelta = new Vector2(Random.Range(40f, 90f), 2f);
                m.velocity = new Vector2(Random.Range(0.5f, 0.9f), -0.01f);
                colour = new Color(0.9f, 0.92f, 0.88f);
                m.alpha = Random.Range(0.2f, 0.4f);
                break;
            case Style.Embers:
                m.size = Random.Range(3f, 7f);
                m.velocity = new Vector2(Random.Range(-0.02f, 0.02f), Random.Range(0.05f, 0.12f));
                colour = Color.Lerp(new Color(1f, 0.35f, 0.1f), new Color(1f, 0.75f, 0.3f), Random.value);
                m.alpha = Random.Range(0.6f, 1f);
                break;
            case Style.Dust:
                m.size = Random.Range(3f, 8f);
                m.velocity = new Vector2(Random.Range(0.15f, 0.35f), -Random.Range(0f, 0.03f));
                colour = new Color(0.78f, 0.62f, 0.4f);
                m.alpha = Random.Range(0.3f, 0.6f);
                break;
            case Style.Stars:
                m.size = Random.Range(2f, 6f);
                m.velocity = Vector2.zero;
                colour = Color.Lerp(new Color(0.8f, 0.85f, 1f), Color.white, Random.value);
                m.alpha = Random.Range(0.5f, 1f);
                break;
            case Style.Light:
                m.size = Random.Range(3f, 7f);
                m.velocity = new Vector2(Random.Range(-0.01f, 0.01f), -Random.Range(0.02f, 0.06f));
                colour = new Color(1f, 0.86f, 0.5f);
                m.alpha = Random.Range(0.4f, 0.85f);
                break;
            case Style.Petals:
                m.size = Random.Range(5f, 9f);
                m.velocity = new Vector2(Random.Range(0.02f, 0.06f), -Random.Range(0.05f, 0.1f));
                colour = Color.Lerp(new Color(1f, 0.7f, 0.8f), new Color(1f, 0.95f, 0.6f), Random.value);
                m.alpha = Random.Range(0.6f, 0.95f);
                break;
            case Style.Ravens:
                m.rect.sizeDelta = new Vector2(Random.Range(10f, 16f), Random.Range(3f, 5f));
                m.velocity = new Vector2(Random.Range(0.18f, 0.3f), Random.Range(-0.02f, 0.02f));
                colour = new Color(0.05f, 0.05f, 0.07f);
                m.alpha = Random.Range(0.6f, 0.9f);
                break;
            default: // Mist
                m.size = Random.Range(120f, 260f);
                m.velocity = new Vector2(Random.Range(0.02f, 0.06f), 0f);
                colour = new Color(0.32f, 0.34f, 0.38f);
                m.alpha = Random.Range(0.12f, 0.25f);
                break;
        }
        if (m.size > 0f) m.rect.sizeDelta = new Vector2(m.size, m.size);
        colour.a = 0f;
        image.color = colour;
        return m;
    }

    private Color Tint(float alpha)
    {
        Color c = style switch
        {
            Style.Rain or Style.Storm => new Color(0.2f, 0.28f, 0.4f),
            Style.Snow => new Color(0.75f, 0.82f, 0.9f),
            Style.Embers => new Color(0.55f, 0.15f, 0.05f),
            Style.Dust => new Color(0.5f, 0.38f, 0.22f),
            Style.Stars => new Color(0.08f, 0.1f, 0.25f),
            Style.Light => new Color(0.9f, 0.7f, 0.3f),
            Style.Petals => new Color(0.6f, 0.45f, 0.5f),
            _ => new Color(0.1f, 0.1f, 0.12f),
        };
        c.a = alpha;
        return c;
    }

    private Image NewImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(root, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static void EnsureSprites()
    {
        if (dot == null) dot = Radial("EnvMoteDot", 32, 0.55f);
        if (soft == null) soft = Radial("EnvMoteSoft", 64, 0f);
    }

    // A round mote: solid out to `core`, then fading to the edge.
    private static Sprite Radial(string name, int n, float core)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = r <= core ? 1f : Mathf.Clamp01(1f - (r - core) / Mathf.Max(0.01f, 1f - core));
                pixels[y * n + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }
}
