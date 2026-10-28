using System.Collections;
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
        StartTabletop();
    }

    private void WireAuthoredControls()
    {
        if (wired) return;
        if (root == null) root = gameObject;
        BuildCardMenu();
        wired = true;
    }

    // Card-menu title (see TitleCardMenu): the authored menu bar is hidden and replaced by a hand
    // of cards. "New Campaign" deals one card per campaign into the same hand — no second panel.
    private TitleCardMenu cardMenu;

    private void BuildCardMenu()
    {
        Transform menuBar = root.transform.Find("Menu Bar");
        if (menuBar != null) menuBar.gameObject.SetActive(false);
        cardMenu = TitleCardMenu.Build((RectTransform)root.transform);
        ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        cardMenu.Clear();
        SaveGame summary = SaveSystem.PeekSave();
        if (summary != null)
            cardMenu.AddCard(TitleCardMenu.MenuArt("Continue"), "Saved game", "Continue", $"Turn {summary.turn}, as {summary.playerLeader}", () =>
            {
                if (!SaveSystem.BeginLoad(out string error)) Debug.LogWarning($"Continue failed: {error}");
            });
        cardMenu.AddCard(TitleCardMenu.MenuArt("NewCampaign"), "Campaign", "New Campaign", "Choose a campaign and a leader", StartCampaign);
        cardMenu.AddCard(TitleCardMenu.MenuArt("Quit"), "Farewell", "Quit", "Leave Runeboard", () => Quit());
    }

    private void ShowCampaigns()
    {
        cardMenu.Clear(back: ShowMainMenu);
        foreach (string scenario in ScenarioLoader.GetAvailableScenarios())
        {
            string captured = scenario;
            ScenarioLoader.ScenarioDisplayInfo info = ScenarioLoader.GetScenarioDisplayInfo(captured);
            Sprite art = CampaignSelectionManager.FindRepresentativeArt(info.representativeCardName);
            Image img = cardMenu.AddCard(art ?? TitleCardMenu.MenuArt("NewCampaign"), "Campaign", info.title,
                string.IsNullOrWhiteSpace(info.description) ? "A scripted campaign" : info.description,
                () => BeginCampaign(captured));
            if (art == null && !string.IsNullOrWhiteSpace(info.representativeCardName)) cardMenu.FillArtWhenLoaded(img, info.representativeCardName);
        }
        cardMenu.AddCard(TitleCardMenu.MenuArt("Continue"), "Skirmish", "Random Middle-earth", "A freshly drawn board, leaders and fortunes",
            () => BeginCampaign(null));
        cardMenu.AddBackCard("Back", ShowMainMenu);
    }

    // Choosing a campaign kicks off scenario parsing, game-UI activation and board generation,
    // which used to freeze the title on its last frame. Put the loading screen up first and
    // only make the choice once it has rendered (it fades when the intro or leader screen shows).
    private void BeginCampaign(string scenario)
    {
        if (SessionLoadingScreen.IsActive || GameConfig.ScenarioChosen) return;
        SessionLoadingScreen.ShowThen(SessionLoadingScreen.Kind.NewCampaign, () =>
        {
            if (campaignSelection != null) campaignSelection.ChooseScenario(scenario);
            else SessionLoadingScreen.Hide("campaign selection is missing");
        });
    }

    // ── Tabletop (TitleDiorama) ─────────────────────────────────────────────────────────────

    private TitleDiorama diorama;
    private Coroutine tabletop;

    // The painted backdrop shows until the real board slice is up behind it, then fades away;
    // if the slice can't be built, the backdrop simply stays.
    private void StartTabletop()
    {
        if (tabletop != null || diorama != null) return;
        diorama = TitleDiorama.Create();
        if (diorama != null) tabletop = StartCoroutine(RevealTabletop());
    }

    private IEnumerator RevealTabletop()
    {
        while (diorama != null && !diorama.Ready) yield return null;
        tabletop = null;
        if (diorama == null) yield break;

        cardMenu.ShowTable(true);
        // Everything authored on the start screen (backdrop, veil, footer) gives way to the table.
        var backdrop = new System.Collections.Generic.List<CanvasGroup>();
        foreach (Transform child in root.transform)
        {
            if (child == cardMenu.transform || !child.gameObject.activeSelf) continue;
            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
            backdrop.Add(group);
        }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.4f)
        {
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, t);
            foreach (CanvasGroup group in backdrop) if (group != null) group.alpha = alpha;
            yield return null;
        }
        foreach (CanvasGroup group in backdrop) if (group != null) group.gameObject.SetActive(false);
    }

    private void StopTabletop()
    {
        if (tabletop != null) StopCoroutine(tabletop);
        tabletop = null;
        if (diorama != null) Destroy(diorama.gameObject);
        diorama = null;
    }

    public void StartCampaign()
    {
        if (campaignSelection == null || cardMenu == null) return;
        ShowCampaigns();
    }

    // Called once a scenario has been chosen; campaignSelection has already hidden itself by then.
    // The tabletop goes before the real board is generated.
    public void Hide()
    {
        StopTabletop();
        root.SetActive(false);
    }

    private void OnDestroy() => StopTabletop();

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
