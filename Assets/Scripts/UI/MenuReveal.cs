using System.Collections;
using UnityEngine;

/// <summary>Short unscaled reveal; owns only its dedicated group's opacity.</summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class MenuReveal : MonoBehaviour
{
    private CanvasGroup group;
    private void OnEnable()
    {
        group = GetComponent<CanvasGroup>();
        StartCoroutine(Reveal());
    }
    private IEnumerator Reveal()
    {
        for (float t = 0; t < 0.24f; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.SmoothStep(0, 1, t / 0.24f);
            yield return null;
        }
        group.alpha = 1;
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (group != null) group.alpha = 1;
    }
}
