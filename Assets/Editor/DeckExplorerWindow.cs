using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class DeckExplorerWindow : EditorWindow
{
    [Serializable]
    private class FeedbackPayload
    {
        public string deckId;
        public int cardId;
        public string name;
        public string type;
        public string actionRef;
        public string spriteName;
        public string quote;
        public string actionEffect;
        public string renderedDescription;
        public string requirementsText;
        public string renderedRequirements;
        public string requirementErrors;
        public string rawFields;
    }

    private class DeckEntryView
    {
        public DeckManifestEntry manifest;
        public DeckData deckData;
        public int depth;
    }

    private readonly List<DeckEntryView> deckViews = new();
    private readonly List<CardData> filteredCards = new();
    private readonly Dictionary<string, Sprite> spriteCache = new(StringComparer.OrdinalIgnoreCase);

    private Vector2 deckScroll;
    private Vector2 cardScroll;
    private Vector2 detailScroll;

    private string searchText = string.Empty;
    private int selectedDeckIndex;
    private int selectedCardIndex;
    private bool onlyShowCardsWithActions;
    private bool sortCardsByTypeThenName = true;
    private string editedCardKey;
    private string copyTargetResourcePath;
    private int editedCommanderSkillRequired;
    private int editedAgentSkillRequired;
    private int editedEmissarySkillRequired;
    private int editedMageSkillRequired;
    private int editedLeatherRequired;
    private int editedMountsRequired;
    private int editedTimberRequired;
    private int editedIronRequired;
    private int editedSteelRequired;
    private int editedMithrilRequired;
    private int editedGoldRequired;
    private int editedJokerRequired;
    private int editedLeatherGranted;
    private int editedMountsGranted;
    private int editedTimberGranted;
    private int editedIronGranted;
    private int editedSteelGranted;
    private int editedMithrilGranted;
    private int editedGoldGranted;
    private bool editedIsUnderground;
    private ArmySpecialAbilityEnum editedArmyAbilityToAdd;
    private TroopsTypeEnum editedTroopType;
    private int editedProcChance;
    private string editedActionEffect = string.Empty;
    private int editedCharacterCommander;
    private int editedCharacterAgent;
    private int editedCharacterEmissary;
    private int editedCharacterMage;
    private string editedStartingPC = string.Empty;
    private CardTypeEnum editedCardType;
    private CardSituationEnum editedSituation;
    private string editedName = string.Empty;
    private string editedQuote = string.Empty;
    private string editedDescription = string.Empty;
    private string editedRequirementsText = string.Empty;
    private string editedHistoryText = string.Empty;
    private string editedRegion = string.Empty;
    private string editedTags = string.Empty;
    private string editedArtifacts = string.Empty;
    private string editedActionRef = string.Empty;
    private string editedSpriteName = string.Empty;
    private string editedPortraitName = string.Empty;
    private string editedDeckSpriteName = string.Empty;
    private string editedCharacterGroup = string.Empty;
    private string editedStatusEffect = string.Empty;
    private int editedDifficulty;
    private int editedAlignment;
    private RacesEnum editedRace;
    private string editedRawJson = string.Empty;
    private bool editedObjectHidden;
    private bool editedObjectTransferable;
    private int editedObjectCopies;
    private int editedObjectCommanderBonus;
    private int editedObjectAgentBonus;
    private int editedObjectEmmissaryBonus;
    private int editedObjectMageBonus;
    private string editedObjectPassiveEffectId = string.Empty;
    private int editedObjectPassiveEffectValue;
    private int editedObjectHealPerTurn;
    private int editedObjectMovementBonus;
    private bool editedObjectIgnoreTerrainMovementPenalty;
    private bool editedObjectGrantsHasteAtSea;
    private bool editedObjectGrantsEnvironmentalImmunity;
    private int editedObjectAutoScoutRadius;
    private int editedObjectDetectionEvasion;
    private int editedObjectRecruitBonusMenAtArms;
    private int editedObjectScryAreaBonus;
    private int editedObjectScryObjectBonus;
    private string editedObjectNegativeStatusImmunity = string.Empty;
    private int editedObjectNegativeStatusDurationReduction;
    private int editedObjectNegativeStatusDamageReduction;
    private int editedObjectPositiveStatusDurationBonus;
    private int editedObjectPositiveStatusEffectBonus;
    private bool showRawJsonEditor;

    private const float PreviewCardW = 275f;
    private const float PreviewCardH = 325f;
    private const float PreviewPad   = 15f;
    private const float PreviewCanvasW = PreviewCardW + PreviewPad * 2f; // 420
    private const float PreviewCanvasH = PreviewCardH + PreviewPad * 2f; // 585

    private TextAsset manifestAsset;
    private CardsManifest cardsManifest;
    private GameObject cardPrefabAsset;
    private GameObject previewRoot;
    private GameObject previewCanvasRoot;
    private GameObject previewCardObject;
    private Card previewCardComponent;
    private Camera previewCamera;
    private RenderTexture previewRenderTexture;
    private Rect previewRect;

    [MenuItem("Tools/Runeboard/Deck Explorer")]
    public static void Open()
    {
        GetWindow<DeckExplorerWindow>("Deck Explorer");
    }

    private void OnEnable()
    {
        RefreshData();
    }

    private void OnDisable()
    {
        ClearPreviewInstance();
    }

    private void OnGUI()
    {
        GUI.enabled = true;
        EditorGUI.showMixedValue = false;
        DrawToolbar();

        if (deckViews.Count == 0)
        {
            EditorGUILayout.HelpBox("No deck data found. Check Assets/Resources/Cards.json and deck resource paths.", MessageType.Warning);
            if (GUILayout.Button("Refresh")) RefreshData();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawDeckPane();
        DrawCardPane();
        DrawDetailPane();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshData();
        }

        bool newOnlyShowCardsWithActions = GUILayout.Toggle(onlyShowCardsWithActions, "Actions only", EditorStyles.toolbarButton, GUILayout.Width(90));
        if (newOnlyShowCardsWithActions != onlyShowCardsWithActions)
        {
            onlyShowCardsWithActions = newOnlyShowCardsWithActions;
            RebuildFilteredCards();
        }

        bool newSort = GUILayout.Toggle(sortCardsByTypeThenName, "Sort by type", EditorStyles.toolbarButton, GUILayout.Width(85));
        if (newSort != sortCardsByTypeThenName)
        {
            sortCardsByTypeThenName = newSort;
            RebuildFilteredCards();
        }

        GUILayout.Space(8);
        GUILayout.Label("Search", GUILayout.Width(45));
        string newSearch = GUILayout.TextField(searchText, EditorStyles.toolbarTextField, GUILayout.MinWidth(180));
        if (!string.Equals(newSearch, searchText, StringComparison.Ordinal))
        {
            searchText = newSearch;
            RebuildFilteredCards();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDeckPane()
    {
        GUILayout.BeginVertical(GUILayout.Width(260));
        EditorGUILayout.LabelField("Decks", EditorStyles.boldLabel);

        deckScroll = EditorGUILayout.BeginScrollView(deckScroll, GUILayout.Width(360));
        for (int i = 0; i < deckViews.Count; i++)
        {
            DeckEntryView view = deckViews[i];
            if (view == null || view.manifest == null) continue;

            string label = BuildDeckLabel(view);
            bool selected = i == selectedDeckIndex;
            GUIStyle style = selected ? EditorStyles.helpBox : EditorStyles.label;

            if (GUILayout.Toggle(selected, label, style))
            {
                if (selectedDeckIndex != i)
                {
                    selectedDeckIndex = i;
                    selectedCardIndex = 0;
                    RebuildFilteredCards();
                }
            }
        }
        EditorGUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    private void DrawCardPane()
    {
        GUILayout.BeginVertical(GUILayout.Width(320));
        EditorGUILayout.LabelField("Cards", EditorStyles.boldLabel);

        DeckEntryView deckView = GetSelectedDeckView();
        if (deckView == null)
        {
            EditorGUILayout.HelpBox("Select a deck.", MessageType.Info);
            GUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField($"{filteredCards.Count} card(s)");

        cardScroll = EditorGUILayout.BeginScrollView(cardScroll, GUILayout.Width(320));
        for (int i = 0; i < filteredCards.Count; i++)
        {
            CardData card = filteredCards[i];
            if (card == null) continue;

            bool selected = i == selectedCardIndex;
            string finalizedPrefix = IsCardFinalized(card, deckView) ? "✔ " : string.Empty;
            string typeLabel = FormatCardTypeLabel(card.GetCardType());
            string referencePrefix = IsReferenceCard(card) ? "↩ " : string.Empty;
            string label = $"{finalizedPrefix}{referencePrefix}{FormatCardTitle(card.name)}  [{typeLabel}]";
            if (GUILayout.Toggle(selected, label, CreateRichTextStyle(selected ? EditorStyles.helpBox : EditorStyles.label)))
            {
                if (selectedCardIndex != i)
                {
                    selectedCardIndex = i;
                    Repaint();
                }
            }
        }
        EditorGUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    private void DrawDetailPane()
    {
        GUI.enabled = true;
        EditorGUI.showMixedValue = false;
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        CardData card = GetSelectedCard();
        bool isReference = IsReferenceCard(card);

        if (card == null)
        {
            EditorGUILayout.HelpBox("Select a card to inspect its preview.", MessageType.Info);
            GUILayout.EndVertical();
            return;
        }

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        DrawCardPreview(card);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = true;
        EditorGUI.showMixedValue = false;
        EditorGUI.BeginDisabledGroup(isReference);
        EditorGUI.BeginChangeCheck();
        bool finalized = EditorGUILayout.ToggleLeft("Finalized", IsCardFinalized(card));
        if (EditorGUI.EndChangeCheck())
        {
            SetCardFinalized(card, finalized);
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.FlexibleSpace();
        DrawCopyToDeckControls(card);
        GUILayout.Space(6);
        if (GUILayout.Button("Provide Feedback", GUILayout.Width(130)))
        {
            CopyCardFeedbackPayload(card);
        }
        GUILayout.Space(6);
        if (GUILayout.Button("Reload Card", GUILayout.Width(100)))
        {
            ReloadSelectedCard();
        }
        EditorGUILayout.EndHorizontal();

        if (isReference)
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox("Reference card locked for editing in this deck. Copy and move are still available.", MessageType.Info);
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Card Data", EditorStyles.boldLabel);
        DrawCardDetails(card);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Editable Requirements", EditorStyles.boldLabel);
        DrawEditableRequirements(card);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Action", EditorStyles.boldLabel);
        DrawActionDetails(card);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Requirement Errors", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(BuildRequirementErrors(card), EditorStyles.textArea, GUILayout.MinHeight(90));

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Raw Fields", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(BuildRawSummary(card), EditorStyles.textArea, GUILayout.MinHeight(110));

        GUILayout.Space(10);
        showRawJsonEditor = EditorGUILayout.Foldout(showRawJsonEditor, "Raw JSON Editor (all fields)", true);
        if (showRawJsonEditor)
        {
            DrawRawJsonEditor(card);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawCardPreview(CardData card)
    {
        Rect box = GUILayoutUtility.GetRect(0f, 540f, GUILayout.ExpandWidth(true));
        GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

        previewRect = new Rect(box.x + 8f, box.y + 8f, box.width - 16f, box.height - 16f);
        try
        {
            UpdateLivePreview(card, previewRect);
        }
        catch (Exception ex)
        {
            EditorGUI.HelpBox(previewRect, $"Preview failed: {ex.Message}", MessageType.Warning);
        }
    }

    private void DrawCardDetails(CardData card)
    {
        SyncEditableCardFields(card);

        EditorGUILayout.LabelField("Name", FormatCardTitle(card.name));
        EditorGUILayout.LabelField("Type", FormatCardTypeLabel(card.GetCardType()), CreateRichTextStyle(EditorStyles.label));
        EditorGUILayout.LabelField("Deck", card.deckId ?? string.Empty);
        if (IsReferenceCard(card))
        {
            EditorGUILayout.LabelField("Reference", $"{card.referenceDeckId} / {card.referenceCardId}");
            if (GUILayout.Button("Go to original card", GUILayout.Width(180)))
            {
                GoToOriginalCard(card);
            }
        }
        if (GUILayout.Button("Remove Card", GUILayout.Width(120)))
        {
            RemoveCard(card);
        }
        EditorGUILayout.LabelField("Gold Cost", card.GetTotalGoldCost().ToString());
        EditorGUILayout.LabelField("Costs", BuildCostSummary(card));
        EditorGUILayout.LabelField("Grants", BuildGrantSummary(card));

        if (card.GetCardType() == CardTypeEnum.Land && !string.IsNullOrWhiteSpace(card.name))
        {
            string areaLabel = PcDescriptionBuilder.FormatDisplayRegionName(card.name);
            EditorGUILayout.LabelField($"Reveals hexes and allows founding PCs originally from {areaLabel}.", EditorStyles.wordWrappedLabel);
        }

        if (card.GetCardType() == CardTypeEnum.PC && !string.IsNullOrWhiteSpace(card.name))
        {
            EditorGUILayout.LabelField($"Allows recruiting characters born in {card.name}.", EditorStyles.wordWrappedLabel);
        }

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Editable Fields", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));
        GUIStyle textAreaStyle = new(EditorStyles.textArea) { wordWrap = true };

        editedName = EditorGUILayout.TextField("Name", editedName);
        editedRegion = EditorGUILayout.TextField("Region", editedRegion);
        editedTags = EditorGUILayout.TextField(new GUIContent("Tags", "Comma-separated list"), editedTags);
        editedArtifacts = EditorGUILayout.TextField(new GUIContent("Artifacts", "Comma-separated list"), editedArtifacts);
        editedActionRef = EditorGUILayout.TextField(new GUIContent("Action Ref", "Action class name resolved for this card"), editedActionRef);
        editedSpriteName = EditorGUILayout.TextField("Sprite Name", editedSpriteName);
        editedPortraitName = EditorGUILayout.TextField("Portrait Name", editedPortraitName);
        editedDeckSpriteName = EditorGUILayout.TextField("Deck Sprite Name", editedDeckSpriteName);
        editedCharacterGroup = EditorGUILayout.TextField("Character Group", editedCharacterGroup);
        editedStatusEffect = EditorGUILayout.TextField("Status Effect", editedStatusEffect);
        editedRace = (RacesEnum)EditorGUILayout.EnumPopup("Race", editedRace);
        editedAlignment = EditorGUILayout.IntField("Alignment", editedAlignment);
        editedDifficulty = EditorGUILayout.IntField("Difficulty", editedDifficulty);

        GUILayout.Space(4);
        EditorGUILayout.LabelField("Quote");
        editedQuote = EditorGUILayout.TextArea(editedQuote, textAreaStyle, GUILayout.MinHeight(40));
        EditorGUILayout.LabelField("Action Effect");
        editedActionEffect = EditorGUILayout.TextArea(editedActionEffect, textAreaStyle, GUILayout.MinHeight(60));
        EditorGUILayout.LabelField("Description");
        editedDescription = EditorGUILayout.TextArea(editedDescription, textAreaStyle, GUILayout.MinHeight(60));
        EditorGUILayout.LabelField("Requirements Text");
        editedRequirementsText = EditorGUILayout.TextArea(editedRequirementsText, textAreaStyle, GUILayout.MinHeight(40));
        EditorGUILayout.LabelField("History Text");
        editedHistoryText = EditorGUILayout.TextArea(editedHistoryText, textAreaStyle, GUILayout.MinHeight(40));

        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save card fields", GUILayout.Width(140)))
        {
            SaveCardFields(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawCopyToDeckControls(CardData card)
    {
        DeckEntryView currentDeck = GetSelectedDeckView();
        List<DeckEntryView> targets = GetAllTransferTargets(currentDeck);
        if (targets.Count == 0)
        {
            EditorGUILayout.LabelField("To deck", "No deck targets");
            return;
        }

        int targetIndex = GetCopyTargetIndex(targets);
        string[] options = targets.Select(BuildDeckLabel).ToArray();

        bool disableTransfer = !IsCardFinalized(card) || (IsCardDisabled(card) && !IsReferenceCard(card));
        EditorGUI.BeginDisabledGroup(disableTransfer);
        EditorGUILayout.BeginHorizontal(GUILayout.Width(460));
        EditorGUILayout.LabelField("To deck", GUILayout.Width(55));

        int newIndex = EditorGUILayout.Popup(targetIndex, options, GUILayout.Width(230));
        if (newIndex != targetIndex)
        {
            copyTargetResourcePath = targets[newIndex].manifest.resourcePath;
        }

        if (GUILayout.Button("Copy", GUILayout.Width(62)))
        {
            CopyFinalizedCardToSubdeck(card, currentDeck, targets[newIndex]);
        }
        if (GUILayout.Button("Move", GUILayout.Width(62)))
        {
            MoveFinalizedCardToSubdeck(card, currentDeck, targets[newIndex]);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableRequirements(CardData card)
    {
        if (card == null) return;

        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));

        EditorGUILayout.BeginHorizontal();
        editedCardType = (CardTypeEnum)EditorGUILayout.EnumPopup("Card Type", editedCardType);
        if (GUILayout.Button("Save type", GUILayout.Width(90)))
        {
            SaveCardType(card);
        }
        EditorGUILayout.EndHorizontal();

        if (editedCardType == CardTypeEnum.Action)
        {
            EditorGUILayout.BeginHorizontal();
            editedSituation = (CardSituationEnum)EditorGUILayout.EnumPopup("Situation", editedSituation);
            if (GUILayout.Button("Save", GUILayout.Width(90)))
            {
                SaveSituation(card);
            }
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(4);
        editedCommanderSkillRequired = EditorGUILayout.IntField("Commander", editedCommanderSkillRequired);
        editedAgentSkillRequired = EditorGUILayout.IntField("Agent", editedAgentSkillRequired);
        editedEmissarySkillRequired = EditorGUILayout.IntField("Emissary", editedEmissarySkillRequired);
        editedMageSkillRequired = EditorGUILayout.IntField("Mage", editedMageSkillRequired);

        GUILayout.Space(4);
        editedLeatherRequired = EditorGUILayout.IntField("Leather", editedLeatherRequired);
        editedTimberRequired = EditorGUILayout.IntField("Timber", editedTimberRequired);
        editedMountsRequired = EditorGUILayout.IntField("Mounts", editedMountsRequired);
        editedIronRequired = EditorGUILayout.IntField("Iron", editedIronRequired);
        editedSteelRequired = EditorGUILayout.IntField("Steel", editedSteelRequired);
        editedMithrilRequired = EditorGUILayout.IntField("Mithril", editedMithrilRequired);
        editedJokerRequired = EditorGUILayout.IntField("Joker", editedJokerRequired);

        if (card.GetCardType() != CardTypeEnum.Character)
        {
            editedGoldRequired = EditorGUILayout.IntField("Gold", editedGoldRequired);
        }

        if (card.GetCardType() == CardTypeEnum.Army)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Army Abilities", EditorStyles.boldLabel);
            DrawEditableArmyType(card);
            DrawEditableArmyAbilities(card);
        }

        if (card.GetCardType() == CardTypeEnum.Character)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Character Stats", EditorStyles.boldLabel);
            DrawEditableCharacterStats(card);
        }

        if (card.GetCardType() == CardTypeEnum.Object)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Object Effects", EditorStyles.boldLabel);
            DrawEditableObjectStats(card);
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Editable Grants", EditorStyles.boldLabel);
        DrawEditableGrants(card);


        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save new requirements", GUILayout.Width(180)))
        {
            SaveNewRequirements(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableGrants(CardData card)
    {
        if (card == null) return;

        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));
        editedLeatherGranted = EditorGUILayout.IntField("Leather", editedLeatherGranted);
        editedTimberGranted = EditorGUILayout.IntField("Timber", editedTimberGranted);
        editedMountsGranted = EditorGUILayout.IntField("Mounts", editedMountsGranted);
        editedIronGranted = EditorGUILayout.IntField("Iron", editedIronGranted);
        editedSteelGranted = EditorGUILayout.IntField("Steel", editedSteelGranted);
        editedMithrilGranted = EditorGUILayout.IntField("Mithril", editedMithrilGranted);
        editedGoldGranted = EditorGUILayout.IntField("Gold", editedGoldGranted);

        if (card.GetCardType() == CardTypeEnum.PC)
        {
            GUILayout.Space(4);
            editedIsUnderground = EditorGUILayout.Toggle(
                new GUIContent("Underground", "The founded PC marks its hex as an entrance to the Underground."),
                editedIsUnderground);
        }

        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save new grants", GUILayout.Width(160)))
        {
            SaveNewGrants(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableArmyAbilities(CardData card)
    {
        if (card == null) return;

        card.specialAbilities ??= new List<ArmySpecialAbilityEnum>();
        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));

        EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel);

        int toRemove = -1;
        for (int i = 0; i < card.specialAbilities.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            card.specialAbilities[i] = (ArmySpecialAbilityEnum)EditorGUILayout.EnumPopup(card.specialAbilities[i]);
            if (GUILayout.Button("-", GUILayout.Width(25)))
                toRemove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (toRemove >= 0)
        {
            card.specialAbilities.RemoveAt(toRemove);
            SaveArmyAbilities(card);
        }

        EditorGUILayout.BeginHorizontal();
        editedArmyAbilityToAdd = (ArmySpecialAbilityEnum)EditorGUILayout.EnumPopup("Add", editedArmyAbilityToAdd);
        if (GUILayout.Button("+", GUILayout.Width(25)))
        {
            if (!card.specialAbilities.Contains(editedArmyAbilityToAdd))
            {
                card.specialAbilities.Add(editedArmyAbilityToAdd);
                SaveArmyAbilities(card);
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);
        editedProcChance = EditorGUILayout.IntSlider("Proc Chance", editedProcChance, 1, 100);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save proc chance", GUILayout.Width(130)))
            SaveArmyAbilities(card);
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableArmyType(CardData card)
    {
        if (card == null) return;

        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Type", GUILayout.Width(40));
        editedTroopType = (TroopsTypeEnum)EditorGUILayout.EnumPopup(editedTroopType);
        if (GUILayout.Button("Save type", GUILayout.Width(90)))
        {
            SaveArmyType(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableCharacterStats(CardData card)
    {
        if (card == null) return;

        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));
        EditorGUILayout.LabelField("Gold Cost", card.GetTotalGoldCost().ToString());
        editedCharacterCommander = EditorGUILayout.IntField("Commander", editedCharacterCommander);
        editedCharacterAgent = EditorGUILayout.IntField("Agent", editedCharacterAgent);
        editedCharacterEmissary = EditorGUILayout.IntField("Emissary", editedCharacterEmissary);
        editedCharacterMage = EditorGUILayout.IntField("Mage", editedCharacterMage);

        GUILayout.Space(4);
        List<string> pcNames = GetAvailablePcNamesForCurrentDeck();
        string[] pcOptions = pcNames.ToArray();
        int currentPcIndex = Mathf.Max(0, pcNames.IndexOf(editedStartingPC));
        int newPcIndex = EditorGUILayout.Popup("Starting PC", currentPcIndex, pcOptions);
        editedStartingPC = pcOptions[newPcIndex];

        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save character stats", GUILayout.Width(180)))
        {
            SaveCharacterStats(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEditableObjectStats(CardData card)
    {
        if (card == null) return;

        SyncEditableCardFields(card);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));

        editedObjectHidden = EditorGUILayout.Toggle(
            new GUIContent("Hidden", "Not revealed to its owner until found/granted."), editedObjectHidden);
        editedObjectTransferable = EditorGUILayout.Toggle(
            new GUIContent("Transferable", "Can be handed off between characters."), editedObjectTransferable);
        editedObjectCopies = Mathf.Max(1, EditorGUILayout.IntField(
            new GUIContent("Copies", "How many instances of this item seed the map's random hidden-object pool. Unique items should stay at 1."), editedObjectCopies));

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Combat Effects", EditorStyles.boldLabel);
        DrawEditableObjectCombatEffects(card);

        GUILayout.Space(4);
        EditorGUILayout.LabelField("Skill Bonuses", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("These only affect whether a character meets an action card's skill requirement — they do not affect duel or army combat.", MessageType.None);
        editedObjectCommanderBonus = EditorGUILayout.IntField("Commander", editedObjectCommanderBonus);
        editedObjectAgentBonus = EditorGUILayout.IntField("Agent", editedObjectAgentBonus);
        editedObjectEmmissaryBonus = EditorGUILayout.IntField("Emissary", editedObjectEmmissaryBonus);
        editedObjectMageBonus = EditorGUILayout.IntField("Mage", editedObjectMageBonus);

        GUILayout.Space(4);
        editedObjectRecruitBonusMenAtArms = EditorGUILayout.IntField("Recruit Bonus (Men-at-Arms)", editedObjectRecruitBonusMenAtArms);

        GUILayout.Space(4);
        EditorGUILayout.LabelField("Utility", EditorStyles.boldLabel);
        editedObjectHealPerTurn = EditorGUILayout.IntField("Heal Per Turn", editedObjectHealPerTurn);
        editedObjectMovementBonus = EditorGUILayout.IntField("Movement Bonus", editedObjectMovementBonus);
        editedObjectIgnoreTerrainMovementPenalty = EditorGUILayout.Toggle("Ignore Terrain Movement Penalty", editedObjectIgnoreTerrainMovementPenalty);
        editedObjectGrantsHasteAtSea = EditorGUILayout.Toggle("Grants Haste At Sea", editedObjectGrantsHasteAtSea);
        editedObjectGrantsEnvironmentalImmunity = EditorGUILayout.Toggle("Grants Environmental Immunity", editedObjectGrantsEnvironmentalImmunity);
        editedObjectAutoScoutRadius = EditorGUILayout.IntField("Auto-Scout Radius", editedObjectAutoScoutRadius);
        editedObjectDetectionEvasion = EditorGUILayout.IntField("Detection Evasion", editedObjectDetectionEvasion);
        editedObjectScryAreaBonus = EditorGUILayout.IntField("Scry Area Bonus", editedObjectScryAreaBonus);
        editedObjectScryObjectBonus = EditorGUILayout.IntField(
            new GUIContent("Scry Object Bonus", "Also reduces Find Object action difficulty."), editedObjectScryObjectBonus);

        GUILayout.Space(4);
        EditorGUILayout.LabelField("Status Effects", EditorStyles.boldLabel);
        editedObjectNegativeStatusImmunity = EditorGUILayout.TextField(
            new GUIContent("Negative Status Immunity", "Name of a StatusEffectEnum value, e.g. Poisoned"), editedObjectNegativeStatusImmunity);
        editedObjectNegativeStatusDurationReduction = EditorGUILayout.IntField("Negative Status Duration Reduction", editedObjectNegativeStatusDurationReduction);
        editedObjectNegativeStatusDamageReduction = EditorGUILayout.IntField("Negative Status Damage Reduction", editedObjectNegativeStatusDamageReduction);
        editedObjectPositiveStatusDurationBonus = EditorGUILayout.IntField("Positive Status Duration Bonus", editedObjectPositiveStatusDurationBonus);
        editedObjectPositiveStatusEffectBonus = EditorGUILayout.IntField("Positive Status Effect Bonus", editedObjectPositiveStatusEffectBonus);

        GUILayout.Space(4);
        editedObjectPassiveEffectId = EditorGUILayout.TextField(
            new GUIContent("Passive Effect Id", "Matches a passive-effect hook, e.g. HexEnemyFearAndDespairChancePerTurn"), editedObjectPassiveEffectId);
        editedObjectPassiveEffectValue = EditorGUILayout.IntField("Passive Effect Value", editedObjectPassiveEffectValue);

        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save object effects", GUILayout.Width(180)))
        {
            SaveObjectStats(card);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    // Combat effects (attack/defense/vs-race/vs-troop-type/army bonuses) are the only Object
    // fields that feed Duel.cs/Army.cs math, so — unlike the free IntFields above — every part
    // of an entry here is a closed dropdown: effect type, race/troop-type target, and magnitude
    // are all enums (ObjectCombatEffect.cs). Nothing here can be free-typed into an unbalanced
    // number. Mutates card.combatEffects directly and saves immediately, mirroring
    // DrawEditableArmyAbilities' pattern for card.specialAbilities.
    private void DrawEditableObjectCombatEffects(CardData card)
    {
        if (card == null) return;

        card.combatEffects ??= new List<ObjectCombatEffect>();
        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));

        bool changed = false;
        int toRemove = -1;
        for (int i = 0; i < card.combatEffects.Count; i++)
        {
            ObjectCombatEffect effect = card.combatEffects[i];
            if (effect == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var newType = (ObjectCombatEffectTypeEnum)EditorGUILayout.EnumPopup(effect.type, GUILayout.MinWidth(160));
            if (newType != effect.type) { effect.type = newType; changed = true; }
            if (GUILayout.Button("x", GUILayout.Width(22))) toRemove = i;
            EditorGUILayout.EndHorizontal();

            if (RequiresRaceTarget(effect.type))
            {
                var newRace = (RacesEnum)EditorGUILayout.EnumPopup("Target Race", effect.targetRace);
                if (newRace != effect.targetRace) { effect.targetRace = newRace; changed = true; }
            }
            else if (RequiresTroopTypeTarget(effect.type))
            {
                var newTroopType = (TroopsTypeEnum)EditorGUILayout.EnumPopup("Target Troop Type", effect.targetTroopType);
                if (newTroopType != effect.targetTroopType) { effect.targetTroopType = newTroopType; changed = true; }
            }

            var newMagnitude = (ObjectCombatEffectMagnitudeEnum)EditorGUILayout.EnumPopup("Magnitude", effect.magnitude);
            if (newMagnitude != effect.magnitude) { effect.magnitude = newMagnitude; changed = true; }

            EditorGUILayout.EndVertical();
        }

        if (toRemove >= 0)
        {
            card.combatEffects.RemoveAt(toRemove);
            changed = true;
        }

        if (GUILayout.Button("Add Combat Effect"))
        {
            card.combatEffects.Add(new ObjectCombatEffect());
            changed = true;
        }

        if (changed) SaveObjectCombatEffects(card);
        EditorGUI.EndDisabledGroup();
    }

    private static bool RequiresRaceTarget(ObjectCombatEffectTypeEnum type) =>
        type is ObjectCombatEffectTypeEnum.AttackBonusVsRace or ObjectCombatEffectTypeEnum.DefenseBonusVsRace;

    private static bool RequiresTroopTypeTarget(ObjectCombatEffectTypeEnum type) =>
        type is ObjectCombatEffectTypeEnum.AttackBonusVsTroopType or ObjectCombatEffectTypeEnum.DefenseBonusVsTroopType;

    private void SaveObjectCombatEffects(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.combatEffects = card.combatEffects != null
            ? card.combatEffects.Select(e => e == null ? null : new ObjectCombatEffect
            {
                type = e.type,
                targetRace = e.targetRace,
                targetTroopType = e.targetTroopType,
                magnitude = e.magnitude
            }).Where(e => e != null).ToList()
            : new List<ObjectCombatEffect>();

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving combat effects.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
    }

    private void DrawRawJsonEditor(CardData card)
    {
        if (card == null) return;

        EditorGUILayout.HelpBox(
            "Edits every serialized field of the card, including nested data (encounter options, flee option, inspire effect). Load the card JSON, edit it, then apply.",
            MessageType.Info);

        EditorGUI.BeginDisabledGroup(IsCardDisabled(card));
        if (GUILayout.Button("Load card JSON", GUILayout.Width(130)))
        {
            editedRawJson = JsonUtility.ToJson(card, true);
            GUI.FocusControl(null);
        }

        if (!string.IsNullOrWhiteSpace(editedRawJson))
        {
            GUIStyle jsonStyle = new(EditorStyles.textArea) { wordWrap = false };
            editedRawJson = EditorGUILayout.TextArea(editedRawJson, jsonStyle, GUILayout.MinHeight(220));

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Discard", GUILayout.Width(80)))
            {
                editedRawJson = string.Empty;
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("Apply && save JSON", GUILayout.Width(150)))
            {
                SaveRawJson(card);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.EndDisabledGroup();
    }

    private List<string> GetAvailablePcNamesForCurrentDeck()
    {
        var names = new List<string> { string.Empty };
        DeckEntryView current = GetSelectedDeckView();
        if (current == null) return names;

        CollectPcNamesFromDeckView(current, names);

        string parentId = current.manifest?.parentDeckId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            DeckEntryView parent = deckViews.FirstOrDefault(d =>
                string.Equals(d.manifest?.deckId, parentId, StringComparison.OrdinalIgnoreCase));
            if (parent == null) break;
            CollectPcNamesFromDeckView(parent, names);
            parentId = parent.manifest?.parentDeckId;
        }

        return names;
    }

    private static void CollectPcNamesFromDeckView(DeckEntryView deckView, List<string> names)
    {
        if (deckView?.deckData?.cards == null) return;
        foreach (CardData c in deckView.deckData.cards)
        {
            if (c?.GetCardType() == CardTypeEnum.PC && !string.IsNullOrWhiteSpace(c.name) && !names.Contains(c.name))
                names.Add(c.name);
        }
    }

    private void SyncEditableCardFields(CardData card)
    {
        string key = GetEditableRequirementsKey(card);
        if (string.Equals(editedCardKey, key, StringComparison.Ordinal)) return;

        editedCardKey = key;
        editedCommanderSkillRequired = Mathf.Max(0, card.commanderSkillRequired);
        editedAgentSkillRequired = Mathf.Max(0, card.agentSkillRequired);
        editedEmissarySkillRequired = Mathf.Max(0, card.emissarySkillRequired);
        editedMageSkillRequired = Mathf.Max(0, card.mageSkillRequired);
        editedLeatherRequired = Mathf.Max(0, card.leatherRequired);
        editedTimberRequired = Mathf.Max(0, card.timberRequired);
        editedMountsRequired = Mathf.Max(0, card.mountsRequired);
        editedIronRequired = Mathf.Max(0, card.ironRequired);
        editedSteelRequired = Mathf.Max(0, card.steelRequired);
        editedMithrilRequired = Mathf.Max(0, card.mithrilRequired);
        editedGoldRequired = Mathf.Max(0, card.goldRequired);
        editedJokerRequired = Mathf.Max(0, card.jokerRequired);
        editedLeatherGranted = Mathf.Max(0, card.leatherGranted);
        editedTimberGranted = Mathf.Max(0, card.timberGranted);
        editedMountsGranted = Mathf.Max(0, card.mountsGranted);
        editedIronGranted = Mathf.Max(0, card.ironGranted);
        editedSteelGranted = Mathf.Max(0, card.steelGranted);
        editedMithrilGranted = Mathf.Max(0, card.mithrilGranted);
        editedGoldGranted = Mathf.Max(0, card.goldGranted);
        editedTroopType = card.troopType;
        editedProcChance = Mathf.Clamp(card.procChance <= 0 ? 100 : card.procChance, 1, 100);
        editedCharacterCommander = Mathf.Max(0, card.commander);
        editedCharacterAgent = Mathf.Max(0, card.agent);
        editedCharacterEmissary = Mathf.Max(0, card.emmissary);
        editedCharacterMage = Mathf.Max(0, card.mage);
        editedStartingPC = card.startingPC ?? string.Empty;
        editedCardType = card.GetCardType();
        editedSituation = card.GetSituation();
        editedActionEffect = card.actionEffect ?? string.Empty;
        editedIsUnderground = card.isUnderground;
        editedName = card.name ?? string.Empty;
        editedQuote = card.quote ?? string.Empty;
        editedDescription = card.description ?? string.Empty;
        editedRequirementsText = card.requirementsText ?? string.Empty;
        editedHistoryText = card.historyText ?? string.Empty;
        editedRegion = card.region ?? string.Empty;
        editedTags = card.tags != null ? string.Join(", ", card.tags) : string.Empty;
        editedArtifacts = card.artifacts != null ? string.Join(", ", card.artifacts) : string.Empty;
        editedActionRef = card.GetActionRef() ?? string.Empty;
        editedSpriteName = card.spriteName ?? string.Empty;
        editedPortraitName = card.portraitName ?? string.Empty;
        editedDeckSpriteName = card.deckSpriteName ?? string.Empty;
        editedCharacterGroup = card.characterGroup ?? string.Empty;
        editedStatusEffect = card.statusEffect ?? string.Empty;
        editedDifficulty = card.difficulty;
        editedAlignment = card.alignment;
        editedRace = card.race;
        editedRawJson = string.Empty;
        editedObjectHidden = card.hidden;
        editedObjectTransferable = card.transferable;
        editedObjectCopies = Mathf.Max(1, card.copies);
        editedObjectCommanderBonus = card.commanderBonus;
        editedObjectAgentBonus = card.agentBonus;
        editedObjectEmmissaryBonus = card.emmissaryBonus;
        editedObjectMageBonus = card.mageBonus;
        editedObjectPassiveEffectId = card.passiveEffectId ?? string.Empty;
        editedObjectPassiveEffectValue = card.passiveEffectValue;
        editedObjectHealPerTurn = card.healPerTurn;
        editedObjectMovementBonus = card.movementBonus;
        editedObjectIgnoreTerrainMovementPenalty = card.ignoreTerrainMovementPenalty;
        editedObjectGrantsHasteAtSea = card.grantsHasteAtSea;
        editedObjectGrantsEnvironmentalImmunity = card.grantsEnvironmentalImmunity;
        editedObjectAutoScoutRadius = card.autoScoutRadius;
        editedObjectDetectionEvasion = card.detectionEvasion;
        editedObjectRecruitBonusMenAtArms = card.recruitBonusMenAtArms;
        editedObjectScryAreaBonus = card.scryAreaBonus;
        editedObjectScryObjectBonus = card.scryObjectBonus;
        editedObjectNegativeStatusImmunity = card.negativeStatusImmunity ?? string.Empty;
        editedObjectNegativeStatusDurationReduction = card.negativeStatusDurationReduction;
        editedObjectNegativeStatusDamageReduction = card.negativeStatusDamageReduction;
        editedObjectPositiveStatusDurationBonus = card.positiveStatusDurationBonus;
        editedObjectPositiveStatusEffectBonus = card.positiveStatusEffectBonus;
    }

    private static string GetEditableRequirementsKey(CardData card)
    {
        if (card == null) return string.Empty;
        string deckId = string.IsNullOrWhiteSpace(card.deckId) ? "unknownDeck" : card.deckId.Trim();
        string cardName = string.IsNullOrWhiteSpace(card.name) ? "unknownCard" : card.name.Trim();
        return $"{deckId}:{card.cardId}:{cardName}";
    }


    private void SaveNewRequirements(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.commanderSkillRequired = Mathf.Max(0, editedCommanderSkillRequired);
        target.agentSkillRequired = Mathf.Max(0, editedAgentSkillRequired);
        target.emissarySkillRequired = Mathf.Max(0, editedEmissarySkillRequired);
        target.mageSkillRequired = Mathf.Max(0, editedMageSkillRequired);
        target.leatherRequired = Mathf.Max(0, editedLeatherRequired);
        target.timberRequired = Mathf.Max(0, editedTimberRequired);
        target.mountsRequired = Mathf.Max(0, editedMountsRequired);
        target.ironRequired = Mathf.Max(0, editedIronRequired);
        target.steelRequired = Mathf.Max(0, editedSteelRequired);
        target.mithrilRequired = Mathf.Max(0, editedMithrilRequired);
        target.goldRequired = Mathf.Max(0, editedGoldRequired);
        target.jokerRequired = Mathf.Max(0, editedJokerRequired);

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving requirements.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved new requirements for '{card.name}'.");
    }

    private void SaveNewGrants(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.leatherGranted = Mathf.Max(0, editedLeatherGranted);
        target.timberGranted = Mathf.Max(0, editedTimberGranted);
        target.mountsGranted = Mathf.Max(0, editedMountsGranted);
        target.ironGranted = Mathf.Max(0, editedIronGranted);
        target.steelGranted = Mathf.Max(0, editedSteelGranted);
        target.mithrilGranted = Mathf.Max(0, editedMithrilGranted);
        target.goldGranted = Mathf.Max(0, editedGoldGranted);
        if (target.GetCardType() == CardTypeEnum.PC) target.isUnderground = editedIsUnderground;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving grants.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved new grants for '{card.name}'.");
    }

    private void SaveArmyAbilities(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.specialAbilities = card.specialAbilities != null
            ? new List<ArmySpecialAbilityEnum>(card.specialAbilities)
            : new List<ArmySpecialAbilityEnum>();
        target.procChance = target.specialAbilities.Count > 0
            ? Mathf.Clamp(editedProcChance <= 0 ? 100 : editedProcChance, 1, 100)
            : 0;
        target.statusEffect = string.Empty;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving army abilities.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved army ability/proc chance for '{card.name}'.");
    }

    private void SaveCardFields(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null) return;

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId) ?? card;

        target.name = editedName?.Trim() ?? string.Empty;
        target.quote = editedQuote ?? string.Empty;
        target.actionEffect = editedActionEffect ?? string.Empty;
        target.description = editedDescription ?? string.Empty;
        target.requirementsText = editedRequirementsText ?? string.Empty;
        target.historyText = editedHistoryText ?? string.Empty;
        target.region = editedRegion?.Trim() ?? string.Empty;
        target.tags = SplitCommaList(editedTags);
        target.artifacts = SplitCommaList(editedArtifacts);
        target.spriteName = editedSpriteName?.Trim() ?? string.Empty;
        target.portraitName = editedPortraitName?.Trim() ?? string.Empty;
        target.deckSpriteName = editedDeckSpriteName?.Trim() ?? string.Empty;
        target.characterGroup = editedCharacterGroup?.Trim() ?? string.Empty;
        target.statusEffect = editedStatusEffect?.Trim() ?? string.Empty;
        target.race = editedRace;
        target.alignment = editedAlignment;
        target.difficulty = Mathf.Max(0, editedDifficulty);

        // Write the new action ref back to whichever field GetActionRef() reads from.
        string newActionRef = editedActionRef?.Trim() ?? string.Empty;
        if (!string.Equals(newActionRef, target.GetActionRef() ?? string.Empty, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(target.action)) target.action = newActionRef;
            else target.actionClassName = newActionRef;
        }

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving card fields.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        editedCardKey = null;
        spriteCache.Clear();
        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved card fields for '{target.name}'.");
    }

    private void SaveRawJson(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null || string.IsNullOrWhiteSpace(editedRawJson)) return;

        CardData parsed;
        try
        {
            parsed = JsonUtility.FromJson<CardData>(editedRawJson);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Invalid JSON", $"Could not parse card JSON:\n{ex.Message}", "OK");
            return;
        }

        if (parsed == null)
        {
            EditorUtility.DisplayDialog("Invalid JSON", "Could not parse card JSON.", "OK");
            return;
        }

        if (parsed.cardId <= 0) parsed.cardId = card.cardId;
        if (string.IsNullOrWhiteSpace(parsed.deckId)) parsed.deckId = card.deckId;

        int index = deckView.deckData.cards.FindIndex(c => c != null && c.cardId == card.cardId);
        if (index < 0)
        {
            Debug.LogWarning($"DeckExplorerWindow: could not find card '{card.name}' in deck to apply raw JSON.");
            return;
        }
        deckView.deckData.cards[index] = parsed;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving raw JSON.");
            return;
        }

        File.WriteAllText(assetPath, JsonUtility.ToJson(deckView.deckData, true));
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        editedCardKey = null;
        editedRawJson = string.Empty;
        spriteCache.Clear();
        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: applied raw JSON for '{parsed.name}'.");
    }

    private static List<string> SplitCommaList(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
    }

    private void SaveCharacterStats(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.commander = Mathf.Max(0, editedCharacterCommander);
        target.agent = Mathf.Max(0, editedCharacterAgent);
        target.emmissary = Mathf.Max(0, editedCharacterEmissary);
        target.mage = Mathf.Max(0, editedCharacterMage);
        target.startingPC = editedStartingPC ?? string.Empty;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving character stats.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved character stats for '{card.name}'.");
    }

    private void SaveObjectStats(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.hidden = editedObjectHidden;
        target.transferable = editedObjectTransferable;
        target.copies = Mathf.Max(1, editedObjectCopies);
        target.commanderBonus = editedObjectCommanderBonus;
        target.agentBonus = editedObjectAgentBonus;
        target.emmissaryBonus = editedObjectEmmissaryBonus;
        target.mageBonus = editedObjectMageBonus;
        target.passiveEffectId = editedObjectPassiveEffectId ?? string.Empty;
        target.passiveEffectValue = editedObjectPassiveEffectValue;
        target.healPerTurn = editedObjectHealPerTurn;
        target.movementBonus = editedObjectMovementBonus;
        target.ignoreTerrainMovementPenalty = editedObjectIgnoreTerrainMovementPenalty;
        target.grantsHasteAtSea = editedObjectGrantsHasteAtSea;
        target.grantsEnvironmentalImmunity = editedObjectGrantsEnvironmentalImmunity;
        target.autoScoutRadius = editedObjectAutoScoutRadius;
        target.detectionEvasion = editedObjectDetectionEvasion;
        target.recruitBonusMenAtArms = editedObjectRecruitBonusMenAtArms;
        target.scryAreaBonus = editedObjectScryAreaBonus;
        target.scryObjectBonus = editedObjectScryObjectBonus;
        target.negativeStatusImmunity = editedObjectNegativeStatusImmunity ?? string.Empty;
        target.negativeStatusDurationReduction = editedObjectNegativeStatusDurationReduction;
        target.negativeStatusDamageReduction = editedObjectNegativeStatusDamageReduction;
        target.positiveStatusDurationBonus = editedObjectPositiveStatusDurationBonus;
        target.positiveStatusEffectBonus = editedObjectPositiveStatusEffectBonus;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving object effects.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved object effects for '{card.name}'.");
    }

    private void SaveArmyType(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId);
        if (target == null)
        {
            target = card;
        }

        target.troopType = editedTroopType;

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving army type.");
            return;
        }

        string json = JsonUtility.ToJson(deckView.deckData, true);
        File.WriteAllText(assetPath, json);
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved army type for '{card.name}'.");
    }

    private void SaveCardType(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null) return;

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId) ?? card;
        target.type = editedCardType.ToString();

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving card type.");
            return;
        }

        File.WriteAllText(assetPath, JsonUtility.ToJson(deckView.deckData, true));
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved card type '{editedCardType}' for '{card.name}'.");
    }

    private void SaveSituation(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null) return;

        CardData target = deckView.deckData.cards.FirstOrDefault(c => c != null && c.cardId == card.cardId) ?? card;
        target.situation = editedSituation == CardSituationEnum.None ? string.Empty : editedSituation.ToString();

        string assetPath = GetDeckAssetPath(deckView.manifest?.resourcePath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for saving situation.");
            return;
        }

        File.WriteAllText(assetPath, JsonUtility.ToJson(deckView.deckData, true));
        AssetDatabase.ImportAsset(ToAssetPath(assetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        ReloadSelectedCard();
        EditorUtility.SetDirty(this);
        Debug.Log($"DeckExplorerWindow: saved situation '{editedSituation}' for '{card.name}'.");
    }

    private static string GetDeckAssetPath(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return string.Empty;
        string normalized = resourcePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "Resources", $"{normalized}.json"));
    }

    private static string ToAssetPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
        string dataPath = Path.GetFullPath(Application.dataPath);
        string normalizedFull = Path.GetFullPath(fullPath);
        if (!normalizedFull.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        string relative = normalizedFull.Substring(dataPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return $"Assets/{relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')}";
    }

    private void DrawActionDetails(CardData card)
    {
        string actionRef = card.GetActionRef();
        EditorGUILayout.LabelField("Action Ref", string.IsNullOrWhiteSpace(actionRef) ? "(none)" : actionRef);

        CharacterAction action = ResolveAction(actionRef, card);
        if (action == null)
        {
            EditorGUILayout.HelpBox("No action could be resolved for this card.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Resolved Type", action.GetType().Name);
        EditorGUILayout.LabelField("Action Name", action.actionName ?? string.Empty);
        EditorGUILayout.LabelField("Action Description", action.GetDescriptionForCard() ?? string.Empty, EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("Required Skills", BuildSkillSummary(card));
    }

    private void UpdateLivePreview(CardData card, Rect rect)
    {
        if (card == null)
        {
            EditorGUI.HelpBox(rect, "No card selected.", MessageType.Info);
            return;
        }

        EnsurePreviewObjects(rect);
        if (previewCardComponent == null || previewCamera == null || previewRenderTexture == null)
        {
            EditorGUI.HelpBox(rect, "Card preview could not be created.", MessageType.Warning);
            return;
        }

        previewCardObject.SetActive(true);
        previewCanvasRoot.SetActive(true);
        ApplyPreviewData(card);
        previewCardComponent.ShowRealCard();
        previewCardObject.transform.SetAsLastSibling();

        // Force TMP to compute mesh bounds immediately so ContentSizeFitter
        // gets correct preferred heights before the layout rebuild runs.
        // Without this, pivot-Y=0 containers collapse or overflow in the editor.
        foreach (TextMeshProUGUI tmp in previewCardObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            tmp.ForceMeshUpdate();
        }

        Canvas.ForceUpdateCanvases();
        RectTransform cardRect = previewCardObject.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);
        }

        previewCamera.backgroundColor = Color.black;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = 6f;
        previewCamera.aspect = PreviewCanvasW / PreviewCanvasH; // match fixed canvas ratio
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.targetTexture = previewRenderTexture;
        previewCamera.Render();
        previewCamera.targetTexture = null;

        // ScaleToFit: card shrinks to fit the pane without distortion.
        GUI.DrawTexture(rect, previewRenderTexture, ScaleMode.ScaleToFit, false);
    }

    private void ApplyPreviewData(CardData card)
    {
        if (previewCardComponent == null || card == null) return;

        SetPreviewText("titleText", FormatCardTitle(card.name));
        SetPreviewText("descriptionText", BuildRenderedDescription(card));
        SetPreviewText("requirementsText", BuildRequirementsText(card));

        TextMeshProUGUI requirementsMessage = GetPreviewField<TextMeshProUGUI>("requirementsMessage");
        if (requirementsMessage != null)
        {
            requirementsMessage.text = BuildPreviewRequirementsMessage(card);
            requirementsMessage.color = Color.red;
        }

        Image cardArtImage = GetPreviewField<Image>("cardArtImage");
        if (cardArtImage != null)
        {
            Sprite sprite = ResolveCardArtwork(card);
            cardArtImage.sprite = sprite;
            cardArtImage.enabled = sprite != null;
        }

        // Card.Show() would normally set this from card.deckSpriteName, which DeckManager
        // populates at load time from the containing deck's manifest entry
        // (card.deckSpriteName = entry.deckSpriteName). This window reads deck JSON files
        // directly and never runs that propagation step, so card.deckSpriteName is whatever
        // was raw-authored in the JSON - almost always blank, even for a card sitting right
        // there in the selected deck. Pull it from the selected deck's manifest instead, same
        // as DeckManager would for this card, and resolve it via AssetDatabase search since the
        // runtime Illustrations/Addressables singleton never loads in this isolated preview.
        Image deckTypeImage = GetPreviewField<Image>("deckTypeImage");
        if (deckTypeImage != null)
        {
            Sprite deckSprite = ResolveDeckTypeArtwork(GetSelectedDeckView());
            deckTypeImage.sprite = deckSprite;
            deckTypeImage.enabled = deckSprite != null;
        }

        Hover hover = GetPreviewField<Hover>("hover");
        if (hover != null)
        {
            hover.Initialize(FormatCardTypeLabel(card.GetCardType()));
        }
    }

    private void SetPreviewText(string fieldName, string text)
    {
        TextMeshProUGUI tmp = GetPreviewField<TextMeshProUGUI>(fieldName);
        if (tmp != null)
        {
            tmp.text = text ?? string.Empty;
            tmp.color = Color.white;
        }
    }

    private T GetPreviewField<T>(string fieldName) where T : class
    {
        if (previewCardComponent == null || string.IsNullOrWhiteSpace(fieldName)) return null;
        FieldInfo field = typeof(Card).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field != null ? field.GetValue(previewCardComponent) as T : null;
    }

    private static string BuildPreviewRequirementsMessage(CardData card)
    {
        if (card == null) return string.Empty;

        List<string> messages = new();

        AppendPreviewLevelMessage(messages, "Commander", card.commanderSkillRequired);
        AppendPreviewLevelMessage(messages, "Agent", card.agentSkillRequired);
        AppendPreviewLevelMessage(messages, "Emissary", card.emissarySkillRequired);
        AppendPreviewLevelMessage(messages, "Mage", card.mageSkillRequired);

        List<string> resourceParts = new();
        AppendPreviewResourcePart(resourceParts, "leather", card.leatherRequired);
        AppendPreviewResourcePart(resourceParts, "timber", card.timberRequired);
        AppendPreviewResourcePart(resourceParts, "mounts", card.mountsRequired);
        AppendPreviewResourcePart(resourceParts, "iron", card.ironRequired);
        AppendPreviewResourcePart(resourceParts, "steel", card.steelRequired);
        AppendPreviewResourcePart(resourceParts, "mithril", card.mithrilRequired);

        int goldCost = card.GetTotalGoldCost();
        if (goldCost > 0)
        {
            resourceParts.Add($"{goldCost}<sprite name=\"gold\">");
        }

        if (resourceParts.Count > 0)
        {
            messages.Add($"<sprite name=\"error\">Need {string.Join(string.Empty, resourceParts)}");
        }

        if (!string.IsNullOrWhiteSpace(card.GetActionRef()))
        {
            messages.Add("<sprite name=\"error\">Action conditions not met.");
        }

        if (messages.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", messages);
    }

    private static void AppendPreviewLevelMessage(List<string> messages, string label, int required)
    {
        if (messages == null || required <= 0) return;
        messages.Add($"<sprite name=\"error\">Need {label} {required}.");
    }

    private static void AppendPreviewResourcePart(List<string> parts, string resourceName, int required)
    {
        if (parts == null || required <= 0) return;
        parts.Add($"{required}<sprite name=\"{resourceName}\">");
    }

    private void EnsurePreviewObjects(Rect rect)
    {
        if (cardPrefabAsset == null)
        {
            cardPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Reusable/Card.prefab");
        }

        if (cardPrefabAsset == null) return;

        if (previewRoot == null)
        {
            previewRoot = new GameObject("DeckExplorerPreviewRoot") { hideFlags = HideFlags.HideAndDontSave };
            previewRoot.layer = 5;
            previewCamera = previewRoot.AddComponent<Camera>();
            previewCamera.hideFlags = HideFlags.HideAndDontSave;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 6f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.black;
            previewCamera.cullingMask = 1 << 5;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 100f;
            previewCamera.transform.position = new Vector3(0f, 0f, -10f);
            previewCamera.transform.rotation = Quaternion.identity;
        }

        if (previewCanvasRoot == null)
        {
            // WorldSpace canvas at a fixed card-sized rect so the preview is
            // independent of editor-pane dimensions.  No CanvasScaler needed.
            previewCanvasRoot = new GameObject("DeckExplorerPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            previewCanvasRoot.hideFlags = HideFlags.HideAndDontSave;
            previewCanvasRoot.layer = 5;
            previewCanvasRoot.transform.SetParent(previewRoot.transform, false);

            Canvas canvas = previewCanvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Scale canvas so its full height fills the camera's ortho view
            // (orthoSize = 6 → 12 world-unit span).
            float worldScale = previewCamera.orthographicSize * 2f / PreviewCanvasH;
            RectTransform canvasRect = previewCanvasRoot.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(PreviewCanvasW, PreviewCanvasH);
            canvasRect.localPosition = new Vector3(0f, 0f, 10f); // 10 units in front of camera
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * worldScale;
        }

        if (previewCardObject == null)
        {
            previewCardObject = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefabAsset);
            if (previewCardObject == null) return;

            previewCardObject.hideFlags = HideFlags.HideAndDontSave;
            previewCardObject.layer = 5;
            previewCardObject.transform.SetParent(previewCanvasRoot.transform, false);
            previewCardObject.SetActive(true);

            RectTransform cardRect = previewCardObject.GetComponent<RectTransform>();
            if (cardRect != null)
            {
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.sizeDelta = new Vector2(PreviewCardW, PreviewCardH);
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.localScale = Vector3.one;
            }

            SetHideFlagsRecursive(previewCardObject.transform);
            SetLayerRecursive(previewCardObject.transform, 5);
            previewCardComponent = previewCardObject.GetComponent<Card>();
        }

        ConfigurePreviewTextMeshPro();

        EnsureRenderTexture();
    }

    private void ConfigurePreviewTextMeshPro()
    {
        TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
        TMP_SpriteAsset defaultSpriteAsset = TMP_Settings.defaultSpriteAsset;
        if (previewCardComponent == null) return;

        ConfigurePreviewTextField("titleText", defaultFont, defaultSpriteAsset);
        ConfigurePreviewTextField("descriptionText", defaultFont, defaultSpriteAsset);
        ConfigurePreviewTextField("requirementsText", defaultFont, defaultSpriteAsset);
        ConfigurePreviewTextField("requirementsMessage", defaultFont, defaultSpriteAsset);
    }

    private void ConfigurePreviewTextField(string fieldName, TMP_FontAsset defaultFont, TMP_SpriteAsset defaultSpriteAsset)
    {
        TextMeshProUGUI tmp = GetPreviewField<TextMeshProUGUI>(fieldName);
        if (tmp == null) return;

        if (tmp.font == null && defaultFont != null)
        {
            tmp.font = defaultFont;
        }

        if (tmp.spriteAsset == null && defaultSpriteAsset != null)
        {
            tmp.spriteAsset = defaultSpriteAsset;
        }

        tmp.richText = true;
    }

    private void EnsureRenderTexture()
    {
        int width  = Mathf.RoundToInt(PreviewCanvasW);
        int height = Mathf.RoundToInt(PreviewCanvasH);
        if (previewRenderTexture != null
            && previewRenderTexture.width  == width
            && previewRenderTexture.height == height)
            return;

        if (previewRenderTexture != null)
        {
            previewRenderTexture.Release();
            DestroyImmediate(previewRenderTexture);
        }

        previewRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static void SetHideFlagsRecursive(Transform root)
    {
        if (root == null) return;
        root.gameObject.hideFlags = HideFlags.HideAndDontSave;
        for (int i = 0; i < root.childCount; i++)
        {
            SetHideFlagsRecursive(root.GetChild(i));
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursive(root.GetChild(i), layer);
        }
    }

    private void ClearPreviewInstance()
    {
        if (previewCardObject != null)
        {
            DestroyImmediate(previewCardObject);
            previewCardObject = null;
        }

        if (previewCanvasRoot != null)
        {
            DestroyImmediate(previewCanvasRoot);
            previewCanvasRoot = null;
        }

        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
            previewCamera = null;
        }

        if (previewRenderTexture != null)
        {
            previewRenderTexture.Release();
            DestroyImmediate(previewRenderTexture);
            previewRenderTexture = null;
        }

        previewCardComponent = null;
        cardPrefabAsset = null;
    }

    private string BuildDeckLabel(DeckEntryView view)
    {
        if (view == null || view.manifest == null) return string.Empty;

        string deckId = string.IsNullOrWhiteSpace(view.manifest.deckId) ? "(no id)" : view.manifest.deckId;
        string nation = string.IsNullOrWhiteSpace(view.manifest.nation) ? "(no nation)" : view.manifest.nation;
        string parent = string.IsNullOrWhiteSpace(view.manifest.parentDeckId) ? string.Empty : $" <- {view.manifest.parentDeckId}";
        string indent = new string(' ', Mathf.Max(0, view.depth) * 2);
        return $"{indent}{nation} / {deckId} ({view.manifest.cardCount} cards)";
        //return $"{indent}{nation} / {deckId}{parent} ({view.manifest.cardCount} cards)";
    }

    private void RefreshData()
    {
        manifestAsset = Resources.Load<TextAsset>("Cards");
        deckViews.Clear();
        filteredCards.Clear();
        selectedDeckIndex = Mathf.Clamp(selectedDeckIndex, 0, int.MaxValue);
        selectedCardIndex = 0;
        spriteCache.Clear();

        if (manifestAsset == null)
        {
            Repaint();
            return;
        }

        CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
        cardsManifest = manifest;
        if (manifest?.decks == null)
        {
            Repaint();
            return;
        }

        Dictionary<string, DeckEntryView> byId = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckManifestEntry entry in manifest.decks.Where(x => x != null))
        {
            DeckEntryView view = new()
            {
                manifest = entry,
                deckData = LoadDeckData(entry.resourcePath),
                depth = 0
            };

            if (view.deckData != null && view.deckData.cards != null && view.deckData.cards.Count > 0)
            {
                entry.cardCount = view.deckData.cards.Count;
            }

            deckViews.Add(view);
            if (!string.IsNullOrWhiteSpace(entry.deckId))
            {
                byId[entry.deckId] = view;
            }
        }

        foreach (DeckEntryView view in deckViews)
        {
            if (view == null || view.manifest == null) continue;
            int depth = 0;
            string parentId = view.manifest.parentDeckId;
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrWhiteSpace(parentId) && byId.TryGetValue(parentId, out DeckEntryView parent) && visited.Add(parentId))
            {
                depth++;
                parentId = parent?.manifest?.parentDeckId;
            }
            view.depth = depth;
        }

        ResolveCardReferences();
        EnsureReferenceCardsFinalized();
        selectedDeckIndex = deckViews.Count > 0 ? Mathf.Clamp(selectedDeckIndex, 0, deckViews.Count - 1) : 0;
        RebuildFilteredCards();
        Repaint();
    }

    private void ReloadSelectedCard()
    {
        DeckEntryView deckView = GetSelectedDeckView();
        CardData selectedCard = GetSelectedCard();
        if (deckView == null || deckView.manifest == null || selectedCard == null)
        {
            return;
        }

        int selectedCardId = selectedCard.cardId;
        string selectedCardName = selectedCard.name;
        string selectedActionRef = selectedCard.GetActionRef();

        DeckData reloadedDeck = LoadDeckData(deckView.manifest.resourcePath);
        if (reloadedDeck == null)
        {
            return;
        }

        deckView.deckData = reloadedDeck;
        if (deckView.manifest != null && reloadedDeck.cards != null)
        {
            deckView.manifest.cardCount = reloadedDeck.cards.Count;
        }

        ResolveCardReferences();
        RebuildFilteredCards();

        int matchIndex = FindCardIndexInFilteredCards(selectedCardId, selectedCardName, selectedActionRef);
        if (matchIndex >= 0)
        {
            selectedCardIndex = matchIndex;
        }

        Repaint();
    }

    private List<DeckEntryView> GetAllTransferTargets(DeckEntryView currentDeck)
    {
        return deckViews
            .Where(view =>
                view != null
                && view.manifest != null
                && view.deckData != null
                && !string.IsNullOrWhiteSpace(view.manifest.resourcePath)
                && (currentDeck == null || !string.Equals(view.manifest.resourcePath, currentDeck.manifest.resourcePath, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private int GetCopyTargetIndex(List<DeckEntryView> targets)
    {
        if (targets == null || targets.Count == 0) return 0;

        if (!string.IsNullOrWhiteSpace(copyTargetResourcePath))
        {
            int matchIndex = targets.FindIndex(view => view != null && view.manifest != null
                && string.Equals(view.manifest.resourcePath, copyTargetResourcePath, StringComparison.OrdinalIgnoreCase));
            if (matchIndex >= 0)
            {
                return matchIndex;
            }
        }

        copyTargetResourcePath = targets[0].manifest.resourcePath;
        return 0;
    }


    private void CopyFinalizedCardToSubdeck(CardData sourceCard, DeckEntryView sourceDeckView, DeckEntryView targetDeckView)
    {
        if (sourceCard == null || sourceDeckView == null || targetDeckView == null || targetDeckView.manifest == null || targetDeckView.deckData == null)
        {
            return;
        }

        if (!IsCardFinalized(sourceCard))
        {
            Debug.LogWarning($"DeckExplorerWindow: '{sourceCard.name}' must be finalized before copying.");
            return;
        }

        string targetDeckId = string.IsNullOrWhiteSpace(targetDeckView.manifest.deckId)
            ? string.Empty
            : targetDeckView.manifest.deckId.Trim();
        string sourceDeckId = ResolveSourceDeckId(sourceCard, sourceDeckView);
        int sourceCardId = IsReferenceCard(sourceCard)
            ? sourceCard.referenceCardId
            : sourceCard.cardId;

        CardData copiedCard = new CardData
        {
            cardId = GetNextCardId(targetDeckView.deckData.cards),
            deckId = targetDeckId,
            referenceDeckId = sourceDeckId,
            referenceCardId = sourceCardId
        };
        if (copiedCard == null)
        {
            return;
        }

        targetDeckView.deckData.cards ??= new List<CardData>();
        targetDeckView.deckData.cards.Add(copiedCard);
        targetDeckView.manifest.cardCount = targetDeckView.deckData.cards.Count;
        SetCardFinalized(copiedCard, true, targetDeckView);
        SetCardDisabled(copiedCard, true, targetDeckView);

        if (!SaveDeckData(targetDeckView, "copying"))
        {
            RefreshData();
            return;
        }

        if (cardsManifest != null)
        {
            SaveCardsManifest();
        }

        AssetDatabase.Refresh();

        copyTargetResourcePath = targetDeckView.manifest.resourcePath;
        RefreshData();
        SelectDeckByResourcePath(targetDeckView.manifest.resourcePath);
        selectedCardIndex = FindCardIndexInFilteredCards(copiedCard.cardId, copiedCard.name, copiedCard.GetActionRef());
        Repaint();

        Debug.Log($"DeckExplorerWindow: copied finalized card '{sourceCard.name}' to deck '{targetDeckView.manifest.deckId}'.");
    }

    private void MoveFinalizedCardToSubdeck(CardData sourceCard, DeckEntryView sourceDeckView, DeckEntryView targetDeckView)
    {
        if (sourceCard == null || sourceDeckView == null || targetDeckView == null || targetDeckView.manifest == null || targetDeckView.deckData == null)
        {
            return;
        }

        if (!IsCardFinalized(sourceCard))
        {
            Debug.LogWarning($"DeckExplorerWindow: '{sourceCard.name}' must be finalized before moving.");
            return;
        }

        string targetDeckId = string.IsNullOrWhiteSpace(targetDeckView.manifest.deckId)
            ? string.Empty
            : targetDeckView.manifest.deckId.Trim();

        if (!EditorUtility.DisplayDialog(
                "Move Card",
                $"Move '{sourceCard.name}' to {targetDeckId}? The card will be removed from the current deck.",
                "Move",
                "Cancel"))
        {
            return;
        }

        CardData movedCard;
        if (IsReferenceCard(sourceCard))
        {
            string sourceDeckId = ResolveSourceDeckId(sourceCard, sourceDeckView);
            int sourceCardId = sourceCard.referenceCardId;
            movedCard = new CardData
            {
                cardId = GetNextCardId(targetDeckView.deckData.cards),
                deckId = targetDeckId,
                referenceDeckId = sourceDeckId,
                referenceCardId = sourceCardId
            };
        }
        else
        {
            movedCard = CloneCard(sourceCard);
            if (movedCard == null)
            {
                Debug.LogWarning($"DeckExplorerWindow: Could not clone '{sourceCard.name}' for moving.");
                return;
            }
            movedCard.cardId = GetNextCardId(targetDeckView.deckData.cards);
            movedCard.deckId = targetDeckId;
            movedCard.referenceDeckId = null;
            movedCard.referenceCardId = 0;
        }

        targetDeckView.deckData.cards ??= new List<CardData>();
        targetDeckView.deckData.cards.Add(movedCard);
        targetDeckView.manifest.cardCount = targetDeckView.deckData.cards.Count;
        SetCardFinalized(movedCard, true, targetDeckView);
        SetCardDisabled(movedCard, false, targetDeckView);

        if (!SaveDeckData(targetDeckView, "moving"))
        {
            RefreshData();
            return;
        }

        int sourceCardIndex = sourceDeckView.deckData.cards.FindIndex(c => c != null && c.cardId == sourceCard.cardId);
        int removedCount = sourceDeckView.deckData.cards.RemoveAll(c => c != null && c.cardId == sourceCard.cardId);
        if (removedCount <= 0)
        {
            Debug.LogWarning($"DeckExplorerWindow: Could not find card '{sourceCard.name}' to move out of deck '{sourceDeckView.manifest.deckId}'.");
            targetDeckView.deckData.cards.RemoveAll(c => c != null && c.cardId == movedCard.cardId);
            targetDeckView.manifest.cardCount = targetDeckView.deckData.cards.Count;
            SaveDeckData(targetDeckView, "rolling back move");
            RefreshData();
            return;
        }

        sourceDeckView.manifest.cardCount = sourceDeckView.deckData.cards.Count;
        if (!SaveDeckData(sourceDeckView, "moving"))
        {
            Debug.LogWarning($"DeckExplorerWindow: Could not save the source deck after moving '{sourceCard.name}'. Rolling back.");
            if (sourceCardIndex < 0 || sourceCardIndex > sourceDeckView.deckData.cards.Count)
            {
                sourceDeckView.deckData.cards.Add(sourceCard);
            }
            else
            {
                sourceDeckView.deckData.cards.Insert(sourceCardIndex, sourceCard);
            }

            sourceDeckView.manifest.cardCount = sourceDeckView.deckData.cards.Count;
            targetDeckView.deckData.cards.RemoveAll(c => c != null && c.cardId == movedCard.cardId);
            targetDeckView.manifest.cardCount = targetDeckView.deckData.cards.Count;
            SaveDeckData(sourceDeckView, "rolling back move");
            SaveDeckData(targetDeckView, "rolling back move");
            RefreshData();
            return;
        }

        if (cardsManifest != null)
        {
            SaveCardsManifest();
        }

        AssetDatabase.Refresh();

        copyTargetResourcePath = targetDeckView.manifest.resourcePath;
        RefreshData();
        SelectDeckByResourcePath(targetDeckView.manifest.resourcePath);
        selectedCardIndex = FindCardIndexInFilteredCards(movedCard.cardId, movedCard.name, movedCard.GetActionRef());
        Repaint();

        Debug.Log($"DeckExplorerWindow: moved finalized card '{sourceCard.name}' to deck '{targetDeckView.manifest.deckId}'.");
    }

    private static bool SaveDeckData(DeckEntryView deckView, string actionVerb)
    {
        if (deckView?.manifest == null || deckView.deckData == null)
        {
            return false;
        }

        string deckAssetPath = GetDeckAssetPath(deckView.manifest.resourcePath);
        if (string.IsNullOrWhiteSpace(deckAssetPath))
        {
            Debug.LogWarning($"DeckExplorerWindow: could not resolve deck asset path while {actionVerb}.");
            return false;
        }

        File.WriteAllText(deckAssetPath, JsonUtility.ToJson(deckView.deckData, true));
        AssetDatabase.ImportAsset(ToAssetPath(deckAssetPath), ImportAssetOptions.ForceUpdate);
        return true;
    }

    private void EnsureReferenceCardsFinalized()
    {
        foreach (DeckEntryView view in deckViews)
        {
            if (view?.deckData?.cards == null) continue;

            foreach (CardData card in view.deckData.cards)
            {
                if (card == null || !IsReferenceCard(card)) continue;
                SetCardFinalized(card, true, view);
                SetCardDisabled(card, true, view);
            }
        }
    }

    private static string ResolveSourceDeckId(CardData sourceCard, DeckEntryView sourceDeckView)
    {
        if (sourceCard != null && !string.IsNullOrWhiteSpace(sourceCard.referenceDeckId))
        {
            return sourceCard.referenceDeckId.Trim();
        }

        if (sourceDeckView?.manifest != null && !string.IsNullOrWhiteSpace(sourceDeckView.manifest.deckId))
        {
            return sourceDeckView.manifest.deckId.Trim();
        }

        if (sourceDeckView?.deckData != null && !string.IsNullOrWhiteSpace(sourceDeckView.deckData.deckId))
        {
            return sourceDeckView.deckData.deckId.Trim();
        }

        return sourceCard?.deckId?.Trim() ?? string.Empty;
    }

    private static CardData CloneCard(CardData source)
    {
        if (source == null) return null;
        string json = JsonUtility.ToJson(source);
        return JsonUtility.FromJson<CardData>(json);
    }

    private void ResolveCardReferences()
    {
        Dictionary<string, CardData> cardIndex = BuildCardIndex();
        Dictionary<string, CardData> resolvedTemplates = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckEntryView view in deckViews)
        {
            if (view?.deckData?.cards == null || string.IsNullOrWhiteSpace(view.deckData.deckId)) continue;

            for (int i = 0; i < view.deckData.cards.Count; i++)
            {
                CardData card = view.deckData.cards[i];
                if (!IsReferenceCard(card)) continue;

                CardData template = ResolveReferencedTemplate(card.referenceDeckId, card.referenceCardId, cardIndex, resolvedTemplates, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (template == null)
                {
                    Debug.LogWarning($"DeckExplorerWindow: Could not resolve reference for card '{card?.name}' in deck '{view.deckData.deckId}' -> {card.referenceDeckId}:{card.referenceCardId}.");
                    continue;
                }

                CardData resolvedCard = CloneCard(template);
                resolvedCard.cardId = card.cardId;
                resolvedCard.deckId = view.deckData.deckId;
                resolvedCard.alignment = view.deckData.alignment;
                resolvedCard.referenceDeckId = card.referenceDeckId;
                resolvedCard.referenceCardId = card.referenceCardId;
                view.deckData.cards[i] = resolvedCard;
            }
        }
    }

    private Dictionary<string, CardData> BuildCardIndex()
    {
        Dictionary<string, CardData> cardIndex = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> parentDeckById = BuildParentDeckIndex();
        Dictionary<string, List<CardData>> cardsByDeckId = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckEntryView view in deckViews)
        {
            if (view?.deckData?.cards == null || string.IsNullOrWhiteSpace(view.deckData.deckId)) continue;

            string deckId = view.deckData.deckId.Trim();
            cardsByDeckId[deckId] = view.deckData.cards;

            foreach (CardData card in view.deckData.cards)
            {
                if (card == null) continue;
                cardIndex[BuildCardReferenceKey(deckId, card.cardId)] = card;
            }
        }

        foreach (string deckId in cardsByDeckId.Keys)
        {
            string currentDeckId = deckId;
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);

            while (parentDeckById.TryGetValue(currentDeckId, out string parentDeckId)
                && !string.IsNullOrWhiteSpace(parentDeckId)
                && visited.Add(parentDeckId))
            {
                currentDeckId = parentDeckId;
                if (!cardsByDeckId.TryGetValue(parentDeckId, out List<CardData> parentCards)) continue;

                foreach (CardData card in parentCards)
                {
                    if (card == null) continue;
                    string inheritedKey = BuildCardReferenceKey(deckId, card.cardId);
                    if (!cardIndex.ContainsKey(inheritedKey))
                    {
                        cardIndex[inheritedKey] = card;
                    }
                }
            }
        }

        return cardIndex;
    }

    private Dictionary<string, string> BuildParentDeckIndex()
    {
        Dictionary<string, string> parentDeckById = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckEntryView view in deckViews)
        {
            string deckId = view?.manifest?.deckId;
            if (string.IsNullOrWhiteSpace(deckId))
            {
                deckId = view?.deckData?.deckId;
            }

            if (string.IsNullOrWhiteSpace(deckId)) continue;
            parentDeckById[deckId.Trim()] = view?.manifest?.parentDeckId?.Trim() ?? string.Empty;
        }

        return parentDeckById;
    }

    private static CardData ResolveReferencedTemplate(
        string referenceDeckId,
        int referenceCardId,
        Dictionary<string, CardData> cardIndex,
        Dictionary<string, CardData> resolvedTemplates,
        HashSet<string> resolving)
    {
        if (string.IsNullOrWhiteSpace(referenceDeckId) || referenceCardId <= 0) return null;

        string referenceKey = BuildCardReferenceKey(referenceDeckId, referenceCardId);
        if (resolvedTemplates.TryGetValue(referenceKey, out CardData cached) && cached != null)
        {
            return cached;
        }

        if (!cardIndex.TryGetValue(referenceKey, out CardData sourceCard) || sourceCard == null)
        {
            return null;
        }

        if (!IsReferenceCard(sourceCard))
        {
            CardData directTemplate = CloneCard(sourceCard);
            resolvedTemplates[referenceKey] = directTemplate;
            return directTemplate;
        }

        if (!resolving.Add(referenceKey))
        {
            Debug.LogWarning($"DeckExplorerWindow: Circular card reference detected at {referenceKey}.");
            return null;
        }

        CardData nestedTemplate = ResolveReferencedTemplate(sourceCard.referenceDeckId, sourceCard.referenceCardId, cardIndex, resolvedTemplates, resolving);
        resolving.Remove(referenceKey);
        if (nestedTemplate == null) return null;

        CardData resolvedTemplate = CloneCard(nestedTemplate);
        resolvedTemplates[referenceKey] = resolvedTemplate;
        return resolvedTemplate;
    }

    private static string BuildCardReferenceKey(string deckId, int cardId)
    {
        return $"{deckId?.Trim().ToLowerInvariant()}::{cardId}";
    }

    private static int GetNextCardId(List<CardData> cards)
    {
        if (cards == null || cards.Count == 0) return 1;
        return cards.Where(c => c != null).Select(c => c.cardId).DefaultIfEmpty(0).Max() + 1;
    }

    private void SaveCardsManifest()
    {
        if (cardsManifest == null) return;

        string manifestAssetPath = Path.GetFullPath(Path.Combine(Application.dataPath, "Resources", "Cards.json"));
        File.WriteAllText(manifestAssetPath, JsonUtility.ToJson(cardsManifest, true));
        AssetDatabase.ImportAsset(ToAssetPath(manifestAssetPath), ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
    }

    private void RemoveCard(CardData card)
    {
        DeckEntryView deckView = GetSelectedDeckView();
        if (card == null || deckView?.deckData?.cards == null)
        {
            return;
        }

        string deckId = string.IsNullOrWhiteSpace(deckView.manifest.deckId) ? "this deck" : deckView.manifest.deckId;
        if (!EditorUtility.DisplayDialog("Remove Card", $"Remove '{card.name}' from {deckId}?", "Remove", "Cancel"))
        {
            return;
        }

        int removedCount = deckView.deckData.cards.RemoveAll(c => c != null && c.cardId == card.cardId);
        if (removedCount <= 0)
        {
            Debug.LogWarning($"DeckExplorerWindow: Could not find card '{card.name}' to remove from deck '{deckId}'.");
            return;
        }

        deckView.manifest.cardCount = deckView.deckData.cards.Count;
        string deckAssetPath = GetDeckAssetPath(deckView.manifest.resourcePath);
        if (string.IsNullOrWhiteSpace(deckAssetPath))
        {
            Debug.LogWarning("DeckExplorerWindow: could not resolve deck asset path for removal.");
            return;
        }

        File.WriteAllText(deckAssetPath, JsonUtility.ToJson(deckView.deckData, true));
        AssetDatabase.ImportAsset(ToAssetPath(deckAssetPath), ImportAssetOptions.ForceUpdate);
        SaveCardsManifest();
        AssetDatabase.Refresh();
        RefreshData();
    }

    private void SelectDeckByResourcePath(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return;

        for (int i = 0; i < deckViews.Count; i++)
        {
            DeckEntryView view = deckViews[i];
            if (view == null || view.manifest == null) continue;
            if (!string.Equals(view.manifest.resourcePath, resourcePath, StringComparison.OrdinalIgnoreCase)) continue;

            selectedDeckIndex = i;
            selectedCardIndex = 0;
            RebuildFilteredCards();
            return;
        }
    }

    private void SelectDeckByDeckId(string deckId)
    {
        if (string.IsNullOrWhiteSpace(deckId)) return;

        for (int i = 0; i < deckViews.Count; i++)
        {
            DeckEntryView view = deckViews[i];
            if (view == null || view.manifest == null) continue;
            if (!string.Equals(view.manifest.deckId, deckId, StringComparison.OrdinalIgnoreCase)) continue;

            selectedDeckIndex = i;
            selectedCardIndex = 0;
            RebuildFilteredCards();
            return;
        }
    }

    private bool GoToOriginalCard(CardData card)
    {
        if (!IsReferenceCard(card)) return false;

        string originalDeckId = card.referenceDeckId;
        int originalCardId = card.referenceCardId;
        if (string.IsNullOrWhiteSpace(originalDeckId) || originalCardId <= 0) return false;

        SelectDeckByDeckId(originalDeckId);
        if (GetSelectedDeckView() == null) return false;

        int matchIndex = FindCardIndexInFilteredCards(originalCardId, null, null);
        if (matchIndex < 0)
        {
            bool originalOnlyShowActions = onlyShowCardsWithActions;
            string originalSearchText = searchText;
            onlyShowCardsWithActions = false;
            searchText = string.Empty;
            RebuildFilteredCards();
            matchIndex = FindCardIndexInFilteredCards(originalCardId, null, null);

            if (matchIndex < 0)
            {
                onlyShowCardsWithActions = originalOnlyShowActions;
                searchText = originalSearchText;
                RebuildFilteredCards();
                return false;
            }
        }

        selectedCardIndex = matchIndex;
        Repaint();
        return true;
    }

    private static DeckData LoadDeckData(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        try
        {
            string assetPath = GetDeckAssetPath(resourcePath);
            if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
                return JsonUtility.FromJson<DeckData>(File.ReadAllText(assetPath));

            TextAsset deckAsset = Resources.Load<TextAsset>(resourcePath);
            if (deckAsset == null) return null;
            return JsonUtility.FromJson<DeckData>(deckAsset.text);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DeckExplorer] Failed to load deck '{resourcePath}': {ex.Message}");
            return null;
        }
    }

    private void RebuildFilteredCards()
    {
        filteredCards.Clear();

        DeckEntryView deckView = GetSelectedDeckView();
        if (deckView?.deckData?.cards == null) return;

        IEnumerable<CardData> cards = deckView.deckData.cards.Where(c => c != null);
        if (onlyShowCardsWithActions)
        {
            cards = cards.Where(c => !string.IsNullOrWhiteSpace(c.GetActionRef()));
        }

        string query = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            cards = cards.Where(card => MatchesSearch(card, query));
        }

        if (sortCardsByTypeThenName)
        {
            cards = cards
                .OrderBy(c => c.GetCardType().ToString())
                .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase);
        }

        filteredCards.AddRange(cards);
        if (filteredCards.Count == 0)
        {
            selectedCardIndex = 0;
        }
        else
        {
            selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, filteredCards.Count - 1);
        }
        Repaint();
    }

    private bool MatchesSearch(CardData card, string query)
    {
        if (card == null || string.IsNullOrWhiteSpace(query)) return true;

        StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        return (card.name != null && card.name.Contains(query, cmp))
            || (card.quote != null && card.quote.Contains(query, cmp))
            || (card.actionEffect != null && card.actionEffect.Contains(query, cmp))
            || (card.requirementsText != null && card.requirementsText.Contains(query, cmp))
            || (card.action != null && card.action.Contains(query, cmp))
            || (card.actionClassName != null && card.actionClassName.Contains(query, cmp))
            || (card.deckId != null && card.deckId.Contains(query, cmp))
            || (card.region != null && card.region.Contains(query, cmp))
            || (card.tags != null && card.tags.Any(tag => tag != null && tag.Contains(query, cmp)));
    }

    private DeckEntryView GetSelectedDeckView()
    {
        if (deckViews.Count == 0) return null;
        selectedDeckIndex = Mathf.Clamp(selectedDeckIndex, 0, deckViews.Count - 1);
        return deckViews[selectedDeckIndex];
    }

    private CardData GetSelectedCard()
    {
        if (filteredCards.Count == 0) return null;
        selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, filteredCards.Count - 1);
        return filteredCards[selectedCardIndex];
    }

    private static bool IsReferenceCard(CardData card)
    {
        return card != null
            && !string.IsNullOrWhiteSpace(card.referenceDeckId)
            && card.referenceCardId > 0;
    }

    private int FindCardIndexInFilteredCards(int cardId, string cardName, string actionRef)
    {
        if (filteredCards.Count == 0) return -1;

        for (int i = 0; i < filteredCards.Count; i++)
        {
            CardData candidate = filteredCards[i];
            if (candidate == null) continue;

            if (cardId > 0 && candidate.cardId == cardId)
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(cardName) && string.Equals(candidate.name, cardName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(actionRef) || string.Equals(candidate.GetActionRef(), actionRef, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private Sprite ResolveCardArtwork(CardData data)
    {
        if (data == null) return null;

        string[] candidates =
        {
            data.spriteName,
            data.portraitName,
            data.name,
            data.actionClassName,
            data.action
        };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (spriteCache.TryGetValue(candidate, out Sprite cached))
            {
                if (cached != null) return cached;
            }

            Sprite found = FindSprite(candidate);
            spriteCache[candidate] = found;
            if (found != null) return found;
        }

        return null;
    }

    private Sprite ResolveDeckTypeArtwork(DeckEntryView deckView)
    {
        string deckSpriteName = deckView?.manifest?.deckSpriteName;
        if (string.IsNullOrWhiteSpace(deckSpriteName)) return null;

        string candidate = deckSpriteName.Trim();
        if (spriteCache.TryGetValue(candidate, out Sprite cached)) return cached;

        Sprite found = FindSprite(candidate);
        spriteCache[candidate] = found;
        return found;
    }

    private Sprite FindSprite(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string[] searchRoots =
        {
            "Assets/Art/Cards",
            "Assets/Art/UI"
        };

        string normalizedTarget = Normalize(name);
        string[] guids = AssetDatabase.FindAssets($"{name} t:Sprite", searchRoots);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) continue;

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is not Sprite sprite) continue;
                if (SpriteNameMatches(sprite.name, name, normalizedTarget))
                {
                    return sprite;
                }
            }
        }

        return null;
    }

    private static bool SpriteNameMatches(string spriteName, string name, string normalizedTarget)
    {
        if (string.Equals(spriteName, name, StringComparison.OrdinalIgnoreCase)) return true;
        if (Normalize(spriteName) == normalizedTarget) return true;

        // Single-sprite imports whose internal name follows the "<name>_<index>" convention
        // (e.g. sliced from a shared spritesheet, or just authored that way) carry a trailing
        // "_<digits>" suffix that the plain/normalized checks above don't account for. Mirrors
        // Illustrations.StripSubSpriteSuffix so this editor-only search stays as tolerant of
        // that convention as the runtime Addressables lookup already is.
        string stripped = StripTrailingIndexSuffix(spriteName);
        return string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase) || Normalize(stripped) == normalizedTarget;
    }

    private static string StripTrailingIndexSuffix(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return rawName;

        int underscoreIndex = rawName.LastIndexOf('_');
        if (underscoreIndex < 0 || underscoreIndex == rawName.Length - 1) return rawName;

        for (int i = underscoreIndex + 1; i < rawName.Length; i++)
        {
            if (!char.IsDigit(rawName[i])) return rawName;
        }

        return rawName.Substring(0, underscoreIndex);
    }

    private static CharacterAction ResolveAction(string actionRef, CardData card)
    {
        if (string.IsNullOrWhiteSpace(actionRef)) return null;

        Type resolved = ResolveActionType(actionRef);
        if (resolved == null || !typeof(CharacterAction).IsAssignableFrom(resolved)) return null;

        try
        {
            CharacterAction action = Activator.CreateInstance(resolved) as CharacterAction;
            if (action == null) return null;
            action.Initialize(null, card);
            return action;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"DeckExplorerWindow: failed to instantiate {actionRef}: {e.Message}");
            return null;
        }
    }

    private static Type ResolveActionType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;

        Type direct = Type.GetType(className, false, true);
        if (direct != null) return direct;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
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

            candidate = types.FirstOrDefault(t => string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase));
            if (candidate != null) return candidate;
        }

        return null;
    }

    private string BuildRenderedDescription(CardData data)
    {
        if (data == null) return string.Empty;

        if (data.GetCardType() == CardTypeEnum.Encounter)
        {
            string encounterDesc = string.IsNullOrWhiteSpace(data.description) ? string.Empty : data.description.Trim();
            return PrefixWithCardType(FormatCardTypeLabel(CardTypeEnum.Encounter), encounterDesc);
        }

        string body = data.GetRenderedDescription(true);
        if (!string.IsNullOrWhiteSpace(body))
        {
            return PrefixWithCardType(FormatCardTypeLabel(data.GetCardType()), body);
        }

        string actionRef = data.GetActionRef();
        CharacterAction action = ResolveAction(actionRef, data);
        return action != null ? PrefixWithCardType(FormatCardTypeLabel(data.GetCardType()), action.GetDescriptionForCard()) : string.Empty;
    }



    private string BuildRequirementsText(CardData data)
    {
        if (data == null) return string.Empty;
        List<string> reqs = new();

        AppendRequirement(reqs, "commander", data.commanderSkillRequired);
        AppendRequirement(reqs, "agent", data.agentSkillRequired);
        AppendRequirement(reqs, "emmissary", data.emissarySkillRequired);
        AppendRequirement(reqs, "mage", data.mageSkillRequired);

        int totalGold = data.GetTotalGoldCost();
        AppendRequirement(reqs, "gold", totalGold);

        AppendRequirement(reqs, "leather", data.leatherRequired);
        AppendRequirement(reqs, "timber", data.timberRequired);
        AppendRequirement(reqs, "mounts", data.mountsRequired);
        AppendRequirement(reqs, "iron", data.ironRequired);
        AppendRequirement(reqs, "steel", data.steelRequired);
        AppendRequirement(reqs, "mithril", data.mithrilRequired);

        return reqs.Count == 0 ? string.Empty : string.Join(" ", reqs);
    }

    private string BuildSkillSummary(CardData data)
    {
        if (data == null) return string.Empty;
        List<string> reqs = new();
        if (data.commanderSkillRequired > 0) reqs.Add($"Commander {data.commanderSkillRequired}");
        if (data.agentSkillRequired > 0) reqs.Add($"Agent {data.agentSkillRequired}");
        if (data.emissarySkillRequired > 0) reqs.Add($"Emissary {data.emissarySkillRequired}");
        if (data.mageSkillRequired > 0) reqs.Add($"Mage {data.mageSkillRequired}");
        return reqs.Count == 0 ? "None" : string.Join(", ", reqs);
    }

    private string BuildCostSummary(CardData data)
    {
        if (data == null) return string.Empty;
        List<string> parts = new();
        if (data.goldRequired > 0) parts.Add($"gold {data.goldRequired}");
        if (data.leatherRequired > 0) parts.Add($"leather {data.leatherRequired}");
        if (data.timberRequired > 0) parts.Add($"timber {data.timberRequired}");
        if (data.mountsRequired > 0) parts.Add($"mounts {data.mountsRequired}");
        if (data.ironRequired > 0) parts.Add($"iron {data.ironRequired}");
        if (data.steelRequired > 0) parts.Add($"steel {data.steelRequired}");
        if (data.mithrilRequired > 0) parts.Add($"mithril {data.mithrilRequired}");
        return parts.Count == 0 ? "None" : string.Join(", ", parts);
    }

    private string BuildGrantSummary(CardData data)
    {
        if (data == null) return string.Empty;
        List<string> parts = new();
        if (data.leatherGranted > 0) parts.Add($"leather +{data.leatherGranted}");
        if (data.timberGranted > 0) parts.Add($"timber +{data.timberGranted}");
        if (data.mountsGranted > 0) parts.Add($"mounts +{data.mountsGranted}");
        if (data.ironGranted > 0) parts.Add($"iron +{data.ironGranted}");
        if (data.steelGranted > 0) parts.Add($"steel +{data.steelGranted}");
        if (data.mithrilGranted > 0) parts.Add($"mithril +{data.mithrilGranted}");
        if (data.goldGranted > 0) parts.Add($"gold +{data.goldGranted}");
        return parts.Count == 0 ? "None" : string.Join(", ", parts);
    }

    private string BuildRawSummary(CardData card)
    {
        if (card == null) return string.Empty;

        StringBuilder sb = new();
        sb.AppendLine($"cardId: {card.cardId}");
        sb.AppendLine($"name: {card.name}");
        sb.AppendLine($"type: {card.type}");
        sb.AppendLine($"action: {card.GetActionRef()}");
        sb.AppendLine($"spriteName: {card.spriteName}");
        sb.AppendLine($"portraitName: {card.portraitName}");
        sb.AppendLine($"region: {card.region}");
        sb.AppendLine($"requirementsText: {card.requirementsText}");
        sb.AppendLine($"quote: {card.quote}");
        sb.AppendLine($"actionEffect: {card.actionEffect}");
        sb.AppendLine($"historyText: {card.historyText}");
        sb.AppendLine($"tags: {(card.tags != null ? string.Join(", ", card.tags) : string.Empty)}");
        return sb.ToString().TrimEnd();
    }

    private string BuildFeedbackPayloadJson(CardData card)
    {
        if (card == null) return string.Empty;

        FeedbackPayload payload = new()
        {
            deckId = card.deckId,
            cardId = card.cardId,
            name = card.name,
            type = card.type,
            actionRef = card.GetActionRef(),
            spriteName = card.spriteName,
            quote = card.quote,
            actionEffect = card.actionEffect,
            renderedDescription = BuildRenderedDescription(card),
            requirementsText = card.requirementsText,
            renderedRequirements = BuildRequirementsText(card),
            requirementErrors = BuildRequirementErrors(card),
            rawFields = BuildRawSummary(card)
        };

        return JsonUtility.ToJson(payload, true);
    }

    private void CopyCardFeedbackPayload(CardData card)
    {
        string json = BuildFeedbackPayloadJson(card);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        EditorGUIUtility.systemCopyBuffer = $"{json}\n\nPLEASE FIX THIS:\n";
        Debug.Log($"DeckExplorerWindow: copied feedback JSON for '{card?.name}' to clipboard.");
    }

    private string BuildRequirementErrors(CardData card)
    {
        if (card == null) return string.Empty;

        List<string> errors = new();
        Board board = UnityEngine.Object.FindAnyObjectByType<Board>();
        Character selected = board != null ? board.selectedCharacter : null;
        Leader resourceOwner = GetHumanPlayerLeader();

        if (selected == null)
        {
            errors.Add("Select a character first.");
        }
        else
        {
            if (card.commanderSkillRequired > 0 && selected.GetCommander() < card.commanderSkillRequired)
            {
                errors.Add($"Need Commander {card.commanderSkillRequired}.");
            }
            if (card.agentSkillRequired > 0 && selected.GetAgent() < card.agentSkillRequired)
            {
                errors.Add($"Need Agent {card.agentSkillRequired}.");
            }
            if (card.emissarySkillRequired > 0 && selected.GetEmmissary() < card.emissarySkillRequired)
            {
                errors.Add($"Need Emissary {card.emissarySkillRequired}.");
            }
            if (card.mageSkillRequired > 0 && selected.GetMage() < card.mageSkillRequired)
            {
                errors.Add($"Need Mage {card.mageSkillRequired}.");
            }
        }

        if (resourceOwner == null)
        {
            errors.Add("No leader is available to pay the card cost.");
        }
        else
        {
            if (card.leatherRequired > 0 && resourceOwner.leatherAmount < card.leatherRequired)
            {
                errors.Add($"Need {card.leatherRequired}<sprite name=\"leather\">");
            }
            if (card.timberRequired > 0 && resourceOwner.timberAmount < card.timberRequired)
            {
                errors.Add($"Need {card.timberRequired}<sprite name=\"timber\">");
            }
            if (card.mountsRequired > 0 && resourceOwner.mountsAmount < card.mountsRequired)
            {
                errors.Add($"Need {card.mountsRequired}<sprite name=\"mounts\">");
            }
            if (card.ironRequired > 0 && resourceOwner.ironAmount < card.ironRequired)
            {
                errors.Add($"Need {card.ironRequired}<sprite name=\"iron\">");
            }
            if (card.steelRequired > 0 && resourceOwner.steelAmount < card.steelRequired)
            {
                errors.Add($"Need {card.steelRequired}<sprite name=\"steel\">");
            }
            if (card.mithrilRequired > 0 && resourceOwner.mithrilAmount < card.mithrilRequired)
            {
                errors.Add($"Need {card.mithrilRequired}<sprite name=\"mithril\">");
            }

            int goldCost = card.GetTotalGoldCost();
            if (goldCost > 0 && resourceOwner.goldAmount < goldCost)
            {
                errors.Add($"Need {goldCost}<sprite name=\"gold\">");
            }
        }

        if (card.GetCardType() == CardTypeEnum.Land && resourceOwner is PlayableLeader playableLeader && playableLeader.HasPlayedLandThisTurn())
        {
            errors.Add("Only one land card can be played each turn.");
        }

        if (!string.IsNullOrWhiteSpace(card.GetActionRef()) && selected != null)
        {
            ActionsManager actionsManager = UnityEngine.Object.FindAnyObjectByType<ActionsManager>();
            if (actionsManager != null)
            {
                CharacterAction action = actionsManager.ResolveActionByRef(card.GetActionRef(), card);
                if (action != null)
                {
                    action.Initialize(selected, card);
                    if (!action.FulfillsConditions())
                    {
                        errors.Add("Action conditions not met.");
                    }
                }
            }
        }

        return errors.Count == 0 ? "No requirement errors." : string.Join("\n", errors.Distinct());
    }

    private string GetCardFinalizedPreferenceKey(CardData card, DeckEntryView deckView = null)
    {
        if (card == null) return null;

        string deckId = ResolvePreferenceDeckId(card, deckView);
        if (card.cardId > 0)
        {
            return $"RetroLOTR.DeckExplorer.Finalized.{deckId}.{card.cardId}";
        }

        string name = string.IsNullOrWhiteSpace(card.name) ? "unknownCard" : Normalize(card.name);
        string sprite = string.IsNullOrWhiteSpace(card.spriteName) ? "nosprite" : Normalize(card.spriteName);
        string action = string.IsNullOrWhiteSpace(card.GetActionRef()) ? "noaction" : Normalize(card.GetActionRef());
        return $"RetroLOTR.DeckExplorer.Finalized.{deckId}.{name}.{sprite}.{action}";
    }

    private bool IsCardFinalized(CardData card, DeckEntryView deckView = null)
    {
        string key = GetCardFinalizedPreferenceKey(card, deckView);
        return !string.IsNullOrWhiteSpace(key) && EditorPrefs.GetBool(key, false);
    }

    private bool IsCardDisabled(CardData card, DeckEntryView deckView = null)
    {
        if (!IsReferenceCard(card)) return false;
        string key = GetCardDisabledPreferenceKey(card, deckView);
        return !string.IsNullOrWhiteSpace(key) && EditorPrefs.GetBool(key, false);
    }

    private void SetCardFinalized(CardData card, bool finalized, DeckEntryView deckView = null)
    {
        string key = GetCardFinalizedPreferenceKey(card, deckView);
        if (string.IsNullOrWhiteSpace(key)) return;
        EditorPrefs.SetBool(key, finalized);
    }

    private void SetCardDisabled(CardData card, bool disabled, DeckEntryView deckView = null)
    {
        string key = GetCardDisabledPreferenceKey(card, deckView);
        if (string.IsNullOrWhiteSpace(key)) return;
        EditorPrefs.SetBool(key, disabled);
    }

    private string GetCardDisabledPreferenceKey(CardData card, DeckEntryView deckView = null)
    {
        if (card == null) return null;

        string deckId = ResolvePreferenceDeckId(card, deckView);
        if (card.cardId > 0)
        {
            return $"RetroLOTR.DeckExplorer.Disabled.{deckId}.{card.cardId}";
        }

        string name = string.IsNullOrWhiteSpace(card.name) ? "unknownCard" : Normalize(card.name);
        string sprite = string.IsNullOrWhiteSpace(card.spriteName) ? "nosprite" : Normalize(card.spriteName);
        string action = string.IsNullOrWhiteSpace(card.GetActionRef()) ? "noaction" : Normalize(card.GetActionRef());
        return $"RetroLOTR.DeckExplorer.Disabled.{deckId}.{name}.{sprite}.{action}";
    }

    private string ResolvePreferenceDeckId(CardData card, DeckEntryView deckView = null)
    {
        if (!string.IsNullOrWhiteSpace(card?.deckId))
        {
            return card.deckId.Trim();
        }

        deckView ??= GetSelectedDeckView();
        if (!string.IsNullOrWhiteSpace(deckView?.deckData?.deckId))
        {
            return deckView.deckData.deckId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(deckView?.manifest?.deckId))
        {
            return deckView.manifest.deckId.Trim();
        }

        return "unknownDeck";
    }

    private static Leader GetHumanPlayerLeader()
    {
        Game game = UnityEngine.Object.FindAnyObjectByType<Game>();
        return game != null ? game.player : null;
    }

    private static void DrawSprite(Rect rect, Sprite sprite)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        if (sprite == null)
        {
            EditorGUI.LabelField(rect, "No art", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        Texture2D tex = sprite.texture;
        if (tex == null)
        {
            EditorGUI.LabelField(rect, "No texture", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        Rect textureRect = sprite.textureRect;
        float aspect = textureRect.width / textureRect.height;
        Rect drawRect = rect;
        if (drawRect.width / drawRect.height > aspect)
        {
            float width = drawRect.height * aspect;
            drawRect.x += (drawRect.width - width) * 0.5f;
            drawRect.width = width;
        }
        else
        {
            float height = drawRect.width / aspect;
            drawRect.y += (drawRect.height - height) * 0.5f;
            drawRect.height = height;
        }

        GUI.DrawTextureWithTexCoords(drawRect, tex, GetTexCoords(sprite), true);
    }

    private static Rect GetTexCoords(Sprite sprite)
    {
        Texture2D tex = sprite.texture;
        if (tex == null) return new Rect(0, 0, 1, 1);
        Rect r = sprite.textureRect;
        return new Rect(
            r.x / tex.width,
            r.y / tex.height,
            r.width / tex.width,
            r.height / tex.height);
    }

    private static string FormatCardTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (ShouldInsertWordSpace(value, i))
            {
                chars.Add(' ');
            }
            chars.Add(current);
        }

        string formatted = new string(chars.ToArray()).Trim().ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(formatted);
    }

    private static string FormatArmyAbilityLabel(ArmySpecialAbilityEnum ability)
    {
        string abilityName = ability switch
        {
            ArmySpecialAbilityEnum.Longrange => "Long range",
            ArmySpecialAbilityEnum.ShortRange => "Short range",
            ArmySpecialAbilityEnum.RefusingDuels => "Refusing duels",
            ArmySpecialAbilityEnum.ArcaneInsight => "Arcane insight",
            ArmySpecialAbilityEnum.DuelSupremacy => "Duel supremacy",
            ArmySpecialAbilityEnum.MorgulTouch => "Morgul touch",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                Regex.Replace(ability.ToString(), "([a-z])([A-Z])", "$1 $2").ToLowerInvariant())
        };

        string spriteName = ability switch
        {
            ArmySpecialAbilityEnum.Longrange => "longrange",
            ArmySpecialAbilityEnum.ShortRange => "shortrange",
            ArmySpecialAbilityEnum.ArcaneInsight => "arcaneinsight",
            ArmySpecialAbilityEnum.RefusingDuels => "refusingduels",
            ArmySpecialAbilityEnum.DuelSupremacy => "duelsupremacy",
            ArmySpecialAbilityEnum.MorgulTouch => "morgultouch",
            _ => ability.ToString().ToLowerInvariant()
        };

        return $"{abilityName} <sprite name=\"{spriteName}\">";
    }

    private static bool ShouldInsertWordSpace(string value, int index)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (index <= 0 || index >= value.Length) return false;

        char current = value[index];
        if (!char.IsUpper(current)) return false;

        char previous = value[index - 1];
        if (char.IsWhiteSpace(previous)) return false;

        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        if (!char.IsUpper(previous)) return false;

        if (index + 1 < value.Length && char.IsLower(value[index + 1]))
        {
            return true;
        }

        return false;
    }

    private static string FormatCardTypeLabel(CardTypeEnum cardType)
    {
        string label = cardType switch
        {
            CardTypeEnum.PC => "PC",
            CardTypeEnum.Land => "Land",
            CardTypeEnum.Character => "Character",
            CardTypeEnum.Army => "Army",
            CardTypeEnum.Event => "Event",
            CardTypeEnum.Action => "Action",
            CardTypeEnum.Spell => "Spell",
            CardTypeEnum.Encounter => "Encounter",
            CardTypeEnum.Environmental => "Environmental",
            CardTypeEnum.Object => "Object",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        Colors colors = GetColorsForEditor();
        string colorName = cardType switch
        {
            CardTypeEnum.PC => "pc",
            CardTypeEnum.Land => "land",
            CardTypeEnum.Character => "character",
            CardTypeEnum.Army => "army",
            CardTypeEnum.Event => "event",
            CardTypeEnum.Action => "action",
            CardTypeEnum.Spell => "spell",
            CardTypeEnum.Encounter => "encounter",
            CardTypeEnum.Environmental => "environmental",
            CardTypeEnum.Object => "object",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(colorName))
        {
            return label;
        }

        if (colors == null)
        {
            return label;
        }

        Color c;
        try { c = colors.GetColorByName(colorName); }
        catch { c = Color.clear; }

        if (c.a < 0.01f)
        {
            c = colorName switch
            {
                "environmental" => new Color(0.42f, 0.67f, 0.42f, 1f),
                "object" => new Color(0.78f, 0.62f, 0.24f, 1f),
                _ => Color.clear
            };
        }

        if (c.a < 0.01f) return label;

        return $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{label}</color>";
    }

    private static Colors GetColorsForEditor()
    {
        Colors colors = UnityEngine.Object.FindAnyObjectByType<Colors>();
        if (colors != null)
        {
            return colors;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Colors.prefab");
        return prefab != null ? prefab.GetComponent<Colors>() : null;
    }

    private static GUIStyle CreateRichTextStyle(GUIStyle baseStyle)
    {
        GUIStyle style = new(baseStyle);
        style.richText = true;
        return style;
    }

    private static string PrefixWithCardType(string typePrefix, string text)
    {
        if (string.IsNullOrWhiteSpace(typePrefix)) return text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return typePrefix;
        return $"{typePrefix}. {text}";
    }

    private static void AppendRequirement(List<string> requirements, string spriteName, int count)
    {
        if (requirements == null || string.IsNullOrWhiteSpace(spriteName) || count <= 0) return;
        requirements.Add($"{count}<sprite name=\"{spriteName}\">");
    }

    private static string FormatRequirementToken(string spriteName, int count)
    {
        return count <= 0 || string.IsNullOrWhiteSpace(spriteName)
            ? string.Empty
            : $"{count}<sprite name=\"{spriteName}\">";
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
