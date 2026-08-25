using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class ActionsManager : MonoBehaviour
{
    [HideInInspector]
    public CharacterAction DEFAULT;
    public CharacterAction[] characterActions;
    public static readonly char[] ActionHotkeyLetters = "BCEFGHIJKLMOQRTUVWYZ".ToCharArray();

    private readonly Dictionary<Type, CharacterAction> actionComponents = new();
    private readonly Dictionary<string, CharacterAction> actionComponentsByClassName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CharacterAction> availableActions = new();
    private Character currentCharacter;
    private Game cachedGame;

    // Same rationale as Board.Instance/Game.Instance: many card actions' condition/effect
    // closures resolved this via FindFirstObjectByType<ActionsManager>() individually — a
    // scene-wide search repeated for every card scored, every pick, every character, every AI
    // turn. Cached here instead.
    public static ActionsManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public void Start()
    {
        characterActions = Array.Empty<CharacterAction>();
        DEFAULT = ResolveActionByRef(Pass.ActionRef);

        currentCharacter = null;
        availableActions.Clear();
    }

    public T GetAction<T>() where T : CharacterAction
    {
        CharacterAction component = GetOrCreateAction(typeof(T));
        if (component != null) return component as T;
        Debug.LogWarning($"Action of type {typeof(T).Name} not found!");
        return null;
    }

    public CharacterAction ResolveActionByRef(string actionRef, CardData card = null)
    {
        string normalizedActionRef = NormalizeActionRef(actionRef);
        if (string.IsNullOrWhiteSpace(normalizedActionRef)) return null;

        if (actionComponentsByClassName.TryGetValue(normalizedActionRef, out CharacterAction loaded))
        {
            return loaded;
        }

        Type resolvedType = ResolveActionType(normalizedActionRef);
        return GetOrCreateAction(resolvedType, card);
    }

    public IReadOnlyList<CharacterAction> GetLoadedActions()
    {
        return actionComponents.Values.ToArray();
    }

    public void Refresh(Character character)
    {
        if (character == null)
        {
            Hide();
            return;
        }

        currentCharacter = character;

        if (IsHumanPlayerCharacter(character))
        {
            availableActions.Clear();
            return;
        }

        foreach (CharacterAction component in GetLoadedActions())
        {
            component.Initialize(character, condition: null, effect: null, asyncEffect: null);
        }

        BuildAvailableActions();
    }

    public void Hide()
    {
        foreach (CharacterAction component in actionComponents.Values)
        {
            component.Reset();
        }

        availableActions.Clear();
        currentCharacter = null;
    }

    // No-op: there is no action-button panel in the scene right now (card play routes through
    // SituationCardsUI's opportunity-card bloom instead, see DeckManager). Kept because
    // Board/Game/PopupManager still call it on turn/popup changes; remove those call sites too
    // if this stays permanently unused.
    public void RefreshInteractableState()
    {
    }

    private void BuildAvailableActions()
    {
        availableActions.Clear();
        if (currentCharacter == null) return;

        foreach (CharacterAction action in GetLoadedActions())
        {
            if (action == null) continue;
            if (!action.IsRoleEligible(currentCharacter)) continue;
            if (!action.FulfillsConditions()) continue;
            availableActions.Add(action);
        }

        availableActions.Sort((a, b) => string.Compare(a?.actionName, b?.actionName, StringComparison.OrdinalIgnoreCase));
    }

    private Game GetGame()
    {
        if (cachedGame == null) cachedGame = FindFirstObjectByType<Game>();
        return cachedGame;
    }

    private bool IsHumanPlayerCharacter(Character character)
    {
        Game game = GetGame();
        return character != null && game != null && game.player != null && character.GetOwner() == game.player;
    }

    private string NormalizeActionName(string value)
    {
        string stripped = ActionNameUtils.StripShortcut(value);
        return string.IsNullOrWhiteSpace(stripped) ? string.Empty : stripped.Trim().ToLowerInvariant();
    }

    private static string NormalizeActionRef(string actionRef)
    {
        if (string.IsNullOrWhiteSpace(actionRef)) return string.Empty;

        string normalized = actionRef.Trim();
        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
        }

        return normalized.Trim();
    }

    private CharacterAction RegisterActionComponent(CharacterAction action)
    {
        if (action == null) return null;

        actionComponents[action.GetType()] = action;
        actionComponentsByClassName[action.GetType().Name] = action;

        characterActions = actionComponents.Values.ToArray();
        return action;
    }

    private CharacterAction GetOrCreateAction(Type actionType, CardData card = null)
    {
        if (actionType == null || !typeof(CharacterAction).IsAssignableFrom(actionType)) return null;
        if (actionComponents.TryGetValue(actionType, out CharacterAction loaded))
        {
            if (card != null)
            {
                // Re-initialize with card data if provided
                loaded.Initialize(loaded.character, card);
            }
            return loaded;
        }
        
        CharacterAction created = Activator.CreateInstance(actionType) as CharacterAction;
        if (card != null)
        {
            created.Initialize(null, card);
        }
        return RegisterActionComponent(created);
    }

    private static Type ResolveActionType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;

        Type direct = Type.GetType(className, false, true);
        if (direct != null) return direct;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type candidate = assembly.GetType(className, false, true);
            if (candidate != null) return candidate;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            candidate = types.FirstOrDefault(t =>
                string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase));
            if (candidate != null) return candidate;
        }

        return null;
    }
}
