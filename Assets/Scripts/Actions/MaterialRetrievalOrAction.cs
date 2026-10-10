using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class PCAction : MaterialRetrieval
{
    protected override bool GrantsResourcesImmediately => false;

    public override void Initialize(Character c, CardData card = null, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.killed || character.hex == null) return false;
            if (!IsOnAssociatedRegion(character)) return false;
            if (character.hex.IsWaterTerrain()) return false;
            if (!IsRegionDiscovered(character)) return false;

            PC hexPc = character.hex.GetPCData();
            if (hexPc != null && hexPc.citySize != PCSizeEnum.NONE)
            {
                string targetName = PcDescriptionBuilder.NormalizeLookupKey(ResolveAssociatedPcName());
                string hexPcName = PcDescriptionBuilder.NormalizeLookupKey(hexPc.pcName);
                if (hexPcName != targetName) return false;
            }

            return true;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            if (character == null) return true;

            Leader actingOwner = character.GetOwner();
            PC existingPc = FindAssociatedPcInGame();
            if (existingPc != null)
            {
                if (existingPc.hex != null && actingOwner != null)
                {
                    existingPc.hex.Reveal(actingOwner);
                }

                bool heldByEnemy = existingPc.owner != null && actingOwner != null
                    && existingPc.owner != actingOwner
                    && existingPc.owner.GetAlignment() != actingOwner.GetAlignment();

                if (heldByEnemy)
                {
                    int loyaltyLoss = UnityEngine.Random.Range(5, 11);
                    existingPc.loyalty = Mathf.Max(0, existingPc.loyalty - loyaltyLoss);
                    existingPc.hex.RedrawPC();
                    ShowLoyaltyReducedNotification(character, existingPc, loyaltyLoss);
                }
                else
                {
                    ApplyResources(character);
                }
            }
            else
            {
                bool founded = TryFoundAssociatedPc(character);
                if (founded)
                {
                    ApplyResources(character);
                }
                else
                {
                    string pcName = ResolveAssociatedPcName();
                    MessageDisplayNoUI.ShowMessage(character.hex, character, $"Could not found {pcName}.", Color.red);
                }
            }

            return true;
        };

        base.Initialize(c, card, condition, effect, asyncEffect);
    }

    protected override string GetDescription()
    {
        return card != null ? card.GetRenderedDescription(true) : string.Empty;
    }

    private bool ApplyResources(Character character)
    {
        if (character == null || card == null) return false;
        Leader owner = character.GetOwner();
        if (owner == null) return false;
        owner.RecordPlayedLandThisTurn();
        if (card.leatherGranted > 0) owner.AddLeather(card.leatherGranted, false);
        if (card.mountsGranted > 0) owner.AddMounts(card.mountsGranted, false);
        if (card.timberGranted > 0) owner.AddTimber(card.timberGranted, false);
        if (card.ironGranted > 0) owner.AddIron(card.ironGranted, false);
        if (card.steelGranted > 0) owner.AddSteel(card.steelGranted, false);
        if (card.mithrilGranted > 0) owner.AddMithril(card.mithrilGranted, false);
        if (card.goldGranted > 0) owner.AddGold(card.goldGranted, false);
        return true;
    }

    private bool TryFoundAssociatedPc(Character character)
    {
        if (!CanFoundAssociatedPc(character)) return false;

        Leader owner = character.GetOwner();
        if (owner == null || character.hex == null) return false;

        string pcName = ResolveAssociatedPcName();
        PC foundedPc = new(owner, pcName, PCSizeEnum.camp, FortSizeEnum.NONE, false, false, character.hex, false, 75);
        foundedPc.isUnderground = card != null && card.isUnderground;
        character.hex.UpdateUndergroundMarker();
        character.hex.RedrawPC();
        MessageDisplayNoUI.ShowMessage(character.hex, character, $"{pcName} is founded.", Color.green);
        // Resets the 5-turn founding-opportunity cooldown regardless of which UI path
        // triggered founding (manual hand card or the opportunity-card popup).
        owner.NotifyPcFounded();
        return foundedPc != null;
    }

    private bool CanFoundAssociatedPc(Character character)
    {
        if (character == null || character.killed || character.hex == null) return false;
        if (!IsOnAssociatedRegion(character)) return false;
        if (character.hex.IsWaterTerrain()) return false;

        PC hexPc = character.hex.GetPCData();
        if (hexPc != null && hexPc.citySize != PCSizeEnum.NONE)
        {
            string targetName = PcDescriptionBuilder.NormalizeLookupKey(ResolveAssociatedPcName());
            string hexPcName = PcDescriptionBuilder.NormalizeLookupKey(hexPc.pcName);
            if (hexPcName != targetName) return false;
        }

        return true;
    }

    private bool IsOnAssociatedRegion(Character character)
    {
        if (character == null || character.hex == null) return false;

        string pcRegion = ResolveAssociatedPcRegion();
        string hexRegion = character.hex.GetLandRegion();
        if (string.IsNullOrWhiteSpace(pcRegion) || string.IsNullOrWhiteSpace(hexRegion)) return false;

        return string.Equals(PcDescriptionBuilder.NormalizeLookupKey(pcRegion), PcDescriptionBuilder.NormalizeLookupKey(hexRegion), StringComparison.OrdinalIgnoreCase);
    }

    private bool IsRegionDiscovered(Character character)
    {
        string pcRegion = ResolveAssociatedPcRegion();
        if (string.IsNullOrWhiteSpace(pcRegion)) return true;

        Leader owner = character.GetOwner();
        if (owner == null) return false;

        Board board = Board.Instance;
        if (board == null) return false;

        string normalizedTarget = PcDescriptionBuilder.NormalizeLookupKey(pcRegion);

        foreach (Hex hex in board.GetHexes())
        {
            if (hex == null) continue;

            string hexRegion = hex.GetLandRegion();
            if (string.IsNullOrWhiteSpace(hexRegion))
            {
                PC pc = hex.GetPCData();
                if (pc != null)
                {
                    hexRegion = ResolveRegionForPc(pc);
                }
            }

            if (string.IsNullOrWhiteSpace(hexRegion)) continue;
            if (!string.Equals(PcDescriptionBuilder.NormalizeLookupKey(hexRegion), normalizedTarget, StringComparison.OrdinalIgnoreCase)) continue;

            if (hex.IsHexRevealed() || hex.IsScoutedBy(owner)) return true;
        }

        return false;
    }

    private string ResolveRegionForPc(PC pc)
    {
        if (pc == null) return null;

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (deckManager != null)
        {
            return deckManager.ResolveRegionForPc(pc);
        }

        return null;
    }

    private PC FindAssociatedPcInGame()
    {
        string targetKey = PcDescriptionBuilder.NormalizeLookupKey(ResolveAssociatedPcName());
        if (string.IsNullOrWhiteSpace(targetKey)) return null;

        Board board = Board.Instance;
        List<Hex> hexes = board?.GetHexes();
        if (hexes == null) return null;

        for (int i = 0; i < hexes.Count; i++)
        {
            PC candidate = hexes[i]?.GetPCData();
            if (candidate == null) continue;
            if (PcDescriptionBuilder.NormalizeLookupKey(candidate.pcName) == targetKey) return candidate;
        }
        return null;
    }

    // Used by the auto-grant triggers (Board.TriggerOwnPcGrantIfStandingOnOne, entering an
    // own PC / turn-start re-trigger) to confirm the PC is already founded and owned by the
    // acting character's leader, so only the (already-correct) granting branch of
    // asyncEffect applies and founding is never re-attempted.
    public bool IsAlreadyFoundedAndOwnedBySelf(Character character)
    {
        PC existingPc = FindAssociatedPcInGame();
        return existingPc != null && character != null && existingPc.owner == character.GetOwner();
    }

    // Founded by anyone (not just the acting character's leader) — used by the founding-
    // opportunity mechanic to filter out PC cards that no longer need founding.
    public bool IsAlreadyFounded()
    {
        return FindAssociatedPcInGame() != null;
    }

    private void ShowLoyaltyReducedNotification(Character character, PC existingPc, int amount)
    {
        string pcName = ResolveAssociatedPcName();
        string message = $"Loyalty is reduced at {pcName} by {amount}";
        EventIconsManager.ShowHexAnchoredMessage(EventIconType.HexMessage, existingPc?.hex, character?.hex, message, Color.red);
    }

    private string ResolveAssociatedPcName()
    {
        if (card != null && !string.IsNullOrWhiteSpace(card.name)) return card.name;
        return HumanizeSourceName(GetType().Name);
    }

    private string ResolveAssociatedPcRegion()
    {
        if (card != null && !string.IsNullOrWhiteSpace(card.region)) return card.region;
        return null;
    }

    private static string HumanizeSourceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        List<string> parts = new();
        string current = string.Empty;
        foreach (char ch in value)
        {
            if (char.IsUpper(ch) && current.Length > 0)
            {
                parts.Add(current);
                current = string.Empty;
            }
            current += ch;
        }
        if (current.Length > 0) parts.Add(current);
        return string.Join(" ", parts);
    }
}

