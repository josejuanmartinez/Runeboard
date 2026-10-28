using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Authors Runeboard's own Humanoid clips from the Mixamo library ones (Character3DAnimationLibrary)
// and adds them to the library:
//   RB Ready Idle — the idle, straightened to face where the unit faces, weapon arm held ready.
//   RB Walk       — the walk, hips straightened to the way it walks, weapon arm carried steady.
//   RB Slash      — a weapon blow: the sword arm's arc from "Standing Melee Attack Downward",
//                   without that clip's spin and leap — feet planted, torso twist reduced, faster.
// The Mixamo clips turn the body ~45° off the facing and twist the neck back to look ahead
// (a crab walk on the board); straightening removes that offset from the root and the turn muscles.
// Run from the menu or in batch mode (-executeMethod AnimationForge.Run).
public static class AnimationForge
{
    private const string Folder = "Assets/Art/3D/Animations/Runeboard";
    private const string LibraryPath = "Assets/Resources/Character3DAnimations.asset";
    private const float Rate = 30f;

    private static readonly string[] RightArm =
    {
        "Right Shoulder Down-Up", "Right Shoulder Front-Back", "Right Arm Down-Up", "Right Arm Front-Back",
        "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out",
    };
    private static readonly string[] Turns =
    {
        "Spine Twist Left-Right", "Chest Twist Left-Right", "UpperChest Twist Left-Right", "Neck Turn Left-Right", "Head Turn Left-Right",
    };

    [MenuItem("RetroLOTR/Forge Runeboard Animations")]
    public static void Run()
    {
        var library = AssetDatabase.LoadAssetAtPath<Character3DAnimationLibrary>(LibraryPath);
        AnimationClip idle = library.Get("standing idle");
        AnimationClip walk = library.Get("Standing Walk Forward");
        AnimationClip melee = library.Get("Standing Melee Attack Downward");
        if (idle == null || walk == null || melee == null) throw new Exception("AnimationForge: source clips missing from the library.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/3D/Animations", "Runeboard");

        var sourceIdle = Read(idle);
        var sourceWalk = Read(walk);
        var sourceMelee = Read(melee);
        // The ready arm: the walk's own carry, held still.
        var readyArm = new Dictionary<string, float>();
        foreach (string m in RightArm) readyArm[m] = Mean(sourceWalk, m, walk.length);
        // The grip: forearm rolled out, arm lowered toward the hip, elbow bent — picked on the
        // figure bench, it holds a blade angled up and forward and a spear upright (the walk's own
        // carry, palm down, laid weapons across the chest).
        readyArm["Right Forearm Twist In-Out"] += 0.6f;
        readyArm["Right Arm Down-Up"] -= 0.35f;
        readyArm["Right Forearm Stretch"] -= 0.2f;

        Save(Make("RB Ready Idle", idle.length, true, (name, t) =>
        {
            if (readyArm.TryGetValue(name, out float hold)) return hold;
            return Straight(sourceIdle, name, t, idle.length);
        }, sourceIdle, idle.length, straighten: true), library);

        Save(Make("RB Walk", walk.length, true, (name, t) =>
        {
            if (readyArm.TryGetValue(name, out float hold)) return hold + (name == "Right Arm Front-Back" ? 0.06f * Mathf.Sin(t / walk.length * Mathf.PI * 2f) : 0f);
            return Straight(sourceWalk, name, t, walk.length);
        }, sourceWalk, walk.length, straighten: true), library);

        // The blow: 1.3 s over the melee clip's 2.27, eased so the cut itself is quick. The Mixamo
        // arc swings an empty fist; the wrist and forearm roll here keeps a held blade pointing up
        // and back over the shoulder in the wind-up and leading edge-first through the cut.
        foreach ((string name, float wrist, float roll) in Variants)
            Save(Slash(name, wrist, roll, readyArm, sourceIdle, idle.length, sourceMelee, melee.length), library);

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log("[AnimationForge] done");
    }

    // The blow, with the wrist (Right Hand Down-Up) and forearm roll (Right Forearm Twist In-Out)
    // offsets held through the swing.
    public static (string name, float wrist, float roll)[] Variants =
    {
        // Picked on the figure bench: rolling the forearm out carries the blade back over the
        // shoulder in the wind-up and down in front in the cut.
        ("RB Slash", 0f, -0.7f),
    };

