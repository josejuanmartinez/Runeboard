using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Full-screen "stunning effect" fired whenever a player-controlled character's Commander,
// Agent, Emmissary or Mage skill changes: shows the character's own card centered on screen
// with the skill counting up (or down) to its new value. Self-builds like SituationCardsUI so
// it never needs manual scene wiring.
public class LevelChangeEffectUI : MonoBehaviour
{
    public static LevelChangeEffectUI Instance { get; private set; }

    [Header("Timing")]
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float holdDuration = 1.4f;
    [SerializeField] private float fadeOutDuration = 0.35f;
    [SerializeField] private float counterDuration = 0.7f;

    [Header("Confetti (level up only)")]
    [SerializeField] private int confettiCount = 40;
    [SerializeField] private float confettiDuration = 1.3f;

    private static readonly string[] SkillSpriteNames = { "commander", "agent", "emmissary", "mage" };
    private static readonly string[] SkillDisplayNames = { "Commander", "Agent", "Emmissary", "Mage" };

    private static readonly Color UpColor = new Color(1f, 0.85f, 0.35f);
    private static readonly Color DownColor = new Color(0.85f, 0.35f, 0.3f);

    private CanvasGroup overlayGroup;
    private RectTransform cardSlot;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI skillLabel;
    private GameObject cardInstance;
    private Coroutine activeCoroutine;

    public static void Show(Character character, CharacterSkillEnum skill, int previousLevel, int newLevel)
    {
        if (character == null || previousLevel == newLevel) return;
        if (Instance == null)
        {
            var go = new GameObject("LevelChangeEffectUI");
            go.AddComponent<LevelChangeEffectUI>();
        }
        Instance.ShowInternal(character, skill, previousLevel, newLevel);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210; // above SituationCardsUI's overlay
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        var overlayGo = new GameObject("Overlay");
        overlayGo.transform.SetParent(transform, false);
        var ort = overlayGo.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.sizeDelta = Vector2.zero;
        var dimImg = overlayGo.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0.02f, 0.6f);
        dimImg.raycastTarget = true;
        overlayGroup = overlayGo.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        overlayGo.SetActive(false);

        var btn = overlayGo.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(DismissEarly);

