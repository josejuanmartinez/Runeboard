using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Layout : MonoBehaviour
{
    [SerializeField]
    private SelectedCharacterIcon selectedCharacterIcon;
    [SerializeField]
    private HexNumberManager hexNumberManager;
    [SerializeField]
    private Card environmentalCard;
    [SerializeField] private TextMeshProUGUI environmentalName;
    [SerializeField]
    private TextMeshProUGUI nationName;
    [SerializeField]
    private Image nationColorImage;

    private void Awake()
    {
        if (environmentalCard != null) environmentalCard.gameObject.SetActive(false);
    }

    public void SetEnvironmentalCard(CardData card)
    {
        if (environmentalName != null) environmentalName.text = card != null ? card.name : "No active effect";
        if (environmentalCard == null)
        {
            Debug.LogWarning("[EnvCardToken] Layout.SetEnvironmentalCard — environmentalCard field is unassigned on this Layout instance.");
            return;
        }
        if (card == null) { environmentalCard.SetEnvironmentalPulse(false); environmentalCard.gameObject.SetActive(false); return; }
        environmentalCard.gameObject.SetActive(true);
        environmentalCard.Initialize(card);
        environmentalCard.ShowEnvironmentalSprite();
        environmentalCard.SetEnvironmentalPulse(true);
        Debug.Log($"[EnvCardToken] Layout.SetEnvironmentalCard('{card.name}') applied — normalizedSpriteName='{CardNameUtility.Normalize(card.name)}', tokenActive={environmentalCard.gameObject.activeInHierarchy}");
    }

    public SelectedCharacterIcon GetSelectedCharacterIcon()
    {
        selectedCharacterIcon.gameObject.SetActive(true);
        return selectedCharacterIcon;
    }

    public HexNumberManager GetHexNumberManager()
    {
        return hexNumberManager;
    }

    public void SetNationColor(Color color)
    {
        if (nationColorImage != null) nationColorImage.color = color;
    }

    public void SetNationName(string name)
    {
        if (nationName != null) nationName.text = name;
    }
}
