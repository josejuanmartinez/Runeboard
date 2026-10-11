using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OptionButtonPrefabManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private Image icon;
    [SerializeField] private Illustrations illustrations;

    public Image IconGraphic => icon;

    public void Setup(string label, string iconSpriteName = null)
    {
        if (text != null) text.text = label;

        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();

        Sprite sprite = !string.IsNullOrWhiteSpace(iconSpriteName) && illustrations != null
            ? illustrations.GetIllustrationByName(iconSpriteName, false)
            : null;

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
