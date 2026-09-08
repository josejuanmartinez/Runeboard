using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the loading screen's preloaded cards ahead of time: for each slot, clones the Card
// prefab once per extra name in "Card Names" (beyond the slot's own existing card), resolves its
// CardData and bakes its artwork straight onto the Image components — all at edit time, using the
// same edit-time artwork lookup CardDataProviderEditor's "Apply" button already relies on. The
// result is saved into the scene/prefab, so at runtime StartupLoadingScreen only ever toggles
// which pre-built sibling is active; it never resolves a card name or its art while the game is
// actually running, which is what the earlier random-catalog rotation kept losing the race on.
[CustomEditor(typeof(StartupLoadingScreen))]
public sealed class StartupLoadingScreenEditor : Editor
{
    private const string CardPrefabPath = "Assets/GameObjects/Reusable/Card.prefab";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        StartupLoadingScreen screen = (StartupLoadingScreen)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preload Baking", EditorStyles.boldLabel);
        if (GUILayout.Button("Bake Preload Cards", GUILayout.Height(30f)))
        {
            BakePreloadCards();
        }
        EditorGUILayout.HelpBox(
            "Run after editing any slot's Card Names. Builds one pre-initialized, art-baked " +
            "sibling card per extra name (beyond the slot's own card) and saves them into the " +
            "scene/prefab, so every preloaded card is already fully ready when the game starts " +
            "— nothing is resolved or loaded at runtime.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Show Next Test Card", GUILayout.Height(30f)))
            {
                screen.TestNextCardSet();
            }
        }
        EditorGUILayout.HelpBox(Application.isPlaying
            ? $"Steps every slot to its own card #{screen.TestCardIndex + 1} (wraps per-slot), stopping the automatic rotation."
            : "Enter Play Mode to step through each slot's baked cards one at a time.",
            MessageType.Info);
    }

    private void BakePreloadCards()
    {
        GameObject cardPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (cardPrefabAsset == null)
        {
            Debug.LogError($"StartupLoadingScreenEditor: {CardPrefabPath} was not found.");
            return;
        }

        StartupLoadingScreen screen = (StartupLoadingScreen)target;

        serializedObject.Update();
        SerializedProperty cardSlotsProp = serializedObject.FindProperty("cardSlots");

        for (int s = 0; s < cardSlotsProp.arraySize; s++)
        {
            SerializedProperty slotProp = cardSlotsProp.GetArrayElementAtIndex(s);
            CardDataProvider template = slotProp.FindPropertyRelative("provider").objectReferenceValue as CardDataProvider;
            if (template == null) continue;

            SerializedProperty namesProp = slotProp.FindPropertyRelative("cardNames");
            SerializedProperty alternatesProp = slotProp.FindPropertyRelative("bakedAlternates");

            // Discard any previously baked siblings before rebaking, so repeated bakes don't pile up.
            for (int i = alternatesProp.arraySize - 1; i >= 0; i--)
            {
                CardDataProvider stale = alternatesProp.GetArrayElementAtIndex(i).objectReferenceValue as CardDataProvider;
                if (stale != null) Undo.DestroyObjectImmediate(stale.gameObject);
            }
            alternatesProp.ClearArray();

            // Re-bake the template itself too, so it always matches cardNames[0] and stays visible.
            string firstName = namesProp.arraySize > 0 ? namesProp.GetArrayElementAtIndex(0).stringValue : null;
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                template.cardName = firstName;
                if (template.Apply())
                {
                    CardEditorArtworkBaker.ApplyArtwork(template.Card);
                    CardEditorArtworkBaker.ApplyDeckArtwork(template.Card);
                }
                else
                {
                    Debug.LogError(
                        $"StartupLoadingScreenEditor: slot {s + 1}'s first card name '{firstName}' matches no " +
                        "card in any deck, so it keeps whatever was baked into it last. Fix the name and re-bake.",
                        template);
                }
            }
            template.gameObject.SetActive(true);
            EditorUtility.SetDirty(template);
            PrefabUtility.RecordPrefabInstancePropertyModifications(template.transform);

            int siblingsAdded = 0;
            for (int n = 1; n < namesProp.arraySize; n++)
            {
                string cardName = namesProp.GetArrayElementAtIndex(n).stringValue;
                if (string.IsNullOrWhiteSpace(cardName)) continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefabAsset, template.transform.parent);
                Undo.RegisterCreatedObjectUndo(instance, "Bake Preload Card");
                instance.name = cardName;
                instance.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + siblingsAdded + 1);

                RectTransform templateRect = template.GetComponent<RectTransform>();
                RectTransform instanceRect = instance.GetComponent<RectTransform>();
                if (templateRect != null && instanceRect != null)
                {
                    instanceRect.anchorMin = templateRect.anchorMin;
                    instanceRect.anchorMax = templateRect.anchorMax;
                    instanceRect.pivot = templateRect.pivot;
                    instanceRect.sizeDelta = templateRect.sizeDelta;
                    instanceRect.anchoredPosition = templateRect.anchoredPosition;
                }
                instance.transform.localScale = template.transform.localScale;

                CardDataProvider provider = instance.AddComponent<CardDataProvider>();
                provider.cardName = cardName;
                provider.startAsToken = template.startAsToken;
                provider.deckId = template.deckId;
                provider.suppressHoverEffects = template.suppressHoverEffects;
                provider.useCardArtFolderOnly = template.useCardArtFolderOnly;
                provider.showRequirementWarnings = template.showRequirementWarnings;
                provider.showCloseIcon = template.showCloseIcon;
                if (template.GetComponent<CardShineEffect>() != null) instance.AddComponent<CardShineEffect>();

                // An unresolvable name (a typo, or a card since renamed) leaves a sibling with no
                // card data, no artwork and the raw Card.prefab defaults. Registering it anyway
                // means the loading screen eventually rotates onto a blank card, which reads as a
                // rendering bug rather than as the bad name it actually is — so drop it here and
                // name it in the console instead.
                if (!provider.Apply())
                {
                    Debug.LogError(
                        $"StartupLoadingScreenEditor: slot {s + 1}'s card name '{cardName}' matches no card in " +
                        "any deck — skipping it. Fix the name in Card Names and re-bake.",
                        screen);
                    Undo.DestroyObjectImmediate(instance);
                    continue;
                }
                CardEditorArtworkBaker.ApplyArtwork(provider.Card);
                CardEditorArtworkBaker.ApplyDeckArtwork(provider.Card);

                // Only the template (cardNames[0]) stays visible until the game rotates to this one.
                instance.SetActive(false);
                EditorUtility.SetDirty(instance);

                alternatesProp.InsertArrayElementAtIndex(alternatesProp.arraySize);
                alternatesProp.GetArrayElementAtIndex(alternatesProp.arraySize - 1).objectReferenceValue = provider;
                siblingsAdded++;
            }
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);

        if (screen.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        }
        else
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(screen.transform);
        }

        Debug.Log("StartupLoadingScreenEditor: baked preload cards for all slots.");
    }
}
