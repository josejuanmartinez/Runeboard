using System.Collections.Generic;
using UnityEngine;

// An army on the board, as a tabletop regiment: the commander's miniature (Character3DModel) leads
// a few rank-and-file troopers standing behind it on one oval stand, the way an army is a unit of
// several painted figures on a shared base. Lives on the commander model's root; the troopers are
// full models (their own animator and weapons) under that root, drawn as part of the commander
// (see Character3DModel.Troops) — one tint, fade, outline, depth slab, light and sort order — and
// destroyed with it. A lone character never gets one.
//
// How many troopers follow from the army's size (the 2D stack copies' thresholds idea), what they
// look like from the nation and the commander's people, what they carry from the army's main troop
// type. They follow the commander's base loop and its fighting moves a beat apart, so the rank
// never moves in lockstep; when the army shrinks past a threshold or is destroyed, the extra
// troopers fall and dissolve where they stood; when the commander dies, they all fall with it.
[RequireComponent(typeof(Character3DModel))]
public class Character3DRegiment : MonoBehaviour
{
    public const int MaxTroopers = 4;
    private const float TrooperScale = 0.82f;  // of the commander's height
    private const float LieStill = 2.4f;       // casualties, as Character3DCorpse
    private const float FadeTime = 1.2f;
    private const float FigureClearance = 0.15f; // stand margin around a trooper's feet, in ModelHeights

    // Slots behind the leader in ModelHeights (x = to its right, y = along its facing, so negative
    // is behind), by regiment size.
    private static readonly Vector2[][] Formations =
    {
        new Vector2[0],
        new[] { new Vector2(0f, -0.28f) },
        new[] { new Vector2(-0.2f, -0.24f), new Vector2(0.2f, -0.24f) },
        new[] { new Vector2(-0.26f, -0.18f), new Vector2(0.26f, -0.18f), new Vector2(0f, -0.34f) },
        new[] { new Vector2(-0.27f, -0.16f), new Vector2(0.27f, -0.16f), new Vector2(-0.1f, -0.34f), new Vector2(0.1f, -0.34f) },
    };

    // What the rank and file act out with their leader: the fighting, not the spells or errands.
    private static readonly HashSet<UnitAnimations.Move> Followed = new()
    {
        UnitAnimations.Move.Attack, UnitAnimations.Move.Strike, UnitAnimations.Move.Hit, UnitAnimations.Move.HitLarge,
        UnitAnimations.Move.Block, UnitAnimations.Move.Flinch, UnitAnimations.Move.Rally, UnitAnimations.Move.Guard,
        UnitAnimations.Move.Cheer, UnitAnimations.Move.Arrive,
    };

    private class Trooper
    {
        public Character3DModel Model;
        public int Index;            // spawn order, for weapon variety
        public Vector3 Target;       // slot, in the leader root's local space
        public bool Dying;
        public float DiedAt;
        public string Loop;          // base loop waiting for its beat
        public float LoopAt;
        public UnitAnimations.Move Move;
        public bool HasMove;
        public bool MoveHidden;
        public float MoveAt;
    }

    private Character3DModel leader;
    private readonly List<Trooper> troopers = new();
    private string folder;      // wanted look
    private string drawnFolder; // the one actually loaded (Common if the wanted one has no model)
    private TroopsTypeEnum troopType;
    private string loop;
    private int spawned;

    // ── Army → regiment ─────────────────────────────────────────────────────────────────────

    // No rank and file for now: troopers crowded a hex as overlapping copies of one figure, so an
    // army shows as its commander alone (its size is on the banners and the unit panel).
    public static int TroopCount(Army army) => 0;

    // The army's main fighting troop type (catapults and warships aside).
    public static TroopsTypeEnum MainTroopType(Army army)
    {
        TroopsTypeEnum best = TroopsTypeEnum.ma;
        int most = army.ma;
        if (army.ar > most) { best = TroopsTypeEnum.ar; most = army.ar; }
        if (army.li > most) { best = TroopsTypeEnum.li; most = army.li; }
        if (army.hi > most) { best = TroopsTypeEnum.hi; most = army.hi; }
        if (army.lc > most) { best = TroopsTypeEnum.lc; most = army.lc; }
        if (army.hc > most) best = TroopsTypeEnum.hc;
        return best;
    }

