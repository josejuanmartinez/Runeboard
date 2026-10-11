using System;
using System.Collections.Generic;
using UnityEngine;

public class SabotageStorage : AgentPCAction
{
    private struct ResourceSlot
    {
        public string label;
        public string sprite;
        public Func<Leader, int> getter;
        public Action<Leader, int> setter;
    }

    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) =>
        {
            if (originalEffect != null && !originalEffect(c)) return false;
            PC pc = c.hex.GetPC();
            if (pc == null) return false;
            Leader target = pc.owner;
            if (target == null) return false;

            List<ResourceSlot> slots = BuildSlots();
            List<ResourceSlot> available = slots.FindAll(slot => slot.getter(target) > 0);
            if (available.Count < 1) return false;

            int sabotageCount = Math.Min(2, available.Count);
            for (int i = 0; i < sabotageCount; i++)
            {
                int index = UnityEngine.Random.Range(0, available.Count);
                ResourceSlot slot = available[index];
                available.RemoveAt(index);

                int maxLoss = Math.Max(1, c.GetAgent());
                int current = slot.getter(target);
                int loss = Math.Min(current, UnityEngine.Random.Range(1, maxLoss + 1));
                if (loss < 1) continue;

                slot.setter(target, current - loss);
                MessageDisplayNoUI.ShowMessage(pc.hex, c, $"-{loss} <sprite name=\"{slot.sprite}\">{slot.sprite} sabotaged!", Color.red);
            }

            if (target == Game.Instance.player)
            {
                FindAnyObjectByType<StoresManager>().RefreshStores();
            }
            return true;
        };
        condition = (c) =>
        {
            if (originalCondition != null && !originalCondition(c)) return false;
            PC pc = c.hex.GetPC();
            if (pc == null || pc.owner == null) return false;

            Leader target = pc.owner;
            return BuildSlots().Exists(slot => slot.getter(target) > 0);
        };
        asyncEffect = async (c) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            return true;
        };
        base.Initialize(c, condition, effect, asyncEffect);
    }

    private static List<ResourceSlot> BuildSlots()
    {
        return new List<ResourceSlot>
        {
            new ResourceSlot
            {
                label = "Leather",
                sprite = "leather",
                getter = leader => leader.leatherAmount,
                setter = (leader, value) => leader.leatherAmount = value
            },
            new ResourceSlot
            {
                label = "Mounts",
                sprite = "mounts",
                getter = leader => leader.mountsAmount,
                setter = (leader, value) => leader.mountsAmount = value
            },
            new ResourceSlot
            {
                label = "Timber",
                sprite = "timber",
                getter = leader => leader.timberAmount,
                setter = (leader, value) => leader.timberAmount = value
            },
            new ResourceSlot
            {
                label = "Iron",
                sprite = "iron",
                getter = leader => leader.ironAmount,
                setter = (leader, value) => leader.ironAmount = value
            },
            new ResourceSlot
            {
                label = "Steel",
                sprite = "steel",
                getter = leader => leader.steelAmount,
                setter = (leader, value) => leader.steelAmount = value
            },
            new ResourceSlot
            {
                label = "Mithril",
                sprite = "mithril",
                getter = leader => leader.mithrilAmount,
                setter = (leader, value) => leader.mithrilAmount = value
            }
        };
    }
}
