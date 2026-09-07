using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace RetroLOTR.Scenarios
{
    /// <summary>
    /// Campaign/scenario selection — the first screen of a fresh game. Lives on a prefab
    /// (Assets/Resources/CampaignSelectionScreen.prefab) so every visual — background, panel,
    /// buttons, texts — is authored in Unity, never generated at runtime. Board instantiates the
    /// prefab and waits on <see cref="GameConfig.ScenarioChosen"/>.
    ///
    /// The scenario list is the only data-driven part: <see cref="scenarioButtonTemplate"/> is
    /// cloned once per authored scenario (ScenarioLoader.GetAvailableScenarios), so styling the
    /// template styles every entry. The clone's child TMP named "Title" receives the scenario
    /// name; everything else on it stays exactly as authored.
    /// </summary>
    public class CampaignSelectionManager : MonoBehaviour
    {
        [Header("Wiring (style everything else freely in the prefab)")]
        [Tooltip("Starts the default random campaign (ScenarioToLoad = null). Its texts/visuals are whatever the prefab says.")]
        [SerializeField] private Button defaultCampaignButton;
        [Tooltip("Parent the per-scenario buttons are instantiated under (usually the scroll view's Content).")]
        [SerializeField] private RectTransform scenarioButtonContainer;
        [Tooltip("Button inside the container cloned once per authored scenario. Kept inactive; its child TMP named 'Title' receives the scenario name.")]
        [SerializeField] private Button scenarioButtonTemplate;
        [Tooltip("Scene object enabled after a campaign or scenario has been selected and GameConfig is ready.")]
        [SerializeField] private GameObject enableAfterSelection;
        [Tooltip("TokenCard prefab used to build each scenario button's representative card token. Same prefab as DeckManager's tokenCardTemplate.")]
        [SerializeField] private GameObject cardTemplate;

        private void Start()
        {
            RemoveLegacySkinControls();

            if (defaultCampaignButton != null)
            {
                defaultCampaignButton.onClick.AddListener(() => Choose(null));
                defaultCampaignButton.gameObject.AddComponent<ClickableCursorOnHover>();
            }
            else
                Debug.LogError("CampaignSelectionManager: defaultCampaignButton is not wired in the prefab.");

            PopulateScenarioButtons();
        }

        // Older InGame scene instances added these controls directly to the prefab root.
        // Skin selection now belongs to the title screen and pause/config screen only.
        private void RemoveLegacySkinControls()
        {
            foreach (string legacyName in new[] { "Skin", "Dropdown", "SkinDropdown" })
            {
                Transform legacyControl = transform.Find(legacyName);
                if (legacyControl == null) continue;
                legacyControl.gameObject.SetActive(false);
                Destroy(legacyControl.gameObject);
            }
        }

        private void PopulateScenarioButtons()
        {
            if (scenarioButtonContainer == null || scenarioButtonTemplate == null)
            {
                Debug.LogError("CampaignSelectionManager: scenario list is not wired (container/template) in the prefab.");
                return;
            }

            scenarioButtonTemplate.gameObject.SetActive(false);

            List<string> scenarios = ScenarioLoader.GetAvailableScenarios();
            foreach (string scenario in scenarios)
            {
                string captured = scenario;
                Button button = Instantiate(scenarioButtonTemplate, scenarioButtonContainer);
                button.gameObject.name = $"Scenario_{captured}";
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => Choose(captured));
                button.gameObject.AddComponent<ClickableCursorOnHover>();

                ScenarioLoader.ScenarioDisplayInfo info = ScenarioLoader.GetScenarioDisplayInfo(captured);

                TMP_Text title = FindLabel(button.transform, "Title");
                if (title != null) title.text = info.title;

                // The author-written blurb (Scenario Creator's Description field) fills the
                // Subtitle; scenarios without one keep whatever the template says.
                if (!string.IsNullOrWhiteSpace(info.description))
                {
                    TMP_Text subtitle = FindLabel(button.transform, "Subtitle");
                    if (subtitle != null) subtitle.text = info.description;
                }

                StartCoroutine(AttachRepresentativeTokenWhenReady(button, info.representativeCardName));
            }
        }

        // Card art loads asynchronously through Addressables (Illustrations); on this very
        // first screen it is usually not ready yet, and building the token too early gives
        // sprite-less white squares. Wait (bounded) for the art before attaching.
        private System.Collections.IEnumerator AttachRepresentativeTokenWhenReady(Button button, string cardName)
        {
            if (string.IsNullOrWhiteSpace(cardName)) yield break;

            Illustrations illustrations = FindFirstObjectByType<Illustrations>();
            float deadline = Time.unscaledTime + 15f;
            while (illustrations != null && !illustrations.IsLoaded && Time.unscaledTime < deadline)
                yield return null;

            if (button == null) yield break; // screen dismissed while waiting
            AttachRepresentativeToken(button, cardName);
        }

        // Shows the scenario's representative card (Scenario Creator's Card field) in its
        // token form on the button. The token parents under the template's authored child
        // named "Token" — position/size it in the prefab to control placement.
        private void AttachRepresentativeToken(Button button, string cardName)
        {
            if (string.IsNullOrWhiteSpace(cardName)) return;

            // The campaign selection screen runs before the game HUD is necessarily active —
            // DeckManager may not have Awoken yet (Instance unset), and a default
            // FindFirstObjectByType skips inactive objects, so search inactive ones too.
            DeckManager deckManager = DeckManager.Instance != null
                ? DeckManager.Instance
                : FindFirstObjectByType<DeckManager>(FindObjectsInactive.Include);
            if (deckManager != null && (deckManager.cards == null || deckManager.cards.Count == 0))
            {
                deckManager.InitializeFromResources();
            }
            CardData cardData = deckManager != null ? deckManager.FindAnyCardByName(cardName) : null;
            if (cardData == null)
            {
                Debug.LogWarning($"CampaignSelectionManager: representative card '{cardName}' not found in any deck.");
                return;
            }

            // The polished chooser has an authored art window. Fill it directly rather than
            // nesting a token's second mask, which cropped the representative art twice.
            Image artwork = button.transform.Find("Polish Artwork")?.GetComponent<Image>();
            if (artwork != null)
            {
                Illustrations illustrations = FindFirstObjectByType<Illustrations>();
                Sprite sprite = null;
                if (illustrations != null)
                {
                    illustrations.TryGetCardArtByName(cardData.spriteName, out sprite);
                    if (sprite == null) illustrations.TryGetCardArtByName(cardData.name, out sprite);
                    if (sprite == null) illustrations.TryGetIllustrationByName(cardName, out sprite);
                }
                artwork.sprite = sprite;
                artwork.enabled = sprite != null;
                return;
            }

            if (cardTemplate == null) return;

            RectTransform host = FindTokenHost(button);
            if (host == null)
            {
                Debug.LogWarning("CampaignSelectionManager: scenario button template has no child named 'Token'; representative card not shown. Add one to the template in CampaignSelectionScreen.prefab.");
                return;
            }

            // Spin up a throwaway card just to borrow its token visual. Token-only init:
            // the full Initialize touches live-game state (board, selected character) that
            // does not exist yet on this screen. Kept inactive so it never renders
            // (Destroy is deferred to end of frame).
            GameObject tempCard = Instantiate(cardTemplate, host);
            tempCard.SetActive(false);
            Card cardComponent = tempCard.GetComponent<Card>();
            GameObject token = null;
            Vector2 tokenSize = Vector2.one;
            if (cardComponent != null)
            {
                cardComponent.InitializeTokenVisualOnly(cardData);
                token = cardComponent.CreateTokenVisualClone(host, out tokenSize);
            }
            Destroy(tempCard);
            if (token == null) return;

            // The clone's root is a plain Transform (see Card.CreateTokenVisualClone), so
            // fit it into the host by scaling the transform, not by rect math. A
            // stretch-anchored authored "Token" child may not have resolved its rect yet
            // on the frame the screen spawns; fall back to natural token size then.
            token.transform.localPosition = Vector3.zero;
            float hostWidth = host.rect.width > 1f ? host.rect.width : tokenSize.x;
            float hostHeight = host.rect.height > 1f ? host.rect.height : tokenSize.y;
            float fit = Mathf.Min(hostWidth / Mathf.Max(tokenSize.x, 1f),
                                  hostHeight / Mathf.Max(tokenSize.y, 1f));
            token.transform.localScale = Vector3.one * Mathf.Min(fit, 1.5f);
        }

        // The template's authored "Token" placeholder (see CampaignSelectionScreen.prefab);
        // null when the author removed it.
        private static RectTransform FindTokenHost(Button button)
        {
            foreach (RectTransform rect in button.GetComponentsInChildren<RectTransform>(true))
                if (string.Equals(rect.name, "Token", System.StringComparison.OrdinalIgnoreCase)) return rect;
            return null;
        }

        // Content goes into the clone's TMP children by name ("Title" gets the scenario name,
        // "Subtitle" the description). For the title, fall back to the first TMP found if the
        // author renamed it, so the button is never nameless.
        private static TMP_Text FindLabel(Transform root, string labelName)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (string.Equals(text.name, labelName, System.StringComparison.OrdinalIgnoreCase)) return text;
            return string.Equals(labelName, "Title", System.StringComparison.OrdinalIgnoreCase)
                ? root.GetComponentInChildren<TMP_Text>(true)
                : null;
        }

        private void Choose(string scenarioName)
        {
            GameConfig.ScenarioToLoad = scenarioName; // null = default random campaign
            GameConfig.ScenarioChosen = true;
            if (enableAfterSelection != null)
                enableAfterSelection.SetActive(true);
            gameObject.SetActive(false);
        }

        public void CloseSelection()
        {
            Sounds.Instance?.PlayUiExit();
            gameObject.SetActive(false);
        }
    }
}
