using System;
using UnityEngine;

public class BuySteel : EmmissaryPCAction
{

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        isBuyCaravans = true;
        isSellCaravans = false;
        steelCost = 5; // quantity purchased
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        goldCost = 0;
        effect = (c) =>
        {
            if (originalEffect != null && !originalEffect(c)) return false;
            if (c.GetOwner() is not PlayableLeader) return false;
            Leader playable = c.GetOwner();
            StoresManager stores = FindAnyObjectByType<StoresManager>();
            if (stores == null) return false;
            int quantity = 5;
            int totalCost = stores.GetBuyPrice(ProducesEnum.steel, quantity);
            if (!stores.HasStock(ProducesEnum.steel, quantity)) return false;
            if (playable.goldAmount < totalCost) return false;

            playable.RemoveGold(totalCost, false);
            playable.AddSteel(quantity);
            stores.AdjustStock(ProducesEnum.steel, -quantity);
            if (playable == Game.Instance.player) FindAnyObjectByType<StoresManager>().RefreshStores();
            return true;
        };
        condition = (c) =>
        {
            if (c == null) return false;
            StoresManager stores = FindAnyObjectByType<StoresManager>();
            if (stores == null) return false;
            Leader playable = c.GetOwner();
            int quantity = 5;
            int totalCost = stores.GetBuyPrice(ProducesEnum.steel, quantity);
            return stores.HasStock(ProducesEnum.steel, quantity)
                && playable != null
                && playable.goldAmount >= totalCost
                && (originalCondition == null || originalCondition(c));
        };
        asyncEffect = async (c) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}
