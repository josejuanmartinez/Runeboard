using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reflects the game's turn lock without owning or advancing gameplay state.</summary>
[RequireComponent(typeof(Button), typeof(CanvasGroup))]
public sealed class EndTurnPresentation : MonoBehaviour
{
    public TMP_Text label;
    private Button button;
    private CanvasGroup group;
    private void Awake() { button = GetComponent<Button>(); group = GetComponent<CanvasGroup>(); }
    private void LateUpdate()
    {
        bool ready = button.interactable;
        group.alpha = ready ? 1f : 0.52f;
        if (label != null)
            label.text = ready ? "End turn" : ConfirmationDialog.IsShowing ? "Confirm end turn" : "Turn in progress";
    }
}
