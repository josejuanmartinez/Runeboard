using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public class Army
{
    [SerializeField] public Character commander;

    [SerializeField] public int ma = 0;
    [SerializeField] public int ar = 0;
    [SerializeField] public int li = 0;
    [SerializeField] public int hi = 0;
    [SerializeField] public int lc = 0;
    [SerializeField] public int hc = 0;
    [SerializeField] public int ca = 0;
    [SerializeField] public int ws = 0;
    [SerializeField] public int xp = 25;
    [SerializeField] public int specialAbilityProcChance = 100;
    [SerializeField] public List<ArmyTroopAbilityGroup> troopAbilityGroups = new();

    [SerializeField] public bool startingArmy = false;

    public bool killed = false;

    public Army(Character commander, bool startingArmy = false, int ma = 0, int ar = 0, int li = 0, int hi = 0, int lc = 0, int hc = 0, int ca = 0, int ws = 0, int xp = 25, int specialAbilityProcChance = 100)
    {
        this.commander = commander;
        this.startingArmy = startingArmy;
        this.ma = ma;
        this.ar = ar;
        this.li = li;
        this.hi = hi;
        this.lc = lc;
        this.hc = hc;
        this.ca = ca;
        this.ws = ws;
        this.xp = Mathf.Clamp(xp, 0, 100);
        this.specialAbilityProcChance = Mathf.Clamp(specialAbilityProcChance <= 0 ? 100 : specialAbilityProcChance, 1, 100);
        troopAbilityGroups = new();
    }

    public Army(Character commander, TroopsTypeEnum troopsType, int amount, bool startingArmy, int ws = 0, int xp = 25, IEnumerable<ArmySpecialAbilityEnum> specialAbilities = null, string troopName = null, int specialAbilityProcChance = 100)
    {
        this.commander = commander;
        this.startingArmy = startingArmy;
        this.xp = Mathf.Clamp(xp, 0, 100);
        this.specialAbilityProcChance = Mathf.Clamp(specialAbilityProcChance <= 0 ? 100 : specialAbilityProcChance, 1, 100);
        troopAbilityGroups = new();

        // Use reflection to set the field based on the enum value
        string fieldName = troopsType.ToString();

        // Get field info using reflection
        var fieldInfo = GetType().GetField(fieldName);

        if (fieldInfo != null)
        {
            // Set the field value to the amount
            fieldInfo.SetValue(this, amount);
        }
        else
        {
            throw new ArgumentException($"Could not find field for troop type: {troopsType}");
        }

        if (ws > 0) this.ws += ws;
        AddTroopGroup(troopsType, amount, troopName, specialAbilities);
    }

    public AlignmentEnum GetAlignment()
    {
        return commander.GetAlignment();
    }

    public void Recruit(Army otherArmy)
    {
        if (otherArmy == null) return;

        EnsureTroopGroupsInitialized();
        otherArmy.EnsureTroopGroupsInitialized();

        ma += otherArmy.ma;
        ar += otherArmy.ar;
        li += otherArmy.li;
        hi += otherArmy.hi;
        lc += otherArmy.lc;
        hc += otherArmy.hc;
        ca += otherArmy.ca;
        ws += otherArmy.ws;
        int totalSize = GetSize() + otherArmy.GetSize();
        if (totalSize > 0)
        {
            xp = Mathf.Clamp(Mathf.RoundToInt((xp * GetSize() + otherArmy.xp * otherArmy.GetSize()) / (float)totalSize), 0, 100);
        }
        if (otherArmy.troopAbilityGroups != null)
        {
            for (int i = 0; i < otherArmy.troopAbilityGroups.Count; i++)
            {
                ArmyTroopAbilityGroup group = otherArmy.troopAbilityGroups[i];
                if (group == null || group.amount <= 0) continue;
                AddTroopGroup(group.troopType, group.amount, group.troopName, group.abilities);
            }
        }
    }
    // showMessage: false during world setup (see Character.CreateArmy's showSpawnMessage for
    // why — the same "unspotted enemy" false-negative applies here).
    public void Recruit(TroopsTypeEnum troopsType, int amount, IEnumerable<ArmySpecialAbilityEnum> specialAbilities = null, string troopName = null, int specialAbilityProcChance = 100, bool showMessage = true)
    {
        // Artifact recruitment bonus for men-at-arms
        if (troopsType == TroopsTypeEnum.ma && commander != null)
        {
            amount += commander.GetTotalRecruitBonusMenAtArms();
        }

        if (showMessage)
            MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"+{amount} <sprite name=\"{troopsType.ToString().ToLower()}\">", Color.green);
        int sizeBefore = GetSize();
        if (troopsType == TroopsTypeEnum.ma) ma += amount;
        if (troopsType == TroopsTypeEnum.ar) ar += amount;
        if (troopsType == TroopsTypeEnum.li) li += amount;
        if (troopsType == TroopsTypeEnum.hi) hi += amount;
        if (troopsType == TroopsTypeEnum.lc) lc += amount;
        if (troopsType == TroopsTypeEnum.hc) hc += amount;
        if (troopsType == TroopsTypeEnum.ca) ca += amount;
        if (troopsType == TroopsTypeEnum.ws) ws += amount;
        int totalSize = sizeBefore + amount;
        if (totalSize > 0)
            xp = Mathf.Clamp(Mathf.RoundToInt((xp * sizeBefore + 25 * amount) / (float)totalSize), 0, 100);
        this.specialAbilityProcChance = Mathf.Clamp(specialAbilityProcChance <= 0 ? 100 : specialAbilityProcChance, 1, 100);
        AddTroopGroup(troopsType, amount, troopName, specialAbilities);
    }

    // Casualty % = net damage / (own strength x this). It was 10, which capped a fight at
    // roughly a tenth of an army and made wars long, unreadable attrition; at 3 a clearly
    // superior force breaks its enemy in two or three battles.
    public const float CombatLethalityDivisor = 3f;

    public int GetSize(bool withoutWs = false)
    {
        int result = ma + ar + li + hi + lc + hc + ca;
        result += withoutWs ? 0 : ws;
        return result;
    }

    public string GetHoverText()
    {
        List<string> result = BuildTroopHoverLines();

        string xpText = GetXpHoverText();

        return $" {string.Join(',', result)}{xpText}";
    }

    public string GetHoverTextHexInfo()
    {
        List<string> lines = BuildTroopHoverLines().Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        string xpText = GetXpHoverText();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append("\n").Append(lines[i]);
            if (i == lines.Count - 1) sb.Append(xpText);
        }
        return sb.ToString();
    }

    public string GetHoverTextNoXp()
    {
        return $" {string.Join(',', BuildTroopHoverLines())}";
    }

    // Same content as GetHoverTextHexInfo(), but keeps each troop group's display line paired
    // with its card name so callers can wrap every troop in its own TMPro link instead of one
    // link spanning the whole army (which made hover/lookup treat every card name in the army
    // as a single concatenated blob rather than separately clickable/hoverable cards).
    public List<(string troopName, string line)> GetLinkableTroopHoverLines()
    {
        List<ArmyTroopAbilityGroup> groups = GetTroopGroups();
        string xpText = GetXpHoverText();
        var result = new List<(string troopName, string line)>();
        for (int i = 0; i < groups.Count; i++)
        {
            string line = BuildTroopHoverLine(groups[i]);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (i == groups.Count - 1) line += xpText;
            string troopName = !string.IsNullOrWhiteSpace(groups[i].troopName) ? groups[i].troopName : GetDefaultTroopName(groups[i].troopType);
            result.Add((troopName, line));
        }
        return result;
    }

    private string GetXpHoverText()
    {
        string color = xp < 25 ? "#ff4d4d" : xp < 50 ? "#ffb347" : xp < 75 ? "#8fd14f" : "#00c853";
        return $" XP <color={color}>{xp}</color>";
    }

    public string GetTrainingLabel()
    {
        if (xp < 25) return "low trained";
        if (xp < 50) return "skilled";
        if (xp < 75) return "well trained";
        return "elite";
    }

    public bool IsCavalryOnly()
    {
        if (lc < 1 && hc < 1) return false;
        if (ma > 0) return false;
        if (ar > 0) return false;
        if (li > 0) return false;
        if (hi > 0) return false;
        if (ca > 0) return false;
        if (ws > 0) return false;
        return true;
    }

    public MovementType GetMovementType()
    {
        return IsCavalryOnly() ? MovementType.ArmyCommanderCavalryOnly : MovementType.ArmyCommander;
    }

    public void AddXp(int amount, string reason = null)
    {
        int before = xp;
        xp = Mathf.Clamp(xp + amount, 0, 100);
        if (xp == before) return;

        if (commander != null && commander.isPlayerControlled && commander.hex != null)
        {
            string text = $"Army XP {(xp > before ? "+" : "")}{xp - before}";
            if (!string.IsNullOrWhiteSpace(reason)) text += $" ({reason})";
            Color color = xp > before ? Color.green : Color.red;
            MessageDisplayNoUI.ShowMessage(commander.hex, commander, text, color);
        }
    }

    public void Killed(Leader killedBy, bool onlyMark = false)
    {
        killed = true;
        int wound = UnityEngine.Random.Range(0, 100);
        // knownIdentity: true — this army just died in combat; whoever they were fighting
        // already knows exactly who this commander is, so there's nothing left to "spot".
        MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"{commander.characterName} army was killed and {commander.characterName} wounded by {wound}", Color.red, knownIdentity: true);
        if(!onlyMark && commander.hex.armies.Contains(this)) commander.hex.armies.Remove(this);        
        commander.hex.RedrawCharacters();
        commander.hex.RedrawArmies();
        ma = 0;
        ar = 0;
        li = 0;
        hi = 0;
        lc = 0;
        hc = 0;
        ca = 0;
        ws = 0;
        troopAbilityGroups?.Clear();
        commander.Wounded(killedBy, wound);
        commander = null;
    }

    public int GetStrength()
    {
        return GetStrengthAgainst(null);
    }

    public int GetStrengthAgainst(Army enemyArmy)
    {
        // An army can briefly outlive its commander/placement while scenario state or combat
        // teardown is being reconciled. It has no usable strength during that interval.
        if (commander == null || commander.hex == null) return 0;

        int strength = 0;
        if (commander.hex.IsWaterTerrain())
        {
            strength += (ma + ar + li + hi + lc + hc + ca) * ArmyData.transportedStrength;
            strength += ws * ArmyData.warshipStrength;
        }
        else
        {
            strength += ma * ArmyData.troopsStrength[TroopsTypeEnum.ma];
            strength += ar * ArmyData.troopsStrength[TroopsTypeEnum.ar];
            strength += li * ArmyData.troopsStrength[TroopsTypeEnum.li];
            strength += hi * ArmyData.troopsStrength[TroopsTypeEnum.hi];
            strength += lc * ArmyData.troopsStrength[TroopsTypeEnum.lc];
            strength += hc * ArmyData.troopsStrength[TroopsTypeEnum.hc];
            // Catapults hit harder when attacking an enemy PC
            PC pc = commander.hex.GetPC();
            bool attackingEnemyPc = pc?.owner != null && pc.owner.GetAlignment() != GetAlignment();
            strength += attackingEnemyPc
                ? ca * ArmyData.troopsStrength[TroopsTypeEnum.ca] * ArmyData.catapultStrengthMultiplierInPC
                : ca * ArmyData.troopsStrength[TroopsTypeEnum.ca];
        }
        Leader owner = commander.GetOwner();
        LeaderBiomeConfig biome = owner?.GetBiome();
        if (biome?.noScenarioStart != null && biome.noScenarioStart.terrain == commander.hex.terrainType)
            strength *= ArmyData.biomeTerrainMultiplier;

        strength = ApplyCommanderBonus(strength);
        strength = ApplyTrainingBonus(strength);
        strength = ApplyArtifactAttackBonus(strength, enemyArmy);
        strength = ApplyStatusAttackBonus(strength);
        strength = ApplyChargingAttackModifier(strength, enemyArmy);
        return strength;
    }

    public int GetOffence()
    {
        return GetStrength();
    }


    public int GetDefence()
    {
        return GetDefenceAgainst(null);
    }

    public int GetDefenceAgainst(Army enemyArmy)
    {
        if (commander == null || commander.hex == null) return 0;

        int defence = 0;
        if (commander.hex.IsWaterTerrain())
        {
            defence += (ma + ar + li + hi + lc + hc + ca) * ArmyData.transportedStrength;
            defence += ws * ArmyData.warshipStrength;
        }
        else
        {
            defence += ma * ArmyData.troopsDefence[TroopsTypeEnum.ma];
            defence += ar * ArmyData.troopsDefence[TroopsTypeEnum.ar];
            defence += li * ArmyData.troopsDefence[TroopsTypeEnum.li];
            defence += hi * ArmyData.troopsDefence[TroopsTypeEnum.hi];
            defence += lc * ArmyData.troopsDefence[TroopsTypeEnum.lc];
            defence += hc * ArmyData.troopsDefence[TroopsTypeEnum.hc];
            // CA usual defence even if it's PC
            defence += ca * ArmyData.troopsDefence[TroopsTypeEnum.ca];
        }
        Leader owner = commander.GetOwner();
        LeaderBiomeConfig biome = owner?.GetBiome();
        if (biome?.noScenarioStart != null && biome.noScenarioStart.terrain == commander.hex.terrainType)
            defence *= ArmyData.biomeTerrainMultiplier;

        defence = ApplyCommanderBonus(defence);
        defence = ApplyTrainingBonus(defence);
        defence = ApplyArtifactDefenseBonus(defence, enemyArmy);
        defence = ApplyEnemyArtifactDefensePenalty(defence);
        defence = ApplyStatusDefenseBonus(defence);
        defence = ApplyPikemenDefenseModifier(defence, enemyArmy);
        return defence;
    }

    private int ApplyCommanderBonus(int value)
    {
        int commanderLevel = commander != null ? commander.GetCommander() : 0;
        // Each commander level adds 5% to both offence and defence for this army (max +50%).
        float bonusMultiplier = 1f + Mathf.Clamp(commanderLevel, 0, 10) * 0.05f;
        return Mathf.Max(0, Mathf.RoundToInt(value * bonusMultiplier));
    }

    private int ApplyTrainingBonus(int value)
    {
        // Training scales 0.75x at 0 XP to 1.25x at 100 XP
        float trainingMultiplier = 0.75f + Mathf.Clamp(xp, 0, 100) / 200f;
        return Mathf.Max(0, Mathf.RoundToInt(value * trainingMultiplier));
    }

    private int ApplyArtifactAttackBonus(int value, Army enemyArmy)
    {
        if (commander == null) return value;
        int bonus = commander.objects.Sum(a => a != null ? a.GetAttackBonus() : 0) * 3;
        // Army-dedicated bonuses (Banners, Horn of Gondor) get the same x3 treatment as a
        // generic weapon's bonusAttack so a card explicitly themed around army command isn't
        // worth less to an army fight than a sword. See balance review, 2026-08-06.
        bonus += commander.objects.Sum(a => a != null ? a.GetArmyAttackStrengthBonus() : 0) * 3;

        if (enemyArmy != null && enemyArmy.commander != null)
        {
            RacesEnum enemyRace = enemyArmy.commander.race;
            foreach (var artifact in commander.objects)
            {
                if (artifact == null) continue;
                bonus += artifact.GetAttackBonusVsRace(enemyRace) * 3;
            }

            var enemyTroops = enemyArmy.GetTroopGroups();
            foreach (var artifact in commander.objects)
            {
                if (artifact == null) continue;
                foreach (var group in enemyTroops)
                {
                    if (group == null) continue;
                    bonus += artifact.GetAttackBonusVsTroopType(group.troopType) * 3;
                }
            }
        }

        return Mathf.Max(0, value + bonus);
    }

    private int ApplyArtifactDefenseBonus(int value, Army enemyArmy)
    {
        if (commander == null) return value;
        int bonus = commander.objects.Sum(a => a != null ? a.GetDefenseBonus() : 0) * 3;
        // See ApplyArtifactAttackBonus: Banners/Horn now get the same x3 as a generic bonusDefense.
        bonus += commander.objects.Sum(a => a != null ? a.GetArmyDefenseStrengthBonus() : 0) * 3;

        if (enemyArmy != null && enemyArmy.commander != null)
        {
            RacesEnum enemyRace = enemyArmy.commander.race;
            foreach (var artifact in commander.objects)
            {
                if (artifact == null) continue;
                bonus += artifact.GetDefenseBonusVsRace(enemyRace) * 3;
            }

            var enemyTroops = enemyArmy.GetTroopGroups();
            foreach (var artifact in commander.objects)
            {
                if (artifact == null) continue;
                foreach (var group in enemyTroops)
                {
                    if (group == null) continue;
                    bonus += artifact.GetDefenseBonusVsTroopType(group.troopType) * 3;
                }
            }
        }

        return Mathf.Max(0, value + bonus);
    }

    private int ApplyEnemyArtifactDefensePenalty(int value)
    {
        if (commander == null || commander.hex == null || commander.hex.armies == null) return value;

        int penalty = 0;
        for (int i = 0; i < commander.hex.armies.Count; i++)
        {
            Army otherArmy = commander.hex.armies[i];
            if (otherArmy == null || otherArmy == this || otherArmy.killed || otherArmy.commander == null || otherArmy.commander.killed) continue;
            if (!otherArmy.commander.IsArmyCommander()) continue;
            if (otherArmy.commander.GetOwner() == commander.GetOwner()) continue;

            AlignmentEnum otherAlignment = otherArmy.commander.GetAlignment();
            if (otherAlignment == commander.GetAlignment() && otherAlignment != AlignmentEnum.neutral) continue;

            penalty += otherArmy.commander.objects.Sum(a => a != null ? a.GetEnemyArmyDefensePenaltySameHex() : 0);
        }

        return Mathf.Max(0, value - penalty);
    }

    private int ApplyStatusAttackBonus(int value)
    {
        if (commander != null && commander.HasStatusEffect(StatusEffectEnum.Strengthened))
        {
            value = Mathf.RoundToInt(value * 1.10f);
        }
        if (commander != null && commander.HasStatusEffect(StatusEffectEnum.Frozen))
        {
            float attackFactor = EnvironmentalCardManager.Instance?.FrozenCombatAttackFactor ?? 1f;
            if (attackFactor < 1f) value = Mathf.RoundToInt(value * attackFactor);
        }
        float globalFactor = EnvironmentalCardManager.Instance?.GlobalArmyAttackFactor ?? 1f;
        if (globalFactor != 1f) value = Mathf.RoundToInt(value * globalFactor);
        EnvironmentalCardManager env = EnvironmentalCardManager.Instance;
        if (env != null)
        {
            float alignFactor = GetAlignment() == AlignmentEnum.freePeople ? env.FreePeopleArmyAttackFactor
                : GetAlignment() == AlignmentEnum.darkServants ? env.DarkServantsArmyAttackFactor
                : 1f;
            if (alignFactor != 1f) value = Mathf.RoundToInt(value * alignFactor);
        }
        return Mathf.Max(0, value);
    }

    private int ApplyStatusDefenseBonus(int value)
    {
        if (commander != null && commander.HasStatusEffect(StatusEffectEnum.Fortified))
        {
            value = Mathf.RoundToInt(value * 1.10f);
        }
        if (commander != null && commander.HasStatusEffect(StatusEffectEnum.Frozen))
        {
            float baseFrozen = commander.hex != null && commander.hex.terrainType == TerrainEnum.mountains ? 0.75f : 0.90f;
            float extraFactor = EnvironmentalCardManager.Instance?.FrozenCombatDefenseExtraFactor ?? 1f;
            value = Mathf.RoundToInt(value * baseFrozen * extraFactor);
        }
        return Mathf.Max(0, value);
    }

    private int ApplyPikemenDefenseModifier(int value, Army enemyArmy)
    {
        int pikemenCount = GetAbilityTroopCount(ArmySpecialAbilityEnum.Pikemen);
        if (pikemenCount <= 0) return value;

        float pikemenRatio = GetAbilityCoverageRatio(ArmySpecialAbilityEnum.Pikemen);
        bool enemyHasCavalry = enemyArmy != null && enemyArmy.HasCavalryTroops();
        float multiplier = enemyHasCavalry
            ? Mathf.Lerp(1f, 1.30f, pikemenRatio)
            : Mathf.Lerp(1f, 0.90f, pikemenRatio);
        return Mathf.Max(0, Mathf.RoundToInt(value * multiplier));
    }

    private int ApplyChargingAttackModifier(int value, Army enemyArmy)
    {
        int chargingCount = GetAbilityTroopCount(ArmySpecialAbilityEnum.Charging);
        if (chargingCount <= 0) return value;
        if (enemyArmy == null || commander == null || commander.hex == null || commander.hex.IsWaterTerrain()) return value;

        float chargingRatio = GetAbilityCoverageRatio(ArmySpecialAbilityEnum.Charging);
        if (enemyArmy.GetAbilityTroopCount(ArmySpecialAbilityEnum.Pikemen) > 0)
        {
            float enemyPikemenRatio = enemyArmy.GetAbilityCoverageRatio(ArmySpecialAbilityEnum.Pikemen);
            float modifier = Mathf.Lerp(1f, 0.80f, Mathf.Max(chargingRatio, enemyPikemenRatio));
            return Mathf.Max(0, Mathf.RoundToInt(value * modifier));
        }

        if (enemyArmy.GetAbilityTroopCount(ArmySpecialAbilityEnum.Shielded) > 0)
        {
            float enemyShieldedRatio = enemyArmy.GetAbilityCoverageRatio(ArmySpecialAbilityEnum.Shielded);
            float modifier = Mathf.Lerp(1.30f, 1f, enemyShieldedRatio);
            modifier = Mathf.Lerp(1f, modifier, chargingRatio);
            return Mathf.Max(0, Mathf.RoundToInt(value * modifier));
        }

        return Mathf.Max(0, Mathf.RoundToInt(value * Mathf.Lerp(1f, 1.30f, chargingRatio)));
    }

    public TroopsTypeEnum? RemoveRandomTroop()
    {
        List<TroopsTypeEnum> available = new();
        if (ma > 0) available.Add(TroopsTypeEnum.ma);
        if (ar > 0) available.Add(TroopsTypeEnum.ar);
        if (li > 0) available.Add(TroopsTypeEnum.li);
        if (hi > 0) available.Add(TroopsTypeEnum.hi);
        if (lc > 0) available.Add(TroopsTypeEnum.lc);
        if (hc > 0) available.Add(TroopsTypeEnum.hc);
        if (ca > 0) available.Add(TroopsTypeEnum.ca);

        if (available.Count == 0) return null;

        TroopsTypeEnum troop = available[UnityEngine.Random.Range(0, available.Count)];
        switch (troop)
        {
            case TroopsTypeEnum.ma:
                ma = Math.Max(0, ma - 1);
                break;
            case TroopsTypeEnum.ar:
                ar = Math.Max(0, ar - 1);
                break;
            case TroopsTypeEnum.li:
                li = Math.Max(0, li - 1);
                break;
            case TroopsTypeEnum.hi:
                hi = Math.Max(0, hi - 1);
                break;
            case TroopsTypeEnum.lc:
                lc = Math.Max(0, lc - 1);
                break;
            case TroopsTypeEnum.hc:
                hc = Math.Max(0, hc - 1);
                break;
            case TroopsTypeEnum.ca:
                ca = Math.Max(0, ca - 1);
                break;
        }

        if (GetSize(true) < 1)
        {
            Killed(commander != null ? commander.GetOwner() : null);
        }
        else
        {
            commander?.hex?.RedrawArmies();
            commander?.RefreshSelectedCharacterIconIfSelected();
        }

        RemoveSpecialTroops(troop, 1);
        return troop;
    }

    public TroopsTypeEnum? RemoveRandomTroopOfTypes(params TroopsTypeEnum[] troopTypes)
    {
        if (troopTypes == null || troopTypes.Length == 0) return null;

        List<TroopsTypeEnum> available = troopTypes
            .Where(troopType => GetTroopCount(troopType) > 0)
            .Distinct()
            .ToList();
        if (available.Count == 0) return null;

        TroopsTypeEnum troop = available[UnityEngine.Random.Range(0, available.Count)];
        switch (troop)
        {
            case TroopsTypeEnum.ma:
                ma = Math.Max(0, ma - 1);
                break;
            case TroopsTypeEnum.ar:
                ar = Math.Max(0, ar - 1);
                break;
            case TroopsTypeEnum.li:
                li = Math.Max(0, li - 1);
                break;
            case TroopsTypeEnum.hi:
                hi = Math.Max(0, hi - 1);
                break;
            case TroopsTypeEnum.lc:
                lc = Math.Max(0, lc - 1);
                break;
            case TroopsTypeEnum.hc:
                hc = Math.Max(0, hc - 1);
                break;
            case TroopsTypeEnum.ca:
                ca = Math.Max(0, ca - 1);
                break;
            case TroopsTypeEnum.ws:
                ws = Math.Max(0, ws - 1);
                break;
        }

        if (GetSize(true) < 1)
        {
            Killed(commander != null ? commander.GetOwner() : null);
        }
        else
        {
            commander?.hex?.RedrawArmies();
            commander?.RefreshSelectedCharacterIconIfSelected();
        }

        RemoveSpecialTroops(troop, 1);
        return troop;
    }

    public int GetArtifactAttackBonusTotal()
    {
        if (commander == null) return 0;
        return commander.objects.Sum(a => a != null ? a.GetAttackBonus() : 0) * 3;
    }

    public int GetArtifactDefenseBonusTotal()
    {
        if (commander == null) return 0;
        return commander.objects.Sum(a => a != null ? a.GetDefenseBonus() : 0) * 3;
    }

    private void GrantCombatXp(Army army, string reason)
    {
        if (army == null || army.killed || army.GetSize(true) < 1) return;
        int commanderLevel = army.commander != null ? army.commander.GetCommander() : 0;
        int gain = Mathf.Clamp(UnityEngine.Random.Range(1, 6) + commanderLevel, 1, 10);
        army.AddXp(gain, reason);
    }

    /// <summary>
    /// Approximate outcome of attacking <paramref name="targetHex"/> now, using the same strength,
    /// defence and casualty formula as ProcessCombat (special abilities and random procs excluded).
    /// Returns null when there is nothing to fight there.
    /// </summary>
    public string PredictAttackSummary(Hex targetHex)
    {
        if (targetHex == null || commander == null || killed) return null;
        AlignmentEnum alignment = commander.GetAlignment();
        Army defender = targetHex.armies.FirstOrDefault(a => a != null && !a.killed && a.commander != null && !a.commander.killed
            && a.commander.GetOwner() != commander.GetOwner()
            && (a.GetAlignment() != alignment || alignment == AlignmentEnum.neutral || a.GetAlignment() == AlignmentEnum.neutral));
        PC pc = targetHex.GetPC();
        if (defender == null && (pc == null || pc.owner == commander.GetOwner())) return null;

        int myStrength = defender != null ? GetStrengthAgainst(defender) : GetStrength();
        int myDefence = defender != null ? GetDefenceAgainst(defender) : GetDefence();
        int theirStrength = 0, theirDefence = 0;
        if (defender != null)
        {
            theirStrength = defender.GetStrengthAgainst(this);
            theirDefence = defender.GetDefenceAgainst(this);
            AlignmentEnum defenderAlignment = defender.GetAlignment();
            foreach (Army ally in targetHex.armies)
            {
                if (ally == null || ally == defender || ally.killed || ally.commander == null) continue;
                if (ally.GetAlignment() == defenderAlignment && defenderAlignment != AlignmentEnum.neutral)
                {
                    theirStrength += ally.GetStrengthAgainst(this);
                    theirDefence += ally.GetDefenceAgainst(this);
                }
            }
        }
        if (pc != null && pc.owner != null && (defender == null || pc.owner.GetAlignment() == defender.GetAlignment()))
        {
            theirStrength += pc.GetDefense();
            theirDefence += pc.GetDefense();
        }

        float myPct = Mathf.Clamp01(Mathf.Max(0, theirStrength - myDefence) / (Mathf.Max(1, myStrength) * CombatLethalityDivisor));
        float theirPct = defender != null
            ? Mathf.Clamp01(Mathf.Max(0, myStrength - theirDefence) / (Mathf.Max(1, defender.GetStrengthAgainst(this)) * CombatLethalityDivisor))
            : 0f;
        int myLosses = CalculateTotalCasualties(myPct);
        int theirLosses = defender != null ? defender.CalculateTotalCasualties(theirPct) : 0;
        float ratio = myStrength / Mathf.Max(1f, theirDefence);
        string odds = ratio >= 1.5f ? "<color=#9BD37A>favourable</color>" : ratio >= 0.9f ? "<color=#E8C66A>even</color>" : "<color=#E07A5F>poor</color>";
        string target = defender != null ? defender.commander.characterName : DisplayNames.Format(pc.pcName);
        return defender != null
            ? $"Odds vs {target}: {odds}. Expected losses: you ~{myLosses}, them ~{theirLosses}."
            : $"Odds vs {target}'s walls: {odds}. Expected losses: you ~{myLosses}.";
    }

    public void Attack(Hex targetHex)
    {
        Leader attackerLeader = commander.GetOwner();
        if(this == null || this.commander == null || killed || this.commander.killed || attackerLeader == null || attackerLeader.killed) return;
        // Get the attacker's alignment
        AlignmentEnum attackerAlignment = commander.GetAlignment();
        bool attackerIsNonPlayable = attackerLeader is NonPlayableLeader;

        // Calculate attacker's base strength
        int attackerStrength = GetStrength();

        // Calculate attacker's defense for counter-attack
        int attackerDefence = GetDefence();
        int attackerAlliesJoined = 0;
        int attackerAlliesStrength = 0;
        int attackerAlliesDefence = 0;

        // Add strength of allied armies in the same hex
        if (commander != null && commander.hex != null)
        {
            foreach (Army ally in commander.hex.armies)
            {
                // Skip the attacker itself
                if (ally != null && ally != this && !ally.killed && ally.commander != null && !ally.commander.killed)
                {
                    // For non-neutral alignments: include armies with same alignment
                    // For neutral alignment: include only armies with same owner
                    if ((attackerAlignment != AlignmentEnum.neutral && ally.GetAlignment() == attackerAlignment) ||
                        (attackerAlignment == AlignmentEnum.neutral && ally.GetAlignment() == AlignmentEnum.neutral && ally.commander.GetOwner() == commander.GetOwner()))
                    {
                        int allyStrength = ally.GetStrength();
                        int allyDefence = ally.GetDefence();
                        attackerStrength += allyStrength;
                        attackerDefence += allyDefence;
                        attackerAlliesJoined++;
                        attackerAlliesStrength += allyStrength;
                        attackerAlliesDefence += allyDefence;
                    }
                }
            }
        }

        // Check if there are any armies to attack in the target hex
        bool foundDefenders = false;

          // First, handle all enemy armies in the hex using a snapshot to avoid collection modification issues
          List<Army> defenderSnapshot = new List<Army>(targetHex.armies);
          Debug.Log($"[AbilityCheck] targetHex.armies at {targetHex.GetBattleLocationLabel()}: " + string.Join(" | ", defenderSnapshot.Select(a =>
              $"Army#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a)} commander={(a.commander != null ? $"{a.commander.characterName}#{a.commander.GetEntityId()} (owner={a.commander.GetOwner()?.characterName ?? "null"})" : "null")} killed={a.killed}")));
          foreach (Army defenderArmy in defenderSnapshot)
          {
            if(defenderArmy == null || defenderArmy.commander == null || defenderArmy.killed || defenderArmy.commander.killed || defenderArmy.killed) continue;
            // Don't attack your own armies (check ownership)
            bool isOwnArmy = defenderArmy.commander.GetOwner() == commander.GetOwner();

            // Never allow NonPlayableLeader vs NonPlayableLeader combat
            if (attackerIsNonPlayable && defenderArmy.commander.GetOwner() is NonPlayableLeader)
            {
                continue;
            }

            // Attack if defender has different alignment OR if attacker is neutral (attacks everyone)
            // OR if defender is neutral (everyone attacks neutrals)
            // BUT never attack your own armies
            if (!isOwnArmy && (defenderArmy.GetAlignment() != attackerAlignment ||
                attackerAlignment == AlignmentEnum.neutral ||
                defenderArmy.GetAlignment() == AlignmentEnum.neutral))
            {
                // Process combat against this defender
                ProcessCombat(targetHex, defenderArmy, attackerStrength, attackerDefence, attackerLeader, attackerAlliesJoined, attackerAlliesStrength, attackerAlliesDefence);
                foundDefenders = true;
            }
        }

        // If no enemy armies were found, check if there's a PC to attack
        if (!foundDefenders && targetHex.GetPC() != null){
            Leader defenderLeader = targetHex.GetPC().owner;
            AlignmentEnum pcAlignment = defenderLeader != null ? defenderLeader.GetAlignment() : AlignmentEnum.neutral;

            // Don't attack your own PC (check both alignment and owner)
            bool isOwnPC = commander && !commander.killed ? defenderLeader == commander.GetOwner() : false;

            // Never allow NonPlayableLeader vs NonPlayableLeader PC attacks
            if (attackerIsNonPlayable && defenderLeader is NonPlayableLeader)
            {
                return;
            }

            // Attack PC if it has different alignment OR if attacker is neutral (attacks all)
            // OR if PC is neutral (everyone attacks neutrals)
            // BUT never attack your own PC
            if (!isOwnPC && (defenderLeader == null || defenderLeader.killed ||
                pcAlignment != attackerAlignment ||
                attackerAlignment == AlignmentEnum.neutral ||
                pcAlignment == AlignmentEnum.neutral))
            {
                AttackPopulationCenter(targetHex, attackerStrength, attackerDefence, attackerLeader);
            }
        }


        // Redraw visuals
        targetHex.RedrawCharacters();
        targetHex.RedrawArmies();
        // Not needed: if captured or decreased or something, it will refresh automatically
        // targetHex.RedrawPC();
    }

    // Helper method to process combat between attacker and a specific defender
    private void ProcessCombat(Hex targetHex, Army defenderArmy, int attackerStrength, int attackerDefence, Leader attackerLeader, int attackerAlliesJoined, int attackerAlliesStrength, int attackerAlliesDefence)
    {
        if(defenderArmy == null || defenderArmy.commander == null || defenderArmy.killed || defenderArmy.commander.killed || attackerLeader == null || attackerLeader.killed) return;
        bool attackerIsPlayer = commander != null && commander.isPlayerControlled;
        bool defenderIsPlayer = defenderArmy.commander != null && defenderArmy.commander.isPlayerControlled;
        bool playerInvolved = attackerIsPlayer || defenderIsPlayer;
        if (playerInvolved)
        {
            Music.Instance?.PlayBattleMusic();
        }
        AlignmentEnum attackerAlignment = commander.GetAlignment();
        string attackerName = commander != null ? commander.characterName : attackerLeader.characterName;
        // Captured now, before casualties can wipe either side's army — Army.Killed() (called
        // from ReceiveCasualties/ApplyCasualtiesToDefenders below if a side's troops hit 0)
        // nulls that Army's own `commander` field, so `commander`/`defenderArmy.commander` can
        // no longer be trusted for the rest of this method once casualties are applied. The
        // Character objects themselves are unaffected by that and stay valid for CombatBanner.
        Character attackerCommander = commander;
        Character defenderCommander = defenderArmy.commander;
        string defenderName = defenderCommander.characterName;
        int attackerXpBefore = xp;
        int defenderXpBefore = defenderArmy.xp;
        Character hudActor = commander;

        Leader defenderLeader = defenderArmy.commander.GetOwner();
        AlignmentEnum defenderAlignment = defenderArmy.GetAlignment();

        // Snapshotted before abilities trigger below so CombatBanner can tell "already had this
        // status effect" apart from "just gained it from this fight" (see attacker/defenderNewStatusEffects).
        List<StatusEffectEnum> attackerStatusBefore = new(commander.statusEffects);
        List<StatusEffectEnum> defenderStatusBefore = new(defenderArmy.commander.statusEffects);

        List<(string message, Color color)> battleAbilityMessages = new();
        List<string> battleAbilityNarration = new();
        TriggerBattleSpecialAbilities(targetHex, defenderArmy, battleAbilityMessages, battleAbilityNarration);
        if (killed || commander == null || commander.killed || defenderArmy == null || defenderArmy.killed || defenderArmy.commander == null || defenderArmy.commander.killed) return;

        List<StatusEffectEnum> attackerNewStatusEffects = commander.statusEffects.Except(attackerStatusBefore).ToList();
        List<StatusEffectEnum> defenderNewStatusEffects = defenderArmy.commander.statusEffects.Except(defenderStatusBefore).ToList();

        attackerStrength = GetStrengthAgainst(defenderArmy);
        attackerDefence = GetDefenceAgainst(defenderArmy);
        attackerAlliesJoined = 0;
        attackerAlliesStrength = 0;
        attackerAlliesDefence = 0;

        if (commander != null && commander.hex != null)
        {
            foreach (Army ally in commander.hex.armies)
            {
                if (ally == null || ally == this || ally.killed || ally.commander == null || ally.commander.killed) continue;
                if ((attackerAlignment != AlignmentEnum.neutral && ally.GetAlignment() == attackerAlignment) ||
                    (attackerAlignment == AlignmentEnum.neutral && ally.GetAlignment() == AlignmentEnum.neutral && ally.commander.GetOwner() == commander.GetOwner()))
                {
                    int allyStrength = ally.GetStrengthAgainst(defenderArmy);
                    int allyDefence = ally.GetDefenceAgainst(defenderArmy);
                    attackerStrength += allyStrength;
                    attackerDefence += allyDefence;
                    attackerAlliesJoined++;
                    attackerAlliesStrength += allyStrength;
                    attackerAlliesDefence += allyDefence;
                }
            }
        }

        int defenderDefense = defenderArmy.GetDefenceAgainst(this);
        int defenderStrength = defenderArmy.GetStrengthAgainst(this);
        int attackerArtifactAttack = GetArtifactAttackBonusTotal();
        int attackerArtifactDefense = GetArtifactDefenseBonusTotal();
        int defenderArtifactAttack = defenderArmy.GetArtifactAttackBonusTotal();
        int defenderArtifactDefense = defenderArmy.GetArtifactDefenseBonusTotal();
        int defenderAlliesJoined = 0;
        int defenderAlliesStrength = 0;
        int defenderAlliesDefence = 0;
        int pcDefenseContribution = 0;

        // Add defense from allied armies in the defender's hex
        foreach (Army ally in targetHex.armies)
        {
            // Skip the primary defender itself
            if (ally != defenderArmy)
            {
                // For non-neutral alignments: include armies with same alignment
                // For neutral alignment: include only armies with same owner
                if ((defenderAlignment != AlignmentEnum.neutral && ally.GetAlignment() == defenderAlignment) ||
                    (defenderAlignment == AlignmentEnum.neutral && ally.GetAlignment() == AlignmentEnum.neutral && ally.commander.GetOwner() == defenderArmy.commander.GetOwner()))
                {
                    int allyDefence = ally.GetDefenceAgainst(this);
                    int allyStrength = ally.GetStrengthAgainst(this);
                    defenderDefense += allyDefence;
                    defenderStrength += allyStrength;
                    defenderAlliesJoined++;
                    defenderAlliesStrength += allyStrength;
                    defenderAlliesDefence += allyDefence;
                }
            }
        }

        // Add defense from Population Center if it exists and is aligned with defender
        if (targetHex.GetPC() != null && targetHex.GetPC().owner.GetAlignment() == defenderAlignment)
        {
            pcDefenseContribution = targetHex.GetPC().GetDefense();
            defenderDefense += pcDefenseContribution;
            defenderStrength += pcDefenseContribution; // PC contributes to counter-attack
        }

        int attackerCommanderLevel = commander != null ? commander.GetCommander() : 0;
        int defenderCommanderLevel = defenderArmy.commander != null ? defenderArmy.commander.GetCommander() : 0;
        int attackerBonusPercent = Mathf.RoundToInt(Mathf.Clamp(attackerCommanderLevel, 0, 10) * 10f);
        int defenderBonusPercent = Mathf.RoundToInt(Mathf.Clamp(defenderCommanderLevel, 0, 10) * 10f);

        // Calculate raw damage values
        float attackerDamage = Math.Max(0, attackerStrength - defenderDefense);
        float defenderDamage = Math.Max(0, defenderStrength - attackerDefence);

        // Calculate casualty percentages (as decimals)
        float attackerCasualtyPercent = defenderDamage / (attackerStrength * CombatLethalityDivisor);
        float defenderCasualtyPercent = attackerDamage / (defenderStrength * CombatLethalityDivisor);

        // Clamp casualty percentages between 0 and 1
        attackerCasualtyPercent = Math.Clamp(attackerCasualtyPercent, 0, 1);
        defenderCasualtyPercent = Math.Clamp(defenderCasualtyPercent, 0, 1);

        // Calculate actual casualties that would occur
        int attackerTotalLosses = CalculateTotalCasualties(attackerCasualtyPercent);
        int defenderTotalLosses = defenderArmy.CalculateTotalCasualties(defenderCasualtyPercent);

        // Check if either side will actually lose units
        bool anyAttackerCasualties = attackerTotalLosses > 0;
        bool anyDefenderCasualties = defenderTotalLosses > 0;

        // If no one is taking casualties, decide who should lose one unit
        bool forceCasualtySide = false;
        if (!anyAttackerCasualties && !anyDefenderCasualties)
        {
            // Compare strengths to decide who loses one unit
            forceCasualtySide = attackerStrength >= defenderStrength;
        }
        string stalemateNote = null;

        if (!anyAttackerCasualties && !anyDefenderCasualties)
        {
            if (forceCasualtySide)
            {
                stalemateNote = $"{defenderCommander.characterName} loses a small band in the grinding stalemate, a bitter price for no ground gained.";
            }
            else
            {
                stalemateNote = $"{attackerName} loses a small band in the grinding stalemate, a bitter price for no ground gained.";
            }
        }

        CasualtyBreakdown attackerLosses = CalculateCasualtyBreakdown(attackerCasualtyPercent, !anyAttackerCasualties && !forceCasualtySide);
        CasualtyBreakdown defenderLosses = defenderArmy.CalculateCasualtyBreakdown(defenderCasualtyPercent, !anyDefenderCasualties && forceCasualtySide);
        string attackerLossesText = BuildLossesShort(attackerLosses);
        string defenderLossesText = BuildLossesShort(defenderLosses);

        string battleLocation = targetHex.GetBattleLocationLabel();
        bool shouldShowPopup = playerInvolved || PlayerCanSeeHex(targetHex);
        List<(string message, Color color)> hudMessages = new();
        if (attackerLosses.Any) hudMessages.Add(($"{attackerName} losses: {attackerLossesText}.", Color.red));
        if (defenderLosses.Any) hudMessages.Add(($"{defenderName} losses: {defenderLossesText}.", Color.red));
        hudMessages.AddRange(battleAbilityMessages);

        // Before casualties, so the attacker swings before anyone falls.
        UnitAnimations.Combat(attackerCommander, defenderCommander, defenderHeld: !anyDefenderCasualties);

        // Apply casualties to attacker troops
        ReceiveCasualties(attackerCasualtyPercent, defenderLeader, !anyAttackerCasualties && !forceCasualtySide);

        // Apply casualties to all defending armies based on their alignment
        ApplyCasualtiesToDefenders(targetHex, defenderCasualtyPercent, attackerAlignment, !anyDefenderCasualties && forceCasualtySide, attackerLeader);

        GrantCombatXp(this, "Combat");
        GrantCombatXp(defenderArmy, "Combat");

        // Check if attacker army was eliminated
        if (!killed && GetSize(true) < 1) Killed(defenderLeader);

        bool attackerAlive = !killed && GetSize(true) > 0;
        bool defendersRemain = AreDefendersStillAlive(targetHex, attackerAlignment, attackerLeader);
        if (playerInvolved)
        {
            bool playerWin = attackerIsPlayer ? (attackerAlive && !defendersRemain) : (!attackerAlive && defendersRemain);
            if (playerWin)
            {
                Music.Instance?.PlayBattleWonMusic();
            }
            else
            {
                Music.Instance?.PlayBattleMusic();
            }
        }

        int attackerXpDelta = xp - attackerXpBefore;
        int defenderXpDelta = defenderArmy.xp - defenderXpBefore;
        if (shouldShowPopup)
        {
            hudMessages.Add(($"XP gained: {attackerName} {FormatDelta(attackerXpDelta)}, {defenderName} {FormatDelta(defenderXpDelta)}", Color.cyan));
        }

        if (shouldShowPopup)
        {
            List<string> noticeMessages = battleAbilityMessages.Select(m => CombatBanner.Colorize(m.message, m.color)).ToList();
            noticeMessages.AddRange(BuildBattleModifierNotices(
                attackerName, defenderName, pcDefenseContribution,
                attackerAlliesJoined, attackerAlliesStrength, attackerAlliesDefence,
                defenderAlliesJoined, defenderAlliesStrength, defenderAlliesDefence,
                attackerBonusPercent, defenderBonusPercent,
                attackerArtifactAttack, attackerArtifactDefense,
                defenderArtifactAttack, defenderArtifactDefense));

            CombatBanner.Show(
                "Combat", "attacks",
                attackerCommander, defenderCommander,
                anyAttackerCasualties, killed || GetSize(true) < 1,
                anyDefenderCasualties, defenderArmy.killed || defenderArmy.GetSize(true) < 1,
                battleLocation,
                noticeMessages,
                attackerStatusBefore, defenderStatusBefore,
                attackerNewStatusEffects, defenderNewStatusEffects,
                onComplete: () => ShowHudMessagesAfterPopup(targetHex, hudActor, hudMessages));
        }
    }


    private string BuildBattleDescription(
        string location,
        string attackerName,
        string defenderName,
        RacesEnum attackerRace,
        RacesEnum defenderRace,
        int attackerAlliesJoined,
        int attackerAlliesStrength,
        int attackerAlliesDefence,
        int defenderAlliesJoined,
        int defenderAlliesStrength,
        int defenderAlliesDefence,
        int pcDefenseContribution,
        int attackerBonusPercent,
        int defenderBonusPercent,
        string attackerTrainingLabel,
        string defenderTrainingLabel,
        int attackerXp,
        int defenderXp,
        int attackerArtifactAttack,
        int attackerArtifactDefense,
        int defenderArtifactAttack,
        int defenderArtifactDefense,
        float attackerDamage,
        float defenderDamage,
        string attackerLossesText,
        string defenderLossesText,
        string stalemateNote,
        string troopNarrative,
        List<string> battleAbilityNarration)
    {
        StringBuilder sb = new StringBuilder();
        int template = UnityEngine.Random.Range(0, 5);

        string edgePhrase = BuildBattleEdgePhrase(attackerDamage, defenderDamage, attackerName, defenderName);
        string raceFlavor = BuildRaceBattleFlavor(attackerRace, defenderRace, attackerName, defenderName);
        bool attackerHasEdge = attackerBonusPercent > defenderBonusPercent + 10;
        bool defenderHasEdge = defenderBonusPercent > attackerBonusPercent + 10;
        bool relicsActive = attackerArtifactAttack + attackerArtifactDefense + defenderArtifactAttack + defenderArtifactDefense > 0;

        void Allies(bool attackerSide)
        {
            if (attackerSide && attackerAlliesJoined > 0)
            {
                sb.AppendLine($"Allies surge in behind {attackerName}, swelling the assault.");
            }
            if (!attackerSide && defenderAlliesJoined > 0)
            {
                sb.AppendLine($"Reinforcements rally to {defenderName}, tightening the defense.");
            }
        }

        void Artifacts()
        {
            if (relicsActive)
            {
                sb.AppendLine("Relics flare and ward the front ranks.");
            }
        }

        void Abilities()
        {
            if (!string.IsNullOrWhiteSpace(troopNarrative))
            {
                sb.AppendLine(troopNarrative);
            }
            if (battleAbilityNarration == null || battleAbilityNarration.Count == 0) return;
            sb.AppendLine(BuildBattleAbilityNarration(battleAbilityNarration));
        }

        void Casualties()
        {
            sb.AppendLine($"Losses: {attackerName} {attackerLossesText}; {defenderName} {defenderLossesText}.");
            if (!string.IsNullOrEmpty(stalemateNote)) sb.AppendLine(stalemateNote);
        }

        switch (template)
        {
            case 0:
                sb.AppendLine($"Under {attackerName}'s banner the host marches on {location}, while {defenderName} braces a shieldwall that will not bend.");
                Allies(true);
                Allies(false);
                if (pcDefenseContribution > 0) sb.AppendLine($"Battlements thunder from {location}, stone and iron lending weight to the defense.");
                if (attackerHasEdge) sb.AppendLine($"{attackerName}'s command sharpens the assault, turning chaos into ordered fury.");
                if (defenderHasEdge) sb.AppendLine($"{defenderName}'s command steadies the defense, closing each gap as it opens.");
                sb.AppendLine($"Steel meets steel: {attackerTrainingLabel} warbands collide with {defenderTrainingLabel} ranks.");
                sb.AppendLine(raceFlavor);
                Abilities();
                Artifacts();
                sb.AppendLine(edgePhrase);
                Casualties();
                break;
            case 1:
                sb.AppendLine($"{location} erupts as {attackerName} advances; {defenderName} locks ranks and answers with a roar.");
                if (pcDefenseContribution > 0) sb.AppendLine("Stones and arrows rain from the walls, biting into the press.");
                Allies(true);
                Allies(false);
                sb.AppendLine($"Veterans steady the lines, {attackerTrainingLabel} blades against {defenderTrainingLabel} resolve.");
                sb.AppendLine(raceFlavor);
                Abilities();
                Artifacts();
                sb.AppendLine(edgePhrase);
                Casualties();
                break;
            case 2:
                sb.AppendLine($"{attackerName} presses into {location}, horns sounding through dust and smoke. {defenderName} signals to hold the line.");
                Allies(true);
                if (pcDefenseContribution > 0) sb.AppendLine("The settlement stiffens the defense, militia braced behind old stone.");
                Allies(false);
                if (attackerHasEdge || defenderHasEdge)
                {
                    sb.AppendLine(attackerHasEdge
                        ? $"{attackerName} finds gaps with disciplined orders and drives wedges between the shields."
                        : $"{defenderName} anticipates the charge and holds, turning the rush into a grind.");
                }
                sb.AppendLine($"Training shows in every step: {attackerTrainingLabel} hosts against {defenderTrainingLabel} defenders.");
                sb.AppendLine(raceFlavor);
                Abilities();
                Artifacts();
                sb.AppendLine(edgePhrase);
                Casualties();
                break;
            case 3:
                sb.AppendLine($"{attackerName} drives a wedge toward {location}; {defenderName} pivots to meet the charge with grounded spears.");
                Allies(true);
                Allies(false);
                if (pcDefenseContribution > 0) sb.AppendLine("Walls and militia strain to hold the breach, shouting orders above the din.");
                if (attackerHasEdge) sb.AppendLine($"{attackerName}'s leadership keeps the pressure on, wave after wave striking in time.");
                if (defenderHasEdge) sb.AppendLine($"{defenderName}'s leadership keeps the line tight, shields overlapping in a living wall.");
                sb.AppendLine(raceFlavor);
                Abilities();
                Artifacts();
                sb.AppendLine(edgePhrase);
                Casualties();
                break;
            default:
                sb.AppendLine($"Battle for {location}: {attackerName} leads the assault, {defenderName} anchors the defense amid smoke and shouting.");
                Allies(true);
                Allies(false);
                if (pcDefenseContribution > 0) sb.AppendLine("Local defenses join the fray, adding grit to every line.");
                sb.AppendLine($"Morale and drill decide the tempo: {attackerTrainingLabel} against {defenderTrainingLabel}.");
                sb.AppendLine(raceFlavor);
                Abilities();
                Artifacts();
                sb.AppendLine(edgePhrase);
                Casualties();
                break;
        }

        return sb.ToString();
    }

    private string BuildTroopBattleNarrative(Army defenderArmy)
    {
        List<string> lines = new();

        string attackerTroops = BuildArmyTroopNarrativeLine(commander != null ? commander.characterName : "The attackers", GetTroopGroups(), true);
        if (!string.IsNullOrWhiteSpace(attackerTroops))
        {
            lines.Add(attackerTroops);
        }

        string defenderTroops = defenderArmy != null
            ? BuildArmyTroopNarrativeLine(defenderArmy.commander != null ? defenderArmy.commander.characterName : "The defenders", defenderArmy.GetTroopGroups(), false)
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(defenderTroops))
        {
            lines.Add(defenderTroops);
        }

        return string.Join(" ", lines);
    }

    private static string BuildArmyTroopNarrativeLine(string armyName, List<ArmyTroopAbilityGroup> groups, bool attacking)
    {
        if (string.IsNullOrWhiteSpace(armyName) || groups == null || groups.Count == 0) return string.Empty;

        List<string> clauses = groups
            .Where(group => group != null && group.amount > 0)
            .OrderByDescending(group => group.amount)
            .Select(group => BuildTroopGroupBattleClause(group, attacking))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (clauses.Count == 0) return string.Empty;

        string joined = JoinNarrativeClauses(clauses);
        return attacking
            ? $"{armyName} unleashes {joined}."
            : $"{armyName} answers with {joined}.";
    }

    private static string BuildTroopGroupBattleClause(ArmyTroopAbilityGroup group, bool attacking)
    {
        if (group == null || group.amount <= 0) return string.Empty;

        string troopName = !string.IsNullOrWhiteSpace(group.troopName) ? group.troopName : group.troopType.ToString();
        string amountText = group.amount == 1 ? $"1 {troopName}" : $"{group.amount} {troopName}";
        List<string> abilityPhrases = (group.abilities ?? new List<ArmySpecialAbilityEnum>())
            .Distinct()
            .Select(BuildAbilityBattleClause)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        string action = BuildTroopTypeBattleClause(group.troopType, attacking);
        if (abilityPhrases.Count == 0) return $"{amountText} {action}";
        return $"{amountText} {action} while {JoinNarrativeClauses(abilityPhrases)}";
    }

    private static string BuildTroopTypeBattleClause(TroopsTypeEnum troopType, bool attacking)
    {
        return troopType switch
        {
            TroopsTypeEnum.ma => attacking ? "press forward at close quarters" : "hold the line with spear and blade",
            TroopsTypeEnum.ar => attacking ? "send measured flights into the enemy front" : "answer with disciplined volleys",
            TroopsTypeEnum.li => attacking ? "dart through gaps and worry the flanks" : "skirmish hard in the broken ground",
            TroopsTypeEnum.hi => attacking ? "drive into the center with crushing weight" : "brace in dense ranks and trade heavy blows",
            TroopsTypeEnum.lc => attacking ? "wheel fast around the edges of the melee" : "circle wide, guarding the flanks",
            TroopsTypeEnum.hc => attacking ? "thunder in with mailed force" : "meet the shock with armored momentum",
            TroopsTypeEnum.ca => attacking ? "hurl ruin from the rear with engines and stones" : "answer from behind the line with smashing shot",
            TroopsTypeEnum.ws => attacking ? "close over the water under oar and ram" : "maneuver across the waves to answer the strike",
            _ => attacking ? "surge into the fray" : "stand firm in the clash"
        };
    }

    private static string BuildAbilityBattleClause(ArmySpecialAbilityEnum ability)
    {
        return ability switch
        {
            ArmySpecialAbilityEnum.Pikemen => "lowering a hedge of pikes to break any mounted rush",
            ArmySpecialAbilityEnum.Shielded => "locking shields into a stubborn wall",
            ArmySpecialAbilityEnum.Berserker => "giving themselves over to a savage blood-rush",
            ArmySpecialAbilityEnum.Raid => "slipping aside to plunder, cut loose baggage, and strike from disorder",
            ArmySpecialAbilityEnum.Encouraging => "lifting banners and hearts with shouted courage",
            ArmySpecialAbilityEnum.Discouraging => "spreading dread with grim cries and black intent",
            ArmySpecialAbilityEnum.Poison => "striking with venom-touched blades and darts",
            ArmySpecialAbilityEnum.Fire => "casting sparks and flame through the press",
            ArmySpecialAbilityEnum.Cursed => "bringing a black foreboding over the field",
            ArmySpecialAbilityEnum.Longrange => "arching long volleys over the melee into distant targets",
            ArmySpecialAbilityEnum.ShortRange => "unleashing vicious close volleys at a stone's throw",
            ArmySpecialAbilityEnum.Charging => "hurling themselves forward in a thunderous charge",
            ArmySpecialAbilityEnum.Halted => "checking the foe in place with a hard sudden stop",
            ArmySpecialAbilityEnum.RefusingDuels => "spoiling challenges and denying single combat",
            ArmySpecialAbilityEnum.Frozen => "leaving the enemy seized by cold and hesitation",
            ArmySpecialAbilityEnum.Blocked => "catching the enemy in a knot of stalled movement",
            ArmySpecialAbilityEnum.Hope => "kindling fresh hope in allied hearts",
            ArmySpecialAbilityEnum.Hidden => "vanishing behind smoke, dusk, and battlefield confusion",
            ArmySpecialAbilityEnum.ArcaneInsight => "finding uncanny clarity in the chaos of battle",
            ArmySpecialAbilityEnum.Strengthened => "surging with renewed force at the key moment",
            ArmySpecialAbilityEnum.Fortified => "bracing under sudden hardening discipline",
            ArmySpecialAbilityEnum.MorgulTouch => "inflicting a black wound that lingers in fear and pain",
            ArmySpecialAbilityEnum.DuelSupremacy => "letting their champion seize the upper hand in single combat",
            ArmySpecialAbilityEnum.Bleeding => "opening cruel wounds that continue to weaken the foe",
            ArmySpecialAbilityEnum.Flying => "sweeping overhead to strike from an angle the enemy cannot answer",
            _ => string.Empty
        };
    }

    private static string JoinNarrativeClauses(List<string> clauses)
    {
        if (clauses == null || clauses.Count == 0) return string.Empty;
        if (clauses.Count == 1) return clauses[0];
        if (clauses.Count == 2) return $"{clauses[0]} and {clauses[1]}";
        return $"{string.Join(", ", clauses.Take(clauses.Count - 1))}, and {clauses[^1]}";
    }



    private string BuildSiegeDescription(
        string location,
        string attackerName,
        string defenderName,
        int attackerBonusPercent,
        string attackerTrainingLabel,
        int attackerXp,
        int attackerArtifactAttack,
        int attackerArtifactDefense,
        float attackerDamage,
        string attackerLossesText,
        bool fortStands)
    {
        StringBuilder sb = new StringBuilder();
        int template = UnityEngine.Random.Range(0, 5);
        bool attackerHasEdge = attackerBonusPercent > 10;
        bool relicsActive = attackerArtifactAttack + attackerArtifactDefense > 0;
        string edgePhrase = attackerDamage > 0
            ? $"{attackerName} finds cracks in the defense and worries the stone with iron and flame."
            : $"{defenderName} holds the line, the walls answering every ladder and ram.";

        switch (template)
        {
            case 0:
                sb.AppendLine($"{attackerName} drives siege lines toward {location}, drums rolling as ladders rise and ramheads bite.");
                sb.AppendLine(attackerHasEdge ? "Tight orders keep the assault steady through smoke and screams." : "The attack grinds forward in waves, stalled and renewed.");
                break;
            case 1:
                sb.AppendLine($"Siege of {location}: rams grind forward under {attackerName}'s shout while {defenderName} braces the parapet.");
                sb.AppendLine("Pressure builds as the walls answer with stones and fire, each impact echoing across the field.");
                break;
            case 2:
                sb.AppendLine($"{location} shakes as {attackerName} orders the climb. {defenderName} answers with volleys and falling iron.");
                sb.AppendLine($"The {attackerTrainingLabel} set the pace up the ladders, driving the first footholds.");
                break;
            case 3:
                sb.AppendLine($"{attackerName} hurls the host at {location}; mantlets and ladders grind ahead.");
                sb.AppendLine("Boiling oil and arrows test the attackers' resolve as shields splinter.");
                break;
            default:
                sb.AppendLine($"At {location}, {attackerName} raises a storm of iron while {defenderName} commands the ramparts.");
                sb.AppendLine("The assault surges and breaks in repeated waves, then surges again.");
                break;
        }

        sb.AppendLine(attackerHasEdge ? $"{attackerName}'s command keeps the siege from stalling." : $"{defenderName} keeps the walls steady, rallying the defenders.");
        sb.AppendLine($"Drill and grit define the climb: {attackerTrainingLabel} in the assault.");
        if (relicsActive)
        {
            sb.AppendLine("Relics flare at the breach, warding the first ranks.");
        }
        sb.AppendLine(edgePhrase);
        sb.AppendLine($"Losses: {attackerName} {attackerLossesText}.");
        if (attackerDamage > 0)
        {
            sb.AppendLine(fortStands ? "The walls crack and the defenders fall back a step." : "Gates burst and the population center falls to the attackers.");
        }
        else
        {
            sb.AppendLine("The gate holds; the attackers recoil beneath a storm of stones.");
        }

        return sb.ToString();
    }

    // Helper method to attack a Population Center

    private void AttackPopulationCenter(Hex targetHex, int attackerStrength, int attackerDefence, Leader attackerLeader)
    {
        if(targetHex.GetPC() == null) return;
        // No defending army, only a PC
        Leader defenderLeader = targetHex.GetPC().owner;
        bool defenderDeadOrMissing = defenderLeader == null || defenderLeader.killed;
        if(attackerLeader == null || attackerLeader.killed) return;
        string attackerName = commander != null ? commander.characterName : attackerLeader.characterName;
        int attackerXpBefore = xp;
        Character hudActor = commander;
        bool attackerIsPlayer = commander != null && commander.isPlayerControlled;
        bool defenderIsPlayer = IsPlayerLeader(defenderLeader);
        bool playerInvolved = attackerIsPlayer || defenderIsPlayer;
        if (playerInvolved)
        {
            Music.Instance?.PlayBattleMusic();
        }

        // Don't attack your own PC (check both alignment and owner)
        bool isOwnPC = commander && !commander.killed ? defenderLeader == commander.GetOwner() : false;
        if (isOwnPC) return;

        // If the defending leader is gone (null or killed), capture immediately
        if (defenderDeadOrMissing)
        {
            targetHex.GetPC().CapturePC(attackerLeader);
            MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"{targetHex.GetPC().pcName} was captured", Color.green);
            return;
        }

        AlignmentEnum defenderAlignment = defenderLeader.GetAlignment();

        if(defenderAlignment == attackerLeader.GetAlignment()) return;

        int defenderDefense = targetHex.GetPC().GetDefense();
        int defenderStrength = defenderDefense; // PC defense is its strength for counter-attack
        int attackerCommanderLevel = commander != null ? commander.GetCommander() : 0;
        int attackerBonusPercent = Mathf.RoundToInt(Mathf.Clamp(attackerCommanderLevel, 0, 10) * 10f);

        // Calculate raw damage values
        float attackerDamage = Math.Max(0, attackerStrength - defenderDefense);
        float defenderDamage = Math.Max(0, defenderStrength - attackerDefence);

        // Calculate casualties for attacker (PC can still cause casualties)
        float attackerCasualtyPercent = defenderDamage / (attackerStrength * CombatLethalityDivisor);

        // Clamp casualty percentage between 0 and 1
        attackerCasualtyPercent = Math.Clamp(attackerCasualtyPercent, 0, 1);

        // Check if attacker will actually lose any units
        int attackerTotalLosses = CalculateTotalCasualties(attackerCasualtyPercent);
        bool anyAttackerCasualties = attackerTotalLosses > 0;

        int attackerArtifactAttack = GetArtifactAttackBonusTotal();
        int attackerArtifactDefense = GetArtifactDefenseBonusTotal();
        CasualtyBreakdown attackerLosses = CalculateCasualtyBreakdown(attackerCasualtyPercent, !anyAttackerCasualties);
        string attackerLossesText = BuildLossesShort(attackerLosses);
        string pcTitle = $"Siege of {DisplayNames.Format(targetHex.GetPC().pcName)}";
        string pcText = BuildSiegeDescription(
            targetHex.GetPC().pcName,
            attackerName,
            defenderLeader.characterName,
            attackerBonusPercent,
            GetTrainingLabel(),
            xp,
            attackerArtifactAttack,
            attackerArtifactDefense,
            attackerDamage,
            attackerLossesText,
            targetHex.GetPC().fortSize > FortSizeEnum.NONE);
        Illustrations pcIllustrations = GameObject.FindAnyObjectByType<Illustrations>();
        bool shouldShowPopup = playerInvolved || PlayerCanSeeHex(targetHex);
        List<(string message, Color color)> hudMessages = new();
        if (attackerLosses.Any) hudMessages.Add(($"{attackerName} losses: {attackerLossesText}.", Color.red));
        if (shouldShowPopup)
        {
            Action onPopupClose = () => ShowHudMessagesAfterPopup(targetHex, hudActor, hudMessages);
            PopupManager.Show(
                pcTitle,
                pcIllustrations.GetIllustrationByName(attackerName),
                pcIllustrations.GetIllustrationByName(defenderLeader.characterName),
                pcText,
                true,
                onClose: onPopupClose);
        }

        // Apply casualties to attacker
        ReceiveCasualties(attackerCasualtyPercent, defenderLeader, !anyAttackerCasualties);

        // Process damage to the PC
        if (attackerDamage > 0)
        {
            if (targetHex.GetPC().fortSize > FortSizeEnum.NONE)
            {
                // Reduce fort size first
                targetHex.GetPC().DecreaseFort();
                MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"Defenses at {targetHex.GetPC().pcName} were damaged", Color.yellow);
            }
            else
            {
                targetHex.GetPC().CapturePC(attackerLeader);
            }
        }
        else
        {
            MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"{targetHex.GetPC().pcName} defenses resisted the attack", Color.red);
        }

        GrantCombatXp(this, attackerDamage > 0 ? "Siege success" : "Assault");

        // Check if attacker army was eliminated
        if (!killed && GetSize(true) < 1) Killed(defenderLeader);

        string outcome = attackerDamage > 0 ? $"{attackerName} seizes the walls." : $"{targetHex.GetPC().pcName} holds fast.";
        if (shouldShowPopup)
        {
            hudMessages.Add((outcome, attackerDamage > 0 ? Color.green : Color.yellow));
        }

        int attackerXpDelta = xp - attackerXpBefore;
        if (shouldShowPopup)
        {
            hudMessages.Add(($"XP gained: {attackerName} {FormatDelta(attackerXpDelta)}", Color.cyan));
        }
        if (playerInvolved)
        {
            bool playerWin = attackerIsPlayer ? (attackerDamage > 0) : (attackerDamage <= 0);
            if (playerWin)
            {
                Music.Instance?.PlayBattleWonMusic();
            }
            else
            {
                Music.Instance?.PlayBattleMusic();
            }
        }
    }

    public void ApplyCasualtiesToDefenders(Hex targetHex, float casualtyPercent, AlignmentEnum attackerAlignment, bool forceOneUnitCasualty, Leader attacker)
    {
        // If we need to force one unit casualty, find the primary defender
        Army primaryDefender = null;
        if (forceOneUnitCasualty)
        {
            foreach (Army army in targetHex.armies)
            {
                // Skip attacker's own armies. Compares against the attacker Leader parameter,
                // not commander.GetOwner() — this army's own commander can already be null here
                // if the attacker's side was wiped out by its own counter-casualties (ReceiveCasualties
                // -> Killed() -> commander = null) earlier in the same ProcessCombat call.
                bool isOwnArmy = army.commander.GetOwner() == attacker;

                // Consider armies that should be attacked
                if (!isOwnArmy && (army.GetAlignment() != attackerAlignment ||
                    attackerAlignment == AlignmentEnum.neutral ||
                    army.GetAlignment() == AlignmentEnum.neutral))
                {
                    if (primaryDefender == null || army.GetSize(true) > primaryDefender.GetSize(true))
                    {
                        primaryDefender = army;
                    }
                }
            }
        }

        int armiesNum = targetHex.armies.Count;
        // Use a for loop with index going backwards to avoid collection modification issues
        for (int i = armiesNum - 1; i >= 0; i--)
        {
            // Check if index is still valid (in case armies were removed)
            if (i >= targetHex.armies.Count) continue;

            Army army = targetHex.armies[i];

            // Skip invalid armies, the attacker itself, or dead armies
            if (army == this || army == null || army.GetSize() < 1 ||
                army.GetCommander() == null || army.killed) continue;

            // Skip the attacker's own armies (see the identical comment above on why this
            // compares against the attacker parameter rather than commander.GetOwner()).
            bool isOwnArmy = army.commander.GetOwner() == attacker;
            if (isOwnArmy) continue;

            // Apply casualties to armies that should be attacked:
            // Different alignment OR attacker is neutral OR defender is neutral
            if (army.GetAlignment() != attackerAlignment ||
                attackerAlignment == AlignmentEnum.neutral ||
                army.GetAlignment() == AlignmentEnum.neutral)
            {
                // Force one unit casualty only on the primary defender
                bool forceUnit = forceOneUnitCasualty && army == primaryDefender;
                army.ReceiveCasualties(casualtyPercent, attacker, forceUnit);
            }
        }

        // Handle damage to population center
        if ((casualtyPercent > 0.4f || UnityEngine.Random.Range(0f, 1f) >= 0.75f) &&
            targetHex.GetPC() != null)
        {
            // Don't damage own PC (see above: compares against the attacker parameter, not
            // commander.GetOwner(), since commander can already be null here)
            bool isOwnPC = targetHex.GetPC().owner == attacker;

            // Damage PC if it belongs to a different alignment OR attacker is neutral OR PC is neutral
            // BUT never damage your own PC
            if (!isOwnPC && (targetHex.GetPC().owner.GetAlignment() != attackerAlignment ||
                attackerAlignment == AlignmentEnum.neutral ||
                targetHex.GetPC().owner.GetAlignment() == AlignmentEnum.neutral))
            {
                targetHex.GetPC().DecreaseFort();
            }
        }
    }

    // Helper method to calculate total casualties without applying them
    // Uses the same rounding function as the actual casualty calculation
    public int CalculateTotalCasualties(float casualtyPercent)
    {
        CasualtyBreakdown breakdown = CalculateCasualtyBreakdown(casualtyPercent, false);
        return breakdown.Total;
    }

    public void ReceiveCasualties(float casualtyPercent, Leader defenderLeader, bool forceOneUnitCasualty = false)
    {
        CasualtyBreakdown breakdown = CalculateCasualtyBreakdown(casualtyPercent, forceOneUnitCasualty);

        // Apply casualties
        this.ma = Math.Max(0, this.ma - breakdown.ma);
        this.ar = Math.Max(0, this.ar - breakdown.ar);
        this.li = Math.Max(0, this.li - breakdown.li);
        this.hi = Math.Max(0, this.hi - breakdown.hi);
        this.lc = Math.Max(0, this.lc - breakdown.lc);
        this.hc = Math.Max(0, this.hc - breakdown.hc);
        this.ca = Math.Max(0, this.ca - breakdown.ca);
        RemoveSpecialTroops(TroopsTypeEnum.ma, breakdown.ma);
        RemoveSpecialTroops(TroopsTypeEnum.ar, breakdown.ar);
        RemoveSpecialTroops(TroopsTypeEnum.li, breakdown.li);
        RemoveSpecialTroops(TroopsTypeEnum.hi, breakdown.hi);
        RemoveSpecialTroops(TroopsTypeEnum.lc, breakdown.lc);
        RemoveSpecialTroops(TroopsTypeEnum.hc, breakdown.hc);
        RemoveSpecialTroops(TroopsTypeEnum.ca, breakdown.ca);

        // Only apply casualties to warships if in water
        if (commander.hex.IsWaterTerrain())
        {
            this.ws = Math.Max(0, this.ws - breakdown.ws);
            RemoveSpecialTroops(TroopsTypeEnum.ws, breakdown.ws);
        }

        // Check if the army was eliminated
        if (this.GetSize(true) < 1)
        {
            this.Killed(defenderLeader);
        }
        else if (breakdown.Any)
        {
            string casualties = BuildCasualtySpriteString(breakdown);
            MessageDisplayNoUI.ShowMessage(commander.hex, commander, $"{commander.characterName} losses: {casualties}", Color.red);
        }
    }

    private struct CasualtyBreakdown
    {
        public int ma;
        public int ar;
        public int li;
        public int hi;
        public int lc;
        public int hc;
        public int ca;
        public int ws;
        public int Total => ma + ar + li + hi + lc + hc + ca + ws;
        public bool Any => Total > 0;
    }

    private CasualtyBreakdown CalculateCasualtyBreakdown(float casualtyPercent, bool forceOneUnitCasualty)
    {
        CasualtyBreakdown breakdown = new()
        {
            ma = (int)Math.Round(this.ma * casualtyPercent),
            ar = (int)Math.Round(this.ar * casualtyPercent),
            li = (int)Math.Round(this.li * casualtyPercent),
            hi = (int)Math.Round(this.hi * casualtyPercent),
            lc = (int)Math.Round(this.lc * casualtyPercent),
            hc = (int)Math.Round(this.hc * casualtyPercent),
            ca = (int)Math.Round(this.ca * casualtyPercent),
            ws = commander != null && commander.hex != null && commander.hex.IsWaterTerrain()
                ? (int)Math.Round(this.ws * casualtyPercent)
                : 0
        };

        if (!breakdown.Any && forceOneUnitCasualty && GetSize(false) > 0)
        {
            if (ma > 0) breakdown.ma = 1;
            else if (ar > 0) breakdown.ar = 1;
            else if (li > 0) breakdown.li = 1;
            else if (lc > 0) breakdown.lc = 1;
            else if (hi > 0) breakdown.hi = 1;
            else if (hc > 0) breakdown.hc = 1;
            else if (ca > 0) breakdown.ca = 1;
            else if (ws > 0 && commander != null && commander.hex != null && commander.hex.IsWaterTerrain()) breakdown.ws = 1;
        }

        return breakdown;
    }

    private static string BuildCasualtySpriteString(CasualtyBreakdown breakdown)
    {
        StringBuilder casualties = new StringBuilder();
        if (breakdown.ma > 0) casualties.Append($"<sprite name=\"ma\">ma[{breakdown.ma}]");
        if (breakdown.ar > 0) casualties.Append($"<sprite name=\"ar\">ar[{breakdown.ar}]");
        if (breakdown.li > 0) casualties.Append($"<sprite name=\"li\">li[{breakdown.li}]");
        if (breakdown.hi > 0) casualties.Append($"<sprite name=\"hi\">hi[{breakdown.hi}]");
        if (breakdown.lc > 0) casualties.Append($"<sprite name=\"lc\">lc[{breakdown.lc}]");
        if (breakdown.hc > 0) casualties.Append($"<sprite name=\"hc\">hc[{breakdown.hc}]");
        if (breakdown.ca > 0) casualties.Append($"<sprite name=\"ca\">ca[{breakdown.ca}]");
        if (breakdown.ws > 0) casualties.Append($"<sprite name=\"ws\">ws[{breakdown.ws}]");
        return casualties.ToString();
    }

    private static string BuildLossesShort(CasualtyBreakdown breakdown)
    {
        string sprites = BuildCasualtySpriteString(breakdown);
        return string.IsNullOrEmpty(sprites) ? "no confirmed losses" : sprites;
    }

    private static string BuildBattleEdgePhrase(float attackerDamage, float defenderDamage, string attackerName, string defenderName)
    {
        if (attackerDamage <= 0f && defenderDamage <= 0f)
        {
            return "Neither side finds a clear opening; shields lock and the lines grind without yield.";
        }
        if (attackerDamage > defenderDamage * 1.25f)
        {
            return $"{attackerName} presses the advantage, driving the foe a few hard paces back.";
        }
        if (defenderDamage > attackerDamage * 1.25f)
        {
            return $"{defenderName} blunts the assault, turning the rush into a costly shove.";
        }
        return "The fight swings back and forth, momentum shifting with each fresh shout.";
    }

    private int GetDefaultArmyCardProcChancePercent()
    {
        return Mathf.Clamp(specialAbilityProcChance <= 0 ? 100 : specialAbilityProcChance, 1, 100);
    }

    private void TriggerBattleSpecialAbilities(Hex battleHex, Army enemyArmy, List<(string message, Color color)> battleMessages, List<string> battleNarration)
    {
        if (battleHex == null || enemyArmy == null || battleMessages == null || battleNarration == null) return;

        // Diagnostic: dump exactly what each side's troopAbilityGroups (the thing
        // HasSpecialAbility/GetAbilityTroopCount actually reads) contains at the moment
        // abilities are checked, since the in-game hover display and this list have been
        // observed to disagree.
        Debug.Log($"[AbilityCheck] {commander?.characterName ?? "?"}'s troop groups: " +
            string.Join(" | ", GetTroopGroups().Select(g => $"{g.amount}x {g.troopName} [{string.Join(",", g.abilities)}]")));
        Debug.Log($"[AbilityCheck] {enemyArmy.commander?.characterName ?? "?"}'s troop groups: " +
            string.Join(" | ", enemyArmy.GetTroopGroups().Select(g => $"{g.amount}x {g.troopName} [{string.Join(",", g.abilities)}]")));

        int procChance = GetDefaultArmyCardProcChancePercent();

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Shielded, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            return ($"{commander.characterName}'s shielded troops gain Fortified.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Shielded, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Berserker, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Strengthened, 1);
            return ($"{commander.characterName}'s berserk fury grants Strengthened.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Berserker, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Raid, procChance, battleMessages, battleNarration, () =>
        {
            commander.GetOwner()?.AddGold(1);
            return ($"{commander.characterName}'s raiders seize +1 <sprite name=\"gold\">.", Color.yellow, BuildAbilityNarration(ArmySpecialAbilityEnum.Raid, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Encouraging, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Encouraged, 2);
            return ($"{commander.characterName}'s army gains Courage (2).", Color.green, BuildAbilityNarration(ArmySpecialAbilityEnum.Encouraging, commander.characterName, null, true));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Discouraging, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Despair, 2);
            return ($"{enemyArmy.commander.characterName} suffers Despair (2).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Discouraging, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Poison, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Poisoned, 5);
            return ($"{enemyArmy.commander.characterName} is Poisoned (5).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Poison, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Fire, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Burning, 3);
            return ($"{enemyArmy.commander.characterName} is Burning (3).", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Fire, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Cursed, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Fear, 2);
            return ($"{enemyArmy.commander.characterName} is gripped by Fear (2).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Cursed, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerRangedBattleAbility(ArmySpecialAbilityEnum.Longrange, procChance, battleHex, enemyArmy, 2, 0.10f, "longrange volley", battleMessages, battleNarration);
        TryTriggerRangedBattleAbility(ArmySpecialAbilityEnum.ShortRange, procChance, battleHex, enemyArmy, 1, 0.20f, "shortrange strike", battleMessages, battleNarration);

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Hope, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Hope, 1);
            return ($"{commander.characterName}'s army gains Hope.", Color.green, BuildAbilityNarration(ArmySpecialAbilityEnum.Hope, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Hidden, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Hidden, 1);
            return ($"{commander.characterName}'s army becomes Hidden.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Hidden, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.ArcaneInsight, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.ArcaneInsight, 1);
            return ($"{commander.characterName}'s army gains Arcane Insight.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.ArcaneInsight, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Strengthened, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Strengthened, 1);
            return ($"{commander.characterName}'s army is Strengthened.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Strengthened, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Fortified, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            return ($"{commander.characterName}'s army is Fortified.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Fortified, commander.characterName, null, true));
        });

        TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.DuelSupremacy, procChance, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.DuelSupremacy, 1);
            return ($"{commander.characterName}'s champion claims Duel Supremacy.", Color.yellow, BuildAbilityNarration(ArmySpecialAbilityEnum.DuelSupremacy, commander.characterName, null, true));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Halted, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Halted, 1);
            return ($"{enemyArmy.commander.characterName} is Halted.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.Halted, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.RefusingDuels, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.RefusingDuels, 1);
            return ($"{enemyArmy.commander.characterName} is Refusing Duels.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.RefusingDuels, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Frozen, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Frozen, 1);
            return ($"{enemyArmy.commander.characterName} is Frozen.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Frozen, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Blocked, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Blocked, 1);
            return ($"{enemyArmy.commander.characterName} is Blocked.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.Blocked, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.MorgulTouch, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.MorgulTouch, 3);
            return ($"{enemyArmy.commander.characterName} suffers Morgul Touch.", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.MorgulTouch, commander.characterName, enemyArmy.commander.characterName, false));
        });

        TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Bleeding, procChance, enemyArmy, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Bleeding, 2);
            return ($"{enemyArmy.commander.characterName} is Bleeding.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Bleeding, commander.characterName, enemyArmy.commander.characterName, false));
        });

        int enemyProcChance = enemyArmy.GetDefaultArmyCardProcChancePercent();

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Shielded, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            return ($"{enemyArmy.commander.characterName}'s shielded troops gain Fortified.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Shielded, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Berserker, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Strengthened, 1);
            return ($"{enemyArmy.commander.characterName}'s berserk fury grants Strengthened.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Berserker, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Raid, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.GetOwner()?.AddGold(1);
            return ($"{enemyArmy.commander.characterName}'s raiders seize +1 <sprite name=\"gold\">.", Color.yellow, BuildAbilityNarration(ArmySpecialAbilityEnum.Raid, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Encouraging, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Encouraged, 2);
            return ($"{enemyArmy.commander.characterName}'s army gains Courage (2).", Color.green, BuildAbilityNarration(ArmySpecialAbilityEnum.Encouraging, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Discouraging, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Despair, 2);
            return ($"{commander.characterName} suffers Despair (2).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Discouraging, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Poison, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Poisoned, 5);
            return ($"{commander.characterName} is Poisoned (5).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Poison, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Fire, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Burning, 3);
            return ($"{commander.characterName} is Burning (3).", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Fire, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Cursed, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Fear, 2);
            return ($"{commander.characterName} is gripped by Fear (2).", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.Cursed, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerRangedBattleAbility(ArmySpecialAbilityEnum.Longrange, enemyProcChance, battleHex, this, 2, 0.10f, "longrange volley", battleMessages, battleNarration);
        enemyArmy.TryTriggerRangedBattleAbility(ArmySpecialAbilityEnum.ShortRange, enemyProcChance, battleHex, this, 1, 0.20f, "shortrange strike", battleMessages, battleNarration);

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Hope, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Hope, 1);
            return ($"{enemyArmy.commander.characterName}'s army gains Hope.", Color.green, BuildAbilityNarration(ArmySpecialAbilityEnum.Hope, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Hidden, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Hidden, 1);
            return ($"{enemyArmy.commander.characterName}'s army becomes Hidden.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Hidden, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.ArcaneInsight, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.ArcaneInsight, 1);
            return ($"{enemyArmy.commander.characterName}'s army gains Arcane Insight.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.ArcaneInsight, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Strengthened, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Strengthened, 1);
            return ($"{enemyArmy.commander.characterName}'s army is Strengthened.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Strengthened, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.Fortified, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            return ($"{enemyArmy.commander.characterName}'s army is Fortified.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Fortified, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum.DuelSupremacy, enemyProcChance, battleMessages, battleNarration, () =>
        {
            enemyArmy.commander.ApplyStatusEffect(StatusEffectEnum.DuelSupremacy, 1);
            return ($"{enemyArmy.commander.characterName}'s champion claims Duel Supremacy.", Color.yellow, BuildAbilityNarration(ArmySpecialAbilityEnum.DuelSupremacy, enemyArmy.commander.characterName, null, true));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Halted, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Halted, 1);
            return ($"{commander.characterName} is Halted.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.Halted, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.RefusingDuels, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.RefusingDuels, 1);
            return ($"{commander.characterName} is Refusing Duels.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.RefusingDuels, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Frozen, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Frozen, 1);
            return ($"{commander.characterName} is Frozen.", Color.cyan, BuildAbilityNarration(ArmySpecialAbilityEnum.Frozen, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Blocked, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Blocked, 1);
            return ($"{commander.characterName} is Blocked.", Color.white, BuildAbilityNarration(ArmySpecialAbilityEnum.Blocked, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.MorgulTouch, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.MorgulTouch, 3);
            return ($"{commander.characterName} suffers Morgul Touch.", Color.magenta, BuildAbilityNarration(ArmySpecialAbilityEnum.MorgulTouch, enemyArmy.commander.characterName, commander.characterName, false));
        });

        enemyArmy.TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum.Bleeding, enemyProcChance, this, battleMessages, battleNarration, () =>
        {
            commander.ApplyStatusEffect(StatusEffectEnum.Bleeding, 2);
            return ($"{commander.characterName} is Bleeding.", Color.red, BuildAbilityNarration(ArmySpecialAbilityEnum.Bleeding, enemyArmy.commander.characterName, commander.characterName, false));
        });
    }

    private void TryTriggerSelfBattleAbility(ArmySpecialAbilityEnum ability, int percentChance, List<(string message, Color color)> battleMessages, List<string> battleNarration, Func<(string message, Color color, string narration)> onSuccess)
    {
        if (!HasSpecialAbility(ability) || commander == null || commander.killed) return;
        if (UnityEngine.Random.Range(0f, 100f) >= GetAbilityTriggerChancePercent(ability, percentChance)) return;
        var result = onSuccess.Invoke();
        battleMessages.Add((result.message, result.color));
        if (!string.IsNullOrWhiteSpace(result.narration)) battleNarration.Add(result.narration);
    }

    private void TryTriggerEnemyBattleAbility(ArmySpecialAbilityEnum ability, int percentChance, Army enemyArmy, List<(string message, Color color)> battleMessages, List<string> battleNarration, Func<(string message, Color color, string narration)> onSuccess)
    {
        if (!HasSpecialAbility(ability) || enemyArmy == null || enemyArmy.killed || enemyArmy.commander == null || enemyArmy.commander.killed) return;
        if (UnityEngine.Random.Range(0f, 100f) >= GetAbilityTriggerChancePercent(ability, percentChance)) return;
        var result = onSuccess.Invoke();
        battleMessages.Add((result.message, result.color));
        if (!string.IsNullOrWhiteSpace(result.narration)) battleNarration.Add(result.narration);
    }

    private void TryTriggerRangedBattleAbility(ArmySpecialAbilityEnum ability, int percentChance, Hex battleHex, Army primaryEnemy, int radius, float casualtyPercent, string label, List<(string message, Color color)> battleMessages, List<string> battleNarration)
    {
        if (!HasSpecialAbility(ability) || battleHex == null || commander == null || commander.killed) return;
        if (UnityEngine.Random.Range(0f, 100f) >= GetAbilityTriggerChancePercent(ability, percentChance)) return;

        Army target = FindRandomEnemyArmyInRadius(battleHex, radius) ?? primaryEnemy;
        if (target == null || target.killed || target.commander == null || target.commander.killed) return;

        float scaledCasualtyPercent = casualtyPercent * Mathf.Max(0.25f, GetAbilityCoverageRatio(ability));
        target.ReceiveCasualties(scaledCasualtyPercent, commander.GetOwner(), false);
        battleMessages.Add(($"{commander.characterName}'s {label} hits {target.commander.characterName}'s army.", Color.yellow));
        battleNarration.Add(BuildAbilityNarration(ability, commander.characterName, target.commander.characterName, false));
    }

    private Army FindRandomEnemyArmyInRadius(Hex origin, int radius)
    {
        if (origin == null) return null;
        List<Hex> hexes = origin.GetHexesInRadius(radius);
        if (hexes == null || hexes.Count == 0) return null;

        List<Army> candidates = hexes
            .Where(hex => hex != null && hex.armies != null)
            .SelectMany(hex => hex.armies)
            .Where(army => army != null
                && army != this
                && !army.killed
                && army.commander != null
                && !army.commander.killed
                && army.commander.GetOwner() != commander.GetOwner()
                && (army.GetAlignment() != GetAlignment() || army.GetAlignment() == AlignmentEnum.neutral || GetAlignment() == AlignmentEnum.neutral))
            .ToList();

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static void ResolveStartOfTurnRangedVolleysForLeader(Leader leader)
    {
        if (leader == null || leader.killed || leader.controlledCharacters == null) return;

        HashSet<Hex> processedHexes = new();
        foreach (Character character in leader.controlledCharacters)
        {
            if (character == null || character.killed || !character.IsArmyCommander() || character.hex == null) continue;
            Army army = character.GetArmy();
            if (army == null || !processedHexes.Add(character.hex)) continue;

            List<Army> alliedArmiesInHex = character.hex.armies
                .Where(a => a != null && !a.killed && a.commander != null && !a.commander.killed && a.commander.GetOwner() == leader)
                .ToList();
            if (alliedArmiesInHex.Count == 0) continue;

            List<Army> longRangeArmies = alliedArmiesInHex
                .Where(a => a.HasSpecialAbility(ArmySpecialAbilityEnum.Longrange) && UnityEngine.Random.Range(0f, 100f) < a.GetDefaultArmyCardProcChancePercent())
                .ToList();
            List<Army> shortRangeArmies = alliedArmiesInHex
                .Where(a => a.HasSpecialAbility(ArmySpecialAbilityEnum.ShortRange) && UnityEngine.Random.Range(0f, 100f) < a.GetDefaultArmyCardProcChancePercent())
                .ToList();

            if (longRangeArmies.Count == 0 && shortRangeArmies.Count == 0) continue;

            int maxRadius = longRangeArmies.Count > 0 ? 2 : 1;
            Army target = FindNearestEnemyArmyForVolley(character.hex, leader, maxRadius);
            if (target == null) continue;

            float totalCasualtyPercent = (longRangeArmies.Count * 0.10f) + (shortRangeArmies.Count * 0.20f);
            totalCasualtyPercent = Mathf.Clamp(totalCasualtyPercent, 0.05f, 0.95f);
            // Read the name first: casualties can destroy the army and clear its commander.
            string targetName = target.commander.characterName;
            target.ReceiveCasualties(totalCasualtyPercent, leader, false);

            string sourceText = $"{longRangeArmies.Count} long-range" + (shortRangeArmies.Count > 0 ? $", {shortRangeArmies.Count} short-range" : string.Empty);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Turn-start volley: {sourceText} army group(s) strike {targetName}'s army.", Color.yellow);
        }
    }

    private static Army FindNearestEnemyArmyForVolley(Hex origin, Leader leader, int radius)
    {
        if (origin == null || leader == null) return null;
        List<Hex> hexes = origin.GetHexesInRadius(radius);
        if (hexes == null || hexes.Count == 0) return null;

        return hexes
            .Where(h => h != null && h.armies != null)
            .SelectMany(h => h.armies)
            .Where(a => a != null && !a.killed && a.commander != null && !a.commander.killed && a.commander.GetOwner() != leader)
            .OrderBy(a => Vector2.Distance(origin.v2, a.commander.hex.v2))
            .FirstOrDefault();
    }

    private static string BuildBattleAbilityNarration(List<string> battleAbilityNarration)
    {
        if (battleAbilityNarration == null || battleAbilityNarration.Count == 0) return string.Empty;

        int template = UnityEngine.Random.Range(0, 5);
        string joined = string.Join(" ", battleAbilityNarration);
        return template switch
        {
            0 => $"{joined}",
            1 => $"In the thickest crush of the fighting, {joined}",
            2 => $"The struggle shifts by sudden turns: {joined}",
            3 => $"Dust, shouting, and steel swallow the field as {joined}",
            _ => $"Across the melee, one sharp moment follows another. {joined}"
        };
    }

    private static string BuildAbilityNarration(ArmySpecialAbilityEnum ability, string sourceName, string targetName, bool selfTargeted)
    {
        List<string> options = ability switch
        {
            ArmySpecialAbilityEnum.Shielded => new List<string>
            {
                $"{sourceName}'s shielded ranks lock together and harden the line.",
                $"A wall of shields rises around {sourceName}'s foremost fighters, and the first shock breaks against it.",
                $"{sourceName}'s troops overlap shields and blunt the worst of the blow.",
                $"{sourceName}'s front ranks crouch behind locked shields, turning the enemy rush into a dull crash of iron.",
                $"{sourceName}'s line knots into a bristling shieldwall that refuses to split."
            },
            ArmySpecialAbilityEnum.Berserker => new List<string>
            {
                $"{sourceName}'s fiercest warriors hurl themselves forward in a killing rage.",
                $"Blood-frenzied fighters in {sourceName}'s host strike with reckless force.",
                $"{sourceName}'s berserkers whip the line into a brutal surge.",
                $"With wild cries, {sourceName}'s maddened champions crash in hard enough to shake the whole front.",
                $"{sourceName}'s most savage fighters tear into the press, heedless of wounds and hungry for blood."
            },
            ArmySpecialAbilityEnum.Raid => new List<string>
            {
                $"{sourceName}'s raiders vanish through the confusion and come back with plunder at their belts.",
                $"While the lines are tangled, riders from {sourceName}'s host strip the field of loose spoils.",
                $"{sourceName}'s raiding parties dart through smoke and panic, carrying off what they can seize.",
                $"In the disorder behind the front, men of {sourceName} snatch coin, baggage, and trophies of war.",
                $"{sourceName}'s scouts slip out of the melee with fresh spoils before anyone can stop them."
            },
            ArmySpecialAbilityEnum.Encouraging => new List<string>
            {
                $"{sourceName}'s cries of courage steady wavering hearts.",
                $"{sourceName}'s standards lift the host and harden its resolve.",
                $"The line around {sourceName} rallies with renewed heart.",
                $"{sourceName}'s captains bellow over the din, and tired fighters straighten and step back into the fight.",
                $"A surge of resolve runs through {sourceName}'s host as banners rise above the press."
            },
            ArmySpecialAbilityEnum.Discouraging => new List<string>
            {
                $"{targetName} falters as dread spreads through the ranks.",
                $"A wave of despair rolls into {targetName}'s line and steals its edge.",
                $"{sourceName}'s grim presence saps the resolve of {targetName}'s host.",
                $"{targetName}'s fighters lose heart for a moment, their advance thinning into hesitation.",
                $"Dark cries from {sourceName}'s side leave {targetName}'s line unsteady and unsure."
            },
            ArmySpecialAbilityEnum.Poison => new List<string>
            {
                $"{targetName}'s warriors reel as poison takes hold.",
                $"Venom from {sourceName}'s host seeps into the struggle and weakens {targetName}.",
                $"{targetName}'s line fights on under the sting of poison.",
                $"{targetName}'s front ranks begin to stagger, their strength ebbing under tainted wounds.",
                $"Blades and darts from {sourceName}'s host leave {targetName}'s soldiers pale and unsteady."
            },
            ArmySpecialAbilityEnum.Fire => new List<string>
            {
                $"Flames spread where {sourceName}'s attack lands, setting {targetName}'s force ablaze.",
                $"{targetName}'s front ranks catch fire in the violence of the clash.",
                $"Burning brands and sparks from {sourceName}'s line leave {targetName}'s host smoking.",
                $"Sudden fire runs along shields and cloaks in {targetName}'s line, throwing the formation into panic.",
                $"A burst of flame from {sourceName}'s side leaves parts of {targetName}'s host burning through the melee."
            },
            ArmySpecialAbilityEnum.Cursed => new List<string>
            {
                $"{targetName}'s fighters shrink back under a sudden pall of fear.",
                $"A cursed dread grips {targetName}'s line at the worst possible moment.",
                $"{sourceName}'s dark omen leaves {targetName}'s ranks shaken.",
                $"An unnatural chill passes through {targetName}'s host, and even bold fighters glance back in dread.",
                $"{targetName}'s line wavers as a black foreboding settles over the field."
            },
            ArmySpecialAbilityEnum.Longrange => new List<string>
            {
                $"{sourceName}'s far-thrown volleys arc over the field and strike beyond the main clash.",
                $"Missiles from {sourceName}'s rear lines find an enemy beyond the main clash.",
                $"{sourceName} sends a distant volley that cuts into exposed foes.",
                $"From well behind the front, shafts and stones from {sourceName}'s host fall on an enemy that thought itself clear of danger.",
                $"{sourceName}'s rear ranks loose at long reach, and distant shapes crumple under the rain."
            },
            ArmySpecialAbilityEnum.ShortRange => new List<string>
            {
                $"{sourceName}'s close-thrown missiles hammer nearby enemies.",
                $"{sourceName}'s short-range barrage tears into a force just beyond the melee.",
                $"A sudden near volley from {sourceName} batters a neighboring enemy line.",
                $"{sourceName}'s skirmishers dart up and unleash a fierce burst at enemies only a stone's throw away.",
                $"At the edge of the fighting, a quick storm from {sourceName}'s line slams into a nearby foe."
            },
            ArmySpecialAbilityEnum.Flying => new List<string>
            {
                $"{sourceName}'s winged host circles wide and stoops on the flank without warning.",
                $"Riders of the air from {sourceName}'s force sweep low, scattering anything caught in the open.",
                $"{sourceName}'s flying vanguard dives through the press from above, breaking the formation's edge.",
                $"Wings darken the sky over {sourceName}'s line as the host strikes from an angle no shield can answer.",
                $"{sourceName}'s airborne troops cut across the field at speed that no foot soldier can match."
            },
            _ => selfTargeted
                ? new List<string>
                {
                    $"{sourceName}'s line finds an unexpected edge in the chaos.",
                    $"{sourceName}'s host turns the moment to its favor with a sudden battlefield knack.",
                    $"Something in {sourceName}'s way of war briefly tilts the clash."
                }
                : new List<string>
                {
                    $"{sourceName} catches {targetName} in a sudden turn of the fight.",
                    $"{targetName}'s line is wrong-footed by an unexpected stroke from {sourceName}.",
                    $"{sourceName} finds a cruel opening and {targetName} pays for it."
                }
        };

        return options[UnityEngine.Random.Range(0, options.Count)];
    }

    private static string BuildRaceBattleFlavor(RacesEnum attackerRace, RacesEnum defenderRace, string attackerName, string defenderName)
    {
        List<string> options = new();

        options.Add($"{GetRaceBattleNoun(attackerRace, true)} crash into {GetRaceBattleNoun(defenderRace, false)}, each side testing the other's nerve.");

        switch (attackerRace)
        {
            case RacesEnum.Elf:
                options.Add($"{attackerName}'s elves move with cold precision, seeking seams before {defenderName} can answer.");
                break;
            case RacesEnum.Dwarf:
                options.Add($"{attackerName}'s dwarves advance like a hammerblow, stubborn and hard to turn aside.");
                break;
            case RacesEnum.Orc:
            case RacesEnum.Goblin:
                options.Add($"{attackerName}'s foul ranks come on in snarling waves, eager to drag the fight into chaos.");
                break;
            case RacesEnum.Troll:
                options.Add($"{attackerName}'s trolls heave forward with brutal force, scattering the front before them.");
                break;
            case RacesEnum.Easterling:
            case RacesEnum.Southron:
                options.Add($"{attackerName}'s eastern host presses with harsh cries and disciplined surges.");
                break;
            case RacesEnum.Hobbit:
                options.Add($"{attackerName}'s smallfolk fight with surprising resolve, refusing to yield the field.");
                break;
        }

        switch (defenderRace)
        {
            case RacesEnum.Elf:
                options.Add($"{defenderName}'s elves answer with measured volleys and quick-footed counters.");
                break;
            case RacesEnum.Dwarf:
                options.Add($"{defenderName}'s dwarves plant themselves like stone, making every yard costly.");
                break;
            case RacesEnum.Orc:
            case RacesEnum.Goblin:
                options.Add($"{defenderName}'s orcish defenders howl behind jagged shields, turning the line into a brawl.");
                break;
            case RacesEnum.Troll:
                options.Add($"{defenderName}'s trolls soak the blow and hurl it back with savage strength.");
                break;
            case RacesEnum.Beast:
            case RacesEnum.Spider:
                options.Add($"{defenderName}'s creatures snap and circle at the flanks, making the melee feel feral and close.");
                break;
            case RacesEnum.Undead:
                options.Add($"{defenderName}'s dead hold without fear, meeting the clash in chilling silence.");
                break;
        }

        if ((attackerRace == RacesEnum.Elf && defenderRace == RacesEnum.Orc) ||
            (attackerRace == RacesEnum.Orc && defenderRace == RacesEnum.Elf))
        {
            options.Add("Ancient hatred sharpens every strike as elf and orc meet in a feud older than the field itself.");
        }
        if ((attackerRace == RacesEnum.Dwarf && defenderRace == RacesEnum.Goblin) ||
            (attackerRace == RacesEnum.Goblin && defenderRace == RacesEnum.Dwarf))
        {
            options.Add("Old grudges flare anew as dwarf and goblin tear into one another at close quarters.");
        }
        if ((attackerRace == RacesEnum.Common && defenderRace == RacesEnum.Undead) ||
            (attackerRace == RacesEnum.Undead && defenderRace == RacesEnum.Common))
        {
            options.Add("The living and the unquiet dead collide in a struggle that chills even the bold.");
        }

        return options[UnityEngine.Random.Range(0, options.Count)];
    }

    private static string GetRaceBattleNoun(RacesEnum race, bool plural)
    {
        return race switch
        {
            RacesEnum.Common => plural ? "the hosts of men" : "the hosts of men",
            RacesEnum.Elf => plural ? "elven ranks" : "elven ranks",
            RacesEnum.Dwarf => plural ? "dwarven shields" : "dwarven shields",
            RacesEnum.Hobbit => plural ? "hobbit companies" : "hobbit companies",
            RacesEnum.Maia => plural ? "maiar servants" : "maiar servants",
            RacesEnum.Orc => plural ? "orc warbands" : "orc warbands",
            RacesEnum.Troll => plural ? "troll packs" : "troll packs",
            RacesEnum.Nazgul => plural ? "nazgul terror" : "nazgul terror",
            RacesEnum.Spider => plural ? "spider broods" : "spider broods",
            RacesEnum.Dragon => plural ? "dragon-kin" : "dragon-kin",
            RacesEnum.Balrog => plural ? "balrog fire" : "balrog fire",
            RacesEnum.Undead => plural ? "the restless dead" : "the restless dead",
            RacesEnum.Dunedain => plural ? "dunedain companies" : "dunedain companies",
            RacesEnum.Beorning => plural ? "beorning fighters" : "beorning fighters",
            RacesEnum.Eagle => plural ? "eagle talons" : "eagle talons",
            RacesEnum.Wildman => plural ? "wildman hunters" : "wildman hunters",
            RacesEnum.Goblin => plural ? "goblin mobs" : "goblin mobs",
            RacesEnum.Ent => plural ? "entish strength" : "entish strength",
            RacesEnum.Southron => plural ? "southron ranks" : "southron ranks",
            RacesEnum.Easterling => plural ? "easterling companies" : "easterling companies",
            RacesEnum.Beast => plural ? "beast packs" : "beast packs",
            RacesEnum.Machine => plural ? "siege engines" : "siege engines",
            _ => plural ? "warbands" : "warbands"
        };
    }

    private static bool IsPlayerLeader(Leader leader)
    {
        Game g = Game.Instance;
        return g != null && leader != null && g.player == leader;
    }

    private static bool PlayerCanSeeHex(Hex hex)
    {
        if (hex == null) return false;
        Game g = Game.Instance;
        if (g == null || g.player == null) return false;
        return g.player.visibleHexes.Contains(hex) && hex.IsHexSeen();
    }

    private void ShowHudMessagesAfterPopup(Hex hex, Character actor, List<(string message, Color color)> messages)
    {
        if (hex == null || actor == null || messages == null) return;
        foreach (var entry in messages)
        {
            MessageDisplayNoUI.ShowMessage(hex, actor, entry.message, entry.color);
        }
    }

    // One CombatBanner notice line per non-zero battle modifier — fortification/PC defense,
    // allied army contributions, commander skill bonus, and artifact bonuses — colored green
    // (bonus) via CombatBanner.ColorizeModifier. Every value here already comes out of
    // ProcessCombat's own math; this only decides which of them are worth calling out.
    private static List<string> BuildBattleModifierNotices(
        string attackerName, string defenderName, int pcDefenseContribution,
        int attackerAlliesJoined, int attackerAlliesStrength, int attackerAlliesDefence,
        int defenderAlliesJoined, int defenderAlliesStrength, int defenderAlliesDefence,
        int attackerBonusPercent, int defenderBonusPercent,
        int attackerArtifactAttack, int attackerArtifactDefense,
        int defenderArtifactAttack, int defenderArtifactDefense)
    {
        List<string> notices = new();

        if (pcDefenseContribution != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{defenderName}'s fortifications: +{pcDefenseContribution} defense", pcDefenseContribution));
        }
        if (attackerAlliesJoined > 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{attackerName}'s allies ({attackerAlliesJoined}): +{attackerAlliesStrength} str / +{attackerAlliesDefence} def", attackerAlliesStrength + attackerAlliesDefence));
        }
        if (defenderAlliesJoined > 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{defenderName}'s allies ({defenderAlliesJoined}): +{defenderAlliesStrength} str / +{defenderAlliesDefence} def", defenderAlliesStrength + defenderAlliesDefence));
        }
        if (attackerBonusPercent != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{attackerName}'s command bonus: +{attackerBonusPercent}%", attackerBonusPercent));
        }
        if (defenderBonusPercent != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{defenderName}'s command bonus: +{defenderBonusPercent}%", defenderBonusPercent));
        }
        if (attackerArtifactAttack != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{attackerName}'s relics: +{attackerArtifactAttack} attack", attackerArtifactAttack));
        }
        if (attackerArtifactDefense != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{attackerName}'s relics: +{attackerArtifactDefense} defense", attackerArtifactDefense));
        }
        if (defenderArtifactAttack != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{defenderName}'s relics: +{defenderArtifactAttack} attack", defenderArtifactAttack));
        }
        if (defenderArtifactDefense != 0)
        {
            notices.Add(CombatBanner.ColorizeModifier($"{defenderName}'s relics: +{defenderArtifactDefense} defense", defenderArtifactDefense));
        }

        return notices;
    }

    private bool AreDefendersStillAlive(Hex targetHex, AlignmentEnum attackerAlignment, Leader attackerLeader)
    {
        return targetHex.armies.Any(army =>
        {
            if (army == null || army.killed || army.GetSize(true) < 1 || army.commander == null) return false;
            bool isOwnArmy = army.commander.GetOwner() == attackerLeader;
            if (isOwnArmy) return false;
            return army.GetAlignment() != attackerAlignment || attackerAlignment == AlignmentEnum.neutral || army.GetAlignment() == AlignmentEnum.neutral;
        });
    }

    private string FormatDelta(int delta)
    {
        if (delta > 0) return $"+{delta}";
        if (delta < 0) return delta.ToString();
        return "0";
    }

    public Character GetCommander()
    {
        if(commander.killed) return null;
        return commander;
    }

    public int GetMaintenanceCost()
    {
        if (startingArmy) return 0;
        float cost = 0f;
        cost += ma / 4;
        cost += ar / 4;
        cost += li / 3;
        cost += hi / 2;
        cost += lc / 2;
        cost += hc;

        return Mathf.FloorToInt(cost);
    }

    public List<ArmySpecialAbilityEnum> GetSpecialAbilities(TroopsTypeEnum troopType)
    {
        EnsureTroopGroupsInitialized();
        if (troopAbilityGroups == null) return new List<ArmySpecialAbilityEnum>();
        return troopAbilityGroups
            .Where(group => group != null && group.troopType == troopType && group.amount > 0)
            .SelectMany(group => group.abilities ?? new List<ArmySpecialAbilityEnum>())
            .Distinct()
            .OrderBy(value => (int)value)
            .ToList();
    }

    public int GetAbilityTroopCount(ArmySpecialAbilityEnum ability)
    {
        EnsureTroopGroupsInitialized();
        if (troopAbilityGroups == null) return 0;

        return troopAbilityGroups
            .Where(group => group != null && group.amount > 0 && group.abilities != null && group.abilities.Contains(ability))
            .Sum(group => group.amount);
    }

    private float GetAbilityCoverageRatio(ArmySpecialAbilityEnum ability)
    {
        int totalTroops = GetSize(false);
        if (totalTroops <= 0) return 0f;
        return Mathf.Clamp01(GetAbilityTroopCount(ability) / (float)totalTroops);
    }

    private bool HasSpecialAbility(ArmySpecialAbilityEnum ability)
    {
        return GetAbilityTroopCount(ability) > 0;
    }

    private bool HasCavalryTroops()
    {
        return lc > 0 || hc > 0;
    }

    public List<ArmyTroopAbilityGroup> GetTroopGroups()
    {
        EnsureTroopGroupsInitialized();
        if (troopAbilityGroups == null) return new List<ArmyTroopAbilityGroup>();

        return troopAbilityGroups
            .Where(group => group != null && group.amount > 0)
            .Select(group => new ArmyTroopAbilityGroup
            {
                troopType = group.troopType,
                amount = group.amount,
                troopName = group.troopName,
                abilities = group.abilities != null ? new List<ArmySpecialAbilityEnum>(group.abilities) : new List<ArmySpecialAbilityEnum>()
            })
            .ToList();
    }

    private List<string> BuildTroopHoverLines()
    {
        return GetTroopGroups()
            .Select(BuildTroopHoverLine)
            .ToList();
    }

    private string BuildTroopHoverLine(ArmyTroopAbilityGroup group)
    {
        if (group == null || group.amount <= 0) return string.Empty;

        string troopName = !string.IsNullOrWhiteSpace(group.troopName) ? group.troopName : GetDefaultTroopName(group.troopType);
        string line = $"<sprite name=\"{group.troopType.ToString().ToLower()}\">{group.amount} {troopName}";
        List<ArmySpecialAbilityEnum> abilities = group.abilities != null
            ? group.abilities.Distinct().OrderBy(value => (int)value).ToList()
            : new List<ArmySpecialAbilityEnum>();
        if (abilities.Count == 0) return line;
        return $"{line} {string.Join(",", abilities.Select(FormatAbilityLabel))}";
    }

    private static string FormatAbilityLabel(ArmySpecialAbilityEnum ability)
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

    private float GetAbilityTriggerChancePercent(ArmySpecialAbilityEnum ability, int basePercentChance)
    {
        int troopCount = GetAbilityTroopCount(ability);
        if (troopCount <= 0) return 0f;

        float singleTroopChance = Mathf.Clamp01(basePercentChance / 100f);
        float combinedChance = 1f - Mathf.Pow(1f - singleTroopChance, troopCount);
        return Mathf.Clamp(combinedChance * 100f, 0f, 100f);
    }

    private void AddTroopGroup(TroopsTypeEnum troopType, int amount, string troopName, IEnumerable<ArmySpecialAbilityEnum> abilities)
    {
        if (amount <= 0) return;

        List<ArmySpecialAbilityEnum> normalized = (abilities ?? Enumerable.Empty<ArmySpecialAbilityEnum>())
            .Distinct()
            .OrderBy(value => (int)value)
            .ToList();
        string normalizedTroopName = !string.IsNullOrWhiteSpace(troopName) ? troopName : GetDefaultTroopName(troopType);

        troopAbilityGroups ??= new List<ArmyTroopAbilityGroup>();
        ArmyTroopAbilityGroup existing = troopAbilityGroups.FirstOrDefault(group => group != null && group.Matches(troopType, normalizedTroopName, normalized));
        if (existing != null)
        {
            existing.amount += amount;
            return;
        }

        troopAbilityGroups.Add(new ArmyTroopAbilityGroup
        {
            troopType = troopType,
            amount = amount,
            troopName = normalizedTroopName,
            abilities = normalized
        });
    }

    private void RemoveSpecialTroops(TroopsTypeEnum troopType, int amount)
    {
        EnsureTroopGroupsInitialized();
        if (amount <= 0 || troopAbilityGroups == null || troopAbilityGroups.Count == 0) return;

        int remaining = amount;
        while (remaining > 0)
        {
            List<ArmyTroopAbilityGroup> matchingGroups = troopAbilityGroups
                .Where(group => group != null && group.troopType == troopType && group.amount > 0)
                .ToList();
            int totalMatching = matchingGroups.Sum(group => group.amount);
            if (totalMatching <= 0) break;

            int roll = UnityEngine.Random.Range(0, totalMatching);
            int cumulative = 0;
            for (int i = 0; i < matchingGroups.Count; i++)
            {
                cumulative += matchingGroups[i].amount;
                if (roll >= cumulative) continue;
                matchingGroups[i].amount = Math.Max(0, matchingGroups[i].amount - 1);
                break;
            }

            remaining--;
        }

        troopAbilityGroups.RemoveAll(group => group == null || group.amount <= 0);
        TrimSpecialTroopGroupsToCurrentCounts();
    }

    private int GetTroopCount(TroopsTypeEnum troopType)
    {
        return troopType switch
        {
            TroopsTypeEnum.ma => ma,
            TroopsTypeEnum.ar => ar,
            TroopsTypeEnum.li => li,
            TroopsTypeEnum.hi => hi,
            TroopsTypeEnum.lc => lc,
            TroopsTypeEnum.hc => hc,
            TroopsTypeEnum.ca => ca,
            TroopsTypeEnum.ws => ws,
            _ => 0
        };
    }

    private void TrimSpecialTroopGroupsToCurrentCounts()
    {
        EnsureTroopGroupsInitialized();
        if (troopAbilityGroups == null || troopAbilityGroups.Count == 0) return;

        foreach (TroopsTypeEnum troopType in Enum.GetValues(typeof(TroopsTypeEnum)))
        {
            int available = GetTroopCount(troopType);
            List<ArmyTroopAbilityGroup> groups = troopAbilityGroups
                .Where(group => group != null && group.troopType == troopType && group.amount > 0)
                .ToList();
            int specialTotal = groups.Sum(group => group.amount);
            int overflow = Math.Max(0, specialTotal - available);
            if (overflow <= 0) continue;

            for (int i = groups.Count - 1; i >= 0 && overflow > 0; i--)
            {
                int remove = Math.Min(groups[i].amount, overflow);
                groups[i].amount -= remove;
                overflow -= remove;
            }
        }

        troopAbilityGroups.RemoveAll(group => group == null || group.amount <= 0);
    }

    private void EnsureTroopGroupsInitialized()
    {
        troopAbilityGroups ??= new List<ArmyTroopAbilityGroup>();
        EnsureTroopTypeCoverage(TroopsTypeEnum.ma, ma);
        EnsureTroopTypeCoverage(TroopsTypeEnum.ar, ar);
        EnsureTroopTypeCoverage(TroopsTypeEnum.li, li);
        EnsureTroopTypeCoverage(TroopsTypeEnum.hi, hi);
        EnsureTroopTypeCoverage(TroopsTypeEnum.lc, lc);
        EnsureTroopTypeCoverage(TroopsTypeEnum.hc, hc);
        EnsureTroopTypeCoverage(TroopsTypeEnum.ca, ca);
        EnsureTroopTypeCoverage(TroopsTypeEnum.ws, ws);
        troopAbilityGroups.RemoveAll(group => group == null || group.amount <= 0);
    }

    private void EnsureTroopTypeCoverage(TroopsTypeEnum troopType, int totalAmount)
    {
        if (totalAmount <= 0) return;

        int groupedAmount = troopAbilityGroups
            .Where(group => group != null && group.troopType == troopType && group.amount > 0)
            .Sum(group => group.amount);

        if (groupedAmount >= totalAmount) return;
        AddTroopGroup(troopType, totalAmount - groupedAmount, null, null);
    }

    private static string GetDefaultTroopName(TroopsTypeEnum troopType)
    {
        return troopType switch
        {
            TroopsTypeEnum.ma => "Men-at-arms",
            TroopsTypeEnum.ar => "Archers",
            TroopsTypeEnum.li => "Light Infantry",
            TroopsTypeEnum.hi => "Heavy Infantry",
            TroopsTypeEnum.lc => "Light Cavalry",
            TroopsTypeEnum.hc => "Heavy Cavalry",
            TroopsTypeEnum.ca => "Catapults",
            TroopsTypeEnum.ws => "Warships",
            _ => troopType.ToString()
        };
    }
}
