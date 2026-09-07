using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Month calendar overlay. Shows the 30 days of the current Shire month in a grid,
/// highlights today, and marks scripted days (from DateEventManager) with a TMP
/// &lt;sprite&gt; under the day number, drawn from the <see cref="eventSpriteSheet"/>
/// TMP sprite asset. Hovering a marked day shows its description.
///
/// This component lives on a pre-built prefab (CalendarWidgetPanel.prefab) so its colors,
/// fonts and layout are authored and tweaked in the Inspector instead of being generated in
/// code. DateManager holds a
/// reference to a disabled instance of this prefab in the scene and enables/disables it
/// on hover; this script never builds or reparents its own GameObject.
/// </summary>
public class CalendarWidget : MonoBehaviour
{
    [Header("Style")]
    [SerializeField]
    [Tooltip("Background sprite for every day cell. The colors below tint this sprite instead of a flat rectangle; leave unset to keep the plain default UI sprite.")]
    private Sprite cellSprite;
    [SerializeField] private Color cellColor = new(0.18f, 0.16f, 0.12f, 1f);
    [SerializeField] private Color todayColor = new(0.45f, 0.36f, 0.15f, 1f);
    [SerializeField] private Color eventCellColor = new(0.28f, 0.20f, 0.10f, 1f);
    [SerializeField] private Color textColor = new(0.92f, 0.87f, 0.72f, 1f);

    [Header("Faction Colors")]
    [Tooltip("Used to tint a marked day's number by storyline faction.")]
    [SerializeField] private Color gandalfColor = new(0.75f, 0.85f, 0.98f, 1f);
    [SerializeField] private Color sarumanColor = new(0.86f, 0.80f, 0.93f, 1f);
    [SerializeField] private Color sauronColor = new(0.90f, 0.30f, 0.22f, 1f);
    [SerializeField] private Color mixedColor = new(0.95f, 0.80f, 0.35f, 1f);

    [Header("Icons")]
    [SerializeField]
    [Tooltip("TMP sprite asset (spritesheet) used to render event icons via <sprite name=...> on each day.")]
    private TMP_SpriteAsset eventSpriteSheet;

    [SerializeField]
    [Tooltip("Render scale (percent of font size) for event sprites in the calendar. 200 = double size.")]
    private int eventSpriteScalePercent = 200;

