using System;
using System.Collections.Generic;
using UnityEngine;

public class Caravans : CharacterAction
{
    private static int GetOwnedAmount(Leader owner, ProducesEnum resource)
    {
        return resource switch
        {
            ProducesEnum.leather => owner.leatherAmount,
            ProducesEnum.timber => owner.timberAmount,
            ProducesEnum.mounts => owner.mountsAmount,
            ProducesEnum.iron => owner.ironAmount,
            ProducesEnum.steel => owner.steelAmount,
            ProducesEnum.mithril => owner.mithrilAmount,
            _ => 0
        };
    }

    private static void RemoveOwned(Leader owner, ProducesEnum resource, int amount)
    {
        switch (resource)
        {
            case ProducesEnum.leather: owner.RemoveLeather(amount, false); break;
            case ProducesEnum.timber: owner.RemoveTimber(amount, false); break;
            case ProducesEnum.mounts: owner.RemoveMounts(amount, false); break;
            case ProducesEnum.iron: owner.RemoveIron(amount, false); break;
            case ProducesEnum.steel: owner.RemoveSteel(amount, false); break;
            case ProducesEnum.mithril: owner.RemoveMithril(amount, false); break;
        }
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (c) =>
        {
            if (originalEffect != null && !originalEffect(c)) return false;
            if (c == null || c.GetOwner() == null) return false;

            Leader owner = c.GetOwner();
            StoresManager stores = FindAnyObjectByType<StoresManager>();
            if (stores == null) return false;

            ProducesEnum[] sellables = {
                ProducesEnum.mithril,
                ProducesEnum.steel,
                ProducesEnum.iron,
                ProducesEnum.mounts,
                ProducesEnum.timber,
                ProducesEnum.leather,
            };

            int totalGold = 0;
            List<string> soldResources = new();

            foreach (var r in sellables)
            {
                int owned = GetOwnedAmount(owner, r);
                int qty = Mathf.Min(3, owned);
                if (qty <= 0) continue;

                int basePayout = stores.GetSellPrice(r, qty);
                int bonusPayout = Mathf.CeilToInt(basePayout * 1.25f); // +25%

                RemoveOwned(owner, r, qty);
                stores.AdjustStock(r, qty);

                totalGold += bonusPayout;
                soldResources.Add($"{qty} <sprite name=\"{r}\">{r}");
            }

            if (soldResources.Count == 0) return false;

            owner.AddGold(totalGold);

            if (owner == Game.Instance.player)
            {
                stores.RefreshStores();
            }

            // Trade income is private to the nation that earned it — other leaders only learn
            // the specifics (what sold, for how much) via the rumour/spying system, never as a
            // standing public LogWidget entry just because their hex happened to be in view.
            MessageDisplayNoUI.ShowMessage(c.hex, c, $"Caravans sold {string.Join(", ", soldResources)}, gaining {totalGold} <sprite name=\"gold\">gold.", Color.yellow, forcePrivateForOthers: true);
            return true;
        };

        condition = (c) =>
        {
            if (originalCondition != null && !originalCondition(c)) return false;
            if (c == null || c.GetOwner() == null) return false;

            Leader owner = c.GetOwner();
            return owner.leatherAmount > 0 || owner.timberAmount > 0 || owner.mountsAmount > 0 || owner.ironAmount > 0 || owner.steelAmount > 0 || owner.mithrilAmount > 0;
        };

        asyncEffect = async (c) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}
