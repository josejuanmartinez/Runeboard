using System.Collections.Generic;
using UnityEngine;

// What a unit on the board does when something happens to it — the gameplay-facing side of the
// live 3D characters (prototype). Game code reports events here (an action executed or failed,
// combat, a wound, a death, a level-up, a recruit); each picks a fitting move, plays it on the
// unit's 3D model through Character3DView/Character3DAnimator (any Humanoid clip in
// Character3DAnimationLibrary, with variety), and falls back to the nearest baked spritesheet state
// on the sprite controller, so the 2D look gets livelier too. Safe to call from anywhere, for human
// and AI alike: units not on screen just ignore it.
public static class UnitAnimations
{
    public enum Move
    {
        Cast, BigCast, Gesture, Sneak, Strike, Rally, Guard, Attack,
        Hit, HitLarge, Block, Death, Flinch, Cheer, Arrive,
    }

    private static readonly Dictionary<Move, string[]> Clips = new()
    {
        { Move.Cast, new[] { "Standing 1H Magic Attack 01", "Standing 1H Magic Attack 02", "Standing 1H Magic Attack 03", "standing 1H cast spell 01" } },
        { Move.BigCast, new[] { "Standing 2H Magic Area Attack 01", "Standing 2H Magic Area Attack 02", "Standing 2H Magic Attack 01",
            "Standing 2H Magic Attack 02", "Standing 2H Magic Attack 03", "Standing 2H Magic Attack 04", "Standing 2H Magic Attack 05", "Standing 2H Cast Spell 01" } },
        { Move.Gesture, new[] { "standing 1H cast spell 01", "Standing 2H Cast Spell 01" } },
        { Move.Strike, new[] { "Standing Melee Attack Downward", "Standing 1H Magic Attack 02" } },
        { Move.Attack, new[] { "Standing Melee Attack Downward" } },
        { Move.Hit, new[] { "Standing React Small From Front", "Standing React Small From Left", "Standing React Small From Right", "Standing React Small From Back" } },
        { Move.HitLarge, new[] { "Standing React Large From Front", "Standing React Large From Left", "Standing React Large From Right", "Standing React Large From Back" } },
        { Move.Block, new[] { "Standing Block React Large" } },
        { Move.Death, new[] { "Standing React Death Backward", "Standing React Death Forward", "Standing React Death Left", "Standing React Death Right" } },
        { Move.Flinch, new[] { "Standing React Small From Front" } },
        { Move.Cheer, new[] { "Standing Jump" } },
        { Move.Arrive, new[] { "Standing Land To Standing Idle" } },
    };

    // Destructive spells get the big two-handed casts; the rest a one-handed one.
    private static readonly HashSet<string> BigSpells = new()
    {
        "IceStorm", "WizardsFire", "Fear", "Curse", "CastDarkness", "Dawn", "DoorsOfNight", "Corruption", "Poison",
        "YouShallNotPass", "WordsOfDispair", "WhiteHorsesFoam", "Fireworks", "Possession", "Root", "Halt", "SummonMA",
    };
    private static readonly HashSet<string> StrikeActions = new() { "AssassinateCharacter", "WoundCharacter", "KidnapCharacter", "Duel" };
    private static readonly HashSet<string> GuardActions = new() { "GuardCharacter", "GuardPC" };

    // Events for units whose 3D view isn't ready yet (just recruited, model still loading, or the
    // hex redraws this frame) wait here a moment.
    private const float PendingLifetime = 1.5f;
    // A small queue per unit: an action that fails reports the attempt and the flinch in the same frame.
    private static readonly Dictionary<Character, List<(Move move, Vector3? facing, float at, float delay)>> pending = new();

    // ── Gameplay events ─────────────────────────────────────────────────────────────────────

    public static void ActionExecuted(Character actor, CharacterAction action)
    {
        if (actor == null || action == null || action is Pass || action is EventAction) return;
        string type = action.GetType().Name;
        Move move;
        if (action is Spell || action is MageAction) move = BigSpells.Contains(type) ? Move.BigCast : Move.Cast;
        else if (StrikeActions.Contains(type)) move = Move.Strike;
        else if (GuardActions.Contains(type)) move = Move.Guard;
        else if (action is AgentAction) move = Move.Sneak;
        else if (action is EmmissaryAction) move = Move.Gesture;
        else if (type == "Attack") move = Move.Attack;
        else if (action is CommanderAction) move = Move.Rally;
        else move = Move.Gesture;
        Play(actor, move);
    }

    public static void ActionFailed(Character actor) => Play(actor, Move.Flinch, delay: 0.9f);

    // Army battle: the attacker turns on the defender and swings; the defender takes it a beat
    // later — a block if they hold, a heavy reaction if not.
    public static void Combat(Character attacker, Character defender, bool defenderHeld)
    {
        Vector3? attackerPos = Position(attacker), defenderPos = Position(defender);
        if (attacker != null) Play(attacker, Move.Attack, facing: defenderPos);
        if (defender != null) Play(defender, defenderHeld ? Move.Block : Move.HitLarge, facing: attackerPos, delay: 0.45f);
    }