        cardSlot = new GameObject("CardSlot", typeof(RectTransform)).GetComponent<RectTransform>();
        cardSlot.SetParent(overlayGo.transform, false);
        cardSlot.anchorMin = cardSlot.anchorMax = new Vector2(0.5f, 0.55f);
        cardSlot.pivot = new Vector2(0.5f, 0.5f);
        cardSlot.anchoredPosition = Vector2.zero;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(overlayGo.transform, false);
        var titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -70f);
        titleRect.sizeDelta = new Vector2(1000f, 130f);

        titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
        titleLabel.fontSize = 72f;
        titleLabel.fontStyle = FontStyles.Bold;
        titleLabel.alignment = TextAlignmentOptions.Center;
        titleLabel.raycastTarget = false;
        titleLabel.enableVertexGradient = true;
        titleLabel.fontMaterial.EnableKeyword("UNDERLAY_ON");
        titleLabel.fontMaterial.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.75f));
        titleLabel.fontMaterial.SetFloat("_UnderlayDilate", 0.4f);
        titleLabel.fontMaterial.SetFloat("_UnderlaySoftness", 0.25f);

        var skillGo = new GameObject("SkillReadout", typeof(RectTransform));
        skillGo.transform.SetParent(overlayGo.transform, false);
        var skillRect = skillGo.GetComponent<RectTransform>();
        skillRect.anchorMin = new Vector2(0.5f, 0f);
        skillRect.anchorMax = new Vector2(0.5f, 0f);
        skillRect.pivot = new Vector2(0.5f, 0f);
        skillRect.anchoredPosition = new Vector2(0f, 90f);
        skillRect.sizeDelta = new Vector2(700f, 140f);

        skillLabel = skillGo.AddComponent<TextMeshProUGUI>();
        skillLabel.fontSize = 64f;
        skillLabel.fontStyle = FontStyles.Bold;
        skillLabel.alignment = TextAlignmentOptions.Center;
        skillLabel.raycastTarget = false;
        skillLabel.enableVertexGradient = true;
        skillLabel.fontMaterial.EnableKeyword("UNDERLAY_ON");
        skillLabel.fontMaterial.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.8f));
        skillLabel.fontMaterial.SetFloat("_UnderlayDilate", 0.4f);
        skillLabel.fontMaterial.SetFloat("_UnderlaySoftness", 0.3f);
    }

    private void DismissEarly()
    {
        if (activeCoroutine != null) StopCoroutine(activeCoroutine);
        activeCoroutine = StartCoroutine(FadeOut());
    }

    private void ShowInternal(Character character, CharacterSkillEnum skill, int previousLevel, int newLevel)
    {
        if (activeCoroutine != null) StopCoroutine(activeCoroutine);
        activeCoroutine = StartCoroutine(PlaySequence(character, skill, previousLevel, newLevel));
    }

    private IEnumerator PlaySequence(Character character, CharacterSkillEnum skill, int previousLevel, int newLevel)
    {
        ClearCard();

        bool leveledUp = newLevel > previousLevel;
        Color accent = leveledUp ? UpColor : DownColor;

        titleLabel.text = leveledUp
            ? $"{character.characterName} grows in skill!"
            : $"{character.characterName} falters...";
        titleLabel.colorGradient = new VertexGradient(accent, accent, accent * 0.75f, accent * 0.75f);
        titleLabel.color = Color.white;

        Transform overlayTransform = cardSlot.parent;
        overlayTransform.gameObject.SetActive(true);
        overlayGroup.alpha = 0f;

        CardData cardData = character.GetCharacterCardData();
        GameObject template = DeckManager.Instance?.GetCardPrefabTemplate();
        RectTransform cardRect = null;

        if (cardData != null && template != null)
        {
            cardInstance = Instantiate(template, cardSlot);
            cardInstance.SetActive(true);

            var cardComp = cardInstance.GetComponent<Card>();
            if (cardComp != null)
            {
                cardComp.Initialize(cardData, startAsToken: false);
                cardComp.SuppressHoverEffects = true;
                cardComp.ApplyCenterPreviewPresentation();
            }

            var cg = cardInstance.GetComponent<CanvasGroup>();
            if (cg != null) { cg.blocksRaycasts = false; cg.interactable = false; }

            cardRect = cardInstance.GetComponent<RectTransform>();
        }

        if (cardRect != null)
        {
            Canvas.ForceUpdateCanvases();
            Vector2 footprint = RectTransformUtility.CalculateRelativeRectTransformBounds(cardRect).size;
            float maxDim = Mathf.Max(footprint.x, footprint.y, 1f);
            float scale = Mathf.Clamp(Mathf.Min(Screen.width * 0.4f, Screen.height * 0.55f) / maxDim, 0.4f, 1.4f);
            cardRect.localScale = Vector3.zero;
            StartCoroutine(PopIn(cardRect, scale, 0.05f));
        }

        skillLabel.text = $"<sprite name=\"{SkillSpriteNames[(int)skill]}\"> {SkillDisplayNames[(int)skill]}  {previousLevel}";
        skillLabel.colorGradient = new VertexGradient(accent, accent, accent * 0.75f, accent * 0.75f);
        skillLabel.color = Color.white;
        var skillRectT = skillLabel.rectTransform;
        skillRectT.localScale = Vector3.zero;
        StartCoroutine(PopIn(skillRectT, 1f, 0.15f));

        if (leveledUp) SpawnConfetti();

        // Fade the whole overlay in.
        float t = 0f;
        while (t < fadeInDuration)
        {
            overlayGroup.alpha = t / fadeInDuration;
            t += Time.deltaTime;
            yield return null;
        }
        overlayGroup.alpha = 1f;

        // Let the pop-ins settle, then count the skill number from its old to new value.
        yield return new WaitForSeconds(0.25f);
        yield return CountSkill(skill, previousLevel, newLevel);

        yield return new WaitForSeconds(holdDuration);

        yield return FadeOut();
    }

    private IEnumerator CountSkill(CharacterSkillEnum skill, int previousLevel, int newLevel)
    {
        string sprite = SkillSpriteNames[(int)skill];
        string name = SkillDisplayNames[(int)skill];
        var rect = skillLabel.rectTransform;

        float t = 0f;
        int lastShown = previousLevel;
        while (t < counterDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / counterDuration);
            int shown = Mathf.RoundToInt(Mathf.Lerp(previousLevel, newLevel, p));
            if (shown != lastShown)
            {
                lastShown = shown;
                rect.localScale = Vector3.one * 1.25f; // punch on every tick
            }
            skillLabel.text = $"<sprite name=\"{sprite}\"> {name}  {shown}";
            rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one, 12f * Time.deltaTime);
            yield return null;
        }

        skillLabel.text = $"<sprite name=\"{sprite}\"> {name}  {newLevel}";
        rect.localScale = Vector3.one;
    }

    // Scale-up with an elastic overshoot (easeOutBack), after an optional delay.
    private IEnumerator PopIn(RectTransform rt, float targetScale, float delay)
    {
        if (rt == null) yield break;
        rt.localScale = Vector3.zero;

        float t = -delay;
        const float dur = 0.42f;
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;

        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.unscaledDeltaTime;
            if (t < 0f) { yield return null; continue; }

            float p = Mathf.Clamp01(t / dur);
            float eased = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
            rt.localScale = Vector3.one * (targetScale * eased);
            yield return null;
        }

        if (rt != null) rt.localScale = Vector3.one * targetScale;
    }

    private void SpawnConfetti()
    {
        Transform overlay = cardSlot.parent;
        if (overlay == null) return;

        var go = new GameObject("Confetti", typeof(RectTransform));
        go.transform.SetParent(overlay, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.SetAsLastSibling();

        go.AddComponent<SituationConfettiBurst>().Emit(confettiCount, Screen.width, confettiDuration);
    }

    private IEnumerator FadeOut()
    {
        float start = overlayGroup != null ? overlayGroup.alpha : 1f;
        float t = 0f;
        while (t < fadeOutDuration)
        {
            if (overlayGroup != null) overlayGroup.alpha = Mathf.Lerp(start, 0f, t / fadeOutDuration);
            t += Time.deltaTime;
            yield return null;
        }
        if (overlayGroup != null) overlayGroup.alpha = 0f;

        Transform overlayTransform = cardSlot?.parent;
        if (overlayTransform != null) overlayTransform.gameObject.SetActive(false);

        ClearCard();
        activeCoroutine = null;
    }

    private void ClearCard()
    {
        if (cardInstance != null) Destroy(cardInstance);
        cardInstance = null;
    }
}
