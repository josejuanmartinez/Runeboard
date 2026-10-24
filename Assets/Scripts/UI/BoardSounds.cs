using UnityEngine;

/// <summary>
/// Small sounds for handling the miniatures on the board, synthesised once at startup (no audio
/// assets): hovering a unit is a soft felt tap, like brushing a figure on a cloth-covered table;
/// selecting it is a short wooden knock — a miniature picked up — with a faint bright ring.
/// Played through Sounds' own source, so they follow the game's sound volume.
/// </summary>
public static class BoardSounds
{
    private const int Rate = 44100;
    private const float HoverInterval = 0.09f;

    private static AudioClip hover, select;
    private static float lastHover;

    public static void UnitHover()
    {
        if (Time.unscaledTime - lastHover < HoverInterval) return;
        lastHover = Time.unscaledTime;
        Play(hover ??= Synthesize("MiniatureHover", 0.07f, HoverSample), 0.55f);
    }

    public static void UnitSelect() => Play(select ??= Synthesize("MiniatureSelect", 0.45f, SelectSample), 0.7f);

    private static void Play(AudioClip clip, float volume)
    {
        AudioSource source = Sounds.Instance != null ? Sounds.Instance.soundAudioSource : null;
        if (source != null && clip != null) source.PlayOneShot(clip, volume);
    }

    private delegate float Sample(float t, ref uint noise, ref float lowpass);

    private static AudioClip Synthesize(string name, float seconds, Sample sample)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        uint noise = 22222;
        float lowpass = 0f;
        for (int i = 0; i < count; i++) data[i] = sample(i / (float)Rate, ref noise, ref lowpass);
        // Short fades at both ends: no clicks.
        int fade = Rate / 400;
        for (int i = 0; i < fade && i < count; i++) { data[i] *= i / (float)fade; data[count - 1 - i] *= i / (float)fade; }
        AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Noise(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state & 0xffff) / 32767.5f - 1f;
    }

    // A muffled tap: low-passed noise and a soft low thump, gone in a few tens of milliseconds.
    private static float HoverSample(float t, ref uint noise, ref float lowpass)
    {
        lowpass += (Noise(ref noise) - lowpass) * 0.18f;
        float thump = Mathf.Sin(2f * Mathf.PI * 170f * t) * Mathf.Exp(-t * 70f);
        return (lowpass * 0.9f * Mathf.Exp(-t * 110f) + thump * 0.6f) * 0.5f;
    }

    // A wooden knock (two inharmonic partials, fast decay), a low thump under it and a faint
    // high ring that lingers a little.
    private static float SelectSample(float t, ref uint noise, ref float lowpass)
    {
        lowpass += (Noise(ref noise) - lowpass) * 0.3f;
        float knock = (Mathf.Sin(2f * Mathf.PI * 510f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 1190f * t)) * Mathf.Exp(-t * 38f);
        float thump = Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-t * 30f);
        float ring = (Mathf.Sin(2f * Mathf.PI * 1760f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 2640f * t)) * Mathf.Exp(-t * 9f) * 0.12f;
        float tap = lowpass * Mathf.Exp(-t * 120f) * 0.5f;
        return (knock * 0.32f + thump * 0.35f + ring + tap) * 0.6f;
    }
}