    public static void Wounded(Character character, int damage)
    {
        // Called before the damage lands: a fatal wound is left to Killed().
        if (character == null || character.killed || character.health - damage < 1) return;
        Play(character, damage >= 15 ? Move.HitLarge : Move.Hit, delay: 0.3f);
    }

    // Called at the start of Character.Killed, before the unit leaves its hex: the 3D model is
    // detached on the spot to fall and fade where it stood.
    public static void Killed(Character character)
    {
        if (character == null || character.killed) return;
        Character3DView view = Character3DView.For(character);
        if (view != null && view.PlayDeathAndDetach(Pick(Move.Death))) return;
        SpriteFallback(character, Move.Death);
    }

    public static void LeveledUp(Character character) => Play(character, Move.Cheer, delay: 0.6f);

    public static void Recruited(Character character)
    {
        if (Game.Instance == null || !Game.Instance.started) return; // setup / save loading
        Play(character, Move.Arrive);
    }

    // Acts a move out on a 3D unit: most are one clip, a few are short chains.
    public static void Perform(Character3DAnimator anim, Move move, string clip, bool hidden)
    {
        switch (move)
        {
            case Move.Sneak:
                // Drop low, then rise (or stay down if the unit is hidden — its loop is a crouch).
                anim.PlayOnce("Standing Idle To Crouch", 0.15f, 1.1f,
                    done: hidden ? null : () => anim.PlayOnce("Crouch To Standing Idle", 0.15f, 1.1f));
                break;
            case Move.Rally:
            case Move.Guard:
                // Raise the guard and lower it again; Guard holds it a moment in between.
                bool hold = move == Move.Guard;
                anim.PlayOnce("Standing Block Start", 0.15f, 1.2f, done: () =>
                {
                    if (hold) anim.PlayOnce("Standing Block Idle", 0.1f, 1f, done: () => anim.PlayOnce("Standing Block End", 0.1f, 1.2f));
                    else anim.PlayOnce("Standing Block End", 0.1f, 1.2f);
                });
                break;
            case Move.Cast:
            case Move.BigCast:
                anim.PlayOnce(clip, 0.12f, 1.15f);
                break;
            default:
                if (clip != null) anim.PlayOnce(clip, 0.12f);
                break;
        }
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────

    public static void Play(Character character, Move move, Vector3? facing = null, float delay = 0f)
    {
        if (character == null || character.killed) return;
        if (!pending.TryGetValue(character, out var queue)) pending[character] = queue = new();
        queue.Add((move, facing, Time.time, delay));
        SpriteFallback(character, move);
    }

    // Polled by each Character3DView: hands over this unit's oldest waiting move once its delay is
    // up. Moves nobody picked up in time (unit off screen, no model) are dropped.
    public static bool TryTake(Character character, out Move move, out string clip, out Vector3? facing)
    {
        move = default; clip = null; facing = null;
        if (character == null || !pending.TryGetValue(character, out var queue)) return false;
        queue.RemoveAll(e => Time.time - e.at > PendingLifetime + e.delay);
        if (queue.Count == 0) { pending.Remove(character); return false; }
        var entry = queue[0];
        if (Time.time - entry.at < entry.delay) return false;
        queue.RemoveAt(0);
        move = entry.move;
        facing = entry.facing;
        clip = Pick(move);
        return true;
    }

    public static string Pick(Move move)
    {
        if (!Clips.TryGetValue(move, out string[] options) || options.Length == 0) return null;
        return options[Random.Range(0, options.Length)];
    }

    private static Vector3? Position(Character character) =>
        character != null && character.hex != null ? character.hex.transform.position : null;

    // The nearest baked state, for units drawn as sprites (3D off, no FBX, or still loading).
    private static void SpriteFallback(Character character, Move move)
    {
        Character3DView view = Character3DView.For(character);
        if (view == null || view.ShowsModel) return;
        CharacterAnimationController controller = view.Controller;
        if (controller == null || !controller.IsShowing) return;
        CharacterAnimationController.AnimationKind kind = move switch
        {
            Move.Cast or Move.BigCast => CharacterAnimationController.AnimationKind.Magic,
            Move.Attack or Move.Strike => CharacterAnimationController.AnimationKind.Attack,
            Move.Hit or Move.HitLarge or Move.Flinch => CharacterAnimationController.AnimationKind.Hit,
            Move.Block or Move.Guard => CharacterAnimationController.AnimationKind.Block,
            Move.Death => CharacterAnimationController.AnimationKind.Death,
            _ => CharacterAnimationController.AnimationKind.Action,
        };
        controller.SetAnimation(kind);
    }
}
