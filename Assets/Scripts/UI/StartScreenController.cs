using RetroLOTR.Scenarios;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>First screen on a fresh scene. "Select campaign" toggles the campaign selection
/// screen on top of it without ever closing this one — it only closes once a scenario is
/// actually chosen (Board calls Hide() at that point).</summary>
public sealed class StartScreenController : MonoBehaviour
{
    [Header("Authored Start Screen")]
    [SerializeField] private GameObject root;
    [SerializeField] public Button startButton;
    [SerializeField] public Button quitButton;
    [SerializeField] public Button skinButton;
    [SerializeField] private TextMeshProUGUI skinValue;

    private CampaignSelectionManager campaignSelection;
    public bool IsShowing => root != null && root.activeInHierarchy;
    private bool wired;

    public void Show(CampaignSelectionManager selector)
    {
        campaignSelection = selector;
        campaignSelection.gameObject.SetActive(false);
        WireAuthoredControls();
        root.SetActive(true);
        ApplySkin(FindAnyObjectByType<SkinManager>()?.CurrentSkin ?? Skins.Default);
    }

    private void WireAuthoredControls()
    {
        if (wired) return;
        if (root == null) root = gameObject;
        BuildCardMenu();
        wired = true;
    }

    // Card-menu title (see TitleCardMenu): the authored left column is hidden and replaced.
    // "New Campaign" swaps the same column to one plaque per campaign — no second panel.
    private TitleCardMenu cardMenu;
    private Coroutine artLoader;

    private void BuildCardMenu()
    {
        Transform menuBar = root.transform.Find("Menu Bar");
        if (menuBar != null) menuBar.gameObject.SetActive(false);
        cardMenu = TitleCardMenu.Build((RectTransform)root.transform);
        ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        if (artLoader != null) { StopCoroutine(artLoader); artLoader = null; }
        cardMenu.Clear();
        SaveGame summary = SaveSystem.PeekSave();
        if (summary != null)
            cardMenu.AddPlaque(TitleCardMenu.MenuArt("Continue"), "Continue", $"Turn {summary.turn}, as {summary.playerLeader}", () =>
            {
                if (!SaveSystem.BeginLoad(out string error)) Debug.LogWarning($"Continue failed: {error}");
            });
        cardMenu.AddPlaque(TitleCardMenu.MenuArt("NewCampaign"), "New Campaign", "Choose a campaign and a leader", StartCampaign);
        cardMenu.AddPlaque(TitleCardMenu.MenuArt("Quit"), "Quit", "Leave Runeboard", () => Quit());
    }

    private void ShowCampaigns()
    {
        cardMenu.Clear(back: ShowMainMenu);
        var pending = new System.Collections.Generic.List<(UnityEngine.UI.Image img, string card)>();
        foreach (string scenario in ScenarioLoader.GetAvailableScenarios())
        {
            string captured = scenario;
            ScenarioLoader.ScenarioDisplayInfo info = ScenarioLoader.GetScenarioDisplayInfo(captured);
            Sprite art = CampaignSelectionManager.FindRepresentativeArt(info.representativeCardName);
            UnityEngine.UI.Image img = cardMenu.AddPlaque(art ?? TitleCardMenu.MenuArt("NewCampaign"), info.title,
                string.IsNullOrWhiteSpace(info.description) ? "A scripted campaign" : info.description,
                () => campaignSelection.ChooseScenario(captured));
            if (art == null && !string.IsNullOrWhiteSpace(info.representativeCardName)) pending.Add((img, info.representativeCardName));
        }
        cardMenu.AddPlaque(TitleCardMenu.MenuArt("Continue"), "Random Middle-earth", "A freshly drawn board, leaders and fortunes",
            () => campaignSelection.ChooseScenario(null));
        cardMenu.AddBackLink("‹  Back", ShowMainMenu);
        if (pending.Count > 0) artLoader = StartCoroutine(FillArtWhenLoaded(pending));
    }

    // Card art arrives through Addressables; on the first screen it is often not loaded yet.
    private System.Collections.IEnumerator FillArtWhenLoaded(System.Collections.Generic.List<(UnityEngine.UI.Image img, string card)> pending)
    {
        float deadline = Time.unscaledTime + 15f;
        while (pending.Count > 0 && Time.unscaledTime < deadline)
        {
            yield return null;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].img == null) { pending.RemoveAt(i); continue; }
                Sprite art = CampaignSelectionManager.FindRepresentativeArt(pending[i].card);
                if (art == null) continue;
                TitleCardMenu.SetArt(pending[i].img, art);
                pending.RemoveAt(i);
            }
        }
        artLoader = null;
    }

    // "Continue campaign" appears above "Begin your journey" when a save exists. Built by
    // cloning the authored start button so it inherits the menu's look and layout slot.
    private void AddContinueButton()
    {
        SaveGame summary = SaveSystem.PeekSave();
        if (summary == null || startButton == null) return;
        GameObject clone = Instantiate(startButton.gameObject, startButton.transform.parent);
        clone.name = "ContinueButton";
        clone.transform.SetSiblingIndex(startButton.transform.GetSiblingIndex());
        Button button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() =>
        {
            Sounds.Instance?.PlayUiClick();
            if (!SaveSystem.BeginLoad(out string error)) Debug.LogWarning($"Continue failed: {error}");
        });
        TMP_Text[] labels = clone.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0) labels[0].text = "Continue campaign";
        if (labels.Length > 1) labels[1].text = $"Turn {summary.turn}, as {summary.playerLeader}";
    }

    public void StartCampaign()
    {
        if (campaignSelection == null || cardMenu == null) return;
        ShowCampaigns();
    }

    // Called once a scenario has been chosen; campaignSelection has already hidden itself by then.
    public void Hide()
    {
        root.SetActive(false);
    }

    public void ToggleSkin()
    {
        SkinManager manager = FindAnyObjectByType<SkinManager>();
        if (manager == null) return;
        ApplySkin(manager.GetNextSkin());
        Sounds.Instance?.PlayUiClick();
    }

    private void ApplySkin(Skins skin)
    {
        FindAnyObjectByType<SkinManager>()?.ChangeSkin(skin);
        if (skinValue != null) skinValue.text = $"Skin: {skin}";
    }

    public static void Quit()
    {
        Sounds.Instance?.PlayUiExit();
        Application.Quit();
    }

}
