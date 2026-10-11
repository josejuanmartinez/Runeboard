using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// Decks: dark (the_iron_crown, the_necromancer)
// 4-part: Undead hasted + DS agents Hidden | DS non-Undead near enemy slowed 1 (death aura chills all) | FP near allied PCs gain Hope 1 (living resist death) | enemies near Undead 25% slowed + DS armies +8% atk
public class UnquietDead : EventAction
{
    private const int ReviveHealth = 50;

    public override void ApplyOngoingEffect()
    {
        Board board = Board.Instance;
        if (board == null) return;

        EnvironmentalCardManager env = EnvironmentalCardManager.Instance;
        if (env != null) env.DarkServantsArmyAttackFactor = 1.08f;

        List<Hex> allHexes = board.GetHexes().Where(h => h != null).ToList();

        int undeadHasted = 0, dsAgentHidden = 0, dsSlowed = 0, fpHoped = 0, enemiesSlowed = 0;
        HashSet<Hex> undeadHexes = new();

        foreach (var hex in allHexes)
        {
            if (hex.characters == null) continue;
            foreach (var ch in hex.characters.Where(ch => ch != null && !ch.killed).ToList())
            {
                if (ch.GetAlignment() == AlignmentEnum.darkServants)
                {
                    if (ch.race == RacesEnum.Undead) { ch.ApplyStatusEffect(StatusEffectEnum.Haste, 1); undeadHasted++; undeadHexes.Add(hex); } // big bonus own
                    if (ch.GetAgent() > 0) { ch.Hide(1); dsAgentHidden++; }                                    // big bonus own
                    bool nearEnemy = hex.characters.Any(x => x != null && !x.killed && x.GetAlignment() == AlignmentEnum.freePeople);
                    if (!nearEnemy && ch.race != RacesEnum.Undead) { ch.moved = Mathf.Min(ch.moved + 1, ch.GetMaxMovement()); dsSlowed++; } // small debuff own
                }
                else if (ch.GetAlignment() == AlignmentEnum.freePeople)
                {
                    PC pc = hex.GetPC();
                    bool nearAlliedPc = pc != null && pc.owner?.GetAlignment() == AlignmentEnum.freePeople;
                    if (nearAlliedPc) { ch.ApplyStatusEffect(StatusEffectEnum.Hope, 1); fpHoped++; }           // small bonus other: living resist near settlements
                }
            }
        }

        HashSet<Hex> adjToUndead = new();
        foreach (var uh in undeadHexes) foreach (var adj in uh.GetHexesInRadius(1)) if (adj != null) adjToUndead.Add(adj);
        foreach (var adj in adjToUndead)
        {
            if (adj.characters == null) continue;
            foreach (var enemy in adj.characters.Where(ch => ch != null && !ch.killed && ch.race != RacesEnum.Undead && !ch.IsImmuneToNegativeEnvironmentalCards()).ToList())
                if (UnityEngine.Random.value < 0.25f) { enemy.moved = Mathf.Min(enemy.moved + 1, enemy.GetMaxMovement()); enemiesSlowed++; }  // big debuff other
        }

        MessageDisplayNoUI.ShowMessage(null, null,
            $"Unquiet Dead (ongoing): {undeadHasted} Undead hasted; {dsAgentHidden} DS agents hidden; {enemiesSlowed} nearby enemies halted; {fpHoped} FP near settlements gain Hope.",
            Color.magenta);
    }

    private static bool IsEnemy(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return false;
        return target.GetAlignment() != source.GetAlignment() || source.GetAlignment() == AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;
            return UnityEngine.Object.FindObjectsByType<Character>().Any(ch => ch != null && ch.killed && ch.GetOwner() == owner);
        };

        async Task<bool> unquietAsync(Character character)
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;
            var deadAllies = UnityEngine.Object.FindObjectsByType<Character>().Where(ch => ch != null && ch.killed && ch.GetOwner() == owner).ToList();
            if (deadAllies.Count == 0) return false;
            bool isAI = !character.isPlayerControlled;
            Character target = null;
            if (!isAI)
            {
                string selected = await SelectionDialog.AskImmediate(
                    "Raise from the dead",
                    "Ok",
                    "Cancel",
                    deadAllies.Select(x => x.characterName).ToList(),
                    null,
                    false,
                    SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(character) : null,
                    EventIconType.MultiChoice,
                    actionName,
                    SelectionDialog.CharacterIconNames(deadAllies));
                if (string.IsNullOrWhiteSpace(selected)) return false;
                target = deadAllies.FirstOrDefault(x => x.characterName == selected);
            }
            else target = deadAllies.OrderByDescending(x => x.GetCommander() + x.GetMage()).FirstOrDefault();
            if (target == null) return false;
            target.Revive(owner, character.hex, ReviveHealth);
            int haltedCount = 0;
            if (character.hex != null)
                foreach (var h in character.hex.GetHexesInRadius(1))
                {
                    if (h?.characters == null) continue;
                    foreach (var enemy in h.characters.Where(e => e != null && !e.killed && IsEnemy(character, e)).ToList()) { enemy.Halt(1); haltedCount++; }
                }
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Unquiet Dead: {target.characterName} rises with {ReviveHealth}% HP. {haltedCount} enemies cannot act.", new Color(0.5f, 0.8f, 0.5f));
            return true;
        }

        base.Initialize(c, condition, effect, unquietAsync);
    }
}
