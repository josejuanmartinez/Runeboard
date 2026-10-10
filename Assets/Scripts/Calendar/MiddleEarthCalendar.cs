using System;

/// <summary>
/// Shared turn <-> Shire-calendar date conversion for the campaign.
/// The campaign opens on 23 Halimath 3018 T.A. (Frodo leaves Bag End) and runs
/// at one turn per day, 30 days per month, 12 months per year.
/// DateManager (text), CalendarWidget (month grid) and DateEventManager (scripted
/// beats) all read dates through here so they never drift apart.
/// </summary>
public static class MiddleEarthCalendar
{
    public static readonly string[] Months =
    {
        "Afteryule", "Solmath", "Rethe", "Astron", "Thrimidge", "Forelithe",
        "Afterlithe", "Wedmath", "Halimath", "Winterfilth", "Blotmath", "Foreyule"
    };

    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear; // 360
    public const string EraSuffix = "T.A.";

    // Epoch is 1 Afteryule 3018 (absolute day 0). The campaign starts later, on
    // 23 Halimath 3018, which is StartAbsoluteDay days after the epoch.
    public const int EpochYear = 3018;
    public const int StartMonthIndex = 8;  // Halimath (~September)
    public const int StartDayOfMonth = 23; // 23rd

    /// <summary>Absolute day (from the epoch) of the campaign's first turn.</summary>
    public static readonly int StartAbsoluteDay = StartMonthIndex * DaysPerMonth + (StartDayOfMonth - 1); // 262

    /// <summary>Converts a game turn (turn 0 is the campaign's first day; each turn is one day) into a date.</summary>
    public static MiddleEarthDate GetDateFromTurn(int turnNumber)
    {
        return FromAbsoluteDay(StartAbsoluteDay + Math.Max(0, turnNumber));
    }

    /// <summary>The turn on which the given date occurs, clamped to 0 for dates before the campaign start.</summary>
    public static int GetTurnFromDate(MiddleEarthDate date)
    {
        return Math.Max(0, ToAbsoluteDay(date) - StartAbsoluteDay);
    }

    public static MiddleEarthDate FromAbsoluteDay(int absoluteDay)
    {
        if (absoluteDay < 0) absoluteDay = 0;
        int year = EpochYear + absoluteDay / DaysPerYear;
        int dayOfYear = absoluteDay % DaysPerYear;
        int monthIndex = dayOfYear / DaysPerMonth;
        int day = dayOfYear % DaysPerMonth + 1;
        return new MiddleEarthDate(day, monthIndex, year);
    }

    public static int ToAbsoluteDay(MiddleEarthDate date)
    {
        return (date.Year - EpochYear) * DaysPerYear + date.MonthIndex * DaysPerMonth + (date.Day - 1);
    }

    public static int MonthIndexOf(string monthName)
    {
        if (string.IsNullOrWhiteSpace(monthName)) return -1;
        for (int i = 0; i < Months.Length; i++)
        {
            if (string.Equals(Months[i], monthName.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Parses a "D Month YYYY" date string (e.g. "23 Halimath 3018"). The era suffix
    /// ("T.A.") is optional and ignored. Returns false if the string is malformed.
    /// </summary>
    public static bool TryParse(string text, out MiddleEarthDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] parts = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;

        if (!int.TryParse(parts[0], out int day)) return false;
        int monthIndex = MonthIndexOf(parts[1]);
        if (monthIndex < 0) return false;
        if (!int.TryParse(parts[2], out int year)) return false;
        if (day < 1 || day > DaysPerMonth) return false;

        date = new MiddleEarthDate(day, monthIndex, year);
        return true;
    }
}

/// <summary>An immutable Shire-calendar date.</summary>
public readonly struct MiddleEarthDate : IEquatable<MiddleEarthDate>
{
    public readonly int Day;
    public readonly int MonthIndex;
    public readonly int Year;

    public string MonthName =>
        MonthIndex >= 0 && MonthIndex < MiddleEarthCalendar.Months.Length
            ? MiddleEarthCalendar.Months[MonthIndex]
            : "?";

    public MiddleEarthDate(int day, int monthIndex, int year)
    {
        Day = day;
        MonthIndex = monthIndex;
        Year = year;
    }

    public bool SameMonth(MiddleEarthDate other) => MonthIndex == other.MonthIndex && Year == other.Year;

    public bool Equals(MiddleEarthDate other) => Day == other.Day && MonthIndex == other.MonthIndex && Year == other.Year;
    public override bool Equals(object obj) => obj is MiddleEarthDate other && Equals(other);
    public override int GetHashCode() => (Year * 1000) + (MonthIndex * 31) + Day;

    public override string ToString() => $"{Day} {MonthName} {Year} {MiddleEarthCalendar.EraSuffix}";
    public string ToShortString() => $"{Day} {MonthName} {Year}";
}
