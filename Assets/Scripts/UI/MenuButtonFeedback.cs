using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Shared, quiet pointer and keyboard feedback for authored menu buttons.</summary>
[RequireComponent(typeof(Button))]
public sealed class MenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    public Graphic highlight;
    private Button button;
    private bool hovered;
    private bool selected;
    private bool pressed;
    private Coroutine transition;

    private void Awake() => button = GetComponent<Button>();
    public void OnPointerEnter(PointerEventData e) { hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; Refresh(); }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
    public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }
    public void OnSelect(BaseEventData e) { selected = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { selected = false; Refresh(); }

    private void Refresh()
    {
        if (highlight == null || !isActiveAndEnabled) return;
        if (transition != null) StopCoroutine(transition);
        float alpha = button != null && button.IsActive() && button.IsInteractable() ? (pressed ? 0.95f : hovered || selected ? 0.65f : 0f) : 0f;
        transition = StartCoroutine(Fade(alpha));
    }

    private IEnumerator Fade(float target)
    {
        float initial = highlight.color.a;
        for (float time = 0; time < 0.14f; time += Time.unscaledDeltaTime)
        {
            highlight.color = new Color(1, 1, 1, Mathf.Lerp(initial, target, Mathf.SmoothStep(0, 1, time / 0.14f)));
            yield return null;
        }
        highlight.color = new Color(1, 1, 1, target);
        transition = null;
    }

    private void OnDisable()
    {
        if (transition != null) StopCoroutine(transition);
        transition = null;
        hovered = selected = pressed = false;
        if (highlight != null) highlight.color = new Color(1, 1, 1, 0);
    }
}
