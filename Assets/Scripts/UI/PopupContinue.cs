using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Popup "Continue" buttons are wired in prefabs straight to Hide, which closed story text
/// mid-sentence. Rewire swaps that persistent call for a handler that first completes the
/// typewriter and only closes on the next press.
/// </summary>
public static class PopupContinue
{
    public static bool Rewire(MonoBehaviour owner, string hideMethod, UnityAction onContinue)
    {
        bool any = false;
        foreach (Button button in owner.GetComponentsInChildren<Button>(true))
        {
            bool matched = false;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) != owner || button.onClick.GetPersistentMethodName(i) != hideMethod) continue;
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                matched = true;
            }
            if (matched) { button.onClick.AddListener(onContinue); any = true; }
        }
        return any;
    }
}
