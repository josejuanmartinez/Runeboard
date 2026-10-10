using System.Collections.Generic;
using System.Text;

/// <summary>
/// Turns authored identifiers ("FangornSCamp", "GuardPc", "PalantirOfOrthanc") into
/// player-facing names ("Fangorn's Camp", "Guard PC", "Palantir of Orthanc").
/// Names are only reformatted for display — the raw strings stay the lookup keys.
/// </summary>
public static class DisplayNames
{
    // Identifiers whose word-splitting can't recover the real name.
    private static readonly Dictionary<string, string> Overrides = new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "FangornSCamp", "Fangorn's Camp" },
        { "HelmDeep", "Helm's Deep" },
        { "MtGram", "Mount Gram" },
        { "WestFold", "Westfold" },
        { "BeornHouse", "Beorn's House" },
        { "ThorinHall", "Thorin's Halls" },
        { "BrownLands", "Brown Lands" },
        { "BountyfulSeason", "Bountiful Season" },
        { "TheDrownOfTheStone", "The Drowning of the Stone" },
        { "BagginsDontLeaveHere", "Bagginses Don't Leave Here" },
        { "LondDaerEnedh", "Lond Daer Enedh" },
    };

    private static readonly HashSet<string> Acronyms = new(System.StringComparer.OrdinalIgnoreCase) { "PC", "PCs", "XP", "AI" };
    private static readonly HashSet<string> MinorWords = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "with"
    };

    public static string Format(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string trimmed = raw.Trim();
        if (Overrides.TryGetValue(trimmed, out string known)) return known;

        string[] words = SplitWords(trimmed).Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (Acronyms.Contains(w)) { words[i] = w.Length > 2 ? "PCs" : w.ToUpperInvariant(); continue; }
            if (i > 0 && MinorWords.Contains(w)) { words[i] = w.ToLowerInvariant(); continue; }
            // Leave mixed-case words alone ("McCoy"); only capitalise the first letter.
            words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
        }
        return string.Join(" ", words);
    }

    // Inserts spaces at lower→Upper and digit boundaries, and before the last capital of an
    // acronym run ("PCName" → "PC Name"). Existing spaces and underscores become single spaces.
    private static string SplitWords(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c))
            {
                char prev = value[i - 1];
                bool nextLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextLower)) sb.Append(' ');
            }
            else if (i > 0 && char.IsDigit(c) && char.IsLetter(value[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