    // The troopers' model: the nation's people, else the commander's, else by allegiance.
    public static string TroopFolder(Character commander)
    {
        Leader owner = commander.GetOwner();
        string people = (owner != null ? PeopleFolder(owner.race) : null) ?? PeopleFolder(commander.race);
        if (people != null) return people;
        AlignmentEnum alignment = owner != null ? owner.alignment : commander.alignment;
        bool orcs = alignment == AlignmentEnum.darkServants || (owner != null && owner.characterName == "Saruman");
        return orcs ? "Orc" : "Common";
    }

    private static string PeopleFolder(RacesEnum race) => race switch
    {
        RacesEnum.Common => "Common",
        RacesEnum.Elf => "Elf",
        RacesEnum.Dwarf => "Dwarf",
        RacesEnum.Hobbit => "Hobbit",
        RacesEnum.Dunedain => "Dunedain",
        RacesEnum.Southron => "Southron",
        RacesEnum.Easterling => "Easterling",
        RacesEnum.Wildman => "Wildman",
        RacesEnum.Beorning => "Beorning",
        RacesEnum.Orc or RacesEnum.Goblin => "Orc",
        _ => null,
    };

    // ── Driven by Character3DView ───────────────────────────────────────────────────────────

    // Every frame while the commander shows; only creates or removes troopers when the count,
    // look or troop type changes. casualties: the same unit lost troops (they fall) rather than
    // the slot now showing someone else (they just go).
    public void Sync(string wantedFolder, TroopsTypeEnum type, int count, bool casualties)
    {
        if (leader == null) leader = GetComponent<Character3DModel>();
        count = Mathf.Clamp(count, 0, MaxTroopers);
        // No folder = no army left: keep the look, the troopers just go.
        if (wantedFolder != null && wantedFolder != folder)
        {
            Disband();
            folder = wantedFolder;
            drawnFolder = null;
        }
        if (count > 0 && type != troopType)
        {
            troopType = type;
            foreach (Trooper t in troopers)
                if (!t.Dying) t.Model.Arm(Character3DWeapons.ForTroop(drawnFolder, type, t.Index));
        }

        int live = Live();
        if (live == count) return;
        for (int i = troopers.Count - 1; i >= 0 && live > count; i--)
        {
            Trooper t = troopers[i];
            if (t.Dying) continue;
            if (casualties) Fall(t, Random.Range(0.1f, 0.5f));
            else Remove(i);
            live--;
        }
        if (live < count && folder != null)
        {
            GameObject prefab = Prefab();
            if (prefab == null) return; // still loading; tried again next frame
            int first = troopers.Count;
            for (; live < count; live++) Spawn(prefab);
            Relayout();
            for (int i = first; i < troopers.Count; i++) troopers[i].Model.transform.localPosition = troopers[i].Target;
            return;
        }
        Relayout();
    }

    public void Mirror(string baseLoop)
    {
        if (baseLoop == loop) return;
        loop = baseLoop;
        float now = Time.time;
        foreach (Trooper t in troopers)
        {
            if (t.Dying) continue;
            t.Loop = baseLoop;
            t.LoopAt = now + Random.Range(0f, 0.7f);
        }
    }

    public void Follow(UnitAnimations.Move move, bool hidden)
    {
        if (!Followed.Contains(move)) return;
        if (move == UnitAnimations.Move.Strike) move = UnitAnimations.Move.Attack; // no spells in the ranks
        float now = Time.time;
        foreach (Trooper t in troopers)
        {
            if (t.Dying) continue;
            t.Move = move;
            t.HasMove = true;
            t.MoveHidden = hidden;
            t.MoveAt = now + Random.Range(0.1f, 0.6f);
        }
    }

    // The commander died (Character3DCorpse): the regiment falls with it and fades with its tint.
    public void FallWithLeader()
    {
        foreach (Trooper t in troopers)
            if (!t.Dying) Fall(t, Random.Range(0.05f, 0.4f), dissolve: false);
    }

    // ── Troopers ────────────────────────────────────────────────────────────────────────────

    private GameObject Prefab()
    {
        if (drawnFolder == null)
        {
            Character3DModel.RequestPrefab(folder);
            drawnFolder = Character3DModel.IsMissing(folder) ? "Common" : folder;
        }
        return Character3DModel.RequestPrefab(drawnFolder);
    }

