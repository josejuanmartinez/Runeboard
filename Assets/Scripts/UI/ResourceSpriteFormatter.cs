using System.Text;
using System.Text.RegularExpressions;

public static class ResourceSpriteFormatter
{
    private static readonly Regex ResourceWordRegex = new(
        @"\b(leather|timber|mounts?|iron|steel|mithril|gold)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // Rich-text tags pass through untouched: rewriting the word inside an existing
    // <sprite name="gold"> nested a tag in a tag, and the message showed the raw markup.
    private static readonly Regex TagRegex = new(@"(<[^>]*>)", RegexOptions.Compiled);

    public static string ReplaceResourceWordsWithSprites(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var sb = new StringBuilder(text.Length + 32);
        string previousTag = null;
        foreach (string part in TagRegex.Split(text))
        {
            if (part.Length == 0) continue;
            if (part[0] == '<')
            {
                sb.Append(part);
                previousTag = part;
                continue;
            }
            string tagBefore = previousTag;
            sb.Append(ResourceWordRegex.Replace(part, match =>
            {
                string token = match.Value.ToLowerInvariant();
                if (token == "mount") token = "mounts";
                // "<sprite name="gold">gold": the word already has its icon.
                bool iconed = match.Index == 0 && tagBefore != null && tagBefore.Contains($"\"{token}\"");
                return iconed ? match.Value : $"<sprite name=\"{token}\">{token}";
            }));
            previousTag = null;
        }
        return sb.ToString();
    }
}
