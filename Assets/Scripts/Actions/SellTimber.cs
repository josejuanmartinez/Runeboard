using System;

public class SellTimber : EmmissaryPCAction
{

    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        isSellCaravans = true;
        isBuyCaravans = false;
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) => {
            if (originalEffect != null && !originalEffect(c)) return false;
            Leader playable = c.GetOwner();
            if (playable == null) return false;
            StoresManager stores = FindAnyObjectByType<StoresManager>();
            if (stores == null) return false;
            int quantity = 5;
            int payout = stores.GetSellPrice(ProducesEnum.timber, quantity);
            playable.AddGold(payout);
            stores.AdjustStock(ProducesEnum.timber, quantity);
            if(playable == Game.Instance.player) FindAnyObjectByType<StoresManager>().RefreshStores();
            return true; 
        };
        condition = (c) => {
            return (originalCondition == null || originalCondition(c));
        };
        asyncEffect = async (c) => {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}

