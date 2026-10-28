using UnityEngine;

/// <summary>
/// What an army fights with. Every army starts with wooden weapons and leather armour; at the
/// battle screen, before the dice, a side can re-arm with iron, steel or mithril from its stores.
/// Re-arming costs one piece of the metal per troop and lasts for the army's life. Weapons raise
/// its attack, armour its defence.
/// </summary>
public static class ArmyEquipment
{
    public const int MaxTier = 3;

    public static readonly string[] WeaponNames = { "Wooden weapons", "Iron weapons", "Steel weapons", "Mithril weapons" };
    public static readonly string[] ArmourNames = { "Leather armour", "Iron armour", "Steel armour", "Mithril armour" };
    public static readonly string[] Metals = { "", "iron", "steel", "mithril" }; // the sprite names too

    /// <summary>Attack (weapons) or defence (armour) multiplier of a tier.</summary>
    public static float Multiplier(int tier) => tier switch
    {
        1 => 1.10f,
        2 => 1.20f,
        3 => 1.35f,
        _ => 1f,
    };

    public static string Bonus(int tier) => tier <= 0 ? "base" : $"+{Mathf.RoundToInt((Multiplier(tier) - 1f) * 100f)}%";

    /// <summary>Troops that would carry the new arms (warships carry none).</summary>
    public static int Troops(Army army) =>
        army == null ? 0 : army.ma + army.ar + army.li + army.hi + army.lc + army.hc + army.ca;

    public static int Stock(Leader leader, int tier) => leader == null ? 0 : tier switch
    {
        1 => leader.ironAmount,
        2 => leader.steelAmount,
        3 => leader.mithrilAmount,
        _ => 0,
    };

    public static int CurrentTier(Army army, bool weapons) => army == null ? 0 : weapons ? army.weaponTier : army.armourTier;

    /// <summary>Whether the army's owner can re-arm it to this tier now (a step up, stores enough).</summary>
    public static bool CanUpgrade(Army army, bool weapons, int tier)
    {
        if (army == null || army.killed || army.commander == null || tier <= CurrentTier(army, weapons) || tier > MaxTier) return false;
        int troops = Troops(army);
        return troops > 0 && Stock(army.commander.GetOwner(), tier) >= troops;
    }

    public static bool Upgrade(Army army, bool weapons, int tier)
    {
        if (!CanUpgrade(army, weapons, tier)) return false;
        Leader owner = army.commander.GetOwner();
        int troops = Troops(army);
        switch (tier)
        {
            case 1: owner.ironAmount -= troops; break;
            case 2: owner.steelAmount -= troops; break;
            case 3: owner.mithrilAmount -= troops; break;
        }
        if (weapons) army.weaponTier = tier;
        else army.armourTier = tier;
        return true;
    }

    /// <summary>A computer-led side re-arms with the best metal it can afford, weapons first.</summary>
    public static void UpgradeForAi(Army army)
    {
        foreach (bool weapons in new[] { true, false })
            for (int tier = MaxTier; tier > CurrentTier(army, weapons); tier--)
                if (Upgrade(army, weapons, tier)) break;
    }
}
