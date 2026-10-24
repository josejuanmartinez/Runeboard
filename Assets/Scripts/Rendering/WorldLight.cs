using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The board's light over time (BoardLookSettings), on top of the tabletop grade BoardLook sets:
///  • seasons, from the calendar (one turn is one day; the campaign opens in Halimath): spring
///    fresh and green, summer warm and bright, autumn golden, winter cold and pale — blended
///    month by month, so the change creeps in over a few turns;
///  • dusk while the other nations move: when the human ends their turn the light cools and
///    dims, and morning comes back as their next turn opens.
/// Drives the board camera's global Volume (white balance, colour filter, saturation, exposure).
/// </summary>
public class WorldLight : MonoBehaviour
{
    private struct Grade
    {
        public float Temperature, Saturation;
        public Color Filter;

        public Grade(float temperature, float saturation, Color filter)
        {
            Temperature = temperature;
            Saturation = saturation;
            Filter = filter;
        }

        public static Grade Lerp(Grade a, Grade b, float t) => new(
            Mathf.Lerp(a.Temperature, b.Temperature, t), Mathf.Lerp(a.Saturation, b.Saturation, t), Color.Lerp(a.Filter, b.Filter, t));
    }

    // Offsets on top of the tabletop grade, kept to a hint: stronger ones (autumn +8 with a warm
    // filter) turned the whole board yellow.
    private static readonly Grade Spring = new(0f, 2f, new Color(0.995f, 1.01f, 0.99f));
    private static readonly Grade Summer = new(1f, 1f, new Color(1.01f, 1.005f, 0.985f));
    private static readonly Grade Autumn = new(2f, -2f, new Color(1.015f, 0.995f, 0.97f));
    private static readonly Grade Winter = new(-8f, -14f, new Color(0.96f, 0.99f, 1.03f));

    // Shire-reckoning months (MiddleEarthCalendar.Months): Afteryule, Solmath, Rethe, Astron,
    // Thrimidge, Forelithe, Afterlithe, Wedmath, Halimath, Winterfilth, Blotmath, Foreyule.
    private static readonly Grade[] ByMonth =
        { Winter, Winter, Spring, Spring, Spring, Summer, Summer, Summer, Autumn, Autumn, Autumn, Winter };

    private static readonly Color NightFilter = new(0.78f, 0.84f, 1.0f);
    private const float NightTemperature = -12f;
    private const float NightExposure = -0.45f;
    private const float NightFadeSeconds = 1.6f;

    private static WorldLight instance;

    private WhiteBalance white;
    private ColorAdjustments color;
    private float baseTemperature, baseSaturation, baseExposure;
    private Game game;
    private float night, nightTarget;

    public static void Ensure(VolumeProfile profile)
    {
        if (instance != null || profile == null) return;
        var go = new GameObject("WorldLight");
        instance = go.AddComponent<WorldLight>();
        instance.Bind(profile);
    }

    private void Bind(VolumeProfile profile)
    {
        white = profile.TryGet(out WhiteBalance w) ? w : profile.Add<WhiteBalance>(true);
        color = profile.TryGet(out ColorAdjustments c) ? c : profile.Add<ColorAdjustments>(true);
        white.active = true;
        color.active = true;
        baseTemperature = white.temperature.overrideState ? white.temperature.value : 0f;
        baseSaturation = color.saturation.overrideState ? color.saturation.value : 0f;
        baseExposure = color.postExposure.overrideState ? color.postExposure.value : 0f;

        game = Game.Instance;
        if (game != null)
        {
            game.HumanTurnEnded += OnHumanTurnEnded;
            game.HumanTurnStarted += OnHumanTurnStarted;
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (game != null)
        {
            game.HumanTurnEnded -= OnHumanTurnEnded;
            game.HumanTurnStarted -= OnHumanTurnStarted;
        }
    }

    private void OnHumanTurnEnded() => nightTarget = BoardLookSettings.Instance.duskDuringOtherTurns ? 1f : 0f;
    private void OnHumanTurnStarted() => nightTarget = 0f;

    private void Update()
    {
        if (white == null || color == null) return;
        BoardLookSettings settings = BoardLookSettings.Instance;
        night = Mathf.MoveTowards(night, nightTarget, Time.unscaledDeltaTime / NightFadeSeconds);
        float dusk = Mathf.SmoothStep(0f, 1f, night) * settings.duskStrength;

        Grade season = settings.seasons ? SeasonFor(game != null ? game.turn : 0) : new Grade(0f, 0f, Color.white);
        white.temperature.Override(baseTemperature + season.Temperature + NightTemperature * dusk);
        color.saturation.Override(baseSaturation + season.Saturation);
        color.colorFilter.Override(season.Filter * Color.Lerp(Color.white, NightFilter, dusk));
        color.postExposure.Override(baseExposure + NightExposure * dusk);
    }

    // Blends between month grades by the day, so seasons turn over gradually.
    private static Grade SeasonFor(int turn)
    {
        MiddleEarthDate date = MiddleEarthCalendar.GetDateFromTurn(turn);
        float month = date.MonthIndex + (date.Day - 1) / (float)MiddleEarthCalendar.DaysPerMonth - 0.5f;
        int from = Mathf.FloorToInt(month);
        float t = month - from;
        int count = ByMonth.Length;
        return Grade.Lerp(ByMonth[(from % count + count) % count], ByMonth[((from + 1) % count + count) % count], t);
    }
}
