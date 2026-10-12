using System.Collections.Generic;
using UnityEngine;

// Every Humanoid clip live 3D units can play (prototype, see Character3DAnimator / UnitAnimations),
// by the name of its source file under Assets/Art/3D/Animations/Humanoid. Lives in Resources so the
// clips ship with the build; Humanoid retargeting lets every rigged character play all of them.
[CreateAssetMenu(menuName = "RetroLOTR/Character 3D Animation Library")]
public class Character3DAnimationLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string name;
        public AnimationClip clip;
    }

    public List<Entry> clips = new();

    private Dictionary<string, AnimationClip> byName;

    public AnimationClip Get(string name)
    {
        if (byName == null)
        {
            byName = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in clips)
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.name)) byName[entry.name] = entry.clip;
        }
        return name != null && byName.TryGetValue(name, out AnimationClip clip) ? clip : null;
    }

    private static Character3DAnimationLibrary instance;
    private static bool looked;

    public static Character3DAnimationLibrary Instance
    {
        get
        {
            if (!looked)
            {
                looked = true;
                instance = Resources.Load<Character3DAnimationLibrary>("Character3DAnimations");
            }
            return instance;
        }
    }
}
