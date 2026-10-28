using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hovering a playable leader in the top bar (or one of the realms listed under them) draws their
/// alliance in the middle of the screen: the leader's portrait at the centre and every realm of
/// their alignment the player knows about around it, each tied to the centre by a line that
/// grows out — gold and solid for realms that have joined, dim for those still to be won, red for
/// the fallen. The hovered realm stands out. Leaving the icon fades it all away.
/// </summary>
public class AllianceConstellation : MonoBehaviour
{
    private const float Radius = 230f;
    private const float CentreSize = 150f;
    private const float RealmSize = 82f;

    private static AllianceConstellation instance;

    private Canvas canvas;
    private RectTransform root;
    private CanvasGroup group;
    private Coroutine running;
    private PlayableLeader shownFor;
    private float currentNode = RealmSize;

    public static void Show(PlayableLeaderIcon icon, NonPlayableLeader highlighted = null)
    {
        if (icon == null || icon.playableLeader == null) return;
        Ensure().Open(icon, highlighted);
    }

    public static void Hide()
    {
        if (instance != null) instance.Close();
    }

    private static AllianceConstellation Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("AllianceConstellation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        instance = go.AddComponent<AllianceConstellation>();
        instance.canvas = go.GetComponent<Canvas>();
        instance.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        instance.canvas.sortingOrder = 150; // over the board and most of the HUD, under dialogs
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        instance.root = (RectTransform)go.transform;
        instance.group = go.GetComponent<CanvasGroup>();
        instance.group.blocksRaycasts = false; // a view only: the pointer stays on the icon
        instance.group.alpha = 0f;
        return instance;
    }

    private void Open(PlayableLeaderIcon icon, NonPlayableLeader highlighted)
    {
        if (running != null) StopCoroutine(running);
        foreach (Transform child in root) Destroy(child.gameObject);
        shownFor = icon.playableLeader;
        running = StartCoroutine(Build(icon, highlighted));
    }

