using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Loads the campaign calendar (Resources/Calendar.json) and, each turn, fires the
/// beat(s) that fall on the current Shire date:
///   - "environment" -> makes the named environmental CardData active via EnvironmentalCardManager
///   - "dateEvent"   -> runs the named DateEvent (Assets/Scripts/Actions/DateEvents)
/// It also exposes the parsed entries so CalendarWidget can mark event days.
/// </summary>
public class DateEventManager : MonoBehaviour
{
    public static DateEventManager Instance { get; private set; }

    [SerializeField] private string calendarResourcePath = "Calendar";

    private readonly List<CalendarEntry> entries = new();
    // Entries indexed by absolute day, for O(1) "what happens today" / "this month" lookups.
    private readonly Dictionary<int, List<CalendarEntry>> entriesByAbsoluteDay = new();
    private readonly HashSet<int> firedDays = new();

    private Game game;
    private DeckManager deckManager;
    private bool loaded;

    public event Action CalendarLoaded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Returns the live manager, creating a GameObject for it if the scene has none.</summary>
    public static DateEventManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        DateEventManager existing = FindAnyObjectByType<DateEventManager>();
        if (existing != null) return existing;
        var go = new GameObject("DateEventManager");
        return go.AddComponent<DateEventManager>();
    }

    private void Start()
    {
        deckManager = DeckManager.Instance;
        LoadCalendar();
        SubscribeToGame();
    }

    private void OnDestroy()
    {
        if (game != null) game.NewTurnStarted -= OnNewTurn;
        if (Instance == this) Instance = null;
    }

    private void SubscribeToGame()
    {
        game = Game.Instance;
        if (game == null) return;
        game.NewTurnStarted -= OnNewTurn;
        game.NewTurnStarted += OnNewTurn;
    }

    private void LoadCalendar()
    {
        entries.Clear();
        entriesByAbsoluteDay.Clear();

        TextAsset json = Resources.Load<TextAsset>(calendarResourcePath);
        if (json == null)
        {
            Debug.LogWarning($"DateEventManager: could not load Resources/{calendarResourcePath}.json");
            return;
        }

        CalendarCollection collection = JsonUtility.FromJson<CalendarCollection>(json.text);
        if (collection?.events == null) return;

        foreach (CalendarEntry entry in collection.events)
        {
            if (entry == null || !entry.HasValidDate) continue;
            entries.Add(entry);
            int absolute = MiddleEarthCalendar.ToAbsoluteDay(entry.Date);
            if (!entriesByAbsoluteDay.TryGetValue(absolute, out List<CalendarEntry> list))
            {
                list = new List<CalendarEntry>();
                entriesByAbsoluteDay[absolute] = list;
            }
            list.Add(entry);
        }

        loaded = true;
        CalendarLoaded?.Invoke();
    }

    private void OnNewTurn(int turn)
    {
        if (!loaded) return;
        MiddleEarthDate today = MiddleEarthCalendar.GetDateFromTurn(turn);
        int absolute = MiddleEarthCalendar.ToAbsoluteDay(today);
        if (firedDays.Contains(absolute)) return;
        if (!entriesByAbsoluteDay.TryGetValue(absolute, out List<CalendarEntry> todays)) return;

        firedDays.Add(absolute);
        foreach (CalendarEntry entry in todays)
        {
            FireEntry(entry, turn, today);
        }
    }

    private void FireEntry(CalendarEntry entry, int turn, MiddleEarthDate today)
    {
        if (entry.HasEnvironment) ApplyEnvironment(entry.environment);
        if (entry.HasDateEvent) RunDateEvent(entry, turn, today);
    }

    private void ApplyEnvironment(string cardName)
    {
        CardData card = FindCardByName(cardName);
        if (card == null)
        {
            Debug.LogWarning($"DateEventManager: environmental card '{cardName}' not found.");
            return;
        }
        EnvironmentalCardManager.GetOrCreate().SetActiveCard(card);
    }

    private void RunDateEvent(CalendarEntry entry, int turn, MiddleEarthDate today)
    {
        Type type = ResolveDateEventType(entry.dateEvent);
        if (type == null)
        {
            Debug.LogWarning($"DateEventManager: dateEvent class '{entry.dateEvent}' not found.");
            return;
        }

        try
        {
            var instance = (DateEvent)Activator.CreateInstance(type);
            instance.Run(new DateEventContext { game = game, turn = turn, date = today, entry = entry });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"DateEventManager: failed to run dateEvent '{entry.dateEvent}': {e.Message}");
        }
    }

    private static Type ResolveDateEventType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;
        Type type = Type.GetType(className) ?? Type.GetType($"{className}, Assembly-CSharp");
        return type != null && typeof(DateEvent).IsAssignableFrom(type) && !type.IsAbstract ? type : null;
    }

    private CardData FindCardByName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return null;
        if (deckManager == null) deckManager = DeckManager.Instance;
        if (deckManager == null) return null;
        deckManager.InitializeFromResources();
        return deckManager.cards.FirstOrDefault(c =>
            c != null && CardNameUtility.Equals(c.name, cardName));
    }

    // ---- Read access for the calendar widget ----

    public bool IsLoaded => loaded;

    /// <summary>All entries whose date falls in the given month/year, in date order.</summary>
    public IEnumerable<CalendarEntry> GetEntriesForMonth(int monthIndex, int year)
    {
        return entries
            .Where(e => e.Date.MonthIndex == monthIndex && e.Date.Year == year)
            .OrderBy(e => e.Date.Day);
    }

    /// <summary>Entries on an exact date (usually 0 or 1).</summary>
    public IEnumerable<CalendarEntry> GetEntriesForDate(MiddleEarthDate date)
    {
        int absolute = MiddleEarthCalendar.ToAbsoluteDay(date);
        return entriesByAbsoluteDay.TryGetValue(absolute, out List<CalendarEntry> list)
            ? list
            : Enumerable.Empty<CalendarEntry>();
    }

    /// <summary>
    /// The environmental card that today's calendar entry (if any) makes active. Pure lookup —
    /// safe to call regardless of whether OnNewTurn has fired yet for this date this frame.
    /// </summary>
    public CardData GetEnvironmentCardForDate(MiddleEarthDate date)
    {
        foreach (CalendarEntry entry in GetEntriesForDate(date))
        {
            if (entry.HasEnvironment) return FindCardByName(entry.environment);
        }
        return null;
    }
}
