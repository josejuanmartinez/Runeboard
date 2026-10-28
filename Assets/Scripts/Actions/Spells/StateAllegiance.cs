using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class StateAllegiance : EmmissaryAction
{
    public const string ActionRef = "StateAllegiance";
    private const int QuestionsPerAttempt = 3;
    private const float AiBaseChance = 0.35f;
    private const float AiChancePerEmissaryLevel = 0.15f;

    private static Dictionary<string, List<NonPlayableLeaderTriviaQuestion>> cachedTrivia;
    private static bool triviaLoaded;

    private static List<NonPlayableLeaderTriviaQuestion> GetQuestionsFor(string characterName)
    {
        if (!triviaLoaded)
        {
            triviaLoaded = true;
            cachedTrivia = new Dictionary<string, List<NonPlayableLeaderTriviaQuestion>>(StringComparer.OrdinalIgnoreCase);

            TextAsset json = Resources.Load<TextAsset>("NonPlayableLeaderTrivia");
            if (json == null)
            {
                Debug.LogWarning("NonPlayableLeaderTrivia.json not found in Resources.");
            }
            else
            {
                NonPlayableLeaderTriviaCollection collection = JsonUtility.FromJson<NonPlayableLeaderTriviaCollection>(json.text);
                foreach (NonPlayableLeaderTriviaEntry entry in collection?.leaders ?? new())
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.characterName)) continue;
                    cachedTrivia[entry.characterName] = entry.questions ?? new();
                }
            }
        }

        return cachedTrivia.TryGetValue(characterName ?? "", out List<NonPlayableLeaderTriviaQuestion> questions) ? questions : null;
    }

    // Asks the acting character three unique trivia questions about the non-playable leader
    // they are trying to recruit; missing one fails the recruitment attempt immediately.
    // A higher Emmissary skill thins out the wrong answers before each question is shown.
    private static async Task<bool> RunRecruitmentGauntlet(NonPlayableLeader nonPlayableLeader, Character character)
    {
        List<NonPlayableLeaderTriviaQuestion> bank = GetQuestionsFor(nonPlayableLeader.characterName);
        if (bank == null || bank.Count < QuestionsPerAttempt) return true;

        List<NonPlayableLeaderTriviaQuestion> chosen = bank
            .OrderBy(_ => UnityEngine.Random.value)
            .Take(QuestionsPerAttempt)
            .ToList();

        bool isAI = !character.isPlayerControlled;
        // The AI cannot read the questions (it would guess at random and almost never win a
        // realm), so it rolls on its emissary skill instead.
        if (isAI)
        {
            bool convinced = UnityEngine.Random.value < Mathf.Clamp01(AiBaseChance + AiChancePerEmissaryLevel * character.GetEmmissary());
            if (!convinced) MessageDisplayNoUI.ShowMessage(character.hex, character, $"{nonPlayableLeader.characterName} is not convinced.", Color.red);
            return convinced;
        }
        Sprite portrait = SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(character) : null;
        int discardCount = character.GetEmmissary() / 2;

        foreach (NonPlayableLeaderTriviaQuestion question in chosen)
        {
            if (question == null || question.options == null || question.options.Count < 1) continue;

            List<string> displayOptions = BuildDisplayOptions(question, discardCount);
            string answer = await SelectionDialog.AskImmediate(
                question.prompt,
                "Choose",
                string.Empty,
                displayOptions,
                null,
                isAI,
                portrait,
                EventIconType.Encounter,
                nonPlayableLeader.characterName,
                displayOptions);
            if (!string.Equals(answer, question.answer, StringComparison.OrdinalIgnoreCase))
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character, $"{nonPlayableLeader.characterName} is not convinced.", Color.red);
                return false;
            }
        }

        return true;
    }

    // Removes up to discardCount wrong options (never the correct one) and shuffles the rest.
    private static List<string> BuildDisplayOptions(NonPlayableLeaderTriviaQuestion question, int discardCount)
    {
        List<string> wrongOptions = question.options
            .Where(option => !string.Equals(option, question.answer, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();

        int toRemove = Mathf.Clamp(discardCount, 0, wrongOptions.Count);
        List<string> remainingWrong = wrongOptions.Skip(toRemove).ToList();

        List<string> display = new() { question.answer };
        display.AddRange(remainingWrong);
        return display.OrderBy(_ => UnityEngine.Random.value).ToList();
    }

    override public void Initialize(
        Character c,
        Func<Character, bool> condition = null,
        Func<Character, bool> effect = null,
        Func<Character, Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;

            return character != null
                && character.hex != null
                && character.hex.GetPCData() != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;

            PC pc = character?.hex != null ? character.hex.GetPCData() : null;
            if (pc == null) return false;
            Leader actorOwner = character.GetOwner();
            if (actorOwner == null) return false;

            if (pc.owner == null)
            {
                return pc.ClaimUnowned(actorOwner);
            }

            if (pc.owner == actorOwner || pc.owner.GetAlignment() == actorOwner.GetAlignment())
            {
                if (pc.isCapital && pc.owner is NonPlayableLeader nonPlayableLeader)
                {
                    if (nonPlayableLeader.joined || nonPlayableLeader.killed) { Debug.Log($"[Allegiance] {nonPlayableLeader.characterName}: already joined or killed"); return false; }
                    if (actorOwner is not PlayableLeader playableLeader) return false;
                    if (!nonPlayableLeader.CanJoinWithStateAllegiance(playableLeader)) { Debug.Log($"[Allegiance] {nonPlayableLeader.characterName} refuses {playableLeader.characterName}: alignment {nonPlayableLeader.alignment} vs {playableLeader.alignment}"); return false; }

                    if (!await RunRecruitmentGauntlet(nonPlayableLeader, character)) { Debug.Log($"[Allegiance] {nonPlayableLeader.characterName}: not convinced by {character.characterName}"); return false; }

                    bool joinedNow = nonPlayableLeader.Joined(playableLeader);
                    Debug.Log($"[Allegiance] {nonPlayableLeader.characterName} joins {playableLeader.characterName}: {joinedNow}");
                    return joinedNow;
                }

                int loyalty = UnityEngine.Random.Range(1, 5);
                pc.IncreaseLoyalty(loyalty, character);
                return true;
            }

            if (pc.owner.GetAlignment() != actorOwner.GetAlignment())
            {
                int loyalty = UnityEngine.Random.Range(1, 5);
                pc.DecreaseLoyalty(loyalty, character);
                return true;
            }

            return false;
        };

        base.Initialize(c, condition, null, asyncEffect);
    }
}
