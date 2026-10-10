using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Title screen in the "card menu" direction: the logo centred over the painted background and
/// the menu as large illustrated cards (Continue · New Campaign · Quit). Replaces the authored
/// left-hand menu column, which is hidden. Card art lives in Resources/UI/MenuCards.
/// </summary>
public class TitleCardMenu : MonoBehaviour
{
    private const float CardWidth = 300f;
    private const float CardHeight = 440f;

    public static TitleCardMenu Build(RectTransform screenRoot, UnityAction onContinue, UnityAction onNew, UnityAction onQuit, string continueSubtitle)
    {
        Transform existing = screenRoot.Find("TitleCardMenu");
        if (existing != null) return existing.GetComponent<TitleCardMenu>();

        var go = new GameObject("TitleCardMenu", typeof(RectTransform));
        var root = go.GetComponent<RectTransform>();
        root.SetParent(screenRoot, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var menu = go.AddComponent<TitleCardMenu>();

        // Gentle vignette so the logo and cards read over the painting.
        var veil = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var veilRt = veil.GetComponent<RectTransform>();
        veilRt.SetParent(root, false);
        veilRt.anchorMin = Vector2.zero;
        veilRt.anchorMax = Vector2.one;
        veilRt.offsetMin = veilRt.offsetMax = Vector2.zero;
        var veilImg = veil.GetComponent<Image>();
        veilImg.sprite = HudSprites.FadeUp;
        veilImg.color = new Color(1f, 1f, 1f, 0.75f);
        veilImg.raycastTarget = false;

        Sprite logo = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == "RuneboardLogo");
        if (logo != null)
        {
            var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var lrt = logoGo.GetComponent<RectTransform>();
            lrt.SetParent(root, false);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -24f);
            float w = 560f;
            lrt.sizeDelta = new Vector2(w, w * logo.rect.height / logo.rect.width);
            var li = logoGo.GetComponent<Image>();
            li.sprite = logo;
            li.preserveAspect = true;
            li.raycastTarget = false;
        }

        var rowGo = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var row = rowGo.GetComponent<RectTransform>();
        row.SetParent(root, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.anchoredPosition = new Vector2(0f, 60f);
        row.sizeDelta = new Vector2(1200f, CardHeight + 40f);
        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 48f;
        hlg.childAlignment = TextAnchor.LowerCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        if (onContinue != null) CreateCard(row, "Continue", "Continue", continueSubtitle ?? "Resume your campaign", onContinue);
        CreateCard(row, "NewCampaign", "New Campaign", "Choose a campaign and a leader", onNew);
        CreateCard(row, "Quit", "Quit", "Leave Runeboard", onQuit);
        return menu;
    }

    private static void CreateCard(RectTransform row, string art, string title, string subtitle, UnityAction onClick)
    {
        var slot = new GameObject(title, typeof(RectTransform));
        var slotRt = slot.GetComponent<RectTransform>();
        slotRt.SetParent(row, false);
        slotRt.sizeDelta = new Vector2(CardWidth, CardHeight);

        var cardGo = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button), typeof(TitleCardHover));
        var card = cardGo.GetComponent<RectTransform>();
        card.SetParent(slotRt, false);
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.one;
        card.offsetMin = card.offsetMax = Vector2.zero;
        var face = cardGo.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.05f, 0.06f, 0.09f, 0.96f);
        face.fillBottom = new Color(0.03f, 0.035f, 0.05f, 0.98f);
        face.inset = 6f;
        face.ornament = 6f;
        face.bottomOrnament = true;
        var button = cardGo.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onClick?.Invoke(); });

        var titleText = Text(card, "Title", title, 30f, true);
        var trt = titleText.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(12f, -62f);
        trt.offsetMax = new Vector2(-12f, -14f);

        Sprite sprite = Resources.Load<Sprite>($"UI/MenuCards/{art}");
        var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var art_rt = artGo.GetComponent<RectTransform>();
        art_rt.SetParent(card, false);
        art_rt.anchorMin = Vector2.zero;
        art_rt.anchorMax = Vector2.one;
        art_rt.offsetMin = new Vector2(16f, 80f);
        art_rt.offsetMax = new Vector2(-16f, -66f);
        var img = artGo.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = false;
        img.raycastTarget = false;
        if (sprite == null) img.color = new Color(0.1f, 0.1f, 0.12f, 1f);

        var subText = Text(card, "Subtitle", subtitle, 16f, false);
        var srt = subText.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.offsetMin = new Vector2(16f, 20f);
        srt.offsetMax = new Vector2(-16f, 74f);
    }

    private static TextMeshProUGUI Text(RectTransform parent, string name, string value, float size, bool display)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        t.text = value;
        return t;
    }
}

/// <summary>Lift and brighten a title card on hover.</summary>
public class TitleCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Vector2 rest;
    private bool init;

    public void OnPointerEnter(PointerEventData e) => Pose(true);
    public void OnPointerExit(PointerEventData e) => Pose(false);

    private void Pose(bool lifted)
    {
        var rt = (RectTransform)transform;
        if (!init) { rest = rt.anchoredPosition; init = true; }
        rt.anchoredPosition = rest + (lifted ? new Vector2(0f, 18f) : Vector2.zero);
        rt.localScale = Vector3.one * (lifted ? 1.04f : 1f);
        if (TryGetComponent(out CinematicPanel p))
        {
            p.line = lifted ? new Color(1f, 0.85f, 0.5f, 1f) : new Color(0.80f, 0.64f, 0.36f, 0.75f);
            p.SetVerticesDirty();
        }
        if (lifted) Sounds.Instance?.PlayUiHover();
    }
}
