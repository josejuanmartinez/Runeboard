using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum LogCategory
{
    Notification,
    Rumour,
    Event
}

public class LogManager : MonoBehaviour
{
    public static LogManager Instance { get; private set; }

    [Header("References")]
    public RectTransform content;
    public GameObject entryTemplate;
    public RectTransform collapsibleArea;
    public ScrollRect scrollRect;

    [Header("Config")]
    public int maxEntries = 200;

    [Header("New Entry Effect")]
    public float newEntryFlashDuration = 2f;
    public float newEntryStartScale = 1.08f;

    private readonly List<GameObject> entryObjects = new();
    private Colors colors;
    private bool collapsed;
    private string lastEntryText;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        colors = FindFirstObjectByType<Colors>();
        if (entryTemplate != null) entryTemplate.SetActive(false);     
    }

    public static void Log(LogCategory category, string nation, string character, string text)
    {
        if (Instance == null || string.IsNullOrWhiteSpace(text)) return;
        Instance.AddEntry(category, nation, character, text);
    }

    private void AddEntry(LogCategory category, string nation, string character, string text)
    {
        if (content == null || entryTemplate == null) return;

        string formatted = FormatEntry(nation, character, text);
        // Several systems (turn-start grants, per-hop notifications, etc.) can fire the exact
        // same line back to back — skip the repeat instead of stacking identical rows.
        if (formatted == lastEntryText) return;
        lastEntryText = formatted;

        GameObject row = Instantiate(entryTemplate, content);
        row.SetActive(true);
        row.transform.SetAsLastSibling();

        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = formatted;
            Color targetColor = ResolveColor(category);
            StartCoroutine(FlashNewEntry(label, row.transform, targetColor));
        }

        entryObjects.Add(row);
        while (entryObjects.Count > maxEntries)
        {
            GameObject oldest = entryObjects[0];
            entryObjects.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    // New entries pop in white/oversized and ease back to their category color and normal
    // scale over newEntryFlashDuration, so the freshest line in the log briefly stands out
    // without needing a separate highlight graphic on the entry prefab.
    private IEnumerator FlashNewEntry(TextMeshProUGUI label, Transform rowTransform, Color targetColor)
    {
        label.color = Color.white;
        if (rowTransform != null) rowTransform.localScale = Vector3.one * newEntryStartScale;

        float elapsed = 0f;
        while (elapsed < newEntryFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / newEntryFlashDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (label == null) yield break;
            label.color = Color.Lerp(Color.white, targetColor, eased);
            if (rowTransform != null) rowTransform.localScale = Vector3.one * Mathf.Lerp(newEntryStartScale, 1f, eased);

            yield return null;
        }

        label.color = targetColor;
        if (rowTransform != null) rowTransform.localScale = Vector3.one;
    }

    private static string FormatEntry(string nation, string character, string text)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(nation)) parts.Add($"[{nation}]");
        if (!string.IsNullOrWhiteSpace(character) && !string.Equals(character, nation)) parts.Add($"[{character}]");
        parts.Add(text);
        return string.Join(" - ", parts);
    }

    private Color ResolveColor(LogCategory category)
    {
        if (colors == null) colors = FindFirstObjectByType<Colors>();
        if (colors == null) return Color.white;

        return category switch
        {
            LogCategory.Rumour => colors.logRumour,
            LogCategory.Event => colors.logEvent,
            _ => colors.logNotification,
        };
    }
}
