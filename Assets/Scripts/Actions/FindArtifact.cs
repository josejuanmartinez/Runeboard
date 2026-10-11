using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class FindArtifact : MageAction
{
    public const string ActionRef = "FindArtifact";

    private const int AnswerCount = 4;
    private const int PixelatedSize = 18;

    private static List<Riddle> cachedRiddles;
    private static bool riddlesLoaded;

    private enum ArtifactTrialType
    {
        PixelatedCard,
        MatchIllustration,
        Riddle,
        CardQuote
    }

    private sealed class ArtifactTrial
    {
        public string prompt;
        public string answer;
        public List<string> options;
        public List<string> optionIcons;
        public Sprite portrait;
        public Sprite temporaryPortrait;
    }

    private static List<Riddle> GetRiddles()
    {
        if (riddlesLoaded) return cachedRiddles;
        riddlesLoaded = true;

        TextAsset json = Resources.Load<TextAsset>("Riddles");
        if (json == null)
        {
            Debug.LogWarning("Riddles.json not found in Resources.");
            cachedRiddles = new();
            return cachedRiddles;
        }

        RiddleCollection collection = JsonUtility.FromJson<RiddleCollection>(json.text);
        cachedRiddles = collection?.riddles ?? new();
        return cachedRiddles;
    }

    private static List<CardData> GetCardPool()
    {
        DeckManager deckManager = DeckManager.Instance ?? DeckManager.Instance;
        if (deckManager == null) return new();
        if (deckManager.cards == null || deckManager.cards.Count == 0) deckManager.InitializeFromResources();

        return deckManager.cards
            .Where(card => card != null && !string.IsNullOrWhiteSpace(card.name))
            .GroupBy(card => card.name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool TryGetArtwork(CardData card, Illustrations illustrations, out Sprite sprite, out string key)
    {
        sprite = null;
        key = null;
        if (card == null || illustrations == null) return false;

        string[] candidates = { card.spriteName, card.portraitName, card.name, card.actionClassName, card.action };
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (!illustrations.TryGetIllustrationByName(candidate, out sprite)) continue;
            key = candidate;
            return true;
        }

        return false;
    }

    private static List<CardData> PickCardAnswers(CardData correct, List<CardData> eligible)
    {
        if (correct == null || eligible == null) return new();

        List<CardData> wrong = eligible
            .Where(card => card != null && !string.Equals(card.name, correct.name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(card => card.GetCardType() == correct.GetCardType() ? 0 : 1)
            .ThenBy(_ => UnityEngine.Random.value)
            .Take(AnswerCount - 1)
            .ToList();

        if (wrong.Count < AnswerCount - 1) return new();
        wrong.Add(correct);
        return wrong.OrderBy(_ => UnityEngine.Random.value).ToList();
    }

    private static ArtifactTrial BuildPixelatedCardTrial(List<CardData> cards, Illustrations illustrations)
    {
        List<CardData> eligible = cards
            .Where(card => TryGetArtwork(card, illustrations, out _, out _))
            .ToList();
        if (eligible.Count < AnswerCount) return null;

        CardData correct = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        List<CardData> answers = PickCardAnswers(correct, eligible);
        if (answers.Count != AnswerCount || !TryGetArtwork(correct, illustrations, out Sprite artwork, out _)) return null;

        Sprite pixelated = CreatePixelatedSprite(artwork);
        if (pixelated == null) return null;

        return new ArtifactTrial
        {
            prompt = "Which card is hidden in this pixelated image?",
            answer = correct.name,
            options = answers.Select(card => card.name).ToList(),
            portrait = pixelated,
            temporaryPortrait = pixelated
        };
    }

    private static ArtifactTrial BuildIllustrationTrial(List<CardData> cards, Illustrations illustrations)
    {
        List<CardData> eligible = cards
            .Where(card => !string.IsNullOrWhiteSpace(card.GetDescriptionBody())
                && TryGetArtwork(card, illustrations, out _, out _))
            .ToList();
        if (eligible.Count < AnswerCount) return null;

        CardData correct = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        List<CardData> answers = PickCardAnswers(correct, eligible);
        if (answers.Count != AnswerCount) return null;

        List<string> labels = Enumerable.Range(1, AnswerCount).Select(index => $"Illustration {index}").ToList();
        List<string> icons = new();
        for (int i = 0; i < answers.Count; i++)
        {
            if (!TryGetArtwork(answers[i], illustrations, out _, out string key)) return null;
            icons.Add(key);
        }

        int correctIndex = answers.FindIndex(card => string.Equals(card.name, correct.name, StringComparison.OrdinalIgnoreCase));
        return new ArtifactTrial
        {
            prompt = $"Which illustration belongs to this card?\n\n{correct.GetDescriptionBody()}",
            answer = labels[correctIndex],
            options = labels,
            optionIcons = icons
        };
    }

    private static ArtifactTrial BuildRiddleTrial()
    {
        List<Riddle> eligible = GetRiddles()
            .Where(riddle => riddle != null
                && !string.IsNullOrWhiteSpace(riddle.answer)
                && riddle.options != null
                && riddle.options.Count(option => !string.Equals(option, riddle.answer, StringComparison.OrdinalIgnoreCase)) >= AnswerCount - 1)
            .ToList();
        if (eligible.Count == 0) return null;

        Riddle riddle = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        List<string> options = riddle.options
            .Where(option => !string.Equals(option, riddle.answer, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => UnityEngine.Random.value)
            .Take(AnswerCount - 1)
            .Append(riddle.answer)
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();

        return new ArtifactTrial { prompt = riddle.prompt, answer = riddle.answer, options = options };
    }

    private static ArtifactTrial BuildQuoteTrial(List<CardData> cards)
    {
        List<CardData> eligible = cards.Where(card => !string.IsNullOrWhiteSpace(card.quote)).ToList();
        if (eligible.Count < AnswerCount) return null;

        CardData correct = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        List<CardData> answers = PickCardAnswers(correct, eligible);
        if (answers.Count != AnswerCount) return null;

        string quote = correct.quote.Trim().Trim('"');
        return new ArtifactTrial
        {
            prompt = $"Which card bears these words?\n\n<i>\"{quote}\"</i>",
            answer = correct.name,
            options = answers.Select(card => card.name).ToList()
        };
    }

    private static ArtifactTrial BuildRandomTrial()
    {
        List<CardData> cards = GetCardPool();
        Illustrations illustrations = UnityEngine.Object.FindAnyObjectByType<Illustrations>();
        ArtifactTrialType[] types = Enum.GetValues(typeof(ArtifactTrialType))
            .Cast<ArtifactTrialType>()
            .OrderBy(_ => UnityEngine.Random.value)
            .ToArray();

        foreach (ArtifactTrialType type in types)
        {
            ArtifactTrial trial = type switch
            {
                ArtifactTrialType.PixelatedCard => BuildPixelatedCardTrial(cards, illustrations),
                ArtifactTrialType.MatchIllustration => BuildIllustrationTrial(cards, illustrations),
                ArtifactTrialType.Riddle => BuildRiddleTrial(),
                ArtifactTrialType.CardQuote => BuildQuoteTrial(cards),
                _ => null
            };
            if (trial != null) return trial;
        }

        return null;
    }

    private static Sprite CreatePixelatedSprite(Sprite source)
    {
        if (source == null || source.texture == null) return null;

        Rect rect = source.textureRect;
        float width = Mathf.Max(1f, source.texture.width);
        float height = Mathf.Max(1f, source.texture.height);
        Vector2 scale = new(rect.width / width, rect.height / height);
        Vector2 offset = new(rect.x / width, rect.y / height);
        RenderTexture target = RenderTexture.GetTemporary(PixelatedSize, PixelatedSize, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source.texture, target, scale, offset);
            RenderTexture.active = target;
            Texture2D texture = new(PixelatedSize, PixelatedSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"ArtifactTrial_{source.name}_Pixelated"
            };
            texture.ReadPixels(new Rect(0, 0, PixelatedSize, PixelatedSize), 0, 0);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, PixelatedSize, PixelatedSize), new Vector2(0.5f, 0.5f), PixelatedSize);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static void DestroyTemporaryPortrait(ArtifactTrial trial)
    {
        if (trial?.temporaryPortrait == null) return;
        Texture texture = trial.temporaryPortrait.texture;
        UnityEngine.Object.Destroy(trial.temporaryPortrait);
        if (texture != null) UnityEngine.Object.Destroy(texture);
    }

    private static void ClaimArtifact(Character character, CardData artifact)
    {
        character.objects.Add(artifact);
        character.hex.hiddenObjects.Remove(artifact);
        character.hex.UpdateArtifactVisibility();
        Character.RefreshArtifactPcVisibilityForHex(character.hex);
        MessageDisplayNoUI.ShowMessage(character.hex, character, $"<sprite name=\"artifact\">object {artifact.name} claimed", Color.green);
        Sounds.Instance?.PlayArtifactFound();
    }

    private static async Task<bool> RunArtifactTrial(Character character)
    {
        ArtifactTrial trial = BuildRandomTrial();
        if (trial == null)
        {
            Debug.LogWarning("No valid artifact trial could be built; allowing the artifact claim.");
            return true;
        }

        try
        {
            string answer = await SelectionDialog.AskImmediate(
                trial.prompt,
                "Choose",
                "Leave",
                trial.options,
                null,
                !character.isPlayerControlled,
                trial.portrait,
                EventIconType.Encounter,
                "Artifact Trial",
                trial.optionIcons);
            return string.Equals(answer, trial.answer, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DestroyTemporaryPortrait(trial);
        }
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = character =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character.hex.hiddenObjects.Count > 0)
            {
                character.hex.RevealArtifact();
                if (character.objects.Count >= Character.MAX_OBJECTS)
                {
                    _ = ConfirmationDialog.AskOk($"{character.characterName} can't hold more objects");
                    return false;
                }
            }
            else
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character, "No <sprite name=\"artifact\">object found", Color.red);
            }

            return true;
        };
        condition = character => originalCondition == null || originalCondition(character);
        asyncEffect = async character =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            if (character.hex.hiddenObjects.Count < 1) return true;
            if (character.objects.Count >= Character.MAX_OBJECTS) return false;

            CardData artifact = character.hex.hiddenObjects[0];
            if (await RunArtifactTrial(character))
            {
                ClaimArtifact(character, artifact);
            }
            else
            {
                int damage = UnityEngine.Random.Range(10, 26);
                MessageDisplayNoUI.ShowMessage(character.hex, character, "The artifact trial is failed.", Color.red);
                character.Wounded(null, damage);
            }

            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}
