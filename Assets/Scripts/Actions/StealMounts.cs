using System;
using UnityEngine;

public class StealMounts : AgentPCAction
{
    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) => {
            if (originalEffect != null && !originalEffect(c)) return false;
            PC pc = c.hex.GetPC();
            if (pc == null || pc.owner == null) return false;
            int maxSteal = Mathf.Min(c.GetAgent(), pc.owner.mountsAmount);
            if (maxSteal < 1) return false;
            int toSteal = UnityEngine.Random.Range(1, maxSteal + 1);
            if (toSteal < 1) return false;
            Leader playable = c.GetOwner();
            if (playable == null) return false;
            playable.AddMounts(toSteal);
            pc.owner.RemoveMounts(toSteal);
            MessageDisplayNoUI.ShowMessage(pc.hex, c, $"-{toSteal} <sprite name=\"mounts\"> stolen!", Color.red);
            MessageDisplay.ShowMessage($"+{toSteal} <sprite name=\"mounts\"> stolen!", Color.green);
            if (playable == Game.Instance.player) FindAnyObjectByType<StoresManager>().RefreshStores();
            return true; 
        };
        condition = (c) => {
            if (originalCondition != null && !originalCondition(c)) return false;
            PC pc = c.hex.GetPC();
            return pc != null && pc.owner != null && pc.owner.mountsAmount > 0;
        };
        asyncEffect = async (c) => {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }
}

