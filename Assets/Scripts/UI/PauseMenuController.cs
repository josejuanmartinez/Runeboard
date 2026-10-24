using RetroLOTR.Scenarios;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Controls the authored ESC pause-menu prefab placed in the game scene.</summary>
public sealed class PauseMenuController : MonoBehaviour
{
    private static PauseMenuController instance;

    [Header("Authored Pause Menu")]
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private RectTransform config;

    [Header("Controls")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button configButton;
    [SerializeField] private Button autoplayButton;
    [SerializeField] private Button returnToStartButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button changeSkinButton;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider soundSlider;
    [SerializeField] private Slider ambienceSlider;

    private bool wired;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("More than one PauseMenuController exists in the scene; using the first one.", this);
            return;
        }

        instance = this;
        WireAuthoredControls();
        SetVisible(false, false);
    }

    public static void Toggle()
    {
        if (instance == null)
        {
            instance = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
            if (instance == null)
            {
                Debug.LogError("Pause menu is missing. Add Assets/GameObjects/PauseMenu.prefab to the scene.");
                return;
            }

            // Recover an authored scene instance that was disabled in the hierarchy.
            if (!instance.gameObject.activeSelf) instance.gameObject.SetActive(true);
            instance.WireAuthoredControls();
        }

        instance.SetVisible(!instance.root.activeSelf);
    }

    /// <summary>Ensures the authored controller survives the pre-game to board transition.</summary>
    public static void PrepareForGameplay()
    {
        PauseMenuController menu = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        if (menu == null)
        {
            Debug.LogError("Pause menu is missing. Add Assets/GameObjects/PauseMenu.prefab to the scene.");
            return;
        }

        instance = menu;
        if (!menu.gameObject.activeSelf) menu.gameObject.SetActive(true);
        menu.WireAuthoredControls();
        menu.SetVisible(false, false);
    }

    private void WireAuthoredControls()
    {
        if (wired) return;

        saveButton?.onClick.AddListener(SaveCampaign);
        loadButton?.onClick.AddListener(LoadCampaign);
        SetButtonLabel(saveButton, "Save campaign");
        SetButtonLabel(loadButton, "Load campaign");
        configButton?.onClick.AddListener(ToggleConfig);
        autoplayButton?.onClick.AddListener(AutoplayTurn);
        returnToStartButton?.onClick.AddListener(ReturnToStart);
        quitButton?.onClick.AddListener(Quit);
        changeSkinButton?.onClick.AddListener(ChangeSkin);
        musicSlider?.onValueChanged.AddListener(SetMusicVolume);
        soundSlider?.onValueChanged.AddListener(SetSoundVolume);
        ambienceSlider?.onValueChanged.AddListener(SetAmbienceVolume);

        RefreshSaveLoadButtons();
        wired = true;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;
        TMPro.TMP_Text text = button.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (text != null) text.text = label;
    }

    private void RefreshSaveLoadButtons()
    {
        if (saveButton != null) saveButton.interactable = SaveSystem.CanSaveNow(out _);
        if (loadButton != null) loadButton.interactable = SaveSystem.HasSave;
    }

    private void SaveCampaign()
    {
        if (SaveSystem.Save(out string error))
        {
            Sounds.Instance?.PlayUiClick();
            SetButtonLabel(saveButton, $"Saved (turn {Game.Instance?.turn})");
            RefreshSaveLoadButtons();
        }
        else
        {
            Sounds.Instance?.PlayUiDenied();
            MessageDisplay.ShowMessage(error ?? "Could not save", Color.red);
        }
    }

    private async void LoadCampaign()
    {
        bool confirm = Game.Instance == null || !Game.Instance.started
            || await ConfirmationDialog.AskImmediate("Load the saved campaign? Unsaved progress will be lost.", "Load", "Cancel");
        if (!confirm) return;
        if (!SaveSystem.BeginLoad(out string error))
        {
            Sounds.Instance?.PlayUiDenied();
            MessageDisplay.ShowMessage(error ?? "Could not load", Color.red);
        }
    }

    private void SetVisible(bool visible, bool playSound = true)
    {
        if (root == null || rootGroup == null)
        {
            Debug.LogError("PauseMenu prefab references are incomplete.", this);
            return;
        }

        root.SetActive(visible);
        rootGroup.alpha = visible ? 1f : 0f;
        rootGroup.interactable = visible;
        rootGroup.blocksRaycasts = visible;
        if (visible)
        {
            RefreshVolumeControls();
            SetButtonLabel(saveButton, "Save campaign");
            RefreshSaveLoadButtons();
            SkinManager skin = FindAnyObjectByType<SkinManager>();
            if (playSound) Sounds.Instance?.PlayUiClick();
        }
        else
        {
            if (config != null) config.gameObject.SetActive(false);
            if (playSound) Sounds.Instance?.PlayUiExit();
        }
    }

    private void RefreshVolumeControls()
    {
        if (musicSlider != null && Music.Instance != null)
            musicSlider.SetValueWithoutNotify(Music.Instance.musicVolume);
        if (soundSlider != null && Sounds.Instance?.soundAudioSource != null)
            soundSlider.SetValueWithoutNotify(Sounds.Instance.soundAudioSource.volume);
        if (ambienceSlider != null && Music.Instance != null)
            ambienceSlider.SetValueWithoutNotify(Music.Instance.ambientVolume);
    }

    private void SetMusicVolume(float value)
    {
        if (Music.Instance == null) return;
        Music.Instance.musicVolume = value;
        if (Music.Instance.musicAudioSource != null) Music.Instance.musicAudioSource.volume = value;
    }

    private static void SetSoundVolume(float value)
    {
        if (Sounds.Instance?.soundAudioSource != null) Sounds.Instance.soundAudioSource.volume = value;
    }

    private void SetAmbienceVolume(float value)
    {
        if (Music.Instance == null) return;
        Music.Instance.ambientVolume = value;
        if (Music.Instance.ambientAudioSource != null) Music.Instance.ambientAudioSource.volume = value;
    }

    private void ToggleConfig()
    {
        if (config == null) return;
        config.gameObject.SetActive(!config.gameObject.activeSelf);
        Sounds.Instance?.PlayUiClick();
    }

    private void ChangeSkin()
    {
        SkinManager manager = FindAnyObjectByType<SkinManager>();
        if (manager == null) return;
        manager.ChangeSkin(manager.GetNextSkin());
        Sounds.Instance?.PlayUiClick();
    }

    private void AutoplayTurn()
    {
        SetVisible(false);
        Game.Instance?.AutoplayOneTurn();
    }

    // The reload rebuilds the whole scene (decks, illustrations, title); show the loading screen
    // instead of a frozen pause menu, and reset the campaign choice only once the new scene exists.
    private static void ReturnToStart()
    {
        if (SessionLoadingScreen.IsActive) return;
        SessionLoadingScreen.ReloadScene(SessionLoadingScreen.Kind.ReturnToTitle, SceneManager.GetActiveScene().buildIndex, () =>
        {
            GameConfig.ScenarioChosen = false;
            GameConfig.ScenarioToLoad = null;
            GameConfig.SkipIntro = false;
        });
    }

    private static void Quit()
    {
        Sounds.Instance?.PlayUiExit();
        Application.Quit();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
