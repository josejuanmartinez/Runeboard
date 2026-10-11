using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CarouselItem : MonoBehaviour, IPointerClickHandler
{
    public Image image;
    public TextMeshProUGUI label;

    private Action clickHandler;
    private bool presentationApplied;

    public void ApplyPresentation()
    {
        if (presentationApplied || image == null || label == null) return;
        presentationApplied = true;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(244, 314);
        // The old portrait was nested beneath Cloud's mask.
        image.transform.SetParent(root, false);
        var cloud = transform.Find("Cloud");
        if (cloud != null) cloud.gameObject.SetActive(false);
        // Art-card face, same language as the hand and the card preview: full-bleed painting in
        // a gold hairline frame, the name on a dark scrim rising from the bottom.
        var background = GetComponent<Image>();
        if (background != null)
        {
            if (GetComponent<ImageUnaffectedBySkin>() == null) gameObject.AddComponent<ImageUnaffectedBySkin>();
            background.enabled = true;
            background.sprite = null;
            background.material = null;
            background.color = new Color(.04f, .037f, .032f, 1); // click target + backing
            var outline = GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        var windowGo = new GameObject("ArtWindow", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ImageUnaffectedBySkin));
        window = (RectTransform)windowGo.transform;
        window.SetParent(root, false);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(Inset, Inset);
        window.offsetMax = new Vector2(-Inset, -Inset);
        windowGo.GetComponent<Image>().raycastTarget = false;
        windowGo.GetComponent<Mask>().showMaskGraphic = false;

        var art = image.rectTransform;
        art.SetParent(window, false);
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, .5f);
        image.preserveAspect = false;
        image.color = Color.white;
        image.raycastTarget = false;
        FitArt();

        var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ImageUnaffectedBySkin));
        var scrim = (RectTransform)scrimGo.transform;
        scrim.SetParent(window, false);
        scrim.anchorMin = new Vector2(0, 0);
        scrim.anchorMax = new Vector2(1, 0);
        scrim.pivot = new Vector2(.5f, 0);
        scrim.sizeDelta = new Vector2(0, 130);
        var scrimImage = scrimGo.GetComponent<Image>();
        scrimImage.sprite = HudSprites.FadeUp;
        scrimImage.color = new Color(1, 1, 1, .97f);
        scrimImage.raycastTarget = false;

        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
        var frame = (RectTransform)frameGo.transform;
        frame.SetParent(root, false);
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        var framePanel = frameGo.GetComponent<CinematicPanel>();
        framePanel.fill = false;
        framePanel.inset = 3f;
        framePanel.lineWidth = 1.4f;
        framePanel.ornament = 0f;
        framePanel.line = new Color(.86f, .70f, .40f, .85f);
        framePanel.raycastTarget = false;

        var title = label.rectTransform;
        title.SetParent(root, false);
        title.SetAsLastSibling();
        title.anchorMin = new Vector2(0, 0);
        title.anchorMax = new Vector2(1, 0);
        title.pivot = new Vector2(.5f, 0);
        title.anchoredPosition = new Vector2(0, 12);
        title.sizeDelta = new Vector2(-24, 60);
        label.alignment = TextAlignmentOptions.Bottom;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14;
        label.fontSizeMax = 24;
        label.color = UiTheme.Text;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
    }

    private const float Inset = 6f;
    private RectTransform window;

    // Cover-fit the portrait into the window, favouring the top (faces) since the scrim covers the bottom.
    private void FitArt()
    {
        if (window == null || image == null) return;
        Vector2 w = new(244 - Inset * 2, 314 - Inset * 2);
        Sprite s = image.sprite;
        float aspect = s != null ? s.rect.width / s.rect.height : w.x / w.y;
        Vector2 size = aspect > w.x / w.y ? new Vector2(w.y * aspect, w.y) : new Vector2(w.x, w.x / aspect);
        image.rectTransform.sizeDelta = size;
        image.rectTransform.anchoredPosition = new Vector2(0, -(size.y - w.y) * 0.5f);
    }

    public void SetClickHandler(Action handler)
    {
        clickHandler = handler;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            clickHandler?.Invoke();
        }
    }

    public void SetSprite(Sprite spr)
    {
        ApplyPresentation();
        image.sprite = spr;
        FitArt();
    }

    public void SetLabel(string str, AlignmentEnum? alignment = null)
    {
        if (label == null) return;
        FontManager.Instance?.ApplyCurrentFont(label);
        label.richText = true;
        label.extraPadding = true;
        ApplyPresentation();
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.text = EnsureAlignmentSpritePrefix(str, alignment);
        label.enableVertexGradient = false;
        label.fontStyle = FontStyles.Normal;
        label.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
        label.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        label.fontMaterial.DisableKeyword("UNDERLAY_ON");
        label.outlineColor = new Color(.025f, .03f, .035f, 1f);
        label.outlineWidth = .06f;
        label.ForceMeshUpdate(true, true);
    }

    string EnsureAlignmentSpritePrefix(string value, AlignmentEnum? alignment)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("<sprite"))
        {
            return value;
        }

        if (alignment == null) return value;

        string spriteName = alignment.Value.ToString();
        return $"<sprite name=\"{spriteName}\">{spriteName} {value.Trim()}";
    }
}
