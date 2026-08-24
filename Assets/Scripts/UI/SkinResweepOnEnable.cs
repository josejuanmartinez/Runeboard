using UnityEngine;

// For prefabs authored with a skin material already baked in (e.g. a leader portrait shipped with
// the Bakshi material assigned): MaterialManager's scene-wide sweep in ApplySkin() only runs once,
// at skin-change time, so anything instantiated afterward is stuck on its spawn-time material.
// This fixes up just this GameObject's subtree against whatever skin is currently active.
public class SkinResweepOnEnable : MonoBehaviour
{
    private void OnEnable()
    {
        MaterialManager.Instance?.ApplyToSubtree(gameObject);
    }
}
