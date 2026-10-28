using System;
public class Attack : CommanderEnemyArmyAction
{
    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) => originalEffect == null || originalEffect(c);
        condition = (c) => {
            return originalCondition == null || originalCondition(c); 
        };
        // The battle screen opens first (arming the host, the dice), then the blow lands.
        asyncEffect = async (c) => {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            Army army = c.GetArmy();
            if (army == null) return false;
            await BattleScreen.PrepareAsync(army, army.FindDefender(c.hex));
            if (army.killed || c.GetArmy() != army) return false;
            army.Attack(c.hex);
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}
