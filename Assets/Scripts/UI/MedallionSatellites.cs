using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lays out a playable leader's allied nations (NonPlayableLeaderIcon children) as a compact row
/// of small round portraits under the medallion, slightly overlapping like stacked avatars.
/// Only revealed nations take a slot (the old grid reserved invisible cells for unrevealed ones,
/// which scattered the few visible icons); beyond MaxShown a "+N" counter is shown.
/// Replaces the authored GridLayoutGroup at runtime (HudDirector.MakeMedallion).
/// </summary>
public class MedallionSatellites : MonoBehaviour
{
    private const int MaxShown = 5;
    private const float Size = 34f;
    private const float Overlap = 7f;

    private readonly List<NonPlayableLeaderIcon> visible = new();
    private TextMeshProUGUI more;
    private float nextRefresh;
    private int lastSignature = -1;

    public static void Attach(RectTransform grid)
    {
        if (grid == null || grid.GetComponent<MedallionSatellites>() != null) return;
        if (grid.TryGetComponent(out GridLayoutGroup layout)) layout.enabled = false;
        grid.gameObject.AddComponent<MedallionSatellites>();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;

        visible.Clear();
        int signature = 17;
        foreach (Transform child in transform)
        {
            if (!child.TryGetComponent(out NonPlayableLeaderIcon icon)) continue;
            Style(icon);
            bool revealed = icon.canvasGroup != null && icon.canvasGroup.alpha > 0.01f;
            if (revealed) { visible.Add(icon); signature = signature * 31 + icon.transform.GetSiblingIndex() * 7 + icon.name.GetHashCode(); }
        }
        if (signature == lastSignature) return;
        lastSignature = signature;

        int shown = Mathf.Min(MaxShown, visible.Count);
        int extra = visible.Count - shown;
        float step = Size - Overlap;
        float width = shown > 0 ? Size + step * (shown - 1) + (extra > 0 ? step : 0f) : 0f;
        float x = -width * 0.5f + Size * 0.5f;
        foreach (Transform child in transform)
            if (child.TryGetComponent(out NonPlayableLeaderIcon icon)) icon.transform.localScale = Vector3.zero;
        for (int i = 0; i < shown; i++)
        {
            var rt = (RectTransform)visible[i].transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Size, Size);
            rt.anchoredPosition = new Vector2(x + step * i, -Size * 0.5f);
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(i); // later ones draw on top of the overlap
        }

        EnsureMore();
        more.gameObject.SetActive(extra > 0);
        if (extra > 0)
        {
            more.text = $"+{extra}";
            more.rectTransform.anchoredPosition = new Vector2(x + step * shown, -Size * 0.5f);
            more.transform.SetAsLastSibling();
        }
    }

    // Round portrait with a thin ring; the alignment badge is redundant here (same side as the
    // medallion) and hidden. SetHired() still recolours the ring, so joined nations read white.
    private static void Style(NonPlayableLeaderIcon icon)
    {
        Transform content = icon.transform.Find("Content");
        if (content == null || content.GetComponent<Mask>() != null) return;
        if (content.TryGetComponent(out Image disc))
        {
            disc.sprite = HudSprites.Circle;
            disc.type = Image.Type.Simple;
            disc.color = new Color(0.03f, 0.03f, 0.035f, 1f);
        }
        content.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        if (icon.border != null)
        {
            icon.border.sprite = HudSprites.Ring;
            icon.border.type = Image.Type.Simple;
            icon.border.rectTransform.localScale = Vector3.one;
            // The realm's own colour, as its borders show on the map.
            if (icon.nonPlayableLeader != null) icon.border.color = UiTheme.NationRing(icon.nonPlayableLeader.nationColor);
        }
        if (icon.alignmentImage != null) icon.alignmentImage.gameObject.SetActive(false);
    }

    private void EnsureMore()
    {
        if (more != null) return;
        var go = new GameObject("More", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(Size, Size);
        more = go.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(more, 15f, UiTheme.Muted);
        more.alignment = TextAlignmentOptions.Center;
        more.raycastTarget = false;
    }
}