public class MaterialRetrievalOrAction : PCAction
{
}

public static class PcDescriptionBuilder
{
    public static string NormalizeLookupKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public static string BuildBody(CardData data, bool includeFoundingText)
    {
        if (data == null) return string.Empty;

        string regionName = FormatDisplayRegionName(data.region);
        bool hasRegion = !string.IsNullOrWhiteSpace(regionName);

        System.Text.StringBuilder sb = new();
        if (hasRegion)
        {
            sb.Append(regionName).Append(". ");
        }

        string resources = BuildResourceSummary(data);
        if (!string.IsNullOrWhiteSpace(resources))
        {
            sb.Append(resources).Append(".");
        }
        else if (hasRegion)
        {
            sb.Append("Resources.");
        }

        if (includeFoundingText && hasRegion)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append("Can be founded in ").Append(regionName).Append(".");
            if (!string.IsNullOrWhiteSpace(data.name))
            {
                sb.Append(" Allows recruiting characters born in ").Append(data.name).Append(".");
            }
        }

        string body = sb.ToString();
        return body;
    }

    public static string FormatDisplayRegionName(string value) => DisplayNames.Format(value);

    private static string BuildResourceSummary(CardData data)
    {
        List<string> parts = new();
        if (data.leatherGranted > 0) parts.Add($"{data.leatherGranted}<sprite name=\"leather\">");
        if (data.timberGranted > 0) parts.Add($"{data.timberGranted}<sprite name=\"timber\">");
        if (data.mountsGranted > 0) parts.Add($"{data.mountsGranted}<sprite name=\"mounts\">");
        if (data.ironGranted > 0) parts.Add($"{data.ironGranted}<sprite name=\"iron\">");
        if (data.steelGranted > 0) parts.Add($"{data.steelGranted}<sprite name=\"steel\">");
        if (data.mithrilGranted > 0) parts.Add($"{data.mithrilGranted}<sprite name=\"mithril\">");
        if (data.goldGranted > 0) parts.Add($"{data.goldGranted}<sprite name=\"gold\">");
        return string.Join(" ", parts);
    }

}
