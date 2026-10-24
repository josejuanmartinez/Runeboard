using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// One live, animated 3D character (prototype — see Character3DView). Sits on its own root
// GameObject whose position/rotation/scale the owner drives; this component handles the rest:
// loading the FBX by its Addressable path (the same Assets/Art/3D/fbx/[Name]/[Name].fbx folders the
// spritesheets were baked from), swapping its materials to RetroLOTR/Character3D (+ outline),
// giving it a Character3DAnimator for its clips, and the per-renderer tint, sorting
// and depth-slab values the shader needs.
[DefaultExecutionOrder(200)] // after Character3DView has placed every model this frame
public class Character3DModel : DepthSlabbed
{
    public const string AddressRoot = "Assets/Art/3D/fbx/";

    private class PrefabEntry
    {
        public GameObject Prefab;
        public bool Missing;
    }

    private static readonly Dictionary<string, PrefabEntry> prefabsByFolder = new();
    private static readonly Dictionary<Material, Material> convertedMaterials = new();
    private static int lightingPublishedFrame = -1;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int OriginId = Shader.PropertyToID("_C3D_Origin");
    private static readonly int UpId = Shader.PropertyToID("_C3D_Up");
    private static readonly int HeightId = Shader.PropertyToID("_C3D_Height");
    private static readonly int RimColorId = Shader.PropertyToID("_C3D_RimColor");
    private static readonly int DepthBaseId = Shader.PropertyToID("_DepthBase");
    private static readonly int DepthSlabWidthId = Shader.PropertyToID("_DepthSlabWidth");
    private static readonly int DepthSlabScaleId = Shader.PropertyToID("_DepthSlabScale");
    private static readonly int FocusId = Shader.PropertyToID("_C3D_Focus");

    public Color Tint = Color.white;
    // Alignment outline colour (rgb, a = strength), from the sprite controller's outline presentation.
    public Color RimColor = Color.clear;
    // Selection glow on the miniature base (0 = idle .. 1 = selected), from the sprite controller.
    public float Focus;
    // Fades the figure (not its base) — the strategic-zoom tokens take over (UnitToken).
    public float FigureAlpha = 1f;
    public int SortingLayerId;
    public int SortingOrder;

    // Bind-pose height in root-local units (root scale 1), feet at the root origin.
    public float ModelHeight { get; private set; } = 1f;
    public string Folder { get; private set; }

    private Animator animator;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    public Character3DAnimator Animation { get; private set; }

    private MeshRenderer miniatureBase;
    private Color32 baseColor;


    private static readonly bool ShadowDebug =
        System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-char3d-shadowdebug") >= 0;

    // Every character model, by folder (Assets/Art/3D/fbx/[Folder]/[any].fbx, weapons excluded),
    // read from the Addressables catalog once it's up.
    private static Dictionary<string, string> addressByFolder;

    private static bool CatalogReady()
    {
        if (addressByFolder != null) return true;
        var found = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var locator in Addressables.ResourceLocators)
            foreach (object key in locator.Keys)
            {
                if (key is not string address
                    || !address.StartsWith(AddressRoot)
                    || !address.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = address.Substring(AddressRoot.Length).Split('/');
                if (parts.Length == 2 && parts[0] != "Weapons") found[parts[0]] = address;
            }
        if (found.Count == 0) return false; // Addressables not initialized yet — try again later
        addressByFolder = found;
        return true;
    }

    // Races with no model of their own borrow the nearest one.
    private static readonly Dictionary<RacesEnum, string> RaceStandIns = new()
    {
        { RacesEnum.Goblin, "Orc" }, { RacesEnum.Undead, "Nazgul" }, { RacesEnum.Ent, "Troll" },
        { RacesEnum.Beast, "Troll" }, { RacesEnum.Machine, "Troll" }, { RacesEnum.Eagle, "Common" },
    };

