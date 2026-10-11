using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runtime debug overlay / cheat engine — Ctrl+Tab toggles a top-right panel. Typing narrows
// the character search dropdown (refreshed with every character in the scene each time the
// panel opens); picking one and clicking Show renders that character's current
// CharacterBlackboard: the active HTN branch/leaf, its preferred-parameter scores, category
// viability, the resolved target hex, and a live re-score of which cards would currently be
// most suitable. The card dropdown + Play Card button below lists every card that scoring
// pass found playable for this character and force-plays whichever one is selected, through
// the exact same AITurnController path a real AI pick takes (deck consumption, map reveal,
// environmental bookkeeping, everything) — see OnPlayCard.
//
// Entirely self-built at runtime (no prefab/scene wiring) — bootstrapped via
// RuntimeInitializeOnLoadMethod so dropping this script into the project is enough; no manual
// scene setup required.
//
// The report itself stays read-only: it only reads whatever the blackboard already holds from
// the AI's last real turn, plus fresh (non-mutating) UtilityAIContext/ScoreFullDeck snapshots.
// The one exception on the read side is a character that has never been processed by an AI turn
// yet (no blackboard) — rather than just reporting that, it triggers a real
// AITurnController.AdvanceHtnStrategy evaluation on the spot so the panel always shows a live
// result instead of "nothing yet". The Play Card button is the one deliberately mutating action
// this panel exposes.
public class AIBlackboardDebugPanel : MonoBehaviour
{
    private const int MaxSuitableCardsShown = 12;

    private GameObject panelRoot;
    private TMP_Text reportText;
    private TMP_InputField characterSearchInput;
    private TMP_Dropdown characterDropdown;
    private Button showButton;
    private TMP_InputField cardSearchInput;
    private TMP_Dropdown cardDropdown;
    private Button playButton;

    // Refreshed from the scene every time the panel opens (see Update's Ctrl+Tab toggle) —
    // characters can die/spawn/change hands between panel opens, so this is deliberately not
    // cached longer than one open/close cycle. Never filtered in place — characterSearchInput
    // narrows it into displayedCharacters instead (see ApplyCharacterFilter).
    private List<Character> allCharacters;

    // The subset of allCharacters currently shown in characterDropdown, narrowed by
    // characterSearchInput's text. characterDropdown.value indexes directly into this list.
    private List<Character> displayedCharacters;

    // The character the report is currently showing — set when Show is clicked, independent of
    // whatever characterDropdown/displayedCharacters happen to hold afterward (e.g. after
    // OnPlayCard re-runs BuildReport to refresh the same character's report post-play).
    private Character currentCharacter;

    // Populated by the last successful BuildReport call — the same ScoreFullDeck result the
    // report's "Cards that would be suitable now" section already computed, just kept around
    // (sorted descending by score, full list rather than the report's MaxSuitableCardsShown-
    // capped view) so the dropdown has something to offer without re-scoring. Never filtered in
    // place — cardSearchInput narrows it into displayedCards instead, so typing a search never
    // throws away cards outside the current filter.
    private List<(CardData card, float score)> lastScoredCards;

