using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The month at a glance, dropped below the top-right turn &amp; calendar block while it is
/// hovered (HudCalendar): the month and year as a title, its thirty days in a six-by-five grid —
/// today ringed in gold, the days gone by dimmed, and a scripted day marked with its side's colour
/// and the event's name — and, under the grid, the story of whichever day the pointer is on.
/// Built in code on the HUD canvas; it stays open while the pointer is on the block or on it.
/// </summary>
public class MonthCalendar : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const int Columns = 6;
    private const int Rows = MiddleEarthCalendar.DaysPerMonth / Columns;
    private const float Cell = 86f, Gap = 6f;

    private static MonthCalendar instance;

    private RectTransform panel;
    private TextMeshProUGUI title, story;
    private readonly List<(RectTransform rect, Image face, Image stripe, TextMeshProUGUI day, TextMeshProUGUI name)> cells = new();
    private readonly string[] stories = new string[MiddleEarthCalendar.DaysPerMonth];
    private int todayIndex;

    public static void Show(RectTransform anchor)
    {
        if (anchor == null) return;
        if (instance == null) instance = Build(anchor);
        if (instance == null) return;
        instance.Refresh();
        instance.panel.gameObject.SetActive(true);
        instance.panel.SetAsLastSibling();
    }

    /// <summary>The pointer left the calendar block: close, unless it moved onto the month.</summary>
    public static void Hide()
    {
        if (instance == null) return;
        instance.CancelInvoke(nameof(HideNow));
        instance.Invoke(nameof(HideNow), 0.2f);
    }

    private bool pointerInside;
    public void OnPointerEnter(PointerEventData eventData) { pointerInside = true; CancelInvoke(nameof(HideNow)); }
    public void OnPointerExit(PointerEventData eventData) { pointerInside = false; Hide(); ShowStory(todayIndex); }

    private void HideNow()
    {
        if (!pointerInside && panel != null) panel.gameObject.SetActive(false);
    }

    // A day under the pointer tells its story below the grid.
    private sealed class DayHover : MonoBehaviour, IPointerEnterHandler
    {
        public MonthCalendar owner;
        public int index;
        public void OnPointerEnter(PointerEventData eventData) => owner.ShowStory(index);
    }

    private static MonthCalendar Build(RectTransform anchor)
    {
        Canvas canvas = anchor.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        RectTransform root = canvas.rootCanvas.transform as RectTransform;

        float width = Columns * Cell + (Columns - 1) * Gap + 48f;
        float height = Rows * Cell + (Rows - 1) * Gap + 190f;
        var panelGo = new GameObject("MonthCalendar", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
        var calendar = panelGo.AddComponent<MonthCalendar>();
        calendar.panel = panelGo.GetComponent<RectTransform>();
        calendar.panel.SetParent(root, false);
        // Under the block, right-aligned with it.
        calendar.panel.anchorMin = calendar.panel.anchorMax = new Vector2(1f, 1f);
        calendar.panel.pivot = new Vector2(1f, 1f);
        calendar.panel.sizeDelta = new Vector2(width, height);
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 below = root.InverseTransformPoint(corners[0]);
        calendar.panel.anchoredPosition = new Vector2(-24f, below.y - root.rect.yMax - 12f);
        var face = panelGo.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.07f, 0.06f, 0.05f, 0.96f);
        face.fillBottom = new Color(0.03f, 0.027f, 0.024f, 0.97f);
        face.raycastTarget = true;

        calendar.title = Text(calendar.panel, "Title", 30f, true);
        Place(calendar.title.rectTransform, new Vector2(0f, -22f), new Vector2(width - 40f, 40f), TextAlignmentOptions.Top);

        for (int i = 0; i < MiddleEarthCalendar.DaysPerMonth; i++)
        {
            int col = i % Columns, row = i / Columns;
            var go = new GameObject($"Day{i + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(calendar.panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(Cell, Cell);
            rt.anchoredPosition = new Vector2((col - (Columns - 1) * 0.5f) * (Cell + Gap), -76f - row * (Cell + Gap));
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            var hover = go.AddComponent<DayHover>();
            hover.owner = calendar;
            hover.index = i;

            var stripeGo = new GameObject("Stripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var sr = stripeGo.GetComponent<RectTransform>();
            sr.SetParent(rt, false);
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(1f, 0f);
            sr.pivot = new Vector2(0.5f, 0f);
            sr.sizeDelta = new Vector2(0f, 4f);
            var stripe = stripeGo.GetComponent<Image>();
            stripe.raycastTarget = false;

            TextMeshProUGUI day = Text(rt, "Number", 22f, true);
            day.alignment = TextAlignmentOptions.TopLeft;
            day.rectTransform.anchorMin = Vector2.zero;
            day.rectTransform.anchorMax = Vector2.one;
            day.rectTransform.offsetMin = new Vector2(8f, 6f);
            day.rectTransform.offsetMax = new Vector2(-6f, -4f);

            TextMeshProUGUI name = Text(rt, "Event", 12f, false);
            name.alignment = TextAlignmentOptions.Bottom;
            name.enableAutoSizing = true;
            name.fontSizeMin = 8f;
            name.fontSizeMax = 12f;
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.rectTransform.anchorMin = Vector2.zero;
            name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = new Vector2(4f, 8f);
            name.rectTransform.offsetMax = new Vector2(-4f, -30f);
            calendar.cells.Add((rt, img, stripe, day, name));
        }

        calendar.story = Text(calendar.panel, "Story", 15f, false);
        calendar.story.textWrappingMode = TextWrappingModes.Normal;
        calendar.story.fontStyle = FontStyles.Italic;
        Place(calendar.story.rectTransform, new Vector2(0f, -(height - 100f)), new Vector2(width - 48f, 86f), TextAlignmentOptions.Top);
        calendar.panel.gameObject.SetActive(false);
        return calendar;
    }

    private void Refresh()
    {
        Game game = Game.Instance;
        int turn = game != null ? game.turn : 0;
        MiddleEarthDate today = MiddleEarthCalendar.GetDateFromTurn(turn);
        title.text = $"{today.MonthName}  <size=60%><color={UiTheme.MutedHex}>{today.Year} {MiddleEarthCalendar.EraSuffix}</color></size>";
        todayIndex = today.Day - 1;

        DateEventManager events = DateEventManager.Instance;
        var byDay = new Dictionary<int, List<CalendarEntry>>();
        if (events != null && events.IsLoaded)
            foreach (CalendarEntry e in events.GetEntriesForMonth(today.MonthIndex, today.Year))
            {
                if (!byDay.TryGetValue(e.Date.Day, out List<CalendarEntry> list)) byDay[e.Date.Day] = list = new List<CalendarEntry>();
                list.Add(e);
            }

        for (int i = 0; i < cells.Count; i++)
        {
            int day = i + 1;
            var cell = cells[i];
            bool isToday = day == today.Day;
            bool past = day < today.Day;
            bool marked = byDay.TryGetValue(day, out List<CalendarEntry> entries) && entries.Count > 0;
            cell.face.color = isToday ? new Color(0.36f, 0.27f, 0.12f, 1f)
                : marked ? new Color(0.13f, 0.11f, 0.09f, 1f)
                : new Color(0.08f, 0.072f, 0.064f, 1f);
            cell.day.text = day.ToString();
            cell.day.color = isToday ? UiTheme.Gold : past ? new Color(UiTheme.Muted.r, UiTheme.Muted.g, UiTheme.Muted.b, 0.5f) : UiTheme.Text;
            cell.stripe.enabled = marked;
            if (marked) cell.stripe.color = SideColour(entries[0].Faction);
            cell.name.text = marked ? EventNames(entries, events) : string.Empty;
            cell.name.color = past ? UiTheme.Muted : UiTheme.Text;
            stories[i] = marked ? string.Join(" ", entries.Where(e => !string.IsNullOrWhiteSpace(e.description)).Select(e => e.description.Trim())) : null;
        }
        ShowStory(todayIndex);
    }

    private void ShowStory(int index)
    {
        string text = index >= 0 && index < stories.Length ? stories[index] : null;
        story.text = string.IsNullOrWhiteSpace(text)
            ? $"<color={UiTheme.MutedHex}>Hover a marked day to read what befalls it.</color>"
            : $"<color=#D8CCAE>“{text}”</color>";
    }

    private static string EventNames(List<CalendarEntry> entries, DateEventManager events)
    {
        var names = new List<string>();
        foreach (CalendarEntry e in entries)
        {
            string name = !string.IsNullOrWhiteSpace(e.dateEvent) ? DisplayNames.Format(e.dateEvent)
                : events != null && events.GetEnvironmentCardForDate(e.Date) is CardData card ? card.name
                : !string.IsNullOrWhiteSpace(e.environment) ? DisplayNames.Format(e.environment) : null;
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        }
        return string.Join("\n", names);
    }

    private static Color SideColour(string faction) => (faction ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "sauron" => new Color(0.86f, 0.3f, 0.22f),
        "saruman" => new Color(0.78f, 0.7f, 0.9f),
        _ => new Color(0.62f, 0.78f, 0.95f),
    };

    private static TextMeshProUGUI Text(RectTransform parent, string name, float size, bool display)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size, UiTheme.Text); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.fontSharedMaterial = UiTheme.PlainMaterial(t.font);
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static void Place(RectTransform rt, Vector2 at, Vector2 size, TextAlignmentOptions alignment)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = at;
        rt.sizeDelta = size;
        if (rt.TryGetComponent(out TextMeshProUGUI text)) text.alignment = alignment;
    }
}
