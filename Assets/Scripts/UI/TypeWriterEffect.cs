using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TypewriterEffect : MonoBehaviour
{
    public TMP_Text textMeshPro;
    public AutoScroll autoScroll;
    
    [TextArea] public string fullText;

    [Tooltip("Characters per second. Higher values type faster.")]
    public float typingSpeed = 20f;

    [System.NonSerialized] public Coroutine coroutine;

    public bool startUponInstantiating = false;

    void Start()
    {
        if (textMeshPro == null) textMeshPro = GetComponent<TMP_Text>();
        fullText ??= string.Empty;
        if (fullText.Trim().Length == 0 && textMeshPro != null && textMeshPro.text.Trim().Length > 0)
        {
            fullText = textMeshPro.text;
            textMeshPro.text = "";
        }
        if (startUponInstantiating) StartWriting(fullText);
    }
    public void StartWriting(string text = null)
    {
        if (coroutine != null) StopCoroutine(coroutine);
        text ??= fullText ?? string.Empty;
        fullText = text;

        // Unity rejects StartCoroutine when any ancestor is inactive. A popup can be populated
        // while its scroll Content is still outside the active hierarchy; render the complete
        // text in that case and let the next visible popup animate normally.
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            coroutine = null;
            if (textMeshPro == null) textMeshPro = GetComponent<TMP_Text>();
            if (textMeshPro != null) textMeshPro.text = text;
            autoScroll?.Refresh();
            return;
        }
        coroutine = StartCoroutine(TypeText(text));
    }

    /// <summary>True while characters are still being revealed.</summary>
    public bool IsTyping => coroutine != null;

    /// <summary>Reveals the rest of the text at once (first click on Continue does this).</summary>
    public void Complete()
    {
        if (coroutine == null) return;
        StopCoroutine(coroutine);
        coroutine = null;
        if (textMeshPro != null) textMeshPro.text = fullText ?? string.Empty;
        autoScroll?.Refresh();
    }

    public void Clear()
    {
        if (coroutine != null) { StopCoroutine(coroutine); coroutine = null; }
        if (textMeshPro != null) textMeshPro.text = string.Empty;
    }

    IEnumerator TypeText(string text = null)
    {
        textMeshPro.text = ""; // Clear text initially

        if (text == null) text = fullText;
        if (string.IsNullOrEmpty(text))
        {
            coroutine = null;
            yield break;
        }

        float charactersPerSecond = Mathf.Max(0.01f, typingSpeed);
        float visibleCharacters = 0f;
        int lastShownCount = -1;

        while (lastShownCount < text.Length)
        {
            visibleCharacters += charactersPerSecond * Time.unscaledDeltaTime;
            int shownCount = Mathf.Clamp(Mathf.FloorToInt(visibleCharacters), 0, text.Length);

            if (shownCount != lastShownCount)
            {
                textMeshPro.text = text.Substring(0, shownCount);
                autoScroll?.Refresh();
                lastShownCount = shownCount;
            }

            if (shownCount >= text.Length)
            {
                coroutine = null;
                break;
            }

            yield return null;
        }
    }
}
