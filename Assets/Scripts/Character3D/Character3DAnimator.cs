using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays any clip from Character3DAnimationLibrary on a live 3D unit, by name, through a small
// Playables graph (prototype, see UnitAnimations). No AnimatorController: a base loop (idle, walk,
// sneak, guard...) that one-shots (spells, blows, hit reactions) crossfade over and then hand back
// to, plus a hold mode for poses that should stay put (death). Idles get a little life: now and
// then a standing unit plays one of the idle variants (looking around, shifting weight).
[RequireComponent(typeof(Animator))]
public class Character3DAnimator : MonoBehaviour
{
    private const int Slots = 4;
    private static readonly string[] IdleVariants = { "standing idle 02", "Standing Idle 03", "Standing Idle 04" };
    private const string Idle = "standing idle";

    private class Track
    {
        public string Name;
        public AnimationClip Clip;
        public AnimationClipPlayable Playable;
        public float Weight;
        public float FadeIn;      // seconds to reach full weight
        public bool OneShot;
        public bool Hold;         // freeze on the last frame instead of returning to the base loop
        public System.Action Done;
    }

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly Track[] tracks = new Track[Slots];
    private Track current;
    private string baseLoop = Idle;
    private float idleTimer;
    private float nextIdleVariant;

    /// <summary>Off for units that aren't the player's: the idle variants are combat-ready stances
    /// with weapon swings, which read as other nations' units attacking out of nowhere.</summary>
    public bool IdleVariety = true;

    public string Current => current?.Name;
    public bool IsBusy => current != null && current.OneShot && !current.Hold;
    public bool IsHolding => current != null && current.Hold;
    public string BaseLoop => baseLoop;

    private void Awake()
    {
        Animator animator = GetComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        graph = PlayableGraph.Create($"{name} (Character3D)");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, Slots);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        nextIdleVariant = Random.Range(6f, 14f);
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // The loop a unit falls back to (idle, walk, crouch walk...). Takes over at once unless a
    // one-shot or a held pose is playing; those return to it when they finish.
    public void SetBaseLoop(string clipName, float fade = 0.2f)
    {
        if (string.IsNullOrEmpty(clipName)) clipName = Idle;
        if (Library(clipName) == null) clipName = Idle;
        bool changed = baseLoop != clipName;
        baseLoop = clipName;
        if (current == null || (!current.OneShot && (changed || current.Name != clipName)))
            Start(clipName, fade, oneShot: false, hold: false, speed: 1f, done: null, randomPhase: current == null);
    }

    // Plays a clip once over the base loop, then crossfades back to it (or, with hold, stays on
    // its last frame — for deaths). Returns false if the clip isn't in the library.
    public bool PlayOnce(string clipName, float fade = 0.15f, float speed = 1f, bool hold = false, System.Action done = null)
    {
        if (Library(clipName) == null) return false;
        Start(clipName, fade, oneShot: true, hold: hold, speed: speed, done: done, randomPhase: false);
        return true;
    }

    // Leaves a held pose (e.g. a unit that was "killed" but turns out to stay on the board).
    public void Release(float fade = 0.3f)
    {
        if (current != null && current.Hold) Start(baseLoop, fade, false, false, 1f, null, false);
    }

    private static readonly HashSet<string> warnedMissing = new();

    private static AnimationClip Library(string clipName)
    {
        AnimationClip clip = Character3DAnimationLibrary.Instance?.Get(clipName);
        if (clip == null && clipName != null && warnedMissing.Add(clipName))
            Debug.LogWarning($"[Character3D] Animation '{clipName}' is not in Resources/Character3DAnimations.");
        return clip;
    }

    private void Start(string clipName, float fade, bool oneShot, bool hold, float speed, System.Action done, bool randomPhase)
    {
        AnimationClip clip = Library(clipName);
        if (clip == null) return;

        int slot = FreeSlot();
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(true);
        playable.SetSpeed(speed);
        if (randomPhase) playable.SetTime(Random.value * clip.length);
        if (oneShot) playable.SetDuration(clip.length);
        graph.Connect(playable, 0, mixer, slot);

        // An interrupted one-shot's follow-up (the rest of a chain) is dropped, not fired.
        if (current != null) current.Done = null;
        current = new Track
        {
            Name = clipName, Clip = clip, Playable = playable, Weight = tracks[slot] == null && AllEmpty() ? 1f : 0f,
            FadeIn = Mathf.Max(fade, 0.01f), OneShot = oneShot, Hold = hold, Done = done,
        };
        tracks[slot] = current;
        idleTimer = 0f;
        ApplyWeights();
    }

    private bool AllEmpty()
    {
        foreach (Track t in tracks) if (t != null) return false;
        return true;
    }

    // An empty slot, or the faintest track (dropped) when all are busy.
    private int FreeSlot()
    {
        int weakest = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (tracks[i] == null) return i;
            if (tracks[i] != current && (tracks[weakest] == current || tracks[i].Weight < tracks[weakest].Weight)) weakest = i;
        }
        Drop(weakest);
        return weakest;
    }

    private void Drop(int slot)
    {
        Track t = tracks[slot];
        if (t == null) return;
        graph.Disconnect(mixer, slot);
        if (t.Playable.IsValid()) t.Playable.Destroy();
        tracks[slot] = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (current != null)
        {
            // Fade the current track in and everything else out at the current track's pace.
            float step = dt / current.FadeIn;
            for (int i = 0; i < Slots; i++)
            {
                Track t = tracks[i];
                if (t == null) continue;
                t.Weight = Mathf.MoveTowards(t.Weight, t == current ? 1f : 0f, step);
                if (t != current && t.Weight <= 0f) Drop(i);
            }
            ApplyWeights();

            if (current.OneShot)
            {
                float time = (float)current.Playable.GetTime();
                float length = current.Clip.length;
                if (current.Hold)
                {
                    if (time >= length) current.Playable.SetSpeed(0);
                }
                else if (time >= length - Mathf.Min(0.2f, length * 0.25f) * (float)current.Playable.GetSpeed())
                {
                    System.Action done = current.Done;
                    current.Done = null;
                    Start(baseLoop, 0.25f, false, false, 1f, null, false);
                    done?.Invoke();
                }
            }
            else if (current.Name == Idle && IdleVariety)
            {
                // Idle life: a variant once in a while, then back to the plain idle.
                idleTimer += dt;
                if (idleTimer >= nextIdleVariant)
                {
                    nextIdleVariant = Random.Range(8f, 18f);
                    PlayOnce(IdleVariants[Random.Range(0, IdleVariants.Length)], 0.35f);
                }
            }
        }
    }

    private void ApplyWeights()
    {
        float total = 0f;
        foreach (Track t in tracks) if (t != null) total += t.Weight;
        if (total <= 0f) total = 1f;
        for (int i = 0; i < Slots; i++)
            mixer.SetInputWeight(i, tracks[i] != null ? tracks[i].Weight / total : 0f);
    }
}