    // The model a character is drawn with — the same tiers as the baked sprites
    // (CharacterSpritesheets.TryResolveRaceOrName): its own name, the leader it's a variant of,
    // its race, then Common. Every character resolves to something, so the board is all 3D.
    public static bool TryResolve(Character character, out string folder)
    {
        folder = null;
        if (character == null || !CatalogReady()) return false;
        RaceStandIns.TryGetValue(character.race, out string standIn);
        foreach (string candidate in new[] { character.characterName, character.SpriteVariantBaseName, character.race.ToString(), standIn, "Common" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string stripped = candidate.Replace(" ", "");
            if (addressByFolder.ContainsKey(stripped)) { folder = stripped; return true; }
        }
        return false;
    }

    // Null until loaded (or forever, if loading failed — IsMissing then says so).
    public static GameObject RequestPrefab(string folder)
    {
        if (prefabsByFolder.TryGetValue(folder, out PrefabEntry entry)) return entry.Prefab;

        entry = new PrefabEntry();
        prefabsByFolder[folder] = entry;
        if (!CatalogReady() || !addressByFolder.TryGetValue(folder, out string address))
        {
            entry.Missing = true;
            return null;
        }
        // Locations first: LoadAssetAsync on an unknown key throws an InvalidKeyException error.
        Addressables.LoadResourceLocationsAsync(address, typeof(GameObject)).Completed += locations =>
        {
            IResourceLocation location = locations.Status == AsyncOperationStatus.Succeeded && locations.Result.Count > 0
                ? locations.Result[0] : null;
            Addressables.Release(locations);
            if (location == null)
            {
                entry.Missing = true;
                Debug.LogWarning($"[Character3D] Could not locate Addressable model '{address}'.");
                return;
            }
            Addressables.LoadAssetAsync<GameObject>(location).Completed += load =>
            {
                if (load.Status == AsyncOperationStatus.Succeeded) entry.Prefab = load.Result;
                else entry.Missing = true;
            };
        };
        return null;
    }

    public static bool IsMissing(string folder) =>
        prefabsByFolder.TryGetValue(folder, out PrefabEntry entry) && entry.Missing;

    public static Character3DModel Create(GameObject prefab, string folder, Transform parent)
    {
        var rootGo = new GameObject($"3D {folder}");
        rootGo.transform.SetParent(parent, false);
        rootGo.transform.rotation = Quaternion.identity;
        GameObject model = Instantiate(prefab, rootGo.transform, false);
        var created = rootGo.AddComponent<Character3DModel>();
        created.Init(model, folder);
        return created;
    }

    private void Init(GameObject model, string folder)
    {
        Character3DSettings settings = Character3DSettings.Instance;
        Folder = folder;
        block = new MaterialPropertyBlock();

        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        // Clips are played straight from the library through a Playables graph — no controller.
        Animation = model.AddComponent<Character3DAnimator>();

        renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            Material[] source = r.sharedMaterials;
            // One extra slot for the alignment outline: Unity draws the last submesh again with
            // each surplus material (single-submesh models — nearly all of them — get it whole).
            var materials = new Material[source.Length + (settings.outlineShader != null && source.Length > 0 ? 1 : 0)];
            for (int i = 0; i < source.Length; i++) materials[i] = Convert(source[i], settings);
            if (materials.Length > source.Length) materials[^1] = OutlineFor(source[^1], settings);
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
        }

        // Measured with the root still unrotated at scale 1: feet go to the root origin, so the
        // owner places the root where the feet should stand. Humanoids measure from the skeleton
        // (soles to crown): a skinned renderer's bounds are the imported, padded box around its
        // root bone, which put the "feet" around the hips — the cast shadow then only sheared the
        // upper body. Other rigs fall back to those bounds.
        float scale = Mathf.Max(transform.lossyScale.y, 1e-6f);
        if (TryMeasureSkeleton(out float soleY, out float crownY))
        {
            ModelHeight = Mathf.Max((crownY - soleY) / scale, 1e-4f);
            model.transform.localPosition = new Vector3(0f, -(soleY - transform.position.y) / scale, 0f);
        }
        else if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            ModelHeight = Mathf.Max(bounds.size.y / scale, 1e-4f);
            model.transform.localPosition = new Vector3(0f, -(bounds.min.y - transform.position.y) / scale, 0f);
        }
    }

    // World-space sole and crown heights of a Humanoid in its current (bind) pose.
    private bool TryMeasureSkeleton(out float soleY, out float crownY)
    {
        soleY = crownY = 0f;
        if (!animator.isHuman) return false;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        float lowest = float.MaxValue;
        foreach (HumanBodyBones bone in FootBones)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t != null) lowest = Mathf.Min(lowest, t.position.y);
        }
        if (head == null || hips == null || lowest == float.MaxValue) return false;

        // Foot/toe joints sit a little above the sole, and the head joint at the skull base:
        // pad both by a fraction of the hips-to-head span (roughly a third of the body).
        float span = head.position.y - hips.position.y;
        soleY = lowest - span * 0.12f;
        crownY = head.position.y + span * 0.45f;
        return crownY > soleY;
    }

    private static readonly HumanBodyBones[] FootBones =
        { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };

    // Lowest foot/toe joint this frame (planted foot while walking), padded down to the sole.
    private Vector3 CurrentFeet()
    {
        if (!animator.isHuman) return transform.position;
        Vector3 lowest = transform.position;
        float lowestY = float.MaxValue;
        foreach (HumanBodyBones bone in FootBones)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t != null && t.position.y < lowestY) { lowestY = t.position.y; lowest = t.position; }
        }
        if (lowestY == float.MaxValue) return transform.position;
        // Never above the root (the sole in bind pose), so idles stay anchored to the ring.
        lowest.y = Mathf.Min(lowestY, transform.position.y);
        return lowest;
    }

    private static readonly Dictionary<Material, Material> outlineMaterials = new();

    private static Material OutlineFor(Material source, Character3DSettings settings)
    {
        if (source == null) return null;
        if (outlineMaterials.TryGetValue(source, out Material outline)) return outline;
        outline = new Material(settings.outlineShader) { name = $"{source.name} (Character3D outline)", mainTexture = Convert(source, settings).mainTexture };
        outlineMaterials[source] = outline;
        return outline;
    }

    private static Material Convert(Material source, Character3DSettings settings)
    {
        if (source == null) return null;
        if (convertedMaterials.TryGetValue(source, out Material converted)) return converted;

        Texture texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
        if (texture == null && source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
        converted = new Material(settings.shader) { name = $"{source.name} (Character3D)", mainTexture = texture };
        if (ShadowDebug)
        {
            // -char3d-shadowdebug: cast shadow as solid magenta with no depth/stencil test.
            converted.SetFloat("_ShadowZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            converted.SetFloat("_ShadowDebug", 1f);
        }
        convertedMaterials[source] = converted;
        return converted;
    }

    // World radius of the miniature's base; 0 while it is hidden.
    public float BaseWorldRadius =>
        miniatureBase != null && miniatureBase.gameObject.activeSelf ? miniatureBase.transform.lossyScale.x : 0f;

    // The miniature's base (Character3DBase), side band in the owner's nation colour; null hides it.
    public void SetBase(Color? nation, float radius)
    {
        if (nation == null)
        {
            if (miniatureBase != null) miniatureBase.gameObject.SetActive(false);
            return;
        }
        if (miniatureBase == null)
        {
            var go = new GameObject("MiniatureBase", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = Character3DBase.Mesh;
            miniatureBase = go.GetComponent<MeshRenderer>();
            miniatureBase.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            miniatureBase.receiveShadows = false;
            baseColor = new Color32(0, 0, 0, 1); // never a real key, so the material is set below
        }
        if (!miniatureBase.gameObject.activeSelf) miniatureBase.gameObject.SetActive(true);
        Color32 key = nation.Value;
        if (!key.Equals(baseColor))
        {
            baseColor = key;
            miniatureBase.sharedMaterial = Character3DBase.MaterialFor(nation.Value, Character3DSettings.Instance.shader);
        }
        miniatureBase.transform.localScale = Vector3.one * (radius * ModelHeight);
    }

    private void LateUpdate()
    {
        if (lightingPublishedFrame != Time.frameCount)
        {
            lightingPublishedFrame = Time.frameCount;
            Character3DSettings frameSettings = Character3DSettings.Instance;
            if (frameSettings != null) Character3DLighting.Publish(frameSettings);
        }

        float slabBase = SlabBase;
        float slabWidth = SlabWidth;
        float worldHeight = Mathf.Max(ModelHeight * transform.lossyScale.y, 1e-4f);
        float rimScale = Character3DSettings.Instance != null ? Character3DSettings.Instance.rimStrength : 1f;
        Vector3 feet = CurrentFeet();
        Color rim = RimColor;
        rim.a *= rimScale;
        Color figureTint = Tint;
        figureTint.a *= FigureAlpha;

        foreach (Renderer r in renderers)
        {
            if (r.sortingLayerID != SortingLayerId) r.sortingLayerID = SortingLayerId;
            if (r.sortingOrder != SortingOrder) r.sortingOrder = SortingOrder;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, figureTint);
            block.SetVector(OriginId, feet);
            block.SetVector(UpId, transform.up);
            block.SetFloat(HeightId, worldHeight);
            block.SetColor(RimColorId, rim);
            block.SetFloat(DepthBaseId, slabBase);
            // A margin under the slab width keeps neighbouring slabs from touching.
            block.SetFloat(DepthSlabWidthId, slabWidth * 0.9f);
            block.SetFloat(DepthSlabScaleId, 0.5f / worldHeight);
            r.SetPropertyBlock(block);
        }

        if (miniatureBase != null && miniatureBase.gameObject.activeSelf)
        {
            Renderer r = miniatureBase;
            if (r.sortingLayerID != SortingLayerId) r.sortingLayerID = SortingLayerId;
            if (r.sortingOrder != SortingOrder) r.sortingOrder = SortingOrder;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, Tint);
            block.SetVector(OriginId, transform.position);
            block.SetVector(UpId, transform.up);
            block.SetFloat(HeightId, worldHeight);
            block.SetColor(RimColorId, Color.clear);
            // One flat depth in the margin just behind this model's slab: the figure always stands
            // in front of its base, and the base's own triangle order (far → near, top last) sorts it.
            block.SetFloat(DepthBaseId, Mathf.Max(slabBase - slabWidth * 0.05f, 0f));
            block.SetFloat(DepthSlabWidthId, 0f);
            block.SetFloat(DepthSlabScaleId, 0f);
            block.SetFloat(FocusId, Focus);
            r.SetPropertyBlock(block);
        }
    }
}
