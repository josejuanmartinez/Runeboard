using TMPro;
using UnityEngine;

public class HexNumberManager : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI textWidget;
    public void Show(Hex hex)
    {
        textWidget.text = hex != null ? hex.GetPlaceLabel() : string.Empty;
    }
}
