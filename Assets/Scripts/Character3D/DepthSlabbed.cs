using System.Collections.Generic;
using UnityEngine;

// Anything drawn in 3D on the flat board — live units (Character3DModel) and settlement
// buildings (CityBuilding) — with Character3D.shader's depth slabs: the board is a 2D plane, so
// real view depth means nothing between these objects and the sprites. Each object's depth is
// squeezed into its own thin slab in a band near the far plane, slabs ordered by board Y (lower on
// screen = nearer), so objects self-occlude correctly, overlap each other in the right order — a
// unit in a town stands in front of the houses behind it and behind the ones in front — and never
// hide a sprite (banners, icons, fog) drawn after them, since sprites test at their real, much
// nearer depth. Subclasses read SlabBase / SlabWidth in LateUpdate (execution order 200, after
// everything has been placed for the frame).
public abstract class DepthSlabbed : MonoBehaviour
{
    // The band, in the 0 (far) .. 1 (near) depth range.
    public const float BandStart = 0.05f;
    public const float BandWidth = 0.04f;

    private static readonly List<DepthSlabbed> live = new();
    private static int sortedFrame = -1;
    private int slot;

    // Where this object stands on the board: higher = farther back.
    protected virtual float SortY => transform.position.y;

    protected virtual void OnEnable() => live.Add(this);
    protected virtual void OnDisable() => live.Remove(this);

    protected static float SlabWidth => BandWidth / Mathf.Max(live.Count, 1);

    protected float SlabBase
    {
        get
        {
            EnsureSorted();
            return BandStart + slot * SlabWidth;
        }
    }

    private static void EnsureSorted()
    {
        if (sortedFrame == Time.frameCount) return;
        sortedFrame = Time.frameCount;
        // Highest on the board = farthest = slot 0.
        live.Sort((a, b) => b.SortY.CompareTo(a.SortY));
        for (int i = 0; i < live.Count; i++) live[i].slot = i;
    }
}