    // The subset of lastScoredCards currently shown in cardDropdown, narrowed by
    // cardSearchInput's text (see ApplyCardFilter). cardDropdown.value indexes directly into
    // this list, so the two must always be kept in the same order.
    private List<(CardData card, float score)> displayedCards;
    private Leader lastLeader;
    private Character lastCharacter;
    private ActionsManager lastActionsManager;
    private UtilityAIContext.PrecomputedData? lastPrecomputed;
    private string lastActiveHtnTaskId;
    private Hex lastActiveHtnTargetHex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<AIBlackboardDebugPanel>() != null) return;
        GameObject go = new("AIBlackboardDebugPanel");
        go.AddComponent<AIBlackboardDebugPanel>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        BuildUi();
        panelRoot.SetActive(false);
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        // Ctrl+Shift+Tab is KeyManager's autoplay toggle — excluding shift here keeps that
        // shortcut from also popping this panel open.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && !shift && Input.GetKeyDown(KeyCode.Tab))
        {
            bool willBeActive = !panelRoot.activeSelf;
            panelRoot.SetActive(willBeActive);
            if (willBeActive)
            {
                RefreshCharacterList();
                characterSearchInput.ActivateInputField();
            }
        }
    }

    // Rebuilds allCharacters from the scene and re-applies whatever search text is currently
    // in the box — called on every panel open so a stale roster (deaths, new spawns, changed
    // ownership) never lingers from a previous session.
    private void RefreshCharacterList()
    {
        allCharacters = FindObjectsByType<Character>()
            .Where(c => c != null && !c.killed && !string.IsNullOrWhiteSpace(c.characterName))
            .OrderBy(c => c.characterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyCharacterFilter(characterSearchInput != null ? characterSearchInput.text : string.Empty);
    }

    // Narrows allCharacters by substring match (case-insensitive, empty = everything) into
    // displayedCharacters and repopulates characterDropdown from it — same pattern as
    // ApplyCardFilter below. Options are labeled with the owner too since character names
    // are not guaranteed unique across leaders (e.g. multiple "Gloin"s).
    private void ApplyCharacterFilter(string filterText)
    {
        characterDropdown.ClearOptions();

        displayedCharacters = allCharacters == null
            ? new List<Character>()
            : allCharacters.Where(c => string.IsNullOrWhiteSpace(filterText)
                || c.characterName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        if (displayedCharacters.Count == 0)
        {
            characterDropdown.AddOptions(new List<string> { allCharacters is { Count: > 0 } ? "(no match)" : "(no characters)" });
            characterDropdown.interactable = false;
            return;
        }

        characterDropdown.AddOptions(displayedCharacters.Select(DescribeCharacterOption).ToList());
        characterDropdown.interactable = true;
        characterDropdown.value = 0;
        characterDropdown.RefreshShownValue();
    }

    private static string DescribeCharacterOption(Character character)
    {
        Leader owner = character.GetOwner();
        return $"{character.characterName} ({(owner != null ? owner.characterName : "no owner")})";
    }

    private void OnShowCharacter()
    {
        if (displayedCharacters == null || displayedCharacters.Count == 0) return;
        int index = characterDropdown.value;
        if (index < 0 || index >= displayedCharacters.Count) return;
        Character character = displayedCharacters[index];
        if (character == null) return;

        currentCharacter = character;
        try
        {
            reportText.text = BuildReport(character);
        }
        catch (Exception e)
        {
            reportText.text = $"<color=#ff6666>Error building report: {e.Message}</color>";
            lastScoredCards = null;
        }
        // A fresh character lookup invalidates whatever the search box was narrowed down to
        // for the previous one.
        cardSearchInput.SetTextWithoutNotify(string.Empty);
        ApplyCardFilter(string.Empty);
    }

    private async void OnPlayCard()
    {
        if (displayedCards == null || displayedCards.Count == 0) return;
        if (lastLeader == null || lastCharacter == null) return;

        int index = cardDropdown.value;
        if (index < 0 || index >= displayedCards.Count) return;
        CardData chosenCard = displayedCards[index].card;
        if (chosenCard == null) return;

        playButton.interactable = false;
        try
        {
            await PresentCardPlayAnimationAsync(chosenCard, lastCharacter);
            UtilityAIContext context = await AITurnController.ExecuteChosenCardAsync(
                lastLeader, lastCharacter, lastActionsManager, lastPrecomputed, chosenCard,
                lastActiveHtnTaskId, lastActiveHtnTargetHex);
            Game.Instance?.RefreshPlayerControlState();

            // ExecuteChosenCardAsync silently falls back to a Pass on failure (see its own
            // implementation) — the animation above already played regardless, so without this
            // check a rejected play looks identical to a successful one from this panel alone.
            bool succeeded = context?.LastChosenAction != null && context.LastChosenAction.LastExecutionSucceeded;
            string report = BuildReport(currentCharacter);
            reportText.text = succeeded ? report : BuildPlayFailureNote(chosenCard, lastLeader) + "\n\n" + report;
        }
        catch (Exception e)
        {
            reportText.text = $"<color=#ff6666>Error playing card: {e.Message}</color>";
        }
        finally
        {
            // Keep whatever the operator had typed rather than resetting it — BuildReport just
            // rescored a fresh lastScoredCards above, so this both repopulates the dropdown and
            // re-applies the still-active search term to it.
            ApplyCardFilter(cardSearchInput.text);
        }
    }

    // ExecuteChosenCardAsync/TryExecuteChosenActionAsync give no reason on failure — this
    // covers the one Environmental-specific gate (PrepareActionForExecution,
    // UtilityAIContext.cs) that scoring itself doesn't pre-filter for a card that was
    // playable when scored but stopped being playable by the time Play Card was clicked
    // (e.g. this leader's one-environmental-card-per-turn allowance got used up in between —
    // by a real AI turn elsewhere, or an earlier cheat-engine play this same turn).
    private static string BuildPlayFailureNote(CardData card, Leader leader)
    {
        string reason = card != null && card.GetCardType() == CardTypeEnum.Environmental
            && leader != null && leader.HasPlayedEnvironmentalCardThisTurn()
            ? "this leader already played an environmental card this turn (one per leader per turn) — likely stale since it was scored"
            : "no specific reason detected — the card may no longer be in the deck, or a playability condition changed since it was scored";
        return $"<color=#ff8080><b>Play did not take effect</b></color> — \"{card?.name}\" was selected but execution failed ({reason}). Deck/leader state unchanged.";
    }

    // Same two-beat presentation a real AI turn shows for the human player's own autoplay
    // (AITurnController.PresentChosenCardAsync, private there) — card enlarged center-screen,
    // then the token spirals down to the acting character's hex (Board's PC/region grant
    // sequences use this identical CenterDisplayLock-held preview+flight pattern) — except
    // unconditional: a deliberate cheat-engine force-play should always show it, not just when
    // the human player's own leader happens to be on autoplay.
    private static async Task PresentCardPlayAnimationAsync(CardData card, Character character)
    {
        if (card == null) return;

        await CenterDisplayLock.WaitAsync();
        try
        {
            CardCenterPreview.Instance?.ShowPreview(card, speedMultiplier: 1.35f, hoverDriven: false);
            await Task.Delay(1200);
            CardCenterPreview.Instance?.HidePreview();

            if (character?.hex != null)
            {
                TaskCompletionSource<bool> arrived = new();
                CardPlayFlight.LaunchFromData(card, character.hex, () => arrived.TrySetResult(true));
                await arrived.Task;
            }
        }
        finally
        {
            CenterDisplayLock.Release();
        }
    }

    // Narrows lastScoredCards by substring match (case-insensitive, empty = everything) into
    // displayedCards and repopulates cardDropdown from it. cardDropdown.value indexes into
    // displayedCards, not lastScoredCards, so a search never has to touch the underlying scored
    // list itself.
    private void ApplyCardFilter(string filterText)
    {
        cardDropdown.ClearOptions();

        displayedCards = lastScoredCards == null
            ? new List<(CardData card, float score)>()
            : lastScoredCards.Where(s => s.card != null
                && (string.IsNullOrWhiteSpace(filterText) || s.card.name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

        if (displayedCards.Count == 0)
        {
            cardDropdown.AddOptions(new List<string> { lastScoredCards is { Count: > 0 } ? "(no match)" : "(no cards)" });
            cardDropdown.interactable = false;
            playButton.interactable = false;
            return;
        }

        cardDropdown.AddOptions(displayedCards.Select(s => $"{s.card.name} ({s.score:0.##})").ToList());
        cardDropdown.interactable = true;
        cardDropdown.value = 0;
        cardDropdown.RefreshShownValue();
        playButton.interactable = true;
    }

    // ------------------------------------------------------------------
    // Report content — read-only inspection, see class header (Play Card is the exception).
    // ------------------------------------------------------------------

    private string BuildReport(Character character)
    {
        lastScoredCards = null;
        lastLeader = null;
        lastCharacter = null;
        lastActionsManager = null;
        lastPrecomputed = null;
        lastActiveHtnTaskId = null;
        lastActiveHtnTargetHex = null;

        if (character == null) return "Search for a character above, pick one from the dropdown, then click Show.";

        StringBuilder sb = new();
        sb.AppendLine($"<b>{character.characterName}</b>");
        Leader owner = character.GetOwner();
        sb.AppendLine($"Owner: {(owner != null ? owner.characterName : "none")}   Hex: {(character.hex != null ? character.hex.GetHoverV2() : "none")}");
        sb.AppendLine();

        if (owner is not Leader leader)
        {
            sb.AppendLine("Not AI-controlled (no owning leader) — no blackboard.");
            return sb.ToString();
        }

        bool hadBlackboard = CharacterBlackboardStore.TryGet(leader, character, out CharacterBlackboard blackboard);
        blackboard ??= CharacterBlackboardStore.GetOrCreate(leader, character);
        if (!hadBlackboard)
        {
            AITurnController.AdvanceHtnStrategy(leader, character, blackboard);
            sb.AppendLine("<color=#80c0ff>(no blackboard yet — triggered a fresh HTN evaluation)</color>");
            sb.AppendLine();
        }

        // The real PC/Region turn-start gathering phase (Leader.RunTurnStartResourceGrants)
        // only runs once this character's leader actually reaches the start of its own turn —
        // for an NPL inspected mid-round, or any leader inspected before its turn comes up,
        // that means the real stockpile is understated relative to what it's about to become.
        // Simulate it one turn ahead: apply the same grants the real phase would apply, run the
        // whole rest of this report (resource shares, advisor viability, card scoring) against
        // that projected stockpile, then revert exactly what was added — the panel stays
        // non-mutating from any caller's perspective, it just borrows the leader's fields for
        // the duration of this synchronous method.
        Dictionary<ProducesEnum, int> projectedGrants = ComputeProjectedTurnStartGrants(leader);
        ApplyProjectedGrants(leader, projectedGrants, apply: true);
        try
        {
            HTNCompoundTask strategyRoot = AIStrategyLibrary.GetStrategyFor(leader);
            HTNPrimitiveTask activePrimitive = HTNPlanner.ResolveActivePrimitive(blackboard.ActiveStack, strategyRoot);
            string stackDescription = blackboard.ActiveStack is { Count: > 0 }
                ? string.Join(" > ", blackboard.ActiveStack.Select(f => $"{f.MethodTaskId}[{f.SubtaskIndex}]"))
                : "(empty)";

            sb.AppendLine("<b>Blackboard</b>");
            sb.AppendLine($"Stack: {stackDescription}");
            sb.AppendLine($"Turns on current task: {blackboard.TurnsOnCurrentTask}");
            sb.AppendLine($"Target hex: {(blackboard.TargetHex != null ? blackboard.TargetHex.GetHoverV2() : "none")}");
            sb.AppendLine();

            sb.AppendLine("<b>Active HTN task</b>");
            if (activePrimitive == null)
            {
                sb.AppendLine("(none resolved)");
            }
            else
            {
                sb.AppendLine($"Task ID: {activePrimitive.TaskId}");
                sb.AppendLine($"Preferred parameters: {(activePrimitive.PreferredParameters is { Count: > 0 } ? string.Join(", ", activePrimitive.PreferredParameters) : "(none)")}");
            }
            sb.AppendLine();

            // Built while the projected grants above are applied, so this snapshot (and the
            // scoring below that reuses it) already reflects the post-gathering stockpile —
            // not a separate mutation of its own.
            UtilityAIContext.PrecomputedData precomputed = UtilityAIContextDataBuilder.Build(leader, character);
            UtilityAIContext ctx = new(leader, character, new List<CharacterAction>(), null, precomputed);

            if (activePrimitive?.PreferredParameters is { Count: > 0 })
            {
                // The exact eligibility check HTNPlanner.Decompose performed for this branch —
                // there is no per-category eligibility concept anymore (no advisor tag), only
                // "does some role-eligible card's own parameters overlap this leaf's".
                bool eligible = ctx.HasEligibleCard(activePrimitive.PreferredParameters);
                sb.AppendLine($"Eligible card for these parameters: {(eligible ? "yes" : "<color=#ff8080>no</color>")}");
                sb.AppendLine();
            }

            if (activePrimitive?.PreferredParameters is { Count: > 0 })
            {
                sb.AppendLine("<b>Preferred parameter values</b>");
                foreach (string parameter in activePrimitive.PreferredParameters)
                {
                    Hex targetHex = ctx.GetTargetHexForParameter(parameter);
                    sb.AppendLine($"{parameter}: {ctx.GetUtilityParameter(parameter):0.##}" + (targetHex != null ? $"  @ {targetHex.GetHoverV2()}" : ""));
                }
                sb.AppendLine();
            }

            bool anyGrantProjected = projectedGrants.Values.Any(v => v > 0);
            sb.AppendLine("<b>Resource distribution (deck target vs current stockpile share)</b>");
            if (anyGrantProjected)
            {
                sb.AppendLine("<color=#80c0ff>(includes this turn's not-yet-applied PC/Region gathering, simulated one turn ahead)</color>");
            }
            ProducesEnum[] tradeableMaterials = { ProducesEnum.leather, ProducesEnum.mounts, ProducesEnum.timber, ProducesEnum.iron, ProducesEnum.steel, ProducesEnum.mithril };
            float totalHeld = 0f;
            foreach (ProducesEnum material in tradeableMaterials) totalHeld += leader.GetResourceAmount(material);
            foreach (ProducesEnum material in tradeableMaterials)
            {
                float target = blackboard.DeckResourceShare != null && blackboard.DeckResourceShare.TryGetValue(material, out float t) ? t : 1f / tradeableMaterials.Length;
                float current = totalHeld > 0f ? leader.GetResourceAmount(material) / totalHeld : 0f;
                string grantNote = projectedGrants.TryGetValue(material, out int granted) && granted > 0 ? $" [+{granted} gathering]" : "";
                sb.AppendLine($"{material}: target {target:P0}  current {current:P0}  (stock {leader.GetResourceAmount(material)}{grantNote})");
            }
            sb.AppendLine();

            // Coarse "is this whole category of response worth considering" aggregates — the
            // same gates HTNRegistry's *.Viable predicates read. Economic has no aggregate of
            // its own (see UtilityAIContext.GetMilitaristicViability's doc comment); its branch
            // gates on the liquid-wealth tier instead, shown here for the same purpose.
            sb.AppendLine("<b>Category viability</b>");
            sb.AppendLine($"Militaristic: {ctx.GetMilitaristicViability():0.##}");
            sb.AppendLine($"Intelligence: {ctx.GetIntelligenceViability():0.##}");
            sb.AppendLine($"Artifacts: {ctx.GetArtifactsViability():0.##}");
            sb.AppendLine($"Diplomatic: {ctx.GetDiplomaticViability():0.##}");
            sb.AppendLine($"Logistics: {ctx.GetLogisticsViability():0.##}");
            sb.AppendLine($"Disruption: {ctx.GetDisruptionViability():0.##}");
            sb.AppendLine($"Economic: {ctx.EconomyStatus} (liquid-wealth tier, not a viability score)");
            sb.AppendLine();

            sb.AppendLine("<b>Cards that would be suitable now</b>");
            ActionsManager actionsManager = ActionsManager.Instance;
            DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
            if (actionsManager == null || deckManager == null || !deckManager.HasDeckFor(leader))
            {
                sb.AppendLine("(deck/actions manager unavailable)");
            }
            else
            {
                List<(CardData card, float score)> scored = AITurnController.ScoreFullDeck(
                    leader, character, actionsManager, deckManager, precomputed, new HashSet<CardData>(),
                    activePrimitive?.PreferredParameters);

                lastScoredCards = scored.OrderByDescending(s => s.score).ToList();
                lastLeader = leader;
                lastCharacter = character;
                lastActionsManager = actionsManager;
                lastPrecomputed = precomputed;
                lastActiveHtnTaskId = activePrimitive?.TaskId;
                lastActiveHtnTargetHex = blackboard.TargetHex;

                if (scored.Count == 0)
                {
                    sb.AppendLine("(no playable card found)");
                }
                else
                {
                    int rank = 1;
                    foreach ((CardData card, float score) in scored.OrderByDescending(s => s.score).Take(MaxSuitableCardsShown))
                    {
                        // Which of this card's own parameters (if any) overlap the active
                        // branch's PreferredParameters — the only thing driving its HTNBiasBonus.
                        string matched = UtilityAI.TryGetProfile(card, out CardParameterProfile profile) && profile.utilityParameters != null && activePrimitive?.PreferredParameters is { Count: > 0 }
                            ? string.Join(",", profile.utilityParameters.Select(p => p.parameter).Where(p => activePrimitive.PreferredParameters.Contains(p, StringComparer.OrdinalIgnoreCase)))
                            : string.Empty;
                        string matchNote = string.IsNullOrEmpty(matched) ? "" : $"  [matches: {matched}]";
                        sb.AppendLine($"{rank}. {card.name} — {score:0.##}{matchNote}");
                        rank++;
                    }
                }
            }

            return sb.ToString();
        }
        finally
        {
            ApplyProjectedGrants(leader, projectedGrants, apply: false);
        }
    }

    // Mirrors Leader.RunTurnStartResourceGrants' own dedup-by-PC-name/region-name loop, but
    // reads each resolved PC/Land card's Granted fields directly instead of calling
    // Board.TriggerOwnPcGrantIfStandingOnOne/TriggerRegionLandGrant — those mutate the leader,
    // play UI animations, and are async; this stays a pure, synchronous read. FindPcCardByPcName/
    // FindLandCardByRegion read DeckManager's global card catalog (built from every loaded
    // deck), not the leader's own deck, so an NPL's still-empty alignment deck has no bearing
    // on whether this resolves correctly.
    private static Dictionary<ProducesEnum, int> ComputeProjectedTurnStartGrants(Leader leader)
    {
        Dictionary<ProducesEnum, int> totals = new()
        {
            [ProducesEnum.leather] = 0, [ProducesEnum.mounts] = 0, [ProducesEnum.timber] = 0,
            [ProducesEnum.iron] = 0, [ProducesEnum.steel] = 0, [ProducesEnum.mithril] = 0, [ProducesEnum.gold] = 0,
        };
        if (leader?.controlledCharacters == null) return totals;

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (deckManager == null) return totals;

        void AddCardGrant(CardData grantCard)
        {
            if (grantCard == null) return;
            totals[ProducesEnum.leather] += grantCard.leatherGranted;
            totals[ProducesEnum.mounts] += grantCard.mountsGranted;
            totals[ProducesEnum.timber] += grantCard.timberGranted;
            totals[ProducesEnum.iron] += grantCard.ironGranted;
            totals[ProducesEnum.steel] += grantCard.steelGranted;
            totals[ProducesEnum.mithril] += grantCard.mithrilGranted;
            totals[ProducesEnum.gold] += grantCard.goldGranted;
        }

        var grantedPcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var grantedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Character c in leader.controlledCharacters)
        {
            if (c == null || c.killed || c.hex == null) continue;
            Hex hex = c.hex;

            PC pc = hex.GetPCData();
            if (pc != null && pc.owner == leader && grantedPcNames.Add(PcDescriptionBuilder.NormalizeLookupKey(pc.pcName)))
            {
                AddCardGrant(deckManager.FindPcCardByPcName(pc.pcName));
            }

            string region = hex.GetLandRegion();
            if (!string.IsNullOrWhiteSpace(region) && grantedRegions.Add(PcDescriptionBuilder.NormalizeLookupKey(region)))
            {
                AddCardGrant(deckManager.FindLandCardByRegion(region));
            }
        }

        return totals;
    }

    // Mutates the raw stockpile fields directly rather than calling Leader.AddX/RemoveX —
    // those also drive StoresManager's resource-gain pulse animation for the human player
    // (TryPulseStoreResourceGain/TryPulseStoreGoldGain), which would flash visibly on apply
    // and silently fail to un-flash on revert (the pulse methods guard out non-positive
    // amounts, so a symmetric Add(+n)/Add(-n) pair isn't actually symmetric in side effects).
    // A direct field adjustment nets to exactly zero with no UI side effect either way.
    private static void ApplyProjectedGrants(Leader leader, Dictionary<ProducesEnum, int> grants, bool apply)
    {
        if (leader == null) return;
        int sign = apply ? 1 : -1;
        leader.leatherAmount += sign * grants[ProducesEnum.leather];
        leader.mountsAmount += sign * grants[ProducesEnum.mounts];
        leader.timberAmount += sign * grants[ProducesEnum.timber];
        leader.ironAmount += sign * grants[ProducesEnum.iron];
        leader.steelAmount += sign * grants[ProducesEnum.steel];
        leader.mithrilAmount += sign * grants[ProducesEnum.mithril];
        leader.goldAmount += sign * grants[ProducesEnum.gold];
    }

    // ------------------------------------------------------------------
    // Runtime UI construction — no prefab available in this environment, so the whole
    // hierarchy (Canvas, panel, input field, scrollable report text) is built in code.
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        GameObject canvasGo = new("AIBlackboardDebugCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        panelRoot = CreatePanel(canvasGo.transform, "Panel", new Color(0f, 0f, 0f, 0.85f), 460f, 620f);
        RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-20f, -20f);

        TMP_Text title = CreateText(panelRoot.transform, "Title", "AI Blackboard / Cheat Engine (Ctrl+Tab to close)", 16, FontStyles.Bold);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -10f);
        titleRect.sizeDelta = new Vector2(-20f, 26f);

        characterSearchInput = CreateInputField(panelRoot.transform, new Vector2(0f, -44f), 440f, "Search characters...");
        characterSearchInput.onValueChanged.AddListener(ApplyCharacterFilter);
        characterSearchInput.onSubmit.AddListener(_ => OnShowCharacter());

        characterDropdown = CreateDropdown(panelRoot.transform, new Vector2(0f, -78f), 300f);

        showButton = CreateButton(panelRoot.transform, "Show", new Vector2(310f, -78f), 130f);
        showButton.onClick.AddListener(OnShowCharacter);

        cardSearchInput = CreateInputField(panelRoot.transform, new Vector2(0f, -112f), 440f, "Search cards...");
        cardSearchInput.onValueChanged.AddListener(ApplyCardFilter);

        cardDropdown = CreateDropdown(panelRoot.transform, new Vector2(0f, -146f), 300f);

        playButton = CreateButton(panelRoot.transform, "Play Card", new Vector2(310f, -146f), 130f);
        playButton.onClick.AddListener(OnPlayCard);

        ApplyCharacterFilter(string.Empty);
        ApplyCardFilter(string.Empty);

        GameObject scrollGo = new("ScrollView", typeof(RectTransform));
        scrollGo.transform.SetParent(panelRoot.transform, false);
        RectTransform scrollRectTransform = scrollGo.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(10f, 10f);
        scrollRectTransform.offsetMax = new Vector2(-10f, -182f);
        ScrollRect scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewportGo = new("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();

        reportText = CreateText(viewportGo.transform, "ReportText", "Search for a character above, pick one from the dropdown, then click Show.", 14, FontStyles.Normal);
        RectTransform reportRect = reportText.rectTransform;
        reportRect.anchorMin = new Vector2(0f, 1f);
        reportRect.anchorMax = new Vector2(1f, 1f);
        reportRect.pivot = new Vector2(0.5f, 1f);
        reportRect.anchoredPosition = Vector2.zero;
        // Stretch anchors alone don't zero out Unity's default 100x100 sizeDelta on a fresh
        // RectTransform, so without this the content is ~100px wider than the viewport and
        // centered — the extra width hangs off both sides and gets clipped by the viewport's
        // RectMask2D, leaving only a strip from the middle of each line visible.
        reportRect.sizeDelta = new Vector2(0f, reportRect.sizeDelta.y);
        reportText.textWrappingMode = TextWrappingModes.Normal;
        // ContentSizeFitter must live on the same GameObject as the TMP component it's sizing
        // around — LayoutUtility only looks for ILayoutElements on the fitter's own GameObject,
        // not its children. Putting it on an empty parent "Content" wrapper (as this used to)
        // finds nothing, so the fitter never grows to fit the text, ScrollRect.content's height
        // never reflects how much text there actually is, and the scrollbar has nothing to
        // scroll to even though the report is still rendering (and getting clipped) below the
        // visible area. reportText's own rect now doubles as the scroll content directly.
        ContentSizeFitter fitter = reportText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = reportRect;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color, float width, float height)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.color = color;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, height);
        return go;
    }

    private static TMP_Text CreateText(Transform parent, string name, string initialText, int fontSize, FontStyles style)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = initialText;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        if (FontManager.Instance != null) FontManager.Instance.ApplyCurrentFont(text);
        return text;
    }

    private static TMP_InputField CreateInputField(Transform parent, Vector2 anchoredPosition, float width, string placeholderText = "Character name...")
    {
        GameObject fieldGo = new("NameInput", typeof(RectTransform));
        fieldGo.transform.SetParent(parent, false);
        Image bg = fieldGo.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.9f);
        RectTransform fieldRect = fieldGo.GetComponent<RectTransform>();
        fieldRect.anchorMin = fieldRect.anchorMax = new Vector2(0f, 1f);
        fieldRect.pivot = new Vector2(0f, 1f);
        fieldRect.anchoredPosition = anchoredPosition;
        fieldRect.sizeDelta = new Vector2(width, 28f);

        GameObject viewportGo = new("TextArea", typeof(RectTransform));
        viewportGo.transform.SetParent(fieldGo.transform, false);
        RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(6f, 4f);
        viewportRect.offsetMax = new Vector2(-6f, -4f);
        viewportGo.AddComponent<RectMask2D>();

        TMP_Text text = CreateText(viewportGo.transform, "Text", string.Empty, 14, FontStyles.Normal);
        text.color = Color.black;
        StretchFull(text.rectTransform);

        TMP_Text placeholder = CreateText(viewportGo.transform, "Placeholder", placeholderText, 14, FontStyles.Italic);
        placeholder.color = new Color(0f, 0f, 0f, 0.5f);
        StretchFull(placeholder.rectTransform);

        TMP_InputField inputField = fieldGo.AddComponent<TMP_InputField>();
        inputField.textViewport = viewportRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        return inputField;
    }

    // Hand-rolled minimal TMP_Dropdown — no editor-only TMP_DefaultControls at runtime, so this
    // reproduces just the pieces the component actually requires (template/captionText/itemText)
    // and skips the purely cosmetic ones (arrow glyph, selected-item checkmark, scrollbar —
    // ScrollRect still supports drag-to-scroll without one). TMP_Dropdown.Show() clones the
    // (inactive) Item template per option and wires its Toggle up internally, so no manual
    // toggle/selection listener is needed here.
    private static TMP_Dropdown CreateDropdown(Transform parent, Vector2 anchoredPosition, float width)
    {
        const float itemHeight = 22f;

        GameObject root = new("CardDropdown", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        Image rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(1f, 1f, 1f, 0.9f);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = new Vector2(width, 28f);

        TMP_Text caption = CreateText(root.transform, "Caption", "(no cards)", 13, FontStyles.Normal);
        caption.color = Color.black;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform captionRect = caption.rectTransform;
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(8f, 2f);
        captionRect.offsetMax = new Vector2(-8f, -2f);

        GameObject template = new("Template", typeof(RectTransform));
        template.transform.SetParent(root.transform, false);
        Image templateImage = template.AddComponent<Image>();
        templateImage.color = new Color(0.12f, 0.12f, 0.12f, 0.98f);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, 2f);
        templateRect.sizeDelta = new Vector2(0f, itemHeight * 8f);

        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewport = new("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(template.transform, false);
        viewport.AddComponent<Image>().color = Color.white;
        Mask viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        GameObject content = new("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, itemHeight);

        GameObject item = new("Item", typeof(RectTransform));
        item.transform.SetParent(content.transform, false);
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 0.5f);
        itemRect.anchorMax = new Vector2(1f, 0.5f);
        itemRect.sizeDelta = new Vector2(0f, itemHeight);

        GameObject itemBackground = new("Item Background", typeof(RectTransform));
        itemBackground.transform.SetParent(item.transform, false);
        Image itemBackgroundImage = itemBackground.AddComponent<Image>();
        itemBackgroundImage.color = new Color(1f, 1f, 1f, 0.08f);
        StretchFull(itemBackground.GetComponent<RectTransform>());

        Toggle itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackgroundImage;
        itemToggle.isOn = true;

        TMP_Text itemLabel = CreateText(item.transform, "Item Label", "Option", 13, FontStyles.Normal);
        itemLabel.color = Color.white;
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform itemLabelRect = itemLabel.rectTransform;
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(8f, 1f);
        itemLabelRect.offsetMax = new Vector2(-8f, -1f);

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        template.SetActive(false);

        TMP_Dropdown dropdown = root.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = rootImage;
        dropdown.captionText = caption;
        dropdown.itemText = itemLabel;
        dropdown.template = templateRect;
        dropdown.options.Clear();

        return dropdown;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, float width)
    {
        GameObject buttonGo = new("Button", typeof(RectTransform));
        buttonGo.transform.SetParent(parent, false);
        Image bg = buttonGo.AddComponent<Image>();
        bg.color = new Color(0.3f, 0.5f, 0.9f, 1f);
        RectTransform rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(width, 28f);

        TMP_Text label1 = CreateText(buttonGo.transform, "Label", label, 14, FontStyles.Bold);
        label1.alignment = TextAlignmentOptions.Center;
        StretchFull(label1.rectTransform);

        return buttonGo.AddComponent<Button>();
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