    private void Close()
    {
        if (running != null) StopCoroutine(running);
        shownFor = null;
        running = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        while (group.alpha > 0f)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / 0.15f);
            yield return null;
        }
        foreach (Transform child in root) Destroy(child.gameObject);
    }

    private IEnumerator Build(PlayableLeaderIcon icon, NonPlayableLeader highlighted)
    {
        PlayableLeader leader = icon.playableLeader;
        Color nation = leader.nationColor;
        // The realms the player knows of on this side (the revealed icons under the leader).
        List<NonPlayableLeaderIcon> realms = icon.nonPlayableLeaderIcons != null && icon.nonPlayableLeaderIcons.nonPlayableLeaderIcons != null
            ? icon.nonPlayableLeaderIcons.nonPlayableLeaderIcons.Where(r => r != null && r.nonPlayableLeader != null && r.IsRevealed).ToList()
            : new List<NonPlayableLeaderIcon>();

        // Many realms: a wider ring of smaller portraits, so names never overlap.
        float nodeSize = realms.Count > 12 ? RealmSize * 0.78f : RealmSize;
        float radius = Mathf.Max(Radius, realms.Count * (nodeSize + 46f) / (2f * Mathf.PI));
        currentNode = nodeSize;
        // A dark pool behind it all, so it reads over the board.
        Image pool = NewImage(root, "Pool", HudSprites.Circle, new Color(0.02f, 0.018f, 0.015f, 0.9f), new Vector2((radius + nodeSize) * 2.5f, (radius + nodeSize) * 2.5f), Vector2.zero);

        // Spokes first, so the portraits sit on top of them.
        var spokes = new List<(Image line, float length)>();
        var nodes = new List<RectTransform>();
        for (int i = 0; i < realms.Count; i++)
        {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI * 2f / Mathf.Max(1, realms.Count);
            Vector2 at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            NonPlayableLeader npl = realms[i].nonPlayableLeader;
            bool joined = npl.joined;
            bool fallen = npl.killed;
            Color lineColour = fallen ? new Color(0.75f, 0.2f, 0.18f, 0.7f) : joined ? UiTheme.Gold : new Color(0.6f, 0.58f, 0.52f, 0.45f);
            Image line = NewImage(root, "Spoke", null, lineColour, new Vector2(0f, joined ? 4f : 2f), Vector2.zero);
            line.rectTransform.pivot = new Vector2(0f, 0.5f);
            line.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle * Mathf.Rad2Deg);
            spokes.Add((line, radius - nodeSize * 0.5f));
            nodes.Add(Node(npl, at, npl == highlighted, joined, fallen));
        }
        RectTransform centre = Portrait(root, "Leader", icon.LeaderSprite, nation, CentreSize, Vector2.zero, 4f);
        Label(centre, leader.characterName, -CentreSize * 0.5f - 18f, 22f, UiTheme.Gold);
        if (realms.Count == 0) Label(centre, "No realm of this side discovered yet", -CentreSize * 0.5f - 44f, 15f, UiTheme.Muted);
        else
        {
            int joinedCount = realms.Count(r => r.nonPlayableLeader.joined);
            Label(centre, $"{joinedCount} of {realms.Count} known realms joined", -CentreSize * 0.5f - 44f, 15f, UiTheme.Muted);
        }

        // The animation: the pool and centre bloom, the spokes grow out, the realms pop in turn.
        float t = 0f;
        const float Total = 0.55f;
        centre.localScale = Vector3.zero;
        foreach (RectTransform node in nodes) node.localScale = Vector3.zero;
        while (t < Total + 0.06f * nodes.Count)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / 0.15f);
            centre.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t / 0.3f));
            pool.rectTransform.localScale = Vector3.one * Mathf.SmoothStep(0.6f, 1f, Mathf.Clamp01(t / 0.3f));
            for (int i = 0; i < spokes.Count; i++)
            {
                float p = Mathf.Clamp01((t - 0.15f - i * 0.06f) / 0.25f);
                spokes[i].line.rectTransform.sizeDelta = new Vector2(spokes[i].length * Mathf.SmoothStep(0f, 1f, p), spokes[i].line.rectTransform.sizeDelta.y);
                float q = Mathf.Clamp01((t - 0.3f - i * 0.06f) / 0.25f);
                nodes[i].localScale = Vector3.one * EaseOutBack(q);
            }
            yield return null;
        }
        // Joined realms keep a slow shimmer along their line while the view is open.
        while (shownFor == leader)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            for (int i = 0; i < spokes.Count; i++)
                if (realms[i].nonPlayableLeader.joined) spokes[i].line.color = new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, pulse);
            yield return null;
        }
    }

    private RectTransform Node(NonPlayableLeader npl, Vector2 at, bool highlighted, bool joined, bool fallen)
    {
        Illustrations art = FindAnyObjectByType<Illustrations>();
        Sprite sprite = art != null ? art.GetIllustrationByName(npl.characterName) : null;
        Color ring = fallen ? new Color(0.6f, 0.15f, 0.12f) : joined ? UiTheme.Gold : new Color(0.45f, 0.42f, 0.36f);
        float size = highlighted ? currentNode * 1.25f : currentNode;
        RectTransform node = Portrait(root, npl.characterName, sprite, ring, size, at, highlighted ? 4f : 2.5f);
        if (!joined || fallen)
        {
            // Not (or no longer) on this side: the portrait sinks back.
            Image face = node.Find("Window/Face")?.GetComponent<Image>();
            if (face != null && sprite != null) face.color = fallen ? new Color(0.45f, 0.3f, 0.3f) : new Color(0.62f, 0.62f, 0.62f);
        }
        string status = fallen ? "<color=#D25A4A>fallen</color>" : joined ? $"<color={UiTheme.GoldHex}>joined</color>" : $"<color={UiTheme.MutedHex}>not yet won</color>";
        Label(node, $"{DisplayNames.Format(npl.characterName)}\n<size=80%>{status}</size>", -size * 0.5f - 22f, highlighted ? 17f : 14f, UiTheme.Text);
        return node;
    }

    // A round portrait: a coloured ring around the art, masked to a disc.
    private static RectTransform Portrait(RectTransform parent, string name, Sprite sprite, Color ring, float size, Vector2 at, float ringWidth)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = at;
        NewImage(rt, "Ring", HudSprites.Circle, ring, new Vector2(size, size), Vector2.zero);
        Image window = NewImage(rt, "Window", HudSprites.Circle, Color.white, new Vector2(size - ringWidth * 2f, size - ringWidth * 2f), Vector2.zero);
        window.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        Image face = NewImage(window.rectTransform, "Face", sprite, Color.white, new Vector2(size - ringWidth * 2f, size - ringWidth * 2f), Vector2.zero);
        if (sprite == null) face.color = new Color(0.12f, 0.11f, 0.1f);
        return rt;
    }

    private static void Label(RectTransform parent, string text, float y, float size, Color colour)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(260f, 60f);
        rt.anchoredPosition = new Vector2(0f, y + size);
        var label = go.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleTitle(label, size, colour);
        label.alignment = TextAlignmentOptions.Top;
        label.richText = true;
        label.raycastTarget = false;
        label.text = text;
    }

    private static Image NewImage(RectTransform parent, string name, Sprite sprite, Color colour, Vector2 size, Vector2 at)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = at;
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}

/// <summary>Opens the alliance view while a playable leader's portrait in the top bar is hovered.</summary>
public class PlayableLeaderHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PlayableLeaderIcon icon;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PopupManager.IsShowing || BoardNavigator.IsNavigationInputLocked()) return;
        AllianceConstellation.Show(icon);
    }
    public void OnPointerExit(PointerEventData eventData) => AllianceConstellation.Hide();
    private void OnDisable() => AllianceConstellation.Hide();
}
