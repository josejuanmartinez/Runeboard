using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas-friendly counterpart to CharacterAnimationController. It resolves and plays the same
/// baked character spritesheets, but writes frames to a UI Image instead of a SpriteRenderer.
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class UIAnimatedCharacter : MonoBehaviour
{
    private const string StandingIdleStateName = "standing idle";

    [SerializeField] private Image characterImage;
    [Header("Character")]
    [Tooltip("Spritesheet character/folder name used when this UI element is not bound to a gameplay Character.")]
    [SerializeField] private string characterName;
    [SerializeField] private RacesEnum race;
    [SerializeField] private bool showOnEnable = true;
    [SerializeField] private string fallback;

    [Header("Playback")]
    [SerializeField] private CharacterAnimationController.AnimationKind currentAnimation =
        CharacterAnimationController.AnimationKind.StandingIdle;
    [SerializeField] private CharacterAnimationController.Orientation orientation =
        CharacterAnimationController.Orientation.Forward;
    [SerializeField] private bool loop;
    [SerializeField, Min(0.01f)] private float turnAnimationSpeedMultiplier = 3f;

    private Character character;
    private Character pendingCharacter;
    private string pendingCharacterName;
    private RacesEnum pendingRace;
    private string resolvedRaceOrName;
    private string currentStateName;
    private int frameIndex;
    private float frameTimer;
    private bool isShowing;

    public Character Character => character;
    public bool IsShowing => isShowing;

    private void Awake()
    {
        EnsureImage();
    }

    private void OnEnable()
    {
        if (showOnEnable && character == null && !string.IsNullOrWhiteSpace(characterName))
            Show(characterName, race);
    }

    private void OnValidate()
    {
        if (characterImage == null) characterImage = GetComponent<Image>();
    }

    private void Update()
    {
        if (!isShowing && pendingCharacter != null)
        {
            Character retry = pendingCharacter;
            pendingCharacter = null;
            Show(retry);
        }

        else if (!isShowing && !string.IsNullOrWhiteSpace(pendingCharacterName))
        {
            string retryName = pendingCharacterName;
            RacesEnum retryRace = pendingRace;
            pendingCharacterName = null;
            Show(retryName, retryRace);
        }

        if (!isShowing || string.IsNullOrEmpty(resolvedRaceOrName)) return;

        string desiredStateName = GetStateName(currentAnimation);
        CharacterSpritesheets.AtlasManifest manifest =
            CharacterSpritesheets.GetManifest(resolvedRaceOrName, orientation.ToString());
        CharacterSpritesheets.AtlasState state = FindState(manifest, desiredStateName);

        if (state == null)
        {
            if (currentAnimation != CharacterAnimationController.AnimationKind.StandingIdle)
                SetAnimation(CharacterAnimationController.AnimationKind.StandingIdle);
            return;
        }

        if (currentStateName != desiredStateName)
        {
            currentStateName = desiredStateName;
            frameIndex = 0;
            frameTimer = 0f;
        }

        bool isTurning = currentAnimation == CharacterAnimationController.AnimationKind.TurnLeft ||
                         currentAnimation == CharacterAnimationController.AnimationKind.TurnRight;
        float speed = isTurning ? Mathf.Max(turnAnimationSpeedMultiplier, 0.01f) : 1f;
        float frameDuration = 1f / (Mathf.Max(state.fps, 0.01f) * speed);
        // Menu UI can remain visible while gameplay time is paused.
        frameTimer += Time.unscaledDeltaTime;

        while (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex++;
            if (frameIndex < state.frameCount) continue;

            if (currentAnimation == CharacterAnimationController.AnimationKind.TurnLeft) AdvanceOrientation(-1);
            else if (currentAnimation == CharacterAnimationController.AnimationKind.TurnRight) AdvanceOrientation(1);

            if (state.loop || loop)
            {
                frameIndex = 0;
            }
            else
            {
                frameIndex = Mathf.Max(0, state.frameCount - 1);
                SetAnimation(CharacterAnimationController.AnimationKind.StandingIdle);
                break;
            }
        }

        Sprite sprite = CharacterSpritesheets.GetSprite($"{state.spriteNamePrefix}_{frameIndex:D2}");
        if (sprite != null) characterImage.sprite = sprite;
    }

    public bool Show(Character value)
    {
        if (value == null)
        {
            Clear();
            return false;
        }

        bool wasLoaded = CharacterSpritesheets.IsLoaded;
        if (!CharacterSpritesheets.TryResolveRaceOrName(
                value.characterName, value.SpriteVariantBaseName, value.race, fallback,
                out string resolved))
        {
            Clear();
            pendingCharacter = wasLoaded ? null : value;
            return false;
        }

        EnsureImage();
        bool changedCharacter = character != value;
        character = value;
        pendingCharacter = null;
        pendingCharacterName = null;
        characterName = value.characterName;
        race = value.race;
        resolvedRaceOrName = resolved;
        isShowing = true;
        characterImage.enabled = true;

        if (changedCharacter)
        {
            SetAnimation(CharacterAnimationController.AnimationKind.StandingIdle);
            currentStateName = null;
            frameIndex = 0;
            frameTimer = 0f;
        }

        return true;
    }

    /// <summary>
    /// Shows a named baked character without requiring a gameplay Character instance. This is
    /// the convenient path for menus such as StartScreen.
    /// </summary>
    public bool Show(string requestedCharacterName, RacesEnum requestedRace)
    {
        if (string.IsNullOrWhiteSpace(requestedCharacterName))
        {
            Clear();
            return false;
        }

        string trimmedName = requestedCharacterName.Trim();
        bool wasLoaded = CharacterSpritesheets.IsLoaded;
        if (!CharacterSpritesheets.TryResolveRaceOrName(
                trimmedName, null, requestedRace, fallback, out string resolved))
        {
            Clear();
            if (!wasLoaded)
            {
                pendingCharacterName = trimmedName;
                pendingRace = requestedRace;
            }
            return false;
        }

        EnsureImage();
        bool changedCharacter = character != null || characterName != trimmedName ||
                                resolvedRaceOrName != resolved;
        character = null;
        pendingCharacter = null;
        pendingCharacterName = null;
        characterName = trimmedName;
        race = requestedRace;
        resolvedRaceOrName = resolved;
        isShowing = true;
        characterImage.enabled = true;

        if (changedCharacter)
        {
            // SetAnimation(CharacterAnimationController.AnimationKind.StandingIdle);
            currentStateName = null;
            frameIndex = 0;
            frameTimer = 0f;
        }

        return true;
    }

    public void Clear()
    {
        EnsureImage();
        character = null;
        pendingCharacter = null;
        pendingCharacterName = null;
        resolvedRaceOrName = null;
        currentStateName = null;
        frameIndex = 0;
        frameTimer = 0f;
        isShowing = false;
        characterImage.sprite = null;
        characterImage.enabled = false;
    }

    public void SetAnimation(CharacterAnimationController.AnimationKind value)
    {
        if (currentAnimation == value) return;
        currentAnimation = value;
        currentStateName = null;
        frameIndex = 0;
        frameTimer = 0f;
    }

    public void SetOrientation(CharacterAnimationController.Orientation value)
    {
        if (orientation == value) return;
        orientation = value;
        currentStateName = null;
        frameIndex = 0;
        frameTimer = 0f;
    }

    public void SetLoop(bool value) => loop = value;

    private void EnsureImage()
    {
        if (characterImage == null) characterImage = GetComponent<Image>();
    }

    private void AdvanceOrientation(int step)
    {
        int current = (int)orientation;
        // CharacterAnimationController's enum order is Forward, Back, Left, Right, while turns
        // follow Forward -> Right -> Back -> Left.
        CharacterAnimationController.Orientation[] cycle =
        {
            CharacterAnimationController.Orientation.Forward,
            CharacterAnimationController.Orientation.Right,
            CharacterAnimationController.Orientation.Back,
            CharacterAnimationController.Orientation.Left
        };
        int index = System.Array.IndexOf(cycle, (CharacterAnimationController.Orientation)current);
        int next = ((index + step) % cycle.Length + cycle.Length) % cycle.Length;
        SetOrientation(cycle[next]);
    }

    private static CharacterSpritesheets.AtlasState FindState(
        CharacterSpritesheets.AtlasManifest manifest, string stateName)
    {
        if (manifest?.states == null) return null;
        foreach (CharacterSpritesheets.AtlasState state in manifest.states)
            if (state.name == stateName) return state;
        return null;
    }

    private static string GetStateName(CharacterAnimationController.AnimationKind kind)
    {
        switch (kind)
        {
            case CharacterAnimationController.AnimationKind.CrouchIdle: return "Crouch Idle";
            case CharacterAnimationController.AnimationKind.Action: return "Action";
            case CharacterAnimationController.AnimationKind.Magic: return "Magic";
            case CharacterAnimationController.AnimationKind.IdleLook: return "idle look";
            case CharacterAnimationController.AnimationKind.Death: return "Death";
            case CharacterAnimationController.AnimationKind.Hit: return "Hit";
            case CharacterAnimationController.AnimationKind.StandingWalkForward: return "Standing Walk Forward";
            // These intentionally match the correction in CharacterAnimationController.
            case CharacterAnimationController.AnimationKind.TurnRight: return "Turn Left";
            case CharacterAnimationController.AnimationKind.TurnLeft: return "Turn Right";
            case CharacterAnimationController.AnimationKind.Block: return "Block";
            case CharacterAnimationController.AnimationKind.Attack: return "Attack";
            default: return StandingIdleStateName;
        }
    }
}
