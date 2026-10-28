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

    // Every character model, by folder (Assets/Art/3D/fbx/[Folder]/[any].fbx; the Weapons folder
    // is the units' held weapons, see Character3DWeapons),
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
        return character != null && TryResolve(character.race, out folder, character.characterName, character.SpriteVariantBaseName);
    }

    // The same tiers from plain names, for a figure that is not a board character (TitleDiorama).
    public static bool TryResolve(RacesEnum race, out string folder, params string[] names)
    {
        folder = null;
        if (!CatalogReady()) return false;
        RaceStandIns.TryGetValue(race, out string standIn);
        var candidates = new List<string>(names) { race.ToString(), standIn, "Common" };
        foreach (string candidate in candidates)
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
        // Some FBXs were exported with their scene's camera (and lights) inside: a live camera
        // there renders over the board camera from the model's own fixed viewpoint.
        foreach (Camera stray in model.GetComponentsInChildren<Camera>(true)) Destroy(stray.gameObject);
        foreach (Light stray in model.GetComponentsInChildren<Light>(true)) Destroy(stray.gameObject);
        var created = rootGo.AddComponent<Character3DModel>();
        created.Init(model, folder);
        return created;
    }

    // ── Regiment troopers (see Character3DRegiment) ─────────────────────────────────────────

    // A trooper is a full model (animator, weapons) standing under its leader's root, but drawn as
    // part of the leader: no depth slab of its own, and the leader's LateUpdate gives its renderers
    // the leader's tint, fade, outline, slab, lighting and sorting — so the regiment occludes
    // itself in real 3D, stands in front of its one base, and fades and dies as one miniature.
    public bool IsTrooper { get; private set; }
    public readonly List<Character3DModel> Troops = new();
    // A trooper's own extra fade (casualties dissolving), on top of the leader's.
    public float Fade = 1f;
    // Regiment base footprint, in ModelHeights: half width, half depth, centre offset along the
    // facing (z). Zero = the plain round base.
    public Vector3 Footprint;

    private static bool spawningTrooper;

    private void Awake() => IsTrooper = spawningTrooper;

    protected override void OnEnable()
    {
        if (!IsTrooper) base.OnEnable();
    }

    public static Character3DModel CreateTrooper(GameObject prefab, string folder, Character3DModel leader)
    {
        spawningTrooper = true;
        try
        {
            Character3DModel trooper = Create(prefab, folder, leader.transform);
            trooper.name = $"Trooper {folder}";
            leader.Troops.Add(trooper);
            return trooper;
        }
        finally
        {
            spawningTrooper = false;
        }
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

        renderers = bodyRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) Dress(r, settings);

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
        rigRestLocal = model.transform.localPosition;

        // Still in the bind pose: where each hand closes, for the weapons (Arm).
        // The fist closes round the hilt a little past the palm's middle.
        rightGrip = MeasureGrip(model.transform, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, "right", 0.42f, -0.22f);
        rightFist = MeasureFist(RightFingers, HumanBodyBones.RightHand);
        leftFist = MeasureFist(LeftFingers, HumanBodyBones.LeftHand);
        leftGrip = MeasureGrip(model.transform, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, "left", -0.35f, 0.12f);
    }

    // The Character3D materials (+ alignment outline) on one of this unit's renderers.
    private static void Dress(Renderer r, Character3DSettings settings)
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

    // ── Weapons (see Character3DWeapons) ────────────────────────────────────────────────────

    // A hand's frame in the bind pose, kept in the hand bone's own space so it holds through
    // every clip: the fist's centre and three directions — the way the figure faces (the line of
    // the knuckles in a palms-down T/A-pose, so a handle crosses the palm), down the forearm
    // toward the fingers, and out of the back of the hand.
    private struct Grip
    {
        public Transform Hand;
        public Vector3 Point, Forward, Forearm, Back;
        public float Unit; // hand-local units per root-local unit
    }

    private Grip rightGrip, leftGrip;
    private Renderer[] bodyRenderers;
    private Character3DWeapons.Loadout loadout = Character3DWeapons.Unarmed;
    private readonly List<GameObject> weapons = new();
    private bool armed = true;

    // reach / back: where the grip sits from the wrist, in forearm lengths, down the forearm and
    // out of the back of the hand (negative = into the palm).
    // ── Falling ─────────────────────────────────────────────────────────────────────────────
    // Root motion is off, so a death clip that drops the hips to the floor would leave the body
    // lying at standing height, in mid air. A fallen figure is lowered until its lowest part
    // rests on the ground (never raised above where it stands).

    /// <summary>The figure has fallen (a death played): keep it on the ground.</summary>
    public bool Fallen;
    private Vector3 rigRestLocal;
    private float fallDrop;

    private static readonly HumanBodyBones[] BodyBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
    };

    private void GroundFallen()
    {
        if (animator == null || animator.transform == transform) return;
        float target = 0f;
        if (Fallen && animator.isHuman)
        {
            float lowest = float.MaxValue;
            foreach (HumanBodyBones bone in BodyBones)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null) lowest = Mathf.Min(lowest, t.position.y);
            }
            if (lowest < float.MaxValue)
            {
                // Joints sit inside the body: leave a limb's thickness under the lowest one.
                float pad = 0.05f * ModelHeight * transform.lossyScale.y;
                float gap = lowest - pad - transform.position.y;
                target = Mathf.Min(0f, fallDrop - gap);
            }
        }
        fallDrop = Mathf.Lerp(fallDrop, target, 0.35f);
        float scale = Mathf.Max(transform.lossyScale.y, 1e-6f);
        animator.transform.localPosition = rigRestLocal + new Vector3(0f, fallDrop / scale, 0f);
    }

    // ── Fists ───────────────────────────────────────────────────────────────────────────────
    // The clips were made for empty hands, so a hand holding a weapon (or a shield's strap) is
    // closed into a fist after the animator: each finger joint is set to its bind rotation plus a
    // fixed curl toward the palm, measured in the bind pose. Only the finger bones are touched,
    // and the result does not depend on the previous frame.

    private struct FingerJoint
    {
        public Transform Bone;
        public Quaternion Bind;
        public Vector3 Axis;   // in the bone's own space: turning about it curls toward the palm
        public float Angle;
    }

    private FingerJoint[] rightFist, leftFist;

    private static readonly HumanBodyBones[][] RightFingers =
    {
        new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
        new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
        new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
        new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
    };
    private static readonly HumanBodyBones[][] LeftFingers =
    {
        new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
        new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
        new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
        new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
    };
    private static readonly float[] CurlByJoint = { 70f, 85f, 60f };

    // In the bind pose (palms down): each joint's curl axis, from the finger's direction and the
    // palm's.
    private FingerJoint[] MeasureFist(HumanBodyBones[][] fingers, HumanBodyBones handBone)
    {
        if (animator == null || !animator.isHuman) return null;
        Transform hand = animator.GetBoneTransform(handBone);
        if (hand == null) return null;
        Vector3 palm = -transform.up; // the back of the hand faces up in the bind pose
        var joints = new List<FingerJoint>();
        foreach (HumanBodyBones[] chain in fingers)
            for (int j = 0; j < chain.Length; j++)
            {
                Transform bone = animator.GetBoneTransform(chain[j]);
                if (bone == null) continue;
                Transform next = j + 1 < chain.Length ? animator.GetBoneTransform(chain[j + 1]) : null;
                Vector3 along = next != null ? next.position - bone.position
                    : j > 0 && animator.GetBoneTransform(chain[j - 1]) is Transform prev ? bone.position - prev.position
                    : bone.position - hand.position;
                Vector3 axis = Vector3.Cross(along.normalized, palm);
                if (axis.sqrMagnitude < 1e-6f) continue;
                joints.Add(new FingerJoint { Bone = bone, Bind = bone.localRotation, Axis = bone.InverseTransformDirection(axis.normalized), Angle = CurlByJoint[j] });
            }
        return joints.Count > 0 ? joints.ToArray() : null;
    }

    private void CloseHands()
    {
        if (!armed) return;
        if (loadout.Right != Character3DWeapons.Weapon.None && rightFist != null) Clench(rightFist);
        if (loadout.Shield && leftFist != null) Clench(leftFist);
    }

    private static void Clench(FingerJoint[] joints)
    {
        foreach (FingerJoint j in joints)
            if (j.Bone != null) j.Bone.localRotation = j.Bind * Quaternion.AngleAxis(j.Angle, j.Axis);
    }

    private Grip MeasureGrip(Transform model, HumanBodyBones handBone, HumanBodyBones forearmBone, string side, float reach, float back)
    {
        Transform hand = animator.isHuman ? animator.GetBoneTransform(handBone) : null;
        if (hand == null) hand = FindBone(model, side + "hand", "hand" + side[0], side[0] + "hand");
        if (hand == null) return default;
        Transform elbow = animator.isHuman ? animator.GetBoneTransform(forearmBone) : null;
        if (elbow == null) elbow = hand.parent;
        if (elbow == null) return default;

        Vector3 along = hand.position - elbow.position;
        float forearm = along.magnitude;
        if (forearm < 1e-5f) return default;
        along /= forearm;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, along);
        if (forward.sqrMagnitude < 1e-4f) return default;
        forward.Normalize();
        // Palms down in the bind pose, so the back of the hand faces up.
        Vector3 up = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(transform.up, along), forward);
        if (up.sqrMagnitude < 1e-4f) return default;
        up.Normalize();

        return new Grip
        {
            Hand = hand,
            Point = hand.InverseTransformPoint(hand.position + forearm * (along * reach + up * back)),
            Forward = hand.InverseTransformDirection(forward),
            Forearm = hand.InverseTransformDirection(along),
            Back = hand.InverseTransformDirection(up),
            Unit = transform.lossyScale.y / Mathf.Max(hand.lossyScale.y, 1e-6f),
        };
    }

    // Rigs without a Humanoid avatar: the first bone whose name (letters only) ends in one of these.
    private static Transform FindBone(Transform root, params string[] endings)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string letters = new string(System.Array.FindAll(t.name.ToLowerInvariant().ToCharArray(), char.IsLetter));
            foreach (string ending in endings)
                if (letters.EndsWith(ending)) return t;
        }
        return null;
    }

    // What this unit holds (Character3DWeapons.For); cheap to call every frame.
    public void Arm(Character3DWeapons.Loadout wanted)
    {
        if (wanted.Equals(loadout)) return;
        loadout = wanted;
        foreach (GameObject weapon in weapons) Destroy(weapon);
        weapons.Clear();
        renderers = bodyRenderers;
        armed = false;
        TryArm();
    }

    // Attaches the loadout once its meshes have loaded — all at once, so a unit never shows half armed.
    private void TryArm()
    {
        Character3DWeapons.Parts right = loadout.Right != Character3DWeapons.Weapon.None && rightGrip.Hand != null
            ? Character3DWeapons.Request(loadout.Right) : null;
        Character3DWeapons.Parts shield = loadout.Shield && leftGrip.Hand != null
            ? Character3DWeapons.Request(Character3DWeapons.Weapon.Shield) : null;
        if ((right != null && !right.Done) || (shield != null && !shield.Done)) return;
        armed = true;

        Character3DSettings settings = Character3DSettings.Instance;
        var all = new List<Renderer>(bodyRenderers);
        if (right != null && right.Mesh != null)
        {
            // Square to the fist, raised toward the elbow by the tilt; the edge leads down the forearm.
            Character3DWeapons.Spec spec = Character3DWeapons.Specs[loadout.Right];
            float tilt = spec.Tilt * Mathf.Deg2Rad;
            Vector3 blade = rightGrip.Forward * Mathf.Cos(tilt) - rightGrip.Forearm * Mathf.Sin(tilt);
            all.Add(Hold(right, spec, rightGrip, Quaternion.LookRotation(blade, rightGrip.Forearm), loadout.Scale, settings));
            bladeLocal = blade.normalized;
            longWeapon = loadout.Right is Character3DWeapons.Weapon.Spear or Character3DWeapons.Weapon.Staff;
        }
        if (shield != null && shield.Mesh != null)
        {
            // Strapped on the forearm: its face out of the back of the arm, its top toward the elbow.
            Character3DWeapons.Spec spec = Character3DWeapons.Specs[Character3DWeapons.Weapon.Shield];
            all.Add(Hold(shield, spec, leftGrip, Quaternion.LookRotation(leftGrip.Back, -leftGrip.Forearm), 1f, settings));
        }
        renderers = all.ToArray();
    }

    // ── The weapon arm (procedural) ─────────────────────────────────────────────────────────
    // The clips were made for empty hands, so the weapon arm is posed by hand on top of them, after
    // the animator, every frame:
    //   • in the ready idle and the walk, the wrist turns the weapon to point where a held weapon
    //     points (a blade up and forward, a spear or staff upright) — never across the body;
    //   • a blow (Swing) is a designed path for the blade — ready, wound back over the shoulder,
    //     a fast cut forward and down, follow-through, back to ready — with the arm solved to
    //     carry it (two-bone IK) and the chest turning into it.

    private Vector3 bladeLocal;     // the held weapon's axis in the hand bone's space
    private bool longWeapon;
    private float swingStart = -1f, swingSpeed = 1f;
    private float readyWeight;

    public const float SwingSeconds = 1.05f;
    public bool Swinging => swingStart >= 0f && (Time.time - swingStart) * swingSpeed < SwingSeconds;

    /// <summary>Strikes one blow with the weapon in hand (or the fist).</summary>
    public void Swing(float speed = 1f)
    {
        swingStart = Time.time;
        swingSpeed = Mathf.Max(speed, 0.05f);
    }

    // Swing keys, in the figure's own frame (x right, y up, z forward; units of its height):
    // time, the hand, where the blade points, and the chest's turn (+ = right shoulder back).
    private static readonly (float t, Vector3 hand, Vector3 blade, float twist)[] SwingKeys =
    {
        (0.00f, new(0.17f, 0.50f, 0.18f), new(0.05f, 0.75f, 0.65f), 0f),
        (0.36f, new(0.20f, 0.96f, -0.06f), new(0.05f, -0.35f, -0.95f), 28f),
        (0.50f, new(0.03f, 0.64f, 0.40f), new(-0.10f, -0.20f, 1.00f), -18f),
        (0.64f, new(-0.12f, 0.40f, 0.28f), new(-0.60f, -0.75f, 0.30f), -26f),
        (1.00f, new(0.17f, 0.50f, 0.18f), new(0.05f, 0.75f, 0.65f), 0f),
    };

    private void PoseWeaponArm()
    {
        if (animator == null || !animator.isHuman || rightGrip.Hand == null) return;
        Transform hand = rightGrip.Hand;
        Transform upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        if (upper == null || lower == null) return;
        Vector3 axis = bladeLocal.sqrMagnitude > 0.5f ? bladeLocal : rightGrip.Forward;
        Transform root = transform;
        float h = ModelHeight * root.lossyScale.y;
        Vector3 Dir(Vector3 v) => (root.right * v.x + root.up * v.y + root.forward * v.z).normalized;

        if (Swinging)
        {
            float u = (Time.time - swingStart) * swingSpeed / SwingSeconds;
            int k = 0;
            while (k < SwingKeys.Length - 2 && u > SwingKeys[k + 1].t) k++;
            var a = SwingKeys[k];
            var b = SwingKeys[k + 1];
            float e = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.t, b.t, u));
            Vector3 handTarget = Vector3.Lerp(a.hand, b.hand, e);
            Vector3 bladeTarget = Vector3.Slerp(a.blade.normalized, b.blade.normalized, e);
            float twist = Mathf.Lerp(a.twist, b.twist, e);
            // Into the blow from whatever the clip shows, and back out of it.
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.1f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.12f));

            Quaternion chestFrom = chest != null ? chest.rotation : Quaternion.identity;
            Quaternion upperFrom = upper.rotation, lowerFrom = lower.rotation, handFrom = hand.rotation;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(twist, root.up) * chest.rotation;
            Vector3 target = root.position + (root.right * handTarget.x + root.up * handTarget.y + root.forward * handTarget.z) * h;
            Vector3 hint = upper.position + (root.right * 0.35f - root.up * 0.25f - root.forward * 0.15f) * h;
            TwoBoneIK(upper, lower, hand, target, hint);
            hand.rotation = Quaternion.FromToRotation(hand.TransformVector(axis).normalized, Dir(bladeTarget)) * hand.rotation;
            if (w < 1f)
            {
                if (chest != null) chest.rotation = Quaternion.Slerp(chestFrom, chest.rotation, w);
                upper.rotation = Quaternion.Slerp(upperFrom, upper.rotation, w);
                lower.rotation = Quaternion.Slerp(lowerFrom, lower.rotation, w);
                hand.rotation = Quaternion.Slerp(handFrom, hand.rotation, w);
            }
            return;
        }

        // Ready: only while standing ready or walking, and only with something in the hand.
        string clip = Animation != null ? Animation.Current : null;
        bool ready = bladeLocal.sqrMagnitude > 0.5f && (clip == "RB Ready Idle" || clip == "RB Walk");
        readyWeight = Mathf.MoveTowards(readyWeight, ready ? 1f : 0f, Time.deltaTime * 4f);
        if (readyWeight <= 0f) return;
        Vector3 want = Dir(longWeapon ? new Vector3(0.05f, 1f, 0.22f) : new Vector3(0.12f, 0.75f, 0.65f));
        // The hand bones carry the FBX's mirrored scale: TransformVector, not rotation * axis.
        Quaternion turn = Quaternion.FromToRotation(hand.TransformVector(axis).normalized, want);
        hand.rotation = Quaternion.Slerp(Quaternion.identity, turn, readyWeight) * hand.rotation;
    }

    // Bends a shoulder–elbow–wrist chain so the wrist reaches `target` (or as near as the arm's
    // length allows), the elbow toward `hint`.
    private static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toTarget = target - a.position;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-4f, (la + lb) * 0.999f);
        Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : a.forward;
        Vector3 toHint = hint - a.position;
        Vector3 bendAxis = Vector3.Cross(dir, toHint);
        if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = Vector3.Cross(dir, Vector3.up);
        bendAxis.Normalize();
        float angle = Mathf.Acos(Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 elbowDir = Quaternion.AngleAxis(angle, bendAxis) * dir;
        if (Vector3.Dot(elbowDir, toHint) < Vector3.Dot(Quaternion.AngleAxis(-angle, bendAxis) * dir, toHint))
            elbowDir = Quaternion.AngleAxis(-angle, bendAxis) * dir;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, elbowDir) * a.rotation;
        Vector3 wrist = a.position + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, wrist - b.position) * b.rotation;
    }

    // One weapon mesh under the hand bone: the spec's grip point at the grip, its grip → tip axis
    // along the frame's forward and its edge toward the frame's up, sized to the figure.
    private Renderer Hold(Character3DWeapons.Parts parts, Character3DWeapons.Spec spec, Grip grip, Quaternion frame, float scale, Character3DSettings settings)
    {
        var go = new GameObject($"Weapon {parts.Mesh.name}", typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = gameObject.layer;
        go.GetComponent<MeshFilter>().sharedMesh = parts.Mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = parts.Material;
        Dress(r, settings);

        Bounds bounds = parts.Mesh.bounds;
        Vector3 gripPoint = bounds.min + Vector3.Scale(spec.Grip, bounds.size);
        Vector3 tipPoint = bounds.min + Vector3.Scale(spec.Tip, bounds.size);
        Quaternion meshFrame = Quaternion.LookRotation(tipPoint - gripPoint, spec.Edge);
        float size = spec.Length * scale * ModelHeight * grip.Unit / Mathf.Max(bounds.size.y, 1e-6f);

        Transform t = go.transform;
        t.SetParent(grip.Hand, false);
        t.localRotation = frame * Quaternion.Inverse(meshFrame);
        t.localScale = Vector3.one * size;
        t.localPosition = grip.Point - t.localRotation * (gripPoint * size);
        weapons.Add(go);
        return r;
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

    // World radius of the miniature's (round, single-figure) base; 0 while it is hidden.
    public float BaseWorldRadius =>
        miniatureBase != null && miniatureBase.gameObject.activeSelf ? miniatureBase.transform.lossyScale.y : 0f;

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
        // A regiment stretches it into an oval stand reaching back under its troopers (Footprint);
        // the thickness and the round radius the token and dust go by stay those of one figure.
        float r = radius * ModelHeight;
        Vector3 footprint = Footprint * ModelHeight;
        miniatureBase.transform.localScale = new Vector3(Mathf.Max(r, footprint.x), r, Mathf.Max(r, footprint.y));
        miniatureBase.transform.localPosition = new Vector3(0f, 0f, footprint.z);
    }

    private void LateUpdate()
    {
        if (IsTrooper)
        {
            if (!armed) TryArm(); // drawn by the leader
            return;
        }
        CloseHands();
        GroundFallen();
        PoseWeaponArm();
        if (lightingPublishedFrame != Time.frameCount)
        {
            lightingPublishedFrame = Time.frameCount;
            Character3DSettings frameSettings = Character3DSettings.Instance;
            if (frameSettings != null) Character3DLighting.Publish(frameSettings);
        }

        if (!armed) TryArm();

        float slabBase = SlabBase;
        float slabWidth = SlabWidth;
        float worldHeight = Mathf.Max(ModelHeight * transform.lossyScale.y, 1e-4f);
        float rimScale = Character3DSettings.Instance != null ? Character3DSettings.Instance.rimStrength : 1f;
        Vector3 feet = CurrentFeet();
        Color rim = RimColor;
        rim.a *= rimScale;
        Color figureTint = Tint;
        figureTint.a *= FigureAlpha;

        DrawFigure(renderers, figureTint, feet, worldHeight, rim, slabBase, slabWidth);
        // The regiment shares the leader's slab, measured from the leader's feet: one ground
        // plane for the cast shadows, and real depth between the figures.
        foreach (Character3DModel trooper in Troops)
        {
            Color trooperTint = figureTint;
            trooperTint.a *= trooper.Fade;
            DrawFigure(trooper.renderers, trooperTint, feet, worldHeight, rim, slabBase, slabWidth);
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

    private void DrawFigure(Renderer[] figure, Color tint, Vector3 feet, float worldHeight, Color rim, float slabBase, float slabWidth)
    {
        if (figure == null) return;
        foreach (Renderer r in figure)
        {
            if (r.sortingLayerID != SortingLayerId) r.sortingLayerID = SortingLayerId;
            if (r.sortingOrder != SortingOrder) r.sortingOrder = SortingOrder;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, tint);
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
    }
}
