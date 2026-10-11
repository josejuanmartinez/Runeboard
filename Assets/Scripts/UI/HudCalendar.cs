using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Top-right turn &amp; calendar block of the cinematic HUD (replaces the authored "Turn N" /
/// date strip, which stays alive but invisible so DateManager keeps driving the month calendar):
///   TURN 3 OF 60 · 3018 T.A.      small gold eyebrow (same style as the ENVIRONMENT label)
///   23 Halimath                   the only large line
///   ○ ● ○ ○ ◆ ○ ○                 one pip per coming day; today lit, scripted days marked
///   In 3 days · Twilight
/// Hovering the block opens the month calendar (DateManager.OpenCalendar).
/// Sizes are Layout-canvas units (2560×1440 reference).
/// </summary>
public class HudCalendar : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const int StripDays = 7;
    private const int LookAhead = 40;

    private TextMeshProUGUI turnText;
    private TextMeshProUGUI dateText;
    private TextMeshProUGUI nextText;
    private readonly Image[] pips = new Image[StripDays];
    private int shownTurn = int.MinValue;
    private int shownAge;
    private DateManager dateManager;

    public static HudCalendar Build(RectTransform parent, Vector2 size)
    {
        Transform existing = parent.Find("HudCalendar");
        if (existing != null) return existing.GetComponent<HudCalendar>();

        var go = new GameObject("HudCalendar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = size;
        var hit = go.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); // hover target for the month calendar
        var cal = go.AddComponent<HudCalendar>();

        cal.turnText = Text(rt, "Turn", new Vector2(0f, -8f), 20f, display: false);
        cal.dateText = Text(rt, "Date", new Vector2(0f, -36f), 50f, display: true);

        var strip = new GameObject("Days", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var srt = strip.GetComponent<RectTransform>();
        srt.SetParent(rt, false);
        srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 1f);
        srt.anchoredPosition = new Vector2(-2f, -102f);
        srt.sizeDelta = new Vector2(StripDays * 26f, 16f);
        var hlg = strip.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = hlg.childControlHeight = false;
        for (int i = 0; i < StripDays; i++)
        {
            var pip = new GameObject($"Day{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var prt = pip.GetComponent<RectTransform>();
            prt.SetParent(srt, false);
            prt.sizeDelta = new Vector2(16f, 16f);
            var img = pip.GetComponent<Image>();
            img.sprite = HudSprites.Circle;
            img.raycastTarget = false;
            cal.pips[i] = img;
        }

        cal.nextText = Text(rt, "Next", new Vector2(0f, -128f), 20f, display: false);
        cal.nextText.textWrappingMode = TextWrappingModes.NoWrap;
        cal.nextText.overflowMode = TextOverflowModes.Ellipsis;
        return cal;
    }

    private static TextMeshProUGUI Text(RectTransform parent, string name, Vector2 position, float size, bool display)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(0f, size * 1.25f);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size, UiTheme.Text); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.alignment = TextAlignmentOptions.TopRight;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

    private void Update()
    {
        Game game = Game.Instance;
        if (game == null) return;
        int age = CampaignObjectives.Instance != null ? CampaignObjectives.Instance.AgeLength : 0;
        if (game.turn == shownTurn && age == shownAge) return;
        shownTurn = game.turn;
        shownAge = age;
        Refresh(game.turn, age);
    }

    private void Refresh(int turn, int age)
    {
        MiddleEarthDate today = MiddleEarthCalendar.GetDateFromTurn(turn);
        turnText.text = $"<color={UiTheme.GoldHex}><cspace=0.12em>TURN {turn}{(age > 0 ? $" OF {age}" : "")}</cspace></color>"
            + $"<color={UiTheme.MutedHex}>  ·  <cspace=0.12em>{today.Year} {MiddleEarthCalendar.EraSuffix}</cspace></color>";
        dateText.text = $"{today.Day} {today.MonthName}";

        DateEventManager events = DateEventManager.Instance;
        for (int i = 0; i < StripDays; i++)
        {
            bool marked = events != null && events.IsLoaded && events.GetEntriesForDate(MiddleEarthCalendar.GetDateFromTurn(turn + i)).Any();
            Image pip = pips[i];
            float size = i == 0 ? 16f : marked ? 12f : 8f;
            pip.rectTransform.sizeDelta = new Vector2(size, size);
            pip.color = i == 0 ? UiTheme.Gold
                : marked ? UiTheme.Text
                : new Color(UiTheme.Muted.r, UiTheme.Muted.g, UiTheme.Muted.b, 0.45f);
        }

        string next = "";
        if (events != null && events.IsLoaded)
        {
            for (int d = 1; d <= LookAhead; d++)
            {
                MiddleEarthDate date = MiddleEarthCalendar.GetDateFromTurn(turn + d);
                CalendarEntry entry = events.GetEntriesForDate(date).FirstOrDefault();
                if (entry == null) continue;
                // Environment keys are normalised card names ("khamulunleashed"): show the card's own name.
                string what = !string.IsNullOrWhiteSpace(entry.dateEvent)
                    ? DisplayNames.Format(entry.dateEvent)
                    : events.GetEnvironmentCardForDate(date)?.name ?? DisplayNames.Format(entry.environment);
                next = $"<color={UiTheme.MutedHex}>{(d == 1 ? "Tomorrow" : $"In {d} days")}  ·  </color>{what}";
                break;
            }
        }
        nextText.text = next;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (dateManager == null) dateManager = FindAnyObjectByType<DateManager>(FindObjectsInactive.Include);
        dateManager?.OpenCalendar();
        CursorManager.Instance?.SetClickableCursor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        dateManager?.CloseCalendar();
        CursorManager.Instance?.SetDefaultCursor();
    }
}