    [Header("Structure (wired on the prefab, do not edit by hand)")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI footerText;
    [SerializeField] private List<DayCellRefs> dayCells = new();

    [Header("Editor Preview")]
    [SerializeField]
    [Tooltip("Check this box to repaint the grid with the current Style colors above, without entering Play mode. Unchecks itself once applied.")]
    private bool refreshPreview;

    [Serializable]
    private class DayCellRefs
    {
        public GameObject cellObject;
        public Image background;
        public TextMeshProUGUI dayLabel;
        public TextMeshProUGUI iconLabel;
    }

    private readonly Dictionary<GameObject, string> cellDescriptions = new();

    private DateEventManager calendar;
    private MiddleEarthDate currentMonth;
    private bool hasCurrentMonth;
    private bool triggersWired;

    // Tooltip-style auto-hide: stay open while the pointer is over the date OR the panel,
    // hide shortly after it leaves both (the grace period lets the pointer cross the gap).
    private bool pointerOverDate;
    private bool pointerOverPanel;
    private const float HideGraceSeconds = 0.18f;

    private static Color FactionColorFor(CalendarWidget w, string faction)
    {
        switch ((faction ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "saruman": return w.sarumanColor;
            case "sauron": return w.sauronColor;
            default: return w.gandalfColor;
        }
    }

    public bool IsVisible => gameObject.activeSelf;

    private void Awake()
    {
        WireTriggers();
    }

    public void Toggle(MiddleEarthDate date)
    {
        if (IsVisible) Hide();
        else ShowMonth(date);
    }

    public void Hide()
    {
        CancelInvoke(nameof(HideIfIdle));
        gameObject.SetActive(false);
    }

    /// <summary>Pointer entered the date text: open (or keep open) the calendar for that date.</summary>
    public void OnDateEnter(MiddleEarthDate date)
    {
        pointerOverDate = true;
        CancelInvoke(nameof(HideIfIdle));
        ShowMonth(date);
    }

    /// <summary>Pointer left the date text: hide unless it has moved onto the panel.</summary>
    public void OnDateExit()
    {
        pointerOverDate = false;
        ScheduleHide();
    }

    private void ScheduleHide()
    {
        if (!isActiveAndEnabled) { HideIfIdle(); return; }
        CancelInvoke(nameof(HideIfIdle));
        Invoke(nameof(HideIfIdle), HideGraceSeconds);
    }

    private void HideIfIdle()
    {
        if (!pointerOverDate && !pointerOverPanel) Hide();
    }

    public void ShowMonth(MiddleEarthDate date)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // draw on top of sibling UI
        currentMonth = date;
        hasCurrentMonth = true;
        Refresh(date);
    }

    /// <summary>Re-paints the open calendar for the given "today" (call on new turn).</summary>
    public void RefreshIfOpen(MiddleEarthDate today)
    {
        if (!IsVisible) return;
        currentMonth = today;
        hasCurrentMonth = true;
        Refresh(today);
    }

    /// <summary>
    /// Inspector-only hook: tick "Refresh Preview" to repaint the grid with the Style colors
    /// above without entering Play mode (Refresh() is what actually reads those fields, and it
    /// otherwise only runs when the calendar opens or a new turn starts).
    /// </summary>
    private void OnValidate()
    {
        if (!refreshPreview) return;
        refreshPreview = false;
        if (dayCells == null || dayCells.Count == 0) return;
        MiddleEarthDate previewDate = hasCurrentMonth ? currentMonth : MiddleEarthCalendar.GetDateFromTurn(1);
        Refresh(previewDate);
    }

    private void Refresh(MiddleEarthDate today)
    {
        if (calendar == null) calendar = DateEventManager.Instance ?? FindFirstObjectByType<DateEventManager>();

        if (headerText != null) headerText.text = $"{today.MonthName}  {today.Year} {MiddleEarthCalendar.EraSuffix}";
        if (footerText != null) footerText.text = "Today is highlighted in gold. Hover a marked day to read its story.";

        // No live DateEventManager exists outside Play mode, so fall back to reading the real
        // Calendar.json entries directly - lets the "Refresh Preview" checkbox show actual marked
        // event days instead of an always-empty grid.
        IEnumerable<CalendarEntry> monthEntries = calendar != null
            ? calendar.GetEntriesForMonth(today.MonthIndex, today.Year)
            : (!Application.isPlaying
                ? LoadEditorPreviewEntries().Where(e => e.Date.MonthIndex == today.MonthIndex && e.Date.Year == today.Year)
                : Enumerable.Empty<CalendarEntry>());

        Dictionary<int, List<CalendarEntry>> byDay = new();
        foreach (CalendarEntry e in monthEntries)
        {
            if (!byDay.TryGetValue(e.Date.Day, out List<CalendarEntry> list))
            {
                list = new List<CalendarEntry>();
                byDay[e.Date.Day] = list;
            }
            list.Add(e);
        }

        for (int i = 0; i < dayCells.Count; i++)
        {
            int day = i + 1;
            DayCellRefs cell = dayCells[i];
            if (cell?.background == null || cell.dayLabel == null || cell.iconLabel == null) continue;

            bool isToday = day == today.Day;
            bool hasEvent = byDay.TryGetValue(day, out List<CalendarEntry> entries) && entries.Count > 0;

            if (cellSprite != null) cell.background.sprite = cellSprite;
            cell.background.color = isToday ? todayColor : (hasEvent ? eventCellColor : cellColor);
            cellDescriptions[cell.cellObject] = hasEvent ? BuildDescription(entries) : null;
            cell.dayLabel.color = hasEvent ? DayMarkerColor(entries) : textColor;

            // Day number stays top-left; the event icon(s) render centered in their own label.
            cell.dayLabel.text = day.ToString();
            if (eventSpriteSheet != null && cell.iconLabel.spriteAsset != eventSpriteSheet) cell.iconLabel.spriteAsset = eventSpriteSheet;
            cell.iconLabel.text = hasEvent ? BuildSpriteMarkup(entries) : string.Empty;
        }
    }

    private static List<CalendarEntry> cachedEditorPreviewEntries;

    /// <summary>
    /// Reads the real Resources/Calendar.json entries directly (no DateEventManager needed) so the
    /// Editor Preview checkbox can show actual marked event days while not in Play mode.
    /// </summary>
    private static List<CalendarEntry> LoadEditorPreviewEntries()
    {
        if (cachedEditorPreviewEntries != null) return cachedEditorPreviewEntries;
        cachedEditorPreviewEntries = new List<CalendarEntry>();

        TextAsset json = Resources.Load<TextAsset>("Calendar");
        if (json == null) return cachedEditorPreviewEntries;

        CalendarCollection collection = JsonUtility.FromJson<CalendarCollection>(json.text);
        if (collection?.events == null) return cachedEditorPreviewEntries;

        cachedEditorPreviewEntries = collection.events.Where(e => e != null && e.HasValidDate).ToList();
        return cachedEditorPreviewEntries;
    }

    private static string BuildDescription(List<CalendarEntry> entries)
    {
        if (entries.Count == 1) return entries[0].description;
        return string.Join("\n", entries.Select(e => e.description));
    }

    private Color DayMarkerColor(List<CalendarEntry> entries)
    {
        string first = entries[0].Faction;
        bool mixed = entries.Any(e => !string.Equals(e.Faction, first, StringComparison.OrdinalIgnoreCase));
        return mixed ? mixedColor : FactionColorFor(this, first);
    }

    /// <summary>
    /// Builds the TMP "&lt;sprite name=...&gt;" markup for a day's events. Uses each entry's
    /// explicit spriteName when set, otherwise the normalized environmental card name
    /// (matching how environmental cards render their sprite). Names resolve against
    /// <see cref="eventSpriteSheet"/>.
    /// </summary>
    private string BuildSpriteMarkup(List<CalendarEntry> entries)
    {
        List<string> names = new();
        foreach (CalendarEntry e in entries)
        {
            string name = !string.IsNullOrWhiteSpace(e.spriteName)
                ? e.spriteName.Trim()
                : (!string.IsNullOrWhiteSpace(e.environment) ? CardNameUtility.Normalize(e.environment) : null);
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        }
        if (names.Count == 0) return string.Empty;

        // <sprite> has no scale attribute, so wrap the icons in a <size> tag (percent of font size).
        // The icon label is centered and clipped, so the scaled sprite stays inside the cell.
        int scale = Mathf.Max(100, eventSpriteScalePercent);
        string sprites = string.Join(" ", names.Select(n => $"<sprite name=\"{n}\">"));
        return scale == 100 ? sprites : $"<size={scale}%>{sprites}</size>";
    }

    // ---------------- Runtime wiring (behavior only; structure/style come from the prefab) ----------------

    private void WireTriggers()
    {
        if (triggersWired) return;
        triggersWired = true;

        EventTrigger panelTrigger = GetComponent<EventTrigger>() ?? gameObject.AddComponent<EventTrigger>();
        AddTrigger(panelTrigger, EventTriggerType.PointerEnter, _ =>
        {
            pointerOverPanel = true;
            CancelInvoke(nameof(HideIfIdle));
        });
        AddTrigger(panelTrigger, EventTriggerType.PointerExit, _ =>
        {
            pointerOverPanel = false;
            ScheduleHide();
        });

        foreach (DayCellRefs cell in dayCells)
        {
            if (cell?.cellObject == null) continue;
            GameObject cellObject = cell.cellObject;
            EventTrigger trigger = cellObject.GetComponent<EventTrigger>() ?? cellObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            {
                if (footerText != null && cellDescriptions.TryGetValue(cellObject, out string desc) && !string.IsNullOrEmpty(desc))
                    footerText.text = desc;
            });
            AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            {
                if (footerText != null)
                    footerText.text = "Today is highlighted in gold. Hover a marked day to read its story.";
            });
        }
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new() { eventID = type };
        entry.callback.AddListener(data => callback(data));
        trigger.triggers.Add(entry);
    }
}
