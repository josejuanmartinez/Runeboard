using System;
using UnityEngine;

public class EnvironmentalCardManager : MonoBehaviour
{
    public const int NationEnvironmentalCooldownTurns = 5;

    public static EnvironmentalCardManager Instance { get; private set; }

    public CardData ActiveCard { get; private set; }

    // Per-turn modifier fields — reset each turn before ApplyOngoingEffect runs.
    // Environmental card scripts set these in ApplyOngoingEffect; core systems read them.
    public int FrozenMovementExtraPenalty = 0;
    public float FrozenCombatAttackFactor = 1f;
    public float FrozenCombatDefenseExtraFactor = 1f;
    public int SunburntMovementExtraPenalty = 0;
    public int SunburntDamageExtraPenalty = 0;
    public float SunburntEntryChanceBonus = 0f;
    public float GlobalArmyAttackFactor = 1f;
    public float FreePeopleArmyAttackFactor = 1f;
    public float DarkServantsArmyAttackFactor = 1f;

    private Game subscribedGame;
    private Leader lastNationToPlay;
    private int lastNationPlayTurn = -999;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        SubscribeToGame();
    }

    private void SubscribeToGame()
    {
        Game game = Game.Instance;
        if (game == null || game == subscribedGame) return;
        if (subscribedGame != null) subscribedGame.NewTurnStarted -= OnNewTurn;
        subscribedGame = game;
        game.NewTurnStarted += OnNewTurn;
    }

    public void SetActiveCard(CardData card)
    {
        if (subscribedGame == null) SubscribeToGame();
        ActiveCard = card;
        Layout layout = FindAnyObjectByType<Layout>();
        Debug.Log($"[EnvCardToken] SetActiveCard('{card?.name}') — layout found={layout != null}, instanceId={(layout != null ? layout.GetEntityId().ToString() : "n/a")}");
        layout?.SetEnvironmentalCard(card);
        // The weather rolls in across the top of the screen (not when a save restores it).
        if (card != null && SaveSystem.PendingLoad == null) EnvironmentBurst.Play(card);
    }

    public bool CanNationPlay(Leader leader, int turn, out string reason)
    {
        if (leader == null)
        {
            reason = "No nation is available to play this environmental card.";
            return false;
        }

        int turnsSinceOwnCard = turn - leader.lastEnvironmentalCardPlayedTurn;
        if (turnsSinceOwnCard < NationEnvironmentalCooldownTurns)
        {
            int turnsRemaining = NationEnvironmentalCooldownTurns - turnsSinceOwnCard;
            reason = $"{leader.characterName} must wait {turnsRemaining} more turn(s) before playing another environmental card.";
            return false;
        }

        if (lastNationToPlay != null && lastNationToPlay != leader && lastNationPlayTurn == turn)
        {
            reason = $"{lastNationToPlay.characterName} already changed the environment this turn. Another nation may do so next turn.";
            return false;
        }

        reason = null;
        return true;
    }

    public bool TrySetActiveCard(CardData card, Leader leader, out string reason)
    {
        int turn = Game.Instance?.turn ?? 0;
        if (!CanNationPlay(leader, turn, out reason)) return false;

        SetActiveCard(card);
        leader.RecordEnvironmentalCardPlayed(turn);
        lastNationToPlay = leader;
        lastNationPlayTurn = turn;
        return true;
    }

    private void OnNewTurn(int turn)
    {
        ResetModifiers();
        if (ActiveCard == null) return;
        ApplyActiveCardEffect();
    }

    private void ResetModifiers()
    {
        FrozenMovementExtraPenalty = 0;
        FrozenCombatAttackFactor = 1f;
        FrozenCombatDefenseExtraFactor = 1f;
        SunburntMovementExtraPenalty = 0;
        SunburntDamageExtraPenalty = 0;
        SunburntEntryChanceBonus = 0f;
        GlobalArmyAttackFactor = 1f;
        FreePeopleArmyAttackFactor = 1f;
        DarkServantsArmyAttackFactor = 1f;
    }

    private void ApplyActiveCardEffect()
    {
        if (ActiveCard == null) return;
        string className = ActiveCard.GetActionRef();
        if (string.IsNullOrEmpty(className)) return;

        Type type = Type.GetType(className);
        if (type == null || !typeof(CharacterAction).IsAssignableFrom(type)) return;

        try
        {
            var instance = (CharacterAction)Activator.CreateInstance(type);
            instance.ApplyOngoingEffect();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"EnvironmentalCardManager: failed to apply ongoing effect for {className}: {e.Message}");
        }
    }

    public void ClearActiveCard()
    {
        ActiveCard = null;
        ResetModifiers();
        Layout layout = FindAnyObjectByType<Layout>();
        layout?.SetEnvironmentalCard(null);
    }

    public static EnvironmentalCardManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("EnvironmentalCardManager");
        return go.AddComponent<EnvironmentalCardManager>();
    }
}
