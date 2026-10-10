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
    private bool wired;

    public void Show(CampaignSelectionManager selector)
    {
        campaignSelection = selector;
        campaignSelection.gameObject.SetActive(false);
        WireAuthoredControls();
        root.SetActive(true);
        ApplySkin(FindFirstObjectByType<SkinManager>()?.CurrentSkin ?? Skins.Default);
    }

    private void WireAuthoredControls()
    {
        if (wired) return;
        if (root == null) root = gameObject;
        AddContinueButton();
        wired = true;
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
        Sounds.Instance?.PlayUiClick();
        campaignSelection.gameObject.SetActive(!campaignSelection.gameObject.activeSelf);
    }

    // Called once a scenario has been chosen; campaignSelection has already hidden itself by then.
    public void Hide()
    {
        root.SetActive(false);
    }

    public void ToggleSkin()
    {
        SkinManager manager = FindFirstObjectByType<SkinManager>();
        if (manager == null) return;
        ApplySkin(manager.GetNextSkin());
        Sounds.Instance?.PlayUiClick();
    }

    private void ApplySkin(Skins skin)
    {
        FindFirstObjectByType<SkinManager>()?.ChangeSkin(skin);
        if (skinValue != null) skinValue.text = $"Skin: {skin}";
    }

    public static void Quit()
    {
        Sounds.Instance?.PlayUiExit();
        Application.Quit();
    }

}