    private void Spawn(GameObject prefab)
    {
        Character3DModel model = Character3DModel.CreateTrooper(prefab, drawnFolder, leader);
        Transform t = model.transform;
        // Each trooper its own man: a little turned, a little taller or shorter, its own tempo and
        // idle fidgets — copies of one figure moving in lockstep read as clones.
        t.localRotation = Quaternion.Euler(0f, Random.Range(-16f, 16f), 0f);
        t.localScale = Vector3.one * (TrooperScale * Random.Range(0.93f, 1.05f) * leader.ModelHeight / model.ModelHeight);
        model.Animation.IdleVariety = true;
        model.Animation.Pace = Random.Range(0.86f, 1.14f);
        model.Animation.LoopRandomPhase = true;
        model.Animation.SetBaseLoop(loop, 0f); // idle until the leader's loop is known
        var trooper = new Trooper { Model = model, Index = spawned++ };
        model.Arm(Character3DWeapons.ForTroop(drawnFolder, troopType, trooper.Index));
        troopers.Add(trooper);
    }

    // dissolve: fade out and go on its own; otherwise lie there (the corpse fades the lot).
    private void Fall(Trooper t, float delay, bool dissolve = true)
    {
        t.Dying = true;
        t.DiedAt = dissolve ? Time.time + delay : float.PositiveInfinity;
        t.Loop = null;
        t.Move = UnitAnimations.Move.Death;
        t.HasMove = true;
        t.MoveAt = Time.time + delay;
    }

    private void Remove(int index)
    {
        Character3DModel model = troopers[index].Model;
        troopers.RemoveAt(index);
        if (model == null) return;
        if (leader != null) leader.Troops.Remove(model);
        Destroy(model.gameObject);
    }

    private void Disband()
    {
        for (int i = troopers.Count - 1; i >= 0; i--) Remove(i);
        Relayout();
    }

    private int Live()
    {
        int live = 0;
        foreach (Trooper t in troopers)
            if (!t.Dying) live++;
        return live;
    }

    private Vector3 SlotFor(int slot, int of)
    {
        Vector2[] formation = Formations[Mathf.Clamp(of, 0, MaxTroopers)];
        Vector2 p = formation.Length > 0 ? formation[Mathf.Clamp(slot, 0, formation.Length - 1)] : Vector2.zero;
        return new Vector3(p.x, 0f, p.y) * leader.ModelHeight;
    }

    // Living troopers take the formation for their number; the stand reaches under everyone
    // still on it (the fallen included, until they're gone).
    private void Relayout()
    {
        if (leader == null) return;
        int live = Live(), slot = 0;
        foreach (Trooper t in troopers)
            if (!t.Dying) t.Target = SlotFor(slot++, live);

        if (troopers.Count == 0)
        {
            leader.Footprint = Vector3.zero;
            return;
        }
        Character3DSettings settings = Character3DSettings.Instance;
        float radius = settings != null ? settings.baseRadius : 0.3f;
        float height = Mathf.Max(leader.ModelHeight, 1e-4f);
        float halfWidth = radius, back = -radius;
        foreach (Trooper t in troopers)
        {
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(t.Target.x) / height + FigureClearance);
            back = Mathf.Min(back, t.Target.z / height - FigureClearance);
        }
        leader.Footprint = new Vector3(halfWidth, (radius - back) * 0.5f, (radius + back) * 0.5f);
    }

    private void Update()
    {
        if (troopers.Count == 0) return;
        float now = Time.time;
        float step = leader.ModelHeight * 0.6f * Time.deltaTime; // stepping into a new slot
        bool gone = false;
        for (int i = troopers.Count - 1; i >= 0; i--)
        {
            Trooper t = troopers[i];
            if (t.Model == null)
            {
                troopers.RemoveAt(i);
                gone = true;
                continue;
            }
            Character3DAnimator anim = t.Model.Animation;
            if (t.HasMove && now >= t.MoveAt)
            {
                t.HasMove = false;
                string clip = UnitAnimations.Pick(t.Move);
                if (t.Move == UnitAnimations.Move.Death) anim.PlayOnce(clip, 0.1f, 1f, hold: true);
                else UnitAnimations.Perform(anim, t.Move, clip, t.MoveHidden);
            }
            if (t.Dying)
            {
                float age = now - t.DiedAt;
                t.Model.Fade = Mathf.Clamp01(1f - (age - LieStill) / FadeTime);
                if (t.Model.Fade <= 0f)
                {
                    Remove(i);
                    gone = true;
                }
                continue;
            }
            if (t.Loop != null && now >= t.LoopAt)
            {
                if (!anim.IsHolding) anim.SetBaseLoop(t.Loop, 0.25f);
                t.Loop = null;
            }
            Transform tr = t.Model.transform;
            if (tr.localPosition != t.Target) tr.localPosition = Vector3.MoveTowards(tr.localPosition, t.Target, step);
        }
        if (gone) Relayout();
    }
}
