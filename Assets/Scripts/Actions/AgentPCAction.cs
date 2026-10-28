using System;
using UnityEngine;

public class AgentPCAction : AgentAction
{
    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) => {
            if (originalEffect != null && !originalEffect(c)) return false;
            if (c.hex.GetPC() == null) return false;
            Hex capitalHex = Board.Instance.GetHexes().Find(x => x.GetPC() != null && x.GetPC().owner == c.GetOwner() && x.GetPC().isCapital);
            if (capitalHex == null) return false;
            int random = UnityEngine.Random.Range(0, 5);
            string message = "Agent returned to capital";
            if (random > c.GetAgent())
            {
                message += " wounded";
                c.Wounded(c.hex.GetPC().owner, random * 10);
            }
            Board.Instance.MoveCharacterOneHex(c, c.hex, capitalHex, true);
            MessageDisplayNoUI.ShowMessage(c.hex, c, message, Color.green);
            return true;
        };
        condition = (c) => {
            if (originalCondition != null && !originalCondition(c)) return false;
            // An unowned PC is nobody's enemy: it is claimed with its card instead.
            return c.hex.GetPC() != null && c.hex.GetPC().owner != null &&
            c.hex.GetPC().owner != c.GetOwner() &&
            (c.hex.GetPC().owner.GetAlignment() == AlignmentEnum.neutral || c.hex.GetPC().owner.GetAlignment() != c.GetAlignment()); 
        };
        asyncEffect = async (c) => {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}

