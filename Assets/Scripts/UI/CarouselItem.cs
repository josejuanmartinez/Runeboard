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
        var background = GetComponent<Image>();
        if (background != null)
        {
            if (GetComponent<ImageUnaffectedBySkin>() == null) gameObject.AddComponent<ImageUnaffectedBySkin>();
            background.enabled = true;
            background.sprite = null;
            background.material = null;
            background.color = new Color(.075f, .082f, .082f, 1);
            var outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.62f, .49f, .28f, 1);
            outline.effectDistance = new Vector2(1, -1);
        }
        var art = image.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(.5f, 1);
        art.pivot = new Vector2(.5f, 1);
        art.anchoredPosition = new Vector2(0, -10);
        art.sizeDelta = new Vector2(224, 224);
        image.preserveAspect = true;
        image.color = Color.white;
        var title = label.rectTransform;
        title.anchorMin = new Vector2(0, 0);
        title.anchorMax = new Vector2(1, 0);
        title.pivot = new Vector2(.5f, 0);
        title.anchoredPosition = new Vector2(0, 7);
        title.sizeDelta = new Vector2(-20, 65);
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14;
        label.fontSizeMax = 22;
        label.color = new Color(.9f, .81f, .61f);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
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