    private static AnimationClip Slash(string clipName, float wrist, float roll, Dictionary<string, float> readyArm,
        Source sourceIdle, float idleLength, Source sourceMelee, float meleeLength)
    {
        const float slashLength = 1.3f;
        return Make(clipName, slashLength, false, (name, t) =>
        {
            float u = t / slashLength;
            // Wind-up takes the first 40%, the cut the next 25%, recovery the rest.
            float warp = u < 0.4f ? Mathf.Lerp(0f, 0.3f, Ease(u / 0.4f)) : u < 0.65f ? Mathf.Lerp(0.3f, 0.6f, Ease((u - 0.4f) / 0.25f)) : Mathf.Lerp(0.6f, 1f, Ease((u - 0.65f) / 0.35f));
            float ts = warp * meleeLength;
            float ti = u * idleLength;
            // In and out of the ready stance at both ends.
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.12f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.15f));
            float rest = readyArm.TryGetValue(name, out float hold) ? hold : Straight(sourceIdle, name, ti, idleLength);
            if (Array.IndexOf(RightArm, name) >= 0)
            {
                float swing = Value(sourceMelee, name, ts);
                if (name == "Right Hand Down-Up") swing += wrist;
                if (name == "Right Forearm Twist In-Out") swing += roll;
                return Mathf.Lerp(rest, swing, blend);
            }
            if (name.StartsWith("Left Arm") || name.StartsWith("Left Forearm") || name.StartsWith("Left Shoulder"))
                return Mathf.Lerp(rest, Mathf.Lerp(rest, Value(sourceMelee, name, ts), 0.5f), blend);
            if (name.StartsWith("Spine") || name.StartsWith("Chest") || name.StartsWith("UpperChest"))
                return Mathf.Lerp(rest, Value(sourceMelee, name, ts) * 0.6f, blend);
            return rest; // legs, feet, head, root: the planted idle stance
        }, sourceIdle, idleLength, straighten: true);
    }

    // ── Clip data ───────────────────────────────────────────────────────────────────────────

    private class Source
    {
        public readonly Dictionary<string, (EditorCurveBinding binding, AnimationCurve curve)> Curves = new();
        public readonly Dictionary<string, float> MeanTurn = new();
        public float MeanYaw;
    }

    private static Source Read(AnimationClip clip)
    {
        var source = new Source();
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            source.Curves[b.propertyName] = (b, AnimationUtility.GetEditorCurve(clip, b));
        foreach (string m in Turns) source.MeanTurn[m] = Mean(source, m, clip.length);
        // The body's average heading over the clip.
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < 32; i++)
        {
            Quaternion q = RootQ(source, clip.length * i / 32f);
            Vector3 f = q * Vector3.forward; f.y = 0f;
            sum += f.normalized;
        }
        source.MeanYaw = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
        return source;
    }

    private static float Value(Source s, string name, float t) => s.Curves.TryGetValue(name, out var c) ? c.curve.Evaluate(t) : 0f;

    private static float Mean(Source s, string name, float length)
    {
        float sum = 0f;
        for (int i = 0; i < 32; i++) sum += Value(s, name, length * i / 32f);
        return sum / 32f;
    }

    private static Quaternion RootQ(Source s, float t) =>
        new Quaternion(Value(s, "RootQ.x", t), Value(s, "RootQ.y", t), Value(s, "RootQ.z", t), Value(s, "RootQ.w", t)).normalized;

    // A source value with the body turned back to face ahead: the turn muscles lose their average
    // (the look back over the offset shoulder), the root loses its average heading.
    private static float Straight(Source s, string name, float t, float length)
    {
        if (s.MeanTurn.TryGetValue(name, out float mean)) return Value(s, name, t) - mean;
        return Value(s, name, t);
    }

    // The straight-line travel of a root curve over the clip (start → end), plus its mean offset.
    private static float Drift(Source s, string name, float t, float length)
    {
        float start = Value(s, name, 0f), end = Value(s, name, length);
        float line = start + (end - start) * (t / Mathf.Max(length, 1e-4f));
        float sum = 0f;
        for (int i = 0; i < 32; i++)
        {
            float ti = length * i / 32f;
            sum += Value(s, name, ti) - (start + (end - start) * (ti / Mathf.Max(length, 1e-4f)));
        }
        return line + sum / 32f;
    }

    private static float Ease(float x) => x * x * (3f - 2f * x);

    // Samples `value` for every muscle the base clip animates (plus the root, straightened) at 30 fps.
    private static AnimationClip Make(string name, float length, bool loop, Func<string, float, float> value, Source rootSource, float rootLength, bool straighten)
    {
        var clip = new AnimationClip { name = name, frameRate = Rate };
        int frames = Mathf.Max(2, Mathf.RoundToInt(length * Rate));
        var curves = new Dictionary<string, AnimationCurve>();
        foreach (var pair in rootSource.Curves)
            curves[pair.Key] = new AnimationCurve();
        Quaternion unturn = Quaternion.Euler(0f, straighten ? -rootSource.MeanYaw : 0f, 0f);
        for (int f = 0; f <= frames; f++)
        {
            float t = length * f / frames;
            float tr = rootLength * f / frames;
            Quaternion rq = unturn * RootQ(rootSource, tr);
            foreach (var pair in curves)
            {
                string p = pair.Key;
                float v = p switch
                {
                    "RootQ.x" => rq.x,
                    "RootQ.y" => rq.y,
                    "RootQ.z" => rq.z,
                    "RootQ.w" => rq.w,
                    "RootT.y" => Value(rootSource, p, tr),
                    // In place: the body keeps its sway but not the clip's travel, which would walk it
                    // off its base (the game moves the unit itself).
                    "RootT.x" or "RootT.z" => Value(rootSource, p, tr) - Drift(rootSource, p, tr, rootLength),
                    _ => value(p, t),
                };
                pair.Value.AddKey(new Keyframe(t, v));
            }
        }
        foreach (var pair in curves)
            AnimationUtility.SetEditorCurve(clip, rootSource.Curves[pair.Key].binding, pair.Value);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true; // in place (see Drift)
        settings.keepOriginalPositionY = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void Save(AnimationClip clip, Character3DAnimationLibrary library)
    {
        string path = $"{Folder}/{clip.name}.anim";
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            clip = existing;
        }
        else AssetDatabase.CreateAsset(clip, path);
        Character3DAnimationLibrary.Entry entry = library.clips.Find(e => e.name == clip.name);
        if (entry == null) library.clips.Add(entry = new Character3DAnimationLibrary.Entry { name = clip.name });
        entry.clip = clip;
    }
}
