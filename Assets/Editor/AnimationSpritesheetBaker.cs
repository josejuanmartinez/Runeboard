using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Renders every state of an AnimatorController through an offscreen camera, one frame at a
// time (via AnimationMode.SampleAnimationClip on each state's clip directly — the same API the
// Inspector's own AnimationClip preview thumbnail uses, which correctly retargets Humanoid
// poses onto the skeleton), and tiles the captures into a single spritesheet PNG — one row per
// state, left-to-right = time through that state's clip. Built for baking a rigged
// Mixamo/Humanoid character (e.g. Gandalf.fbx + Gandalf.controller) down to a 2D sprite atlas
// for characters that don't render as live 3D in-game.
public class AnimationSpritesheetBaker : EditorWindow
{
    private class StateEntry
    {
        public AnimatorState State;
        public AnimationClip Clip;
        public bool Include = true;
        // Whether this state's pose range counts toward the shared camera zoom calculation.
        // A one-off pose like Death (falling flat) or a big Hit knockback can stretch the
        // bounding box far along one axis, inflating the required zoom for every OTHER state
        // and every facing that has to share one consistent scale — even though only that one
        // outlier state actually needs the extra room. Defaults off for states that look like
        // they'd cause that (name contains "death" or "hit"), on for everything else.
        public bool UseForFraming = true;
    }

    // See CreateBakeProxies/RebakeProxies/DestroyBakeProxies for why this exists: a
    // SkinnedMeshRenderer's GPU-skin deformation can't be trusted to update on repeated manual
    // Camera.Render() calls, so each frame gets baked to a plain static Mesh instead.
    private class BakeProxy
    {
        public SkinnedMeshRenderer Source;
        public Mesh BakedMesh;
        public MeshFilter Filter;
    }

    // A prop (weapon, staff, etc.) parented to a Humanoid bone at rig-build time, so it inherits
    // that bone's animated position/rotation for free — no changes to the model prefab or any
    // .anim clip needed. Attached BEFORE CreateBakeProxies() runs on the model, so a prop with
    // its own SkinnedMeshRenderer (e.g. a cloth cape) gets swept into the same proxy-baking pass
    // as the character; a plain static-mesh prop (the common case, e.g. a staff) needs no proxy
    // at all and is already picked up by the existing MeshRenderer bounds/capture sweep.
    private class PropEntry
    {
        public GameObject Prefab;
        public bool Include = true;
        public HumanBodyBones Bone = HumanBodyBones.RightHand;
        public Vector3 PositionOffset;
        public Vector3 RotationOffsetEuler;
        public Vector3 ScaleMultiplier = Vector3.one;
        // Where the prop's own mesh sits relative to the rotation anchor (PositionOffset).
        // Rotation is applied to the anchor, not the mesh, so a prop whose authored pivot isn't
        // at its grip point can still be spun in place — dial this in once with Rotation at
        // zero until the grip lines up with the bone, and Rotation will orbit around that point
        // afterward instead of swinging the whole prop away from the hand.
        public Vector3 PivotOffset;
        public bool UseForFraming = true;
    }

    // ── Inputs ────────────────────────────────────────────────────────
    private AnimatorController _controller;
    private GameObject _modelPrefab;
    private List<StateEntry> _states = new();
    private List<PropEntry> _props = new();

    // Prop choices are restricted to FBX models under this folder (rather than an open
    // ObjectField) so a prop can't accidentally point at some unrelated GameObject elsewhere
    // in the project. Cached per-window and rebuilt lazily; "Refresh" next to the Props header
    // covers a weapon FBX added to the folder after the window was already open.
    private const string WeaponPrefabsFolder = "Assets/Art/3D/fbx/Weapons";
    private GameObject[] _weaponPrefabs;
    private string[] _weaponPrefabOptions;

    private void RefreshWeaponPrefabs()
    {
        _weaponPrefabs = AssetDatabase.FindAssets("t:GameObject", new[] { WeaponPrefabsFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(go => go != null)
            .OrderBy(go => go.name)
            .ToArray();
        _weaponPrefabOptions = new[] { "(None)" }.Concat(_weaponPrefabs.Select(go => go.name)).ToArray();
    }

    // ── Bake settings ─────────────────────────────────────────────────
    private int _framesPerState = 16;
    private int _cellWidth = 512;
    private int _cellHeight = 512;
    private float _boundsMarginPct = 0f;
    private float _cameraYaw = -180f;
    private float _cameraPitch;
    private bool _recenterRoot = true;
    private bool _transparentBackground = true;
    private bool _pointFilter;
    private bool _generateClips = true;
    private bool _generateAtlasJson = true;

    // ── Facing batch bake ─────────────────────────────────────────────
    // "Turn Left"/"Turn Right" physically rotate the character by whatever their raw mocap
    // root motion actually produces — rarely an exact round number. Baking each facing's camera
    // at a hand-picked angle like 90° would create a visible pop wherever a turn animation
    // hands off into a facing-specific walk/idle/action sheet baked at a slightly different
    // angle. Measuring the real rotation and deriving all 4 facings from it keeps every
    // transition seamless.
    private bool _bakeAllFacings = true;
    private int _turnLeftStateIndex = -1;
    private int _turnRightStateIndex = -1;
    private float _measuredLeftYaw;
    private float _measuredRightYaw;
    private bool _anglesMeasured;

    // ── Output ────────────────────────────────────────────────────────
    private string _outFolder = "Assets/Art/Characters/AnimationSpritesheets";
    private string _sheetName = "";

    // Set for the duration of one Bake() call once ConfirmOverwrite has already asked the user
    // about every output path that bake will touch — suppresses SaveAtlas's own per-file prompt
    // so a 4-facing bake never asks more than once.
    private bool _overwriteConfirmed;

    private Vector2 _scroll;

    // ── Live Preview (see all EnsurePreviewRig/DrawPreview/TeardownPreviewRig below) ──
    private GameObject _previewRigRoot;
    private GameObject _previewSourcePrefab;
    private string _previewPropsSignature;
    private GameObject _previewModel;
    private Animator _previewAnimator;
    private Camera _previewCamera;
    private RenderTexture _previewRT;
    private List<BakeProxy> _previewBakeProxies;
    private bool _previewAnimModeActive;
    private int _previewStateIndex;
    private float _previewTime01;
    private Vector3? _previewBaselineXZ;
    private AnimatorState _previewBaselineState;
    private bool _autoPlay;
    private double _lastEditorTime;

    [MenuItem("Tools/Runeboard/Animation/Spritesheet Baker/Open")]
    public static void Open()
    {
        var w = GetWindow<AnimationSpritesheetBaker>("Spritesheet Baker");
        w.minSize = new Vector2(420, 520);
    }

    void OnEnable()
    {
        _lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        TeardownPreviewRig();
    }

    void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        double dt = now - _lastEditorTime;
        _lastEditorTime = now;
        if (!_autoPlay) return;

        var included = _states.Where(s => s.Include).ToList();
        if (included.Count == 0) return;
        _previewStateIndex = Mathf.Clamp(_previewStateIndex, 0, included.Count - 1);
        AnimationClip clip = included[_previewStateIndex].Clip;
        if (clip.length > 0.01f) _previewTime01 += (float)(dt / clip.length);
        if (_previewTime01 > 1f) _previewTime01 -= 1f;
        Repaint();
    }

    [MenuItem("Assets/Runeboard/Animation/Bake Spritesheet From Controller", true)]
    private static bool ValidateOpenFromController() => Selection.activeObject is AnimatorController;

    [MenuItem("Assets/Runeboard/Animation/Bake Spritesheet From Controller")]
    private static void OpenFromController()
    {
        var w = GetWindow<AnimationSpritesheetBaker>("Spritesheet Baker");
        w.minSize = new Vector2(420, 520);
        w._controller = Selection.activeObject as AnimatorController;
        w.OnControllerChanged();
    }

    // ── GUI ───────────────────────────────────────────────────────────
    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _controller = (AnimatorController)EditorGUILayout.ObjectField("Animator Controller", _controller, typeof(AnimatorController), false);
        if (EditorGUI.EndChangeCheck()) OnControllerChanged();

        _modelPrefab = (GameObject)EditorGUILayout.ObjectField("Model Prefab (Humanoid rig)", _modelPrefab, typeof(GameObject), false);

        if (_controller == null)
        {
            EditorGUILayout.HelpBox("Assign the AnimatorController to bake (e.g. Gandalf.controller). Its model prefab (e.g. Gandalf.fbx) will be auto-detected if left empty.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        GUILayout.Space(6);
        EditorGUILayout.LabelField("States to Bake", EditorStyles.boldLabel);
        if (_states.Count == 0)
        {
            EditorGUILayout.HelpBox("No states with an assigned AnimationClip motion were found.", MessageType.Warning);
        }
        foreach (var entry in _states)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                entry.Include = EditorGUILayout.ToggleLeft(entry.State.name, entry.Include, GUILayout.Width(260));
                GUILayout.Label($"{entry.Clip.length:F2}s", EditorStyles.miniLabel);
                bool loops = AnimationUtility.GetAnimationClipSettings(entry.Clip).loopTime;
                GUILayout.Label(loops ? "loop" : "one-shot", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                entry.UseForFraming = EditorGUILayout.ToggleLeft(
                    new GUIContent("Frame", "Include this state's pose range when computing camera zoom. " +
                        "Uncheck for outlier poses (Death lying flat, a big Hit knockback) that would otherwise " +
                        "force every state and every facing to zoom out just to fit that one pose."),
                    entry.UseForFraming, GUILayout.Width(60));
            }
        }

        GUILayout.Space(6);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh", GUILayout.Width(60))) RefreshWeaponPrefabs();
        }
        if (_weaponPrefabOptions == null) RefreshWeaponPrefabs();
        for (int i = 0; i < _props.Count; i++)
        {
            var prop = _props[i];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    prop.Include = EditorGUILayout.ToggleLeft(GUIContent.none, prop.Include, GUILayout.Width(18));
                    int currentIndex = prop.Prefab != null ? System.Array.IndexOf(_weaponPrefabs, prop.Prefab) : -1;
                    int selected = EditorGUILayout.Popup(currentIndex + 1, _weaponPrefabOptions);
                    prop.Prefab = selected == 0 ? null : _weaponPrefabs[selected - 1];
                    prop.Bone = (HumanBodyBones)EditorGUILayout.EnumPopup(prop.Bone, GUILayout.Width(140));
                    prop.UseForFraming = EditorGUILayout.ToggleLeft(
                        new GUIContent("Frame", "Let this prop's reach count toward the shared camera zoom. " +
                            "Off by default — a prop swinging wide during one attack/cast state would otherwise " +
                            "force every OTHER state (and facing) to zoom out just to fit that one pose."),
                        prop.UseForFraming, GUILayout.Width(60));
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        _props.RemoveAt(i);
                        i--;
                        continue;
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Offset", GUILayout.Width(50));
                    prop.PositionOffset = EditorGUILayout.Vector3Field(GUIContent.none, prop.PositionOffset);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Rotation", GUILayout.Width(50));
                    prop.RotationOffsetEuler = EditorGUILayout.Vector3Field(GUIContent.none, prop.RotationOffsetEuler);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent pivotLabel = new GUIContent("Pivot", "Where the mesh sits relative to the rotation anchor. " +
                        "Rotation spins the anchor, not the mesh, so if the prop swings away from the hand instead of " +
                        "turning in place, the mesh's own pivot isn't at its grip point — set Rotation back to zero, " +
                        "adjust Pivot until the grip lines up with the bone, then Rotation will orbit around that point.");
                    EditorGUILayout.LabelField(pivotLabel, GUILayout.Width(50));
                    prop.PivotOffset = EditorGUILayout.Vector3Field(GUIContent.none, prop.PivotOffset);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Scale", GUILayout.Width(50));
                    prop.ScaleMultiplier = EditorGUILayout.Vector3Field(GUIContent.none, prop.ScaleMultiplier);
                }
            }
        }
        if (GUILayout.Button("Add Prop")) _props.Add(new PropEntry());
        if (_props.Any(p => p.Include && p.Prefab != null))
        {
            EditorGUILayout.HelpBox(
                "Props are parented to the chosen bone at preview/bake time, so they inherit that " +
                "bone's animated position/rotation directly — no changes to the model prefab or any " +
                ".anim clip needed. Requires the model's Animator to be Humanoid (isHuman); a prop " +
                "with no bone match or a non-Humanoid rig is skipped with a Console warning.",
                MessageType.None);
        }

        GUILayout.Space(6);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);

        _framesPerState = Mathf.Max(1, EditorGUILayout.IntSlider("Frames Per State", _framesPerState, 1, 32));
        using (new EditorGUILayout.HorizontalScope())
        {
            _cellWidth = Mathf.Max(16, EditorGUILayout.IntField("Cell Width", _cellWidth));
            _cellHeight = Mathf.Max(16, EditorGUILayout.IntField("Cell Height", _cellHeight));
        }
        _boundsMarginPct = EditorGUILayout.Slider("Frame Margin", _boundsMarginPct, 0f, 1.5f);
        _cameraYaw = EditorGUILayout.Slider("Camera Yaw", _cameraYaw, -180f, 180f);
        _cameraPitch = EditorGUILayout.Slider("Camera Pitch (+ = look down)", _cameraPitch, -89f, 89f);
        _recenterRoot = EditorGUILayout.Toggle("Recenter Root (Humanoid Hips)", _recenterRoot);
        EditorGUILayout.HelpBox(
            "Recenter Root cancels sideways drift baked into the Hips bone (common on raw " +
            "Mixamo walk/turn clips) by shifting the whole rig each frame so the Hips bone's " +
            "X/Z position matches where it started that state. Vertical movement and rotation " +
            "(falling, crouching, turning) are preserved.", MessageType.None);
        _transparentBackground = EditorGUILayout.Toggle("Transparent Background", _transparentBackground);
        _pointFilter = EditorGUILayout.Toggle("Pixel-Perfect (Point Filter)", _pointFilter);
        _generateClips = EditorGUILayout.Toggle("Also Generate .anim Sprite Clips", _generateClips);
        _generateAtlasJson = EditorGUILayout.Toggle("Also Generate Atlas JSON", _generateAtlasJson);

        int included = _states.Count(s => s.Include);
        int atlasW = _cellWidth * _framesPerState;
        int atlasH = _cellHeight * included;
        int hwLimit = SystemInfo.maxTextureSize;
        GUILayout.Label($"→ atlas will be {_framesPerState} cols × {included} rows = {atlasW}×{atlasH}px", EditorStyles.miniLabel);
        if (Mathf.Max(atlasW, atlasH) > hwLimit)
        {
            EditorGUILayout.HelpBox(
                $"This exceeds the {hwLimit}px texture size limit on this GPU. Unity would silently " +
                "downscale the saved PNG on import while the sprite slice rects stay computed for the " +
                "FULL size — every frame would then crop the wrong (shrunken) region, which looks like " +
                "a tiny character crammed in one corner of an otherwise-empty sprite. Reduce Frames Per " +
                "State, Cell Width/Height, or the number of included states, or bake in multiple passes.",
                MessageType.Error);
        }

        DrawPreview();

        GUILayout.Space(6);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Facing Batch Bake", EditorStyles.boldLabel);
        _bakeAllFacings = EditorGUILayout.Toggle("Bake All 4 Facings (Fwd/Left/Back/Right)", _bakeAllFacings);
        if (_bakeAllFacings && _states.Count > 0)
        {
            string[] stateNames = _states.Select(s => s.State.name).ToArray();
            if (_turnLeftStateIndex < 0) _turnLeftStateIndex = AutoDetectTurnState(true);
            if (_turnRightStateIndex < 0) _turnRightStateIndex = AutoDetectTurnState(false);

            EditorGUI.BeginChangeCheck();
            _turnLeftStateIndex = EditorGUILayout.Popup("Turn Left State", Mathf.Clamp(_turnLeftStateIndex, 0, stateNames.Length - 1), stateNames);
            _turnRightStateIndex = EditorGUILayout.Popup("Turn Right State", Mathf.Clamp(_turnRightStateIndex, 0, stateNames.Length - 1), stateNames);
            if (EditorGUI.EndChangeCheck()) _anglesMeasured = false;

            if (GUILayout.Button("Measure Turn Angles"))
            {
                _measuredLeftYaw = MeasureNetYaw(_states[_turnLeftStateIndex].Clip);
                _measuredRightYaw = MeasureNetYaw(_states[_turnRightStateIndex].Clip);
                _anglesMeasured = true;
            }

            if (_anglesMeasured)
            {
                EditorGUILayout.LabelField($"Turn Left rotates {_measuredLeftYaw:F1}°, Turn Right rotates {_measuredRightYaw:F1}°", EditorStyles.miniLabel);
                if (Mathf.Abs(_measuredLeftYaw + _measuredRightYaw) > 5f)
                {
                    EditorGUILayout.HelpBox(
                        $"Left and Right don't look symmetric (sum = {_measuredLeftYaw + _measuredRightYaw:F1}°, expected ~0°). " +
                        "The 4-facing bake assumes Turn Left/Right are exact mirror opposites of each other — confirm these " +
                        "are the right clips, since the Back facing (2× Left) may not line up with 2× Right otherwise.",
                        MessageType.Warning);
                }
                EditorGUILayout.HelpBox(
                    $"Will bake, relative to the Camera Yaw above: Forward (+0°), Left ({_measuredLeftYaw:F1}°), " +
                    $"Back ({2f * _measuredLeftYaw:F1}°), Right ({-_measuredLeftYaw:F1}°) — every included state (including " +
                    "the turn clips themselves) at each facing, so \"turn again while already facing X\" has a matching sprite.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Click Measure Turn Angles before baking — this reads the REAL rotation your Turn Left/Right clips produce, so facing transitions don't pop.", MessageType.Warning);
            }
        }

        GUILayout.Space(6);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        _outFolder = EditorGUILayout.TextField("Folder", _outFolder);
        _sheetName = EditorGUILayout.TextField("Sheet Name", _sheetName);

        GUILayout.Space(10);
        GUI.enabled = _modelPrefab != null && included > 0 && !string.IsNullOrWhiteSpace(_sheetName)
                      && (!_bakeAllFacings || _anglesMeasured);
        if (GUILayout.Button(_bakeAllFacings ? "Bake All 4 Facings" : "Bake Spritesheet", GUILayout.Height(32)))
            Bake();
        GUI.enabled = true;

        if (_modelPrefab == null)
            EditorGUILayout.HelpBox("Assign the model prefab (the FBX with the Humanoid Animator) — could not auto-detect it.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    void OnControllerChanged()
    {
        _states.Clear();
        if (_controller == null) return;

        foreach (ChildAnimatorState child in _controller.layers[0].stateMachine.states)
        {
            if (child.state.motion is AnimationClip clip)
            {
                string n = child.state.name.ToLowerInvariant();
                bool looksLikeOutlier = n.Contains("death") || n.Contains("hit");
                _states.Add(new StateEntry { State = child.state, Clip = clip, UseForFraming = !looksLikeOutlier });
            }
        }

        if (string.IsNullOrWhiteSpace(_sheetName))
            _sheetName = $"{_controller.name}_baked";

        if (_modelPrefab == null)
            _modelPrefab = AutoDetectModelPrefab(_controller);
    }

    static GameObject AutoDetectModelPrefab(AnimatorController controller)
    {
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller))?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return null;

        string preferred = $"{folder}/{controller.name}.fbx";
        var byName = AssetDatabase.LoadAssetAtPath<GameObject>(preferred);
        if (byName != null) return byName;

        string fallbackGuid = AssetDatabase.FindAssets("t:GameObject", new[] { folder }).FirstOrDefault();
        return fallbackGuid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(fallbackGuid)) : null;
    }

    // ── Live Preview ─────────────────────────────────────────────────
    // Renders the same offscreen rig used by Bake(), but into a persistent RenderTexture
    // drawn directly in this window, with a scrubbable time slider. Exists because the real
    // bake rig is HideAndDontSave and off in RenderTexture-land — there was previously no way
    // to see camera framing or confirm bones are actually animating before committing to a
    // full bake.
    void DrawPreview()
    {
        GUILayout.Space(6);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);

        var included = _states.Where(s => s.Include).ToList();
        if (_modelPrefab == null || included.Count == 0)
        {
            EditorGUILayout.HelpBox("Assign a model prefab and include at least one state to preview.", MessageType.None);
            TeardownPreviewRig();
            return;
        }

        _previewStateIndex = Mathf.Clamp(_previewStateIndex, 0, included.Count - 1);
        string[] names = included.Select(s => s.State.name).ToArray();
        _previewStateIndex = EditorGUILayout.Popup("Preview State", _previewStateIndex, names);
        _autoPlay = EditorGUILayout.Toggle("Auto Play", _autoPlay);
        using (new EditorGUI.DisabledScope(_autoPlay))
            _previewTime01 = EditorGUILayout.Slider("Preview Time", _previewTime01, 0f, 1f);

        EnsurePreviewRig();
        StateEntry entry = included[_previewStateIndex];
        if (_previewBaselineState != entry.State)
        {
            _previewBaselineState = entry.State;
            _previewBaselineXZ = null;
        }

        Transform hips = _recenterRoot && _previewAnimator.isHuman
            ? _previewAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
        _previewModel.transform.position = Vector3.zero;
        PlayAtRecentered(_previewModel, hips, entry.Clip, _previewTime01, ref _previewBaselineXZ);
        RebakeProxies(_previewBakeProxies);

        // Deliberately NOT applying _previewFramingExclusions here: that set exists so a prop
        // with "Frame" off doesn't inflate the ONE SHARED zoom used across every cell of the
        // real bake. This preview only ever shows a single frame at a time and refits its
        // camera fresh every call regardless (see the help box below), so there's no shared-zoom
        // concern to protect — and excluding the prop here would just crop it out of view,
        // making it impossible to see what you're doing while dialing in Position/Pivot/Rotation.
        var renderers = _previewModel.GetComponentsInChildren<Renderer>(true)
            .Where(r => !(r is SkinnedMeshRenderer))
            .Concat(_previewBakeProxies.Select(p => (Renderer)p.Filter.GetComponent<MeshRenderer>()))
            .ToArray();
        Bounds b = CombinedBounds(renderers);
        Quaternion camRot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        Vector3 forward = camRot * Vector3.forward;
        float aspect = (float)_cellWidth / _cellHeight;
        ComputeOrthographicFit(b, camRot, out float horizontalHalf, out float verticalHalf);
        horizontalHalf *= 1f + _boundsMarginPct;
        verticalHalf *= 1f + _boundsMarginPct;
        _previewCamera.aspect = aspect;
        _previewCamera.orthographicSize = Mathf.Max(verticalHalf, horizontalHalf / aspect, 0.01f);
        float distance = b.extents.magnitude + 1f;
        _previewCamera.transform.position = b.center - forward * distance;
        _previewCamera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        _previewCamera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f); // opaque neutral — alpha isn't meaningful to look at here

        // Tight near/far clip around the character instead of a generic wide range — see the
        // matching comment in Bake() for why: it's what was causing the z-fighting seam line.
        float clipPad = b.extents.magnitude + 0.5f;
        _previewCamera.nearClipPlane = Mathf.Max(0.01f, distance - clipPad);
        _previewCamera.farClipPlane = distance + clipPad;

        _previewCamera.targetTexture = _previewRT;
        _previewCamera.Render();
        _previewCamera.targetTexture = null;

        float previewWidth = Mathf.Min(position.width - 24f, 384f);
        Rect texRect = GUILayoutUtility.GetRect(previewWidth, previewWidth / aspect, GUILayout.ExpandWidth(false));
        GUI.DrawTexture(texRect, _previewRT, ScaleMode.ScaleToFit);

        if (_previewAnimator.isHuman)
        {
            Transform probe = _previewAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm)
                              ?? _previewAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (probe != null)
            {
                AnimationMode.SampleAnimationClip(_previewModel, entry.Clip, 0f);
                Quaternion rotAtStart = probe.localRotation;
                AnimationMode.SampleAnimationClip(_previewModel, entry.Clip, _previewTime01 * entry.Clip.length);
                float delta = Quaternion.Angle(rotAtStart, probe.localRotation);
                EditorGUILayout.HelpBox(
                    delta > 0.5f
                        ? $"Bones are animating ({delta:F1}° arm rotation from state start)."
                        : "Bones are NOT moving at this time — mesh will look rigid in the bake. Check Animator.avatar / isHuman on the model prefab (see Console after a Bake for the full diagnostic).",
                    delta > 0.5f ? MessageType.None : MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Animator is missing or not Humanoid — bone diagnostic unavailable, and muscle retargeting will not apply.", MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "This view auto-fits the CURRENT frame only, so zoom will differ slightly from the " +
            "real bake (which auto-fits across every sampled frame of every included state). Use " +
            "it to dial in yaw/pitch/margin and confirm bones are actually animating before running " +
            "a full bake.", MessageType.None);
    }

    // Prop instances are only (re)parented when the preview rig itself is rebuilt, so without
    // this the rig would go stale the moment a prop's prefab/bone/offset is tweaked — the
    // sliders would move nothing until some unrelated field (e.g. Cell Width) forced a rebuild.
    // Cheap enough to recompute every OnGUI: this window's prop lists are always small.
    string ComputePropsSignature()
    {
        return string.Join("|", _props.Select(p =>
            $"{(p.Include ? 1 : 0)}:{(p.Prefab != null ? p.Prefab.GetEntityId().ToString() : "0")}:{p.Bone}:{p.PositionOffset}:{p.RotationOffsetEuler}:{p.PivotOffset}:{p.ScaleMultiplier}:{p.UseForFraming}"));
    }

    void EnsurePreviewRig()
    {
        string propsSignature = ComputePropsSignature();
        bool needsRebuild = _previewRigRoot == null || _previewSourcePrefab != _modelPrefab
            || _previewRT == null || _previewRT.width != _cellWidth || _previewRT.height != _cellHeight
            || _previewPropsSignature != propsSignature;
        _previewPropsSignature = propsSignature;
        if (!needsRebuild) return;

        TeardownPreviewRig();

        // Deliberately NOT layer 31 — that's Bake()'s reserved layer. This rig stays alive in
        // the scene for as long as the window is open, including while a bake runs (both rigs
        // sit near the world origin), so sharing a layer would make the bake camera's
        // cullingMask pick up BOTH rigs at once — a real double-exposure of two overlapping
        // Gandalfs baked directly into the exported PNG, not a rendering/AA artifact.
        const int previewLayer = 30;
        _previewRigRoot = new GameObject("__SpritesheetPreviewRig__") { hideFlags = HideFlags.HideAndDontSave };
        _previewSourcePrefab = _modelPrefab;

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(_modelPrefab);
        if (model == null) model = Object.Instantiate(_modelPrefab);
        model.transform.SetParent(_previewRigRoot.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        SetLayerRecursive(model.transform, previewLayer);
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        _previewModel = model;

        _previewAnimator = model.GetComponentInChildren<Animator>(true);
        if (_previewAnimator == null) _previewAnimator = model.AddComponent<Animator>();

        AttachProps(model, _previewAnimator, previewLayer);
        _previewBakeProxies = CreateBakeProxies(model, previewLayer);

        var camGo = new GameObject("__SpritesheetPreviewCamera__") { hideFlags = HideFlags.HideAndDontSave };
        camGo.transform.SetParent(_previewRigRoot.transform, false);
        _previewCamera = camGo.AddComponent<Camera>();
        _previewCamera.orthographic = true;
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.cullingMask = 1 << previewLayer;
        _previewCamera.nearClipPlane = 0.01f;
        _previewCamera.farClipPlane = 100f;
        DisableTemporalEffects(_previewCamera);

        var lightGo = new GameObject("__SpritesheetPreviewLight__") { hideFlags = HideFlags.HideAndDontSave };
        lightGo.transform.SetParent(_previewRigRoot.transform, false);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        _previewRT = new RenderTexture(_cellWidth, _cellHeight, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };

        if (!_previewAnimModeActive)
        {
            AnimationMode.StartAnimationMode();
            _previewAnimModeActive = true;
        }
    }

    void TeardownPreviewRig()
    {
        if (_previewAnimModeActive)
        {
            AnimationMode.StopAnimationMode();
            _previewAnimModeActive = false;
        }
        if (_previewRT != null) { _previewRT.Release(); Object.DestroyImmediate(_previewRT); _previewRT = null; }
        if (_previewBakeProxies != null) { DestroyBakeProxies(_previewBakeProxies); _previewBakeProxies = null; }
        if (_previewRigRoot != null) { Object.DestroyImmediate(_previewRigRoot); _previewRigRoot = null; }
        _previewModel = null;
        _previewAnimator = null;
        _previewCamera = null;
        _previewSourcePrefab = null;
        _previewBaselineXZ = null;
        _previewBaselineState = null;
    }

    // ── Bake ──────────────────────────────────────────────────────────
    int AutoDetectTurnState(bool left)
    {
        string needle = left ? "left" : "right";
        for (int i = 0; i < _states.Count; i++)
        {
            string n = _states[i].State.name.ToLowerInvariant();
            if (n.Contains("turn") && n.Contains(needle)) return i;
        }
        return 0;
    }

    // Samples a clip's Hips bone at t=0 and t=length and returns the signed net yaw (rotation
    // around the world Y axis) it produces — the character's real turn amount, not a guess.
    // Runs on its own throwaway instance so it doesn't disturb the Live Preview or an in-progress
    // bake's rig.
    float MeasureNetYaw(AnimationClip clip)
    {
        if (clip == null || _modelPrefab == null) return 0f;

        GameObject temp = null;
        bool alreadyActive = AnimationMode.InAnimationMode();
        try
        {
            temp = (GameObject)PrefabUtility.InstantiatePrefab(_modelPrefab);
            if (temp == null) temp = Object.Instantiate(_modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;

            Animator animator = temp.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = temp.AddComponent<Animator>();
            Transform hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null)
            {
                Debug.LogWarning("[SpritesheetBaker] Can't measure turn angle — no Hips bone (Humanoid avatar required).");
                return 0f;
            }

            if (!alreadyActive) AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(temp, clip, 0f);
            float yawStart = Mathf.Atan2(hips.forward.x, hips.forward.z) * Mathf.Rad2Deg;
            AnimationMode.SampleAnimationClip(temp, clip, clip.length);
            float yawEnd = Mathf.Atan2(hips.forward.x, hips.forward.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(yawStart, yawEnd);
        }
        finally
        {
            if (!alreadyActive && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (temp != null) Object.DestroyImmediate(temp);
        }
    }

    // Dispatches either a single bake (current Camera Yaw only) or, with Bake All 4 Facings on,
    // one full bake per facing — each including every checked state (turn clips too, so "turn
    // again while already facing X" has a correctly-angled sprite) — all sharing one Sheet Name
    // stem with a facing suffix appended.
    void Bake()
    {
        _overwriteConfirmed = false;

        if (!_bakeAllFacings)
        {
            if (!ConfirmOverwrite(new[] { ComputeAtlasOutPath(_sheetName) })) return;
            BakeOneFacing(0f);
            return;
        }

        if (!_anglesMeasured)
        {
            Debug.LogError("[SpritesheetBaker] Click 'Measure Turn Angles' before baking all facings.");
            return;
        }

        float leftYaw = _measuredLeftYaw;
        var facings = new (string suffix, float yawOffset)[]
        {
            ("_Forward", 0f),
            ("_Left", leftYaw),
            ("_Back", 2f * leftYaw),
            ("_Right", -leftYaw),
        };

        if (!ConfirmOverwrite(facings.Select(f => ComputeAtlasOutPath(_sheetName + f.suffix)))) return;

        var entries = _states.Where(s => s.Include).ToList();
        if (entries.Count == 0) return;

        // Only states marked "Frame" contribute to the shared zoom — see StateEntry.UseForFraming.
        // Falls back to every included state if the user unchecked Frame on all of them, rather
        // than measuring an empty set.
        var framingEntries = entries.Where(e => e.UseForFraming).ToList();
        if (framingEntries.Count == 0) framingEntries = entries;

        Bounds? measured = MeasureCombinedBoundsForEntries(framingEntries);
        if (!measured.HasValue)
        {
            Debug.LogError("[SpritesheetBaker] Could not measure bounds for shared facing framing — is the model prefab missing a mesh?");
            return;
        }
        Bounds sharedBounds = measured.Value;

        // A humanoid's side-profile footprint (front-to-back reach — arms/robe swinging during
        // an attack) is usually deeper than its front-on footprint (roughly shoulder-width), so
        // fitting each facing's camera independently zooms Left/Right out relative to
        // Forward/Back. Measuring the SAME bounds against all 4 facing angles up front and
        // sharing the largest required size keeps scale consistent across every facing.
        float aspect = (float)_cellWidth / _cellHeight;
        float sharedOrthoSize = 0.01f;
        foreach (var (suffix, yawOffset) in facings)
        {
            Quaternion camRot = Quaternion.Euler(_cameraPitch, _cameraYaw + yawOffset, 0f);
            ComputeOrthographicFit(sharedBounds, camRot, out float h, out float v);
            h *= 1f + _boundsMarginPct;
            v *= 1f + _boundsMarginPct;
            float thisFacingSize = Mathf.Max(v, h / aspect);
            Debug.Log($"[SpritesheetBaker] Facing {suffix}: yawOffset={yawOffset:F1}°, own required orthoSize={thisFacingSize:F3} (h={h:F3}, v={v:F3})");
            sharedOrthoSize = Mathf.Max(sharedOrthoSize, thisFacingSize);
        }
        Debug.Log($"[SpritesheetBaker] Shared bounds: center={sharedBounds.center}, extents={sharedBounds.extents} — sharedOrthoSize (max across all facings) = {sharedOrthoSize:F3}");

        string originalSheetName = _sheetName;
        try
        {
            foreach (var (suffix, yawOffset) in facings)
            {
                _sheetName = originalSheetName + suffix;
                BakeOneFacing(yawOffset, sharedBounds, sharedOrthoSize);
            }
        }
        finally
        {
            _sheetName = originalSheetName;
        }
    }

    // Builds a minimal throwaway rig (no camera/light/render — just enough to sample poses and
    // read renderer bounds) purely to measure the combined AABB across every sampled frame of
    // every included state. World-space bounds don't depend on which way a camera looks, so this
    // runs once and the result is shared across all 4 facing bakes instead of re-measured per
    // facing (which would also risk tiny floating-point differences between otherwise-identical
    // measurements).
    Bounds? MeasureCombinedBoundsForEntries(List<StateEntry> entries)
    {
        GameObject rigRoot = null;
        try
        {
            const int measureLayer = 29; // distinct from Bake's (31) and Preview's (30) reserved layers
            rigRoot = new GameObject("__SpritesheetMeasureRig__") { hideFlags = HideFlags.HideAndDontSave };

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(_modelPrefab);
            if (model == null) model = Object.Instantiate(_modelPrefab);
            model.transform.SetParent(rigRoot.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SetLayerRecursive(model.transform, measureLayer);

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();

            HashSet<Renderer> framingExclusions = AttachProps(model, animator, measureLayer);
            List<BakeProxy> proxies = CreateBakeProxies(model, measureLayer);
            var allRenderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is SkinnedMeshRenderer) && !framingExclusions.Contains(r))
                .Concat(proxies.Where(p => !framingExclusions.Contains(p.Source)).Select(p => (Renderer)p.Filter.GetComponent<MeshRenderer>()))
                .ToArray();

            Transform hips = _recenterRoot && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;

            bool alreadyActive = AnimationMode.InAnimationMode();
            if (!alreadyActive) AnimationMode.StartAnimationMode();
            try
            {
                Bounds? measured = null;
                foreach (var entry in entries)
                {
                    var frameBoundsList = new List<Bounds>();
                    model.transform.position = Vector3.zero;
                    Vector3? baselineXZ = null;
                    foreach (float t in SampleTimes(entry))
                    {
                        PlayAtRecentered(model, hips, entry.Clip, t, ref baselineXZ);
                        RebakeProxies(proxies);
                        frameBoundsList.Add(CombinedBounds(allRenderers));
                    }
                    Bounds? perState = AggregateBoundsRejectingOutliers(frameBoundsList, entry.State.name);
                    if (perState.HasValue)
                    {
                        Debug.Log($"[SpritesheetBaker] Framing contribution — '{entry.State.name}': extents={perState.Value.extents}, center={perState.Value.center}");
                        measured = measured.HasValue ? Encapsulate(measured.Value, perState.Value) : perState.Value;
                    }
                }
                return measured;
            }
            finally
            {
                DestroyBakeProxies(proxies);
                if (!alreadyActive) AnimationMode.StopAnimationMode();
            }
        }
        finally
        {
            if (rigRoot != null) Object.DestroyImmediate(rigRoot);
        }
    }

    void BakeOneFacing(float yawOffset, Bounds? forcedBounds = null, float? forcedOrthoSize = null)
    {
        var entries = _states.Where(s => s.Include).ToList();
        if (entries.Count == 0) return;

        // Abort loudly rather than let Unity silently downscale the saved PNG on import while
        // the sprite slice rects stay computed for the full (pre-scale) size — that mismatch is
        // what makes every frame crop the wrong region (see the GUI warning above this button).
        int atlasW = _cellWidth * _framesPerState;
        int atlasH = _cellHeight * entries.Count;
        int hwLimit = SystemInfo.maxTextureSize;
        if (Mathf.Max(atlasW, atlasH) > hwLimit)
        {
            Debug.LogError($"[SpritesheetBaker] Aborted: computed atlas {atlasW}x{atlasH} exceeds this GPU's {hwLimit}px texture size limit. Reduce Frames Per State, Cell Width/Height, or included states.");
            return;
        }

        GameObject rigRoot = null;
        Texture2D atlas = null;
        RenderTexture rt = null;

        try
        {
            const int previewLayer = 31; // reserved, unused gameplay layer
            rigRoot = new GameObject("__SpritesheetBakeRig__") { hideFlags = HideFlags.HideAndDontSave };

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(_modelPrefab);
            if (model == null) model = Object.Instantiate(_modelPrefab);
            model.transform.SetParent(rigRoot.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SetLayerRecursive(model.transform, previewLayer);

            // Note: we deliberately do NOT drive this via animator.Play()/Update(). That combo
            // does not reliably retarget a Humanoid pose onto the skeleton outside Play Mode —
            // it can move/rotate the root while leaving every bone frozen. AnimationMode
            // .SampleAnimationClip (below) is the supported edit-time API for this: it's the
            // same call the Inspector's own AnimationClip preview thumbnail uses, and it does
            // perform correct muscle-to-bone Humanoid retargeting via the Animator's Avatar.
            //
            // Search the whole hierarchy (not just the root) for a pre-existing Animator —
            // Unity's Humanoid model import normally puts one on the root with a valid Avatar
            // already wired up, but if that lookup ever misses, AddComponent<Animator>() would
            // silently create a fresh Animator with avatar == null. Sampling a Humanoid clip
            // against an avatar-less Animator can't retarget any muscle curve to a bone, yet
            // the clip's separate root-motion curve (position/rotation of the whole transform)
            // still applies fine — which looks exactly like "only the whole mesh moves, no
            // bones animate". So this is checked explicitly and logged rather than assumed.
            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("[SpritesheetBaker] No Animator found anywhere on the model prefab — adding one, but it will have no Avatar, so Humanoid bone poses will NOT apply (only root motion will).");
                animator = model.AddComponent<Animator>();
            }
            else if (animator.gameObject != model)
            {
                Debug.Log($"[SpritesheetBaker] Animator found on child '{animator.gameObject.name}', not the model root.");
            }

            if (animator.avatar == null)
                Debug.LogError("[SpritesheetBaker] Animator.avatar is NULL — Humanoid muscle curves cannot retarget onto any bone. Only the clip's root motion (position/rotation) will show. Check the model prefab's Rig import settings.");
            else if (!animator.avatar.isValid)
                Debug.LogError($"[SpritesheetBaker] Animator.avatar '{animator.avatar.name}' is not valid — Humanoid retargeting will fail.");
            else if (!animator.isHuman)
                Debug.LogWarning($"[SpritesheetBaker] Animator.avatar '{animator.avatar.name}' is valid but not Humanoid (isHuman=false) — GetBoneTransform-based recentering and Humanoid retargeting won't apply.");
            else
                Debug.Log($"[SpritesheetBaker] Animator OK: avatar='{animator.avatar.name}', isHuman=true, isValid=true.");

            HashSet<Renderer> framingExclusions = AttachProps(model, animator, previewLayer);
            List<BakeProxy> bakeProxies = CreateBakeProxies(model, previewLayer);
            var allRenderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is SkinnedMeshRenderer) && !framingExclusions.Contains(r))
                .Concat(bakeProxies.Where(p => !framingExclusions.Contains(p.Source)).Select(p => (Renderer)p.Filter.GetComponent<MeshRenderer>()))
                .ToArray();

            Transform hips = _recenterRoot && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (_recenterRoot && animator.isHuman && hips == null)
                Debug.LogWarning("[SpritesheetBaker] Recenter Root is on but no Hips bone was found on the Avatar — drift will not be corrected.");

            // One-off proof: sample the first included state's clip at two different times and
            // compare a limb bone's local rotation. If they're identical, muscle retargeting is
            // not applying at all (confirms the avatar diagnosis above); if they differ, the
            // pose data IS reaching the bones and the bug is elsewhere (e.g. render/capture
            // timing), which would need a different fix entirely.
            if (animator.isHuman && entries.Count > 0)
            {
                Transform probeBone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
                                      ?? animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (probeBone != null)
                {
                    AnimationClip probeClip = entries[0].Clip;
                    AnimationMode.SampleAnimationClip(model, probeClip, 0f);
                    Quaternion rotAtStart = probeBone.localRotation;
                    AnimationMode.SampleAnimationClip(model, probeClip, probeClip.length * 0.5f);
                    Quaternion rotAtMid = probeBone.localRotation;
                    float angleDelta = Quaternion.Angle(rotAtStart, rotAtMid);
                    Debug.Log($"[SpritesheetBaker] DIAGNOSTIC on '{entries[0].State.name}' ({probeClip.name}): bone '{probeBone.name}' local rotation changed {angleDelta:F2}° between t=0 and t=mid. " +
                              (angleDelta > 0.5f
                                  ? "Bone data IS being applied — if the baked PNG still looks rigid, the bug is in capture/render, not sampling."
                                  : "Bone data is NOT changing — retargeting is failing (see Animator/Avatar diagnostics above)."));
                }
                else
                {
                    Debug.LogWarning("[SpritesheetBaker] DIAGNOSTIC: could not find RightLowerArm/RightUpperArm bone to probe.");
                }
            }

            var camGo = new GameObject("__SpritesheetBakeCamera__") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(rigRoot.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = _transparentBackground ? new Color(0f, 0f, 0f, 0f) : Color.black;
            cam.cullingMask = 1 << previewLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.aspect = (float)_cellWidth / _cellHeight;
            DisableTemporalEffects(cam);

            var lightGo = new GameObject("__SpritesheetBakeLight__") { hideFlags = HideFlags.HideAndDontSave };
            lightGo.transform.SetParent(rigRoot.transform, false);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            rt = new RenderTexture(_cellWidth, _cellHeight, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };

            // Don't stomp on Animation Mode if the Live Preview panel already turned it on —
            // only whoever switched it on should switch it back off.
            bool animModeAlreadyActive = AnimationMode.InAnimationMode();
            if (!animModeAlreadyActive) AnimationMode.StartAnimationMode();
            try
            {
                // Pass 1: measure combined bounds across every sampled frame of every included
                // state, so one fixed camera framing covers the widest pose (e.g. raised arms
                // mid-attack) without the character changing scale between rows. Skipped when
                // the facing batch bake already measured this (bounds are facing-independent —
                // world-space geometry doesn't care which way the camera looks — so it's
                // measured once up front and shared across all 4 facings instead of redone here).
                Bounds b;
                if (forcedBounds.HasValue)
                {
                    b = forcedBounds.Value;
                }
                else
                {
                    // Only states marked "Frame" contribute — see StateEntry.UseForFraming.
                    var framingEntries = entries.Where(e => e.UseForFraming).ToList();
                    if (framingEntries.Count == 0) framingEntries = entries;

                    Bounds? measured = null;
                    foreach (var entry in framingEntries)
                    {
                        var frameBoundsList = new List<Bounds>();
                        model.transform.position = Vector3.zero;
                        Vector3? baselineXZ = null;
                        foreach (float t in SampleTimes(entry))
                        {
                            PlayAtRecentered(model, hips, entry.Clip, t, ref baselineXZ);
                            RebakeProxies(bakeProxies);
                            frameBoundsList.Add(CombinedBounds(allRenderers));
                        }
                        Bounds? perState = AggregateBoundsRejectingOutliers(frameBoundsList, entry.State.name);
                        if (perState.HasValue)
                            measured = measured.HasValue ? Encapsulate(measured.Value, perState.Value) : perState.Value;
                    }

                    if (!measured.HasValue)
                    {
                        Debug.LogError("[SpritesheetBaker] Could not measure any renderer bounds — is the model prefab missing a mesh?");
                        return;
                    }

                    b = measured.Value;
                }

                // Camera orbits the (now-stationary) character at the requested yaw/pitch.
                // orthographicSize only controls the VERTICAL half-height of the view — mixing
                // in a horizontal extent there (as an earlier version of this did) either
                // over-zooms or crops the sides depending on aspect, it doesn't affect vertical
                // fit at all. Horizontal fit is guaranteed separately below via aspect.
                // yawOffset rotates this facing's camera relative to the base Camera Yaw — see
                // Bake()'s facing table (0°/leftYaw/2×leftYaw/−leftYaw for Forward/Left/Back/Right).
                Quaternion camRot = Quaternion.Euler(_cameraPitch, _cameraYaw + yawOffset, 0f);
                Vector3 forward = camRot * Vector3.forward;

                ComputeOrthographicFit(b, camRot, out float horizontalHalf, out float verticalHalf);
                horizontalHalf *= 1f + _boundsMarginPct;
                verticalHalf *= 1f + _boundsMarginPct;
                float aspect = (float)_cellWidth / _cellHeight;
                // forcedOrthoSize (facing batch bake only) is the MAX tight-fit size needed across
                // all 4 facings, shared so Forward/Left/Back/Right render at the same scale — a
                // side profile's front-to-back reach (e.g. arms/robe swinging during an attack)
                // is often deeper than the front view's shoulder-width footprint, so sizing each
                // facing independently made Left/Right zoom out relative to Forward/Back.
                cam.orthographicSize = forcedOrthoSize ?? Mathf.Max(verticalHalf, horizontalHalf / aspect, 0.01f);
                Debug.Log($"[SpritesheetBaker] BakeOneFacing yawOffset={yawOffset:F1}°: forcedOrthoSize={(forcedOrthoSize.HasValue ? forcedOrthoSize.Value.ToString("F3") : "null")}, own fit would be {Mathf.Max(verticalHalf, horizontalHalf / aspect, 0.01f):F3} → cam.orthographicSize={cam.orthographicSize:F3}");

                float distance = b.extents.magnitude + 1f;
                cam.transform.position = b.center - forward * distance;
                cam.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

                // Tight near/far clip around the character rather than the generic 0.01–100
                // range the camera was initialized with. With an orthographic camera sitting
                // only ~distance units away, that huge fixed range wastes almost all of the
                // depth buffer's precision on empty space the character never occupies —
                // causing z-fighting between nearly-coplanar surfaces (e.g. a robe's
                // overlapping front panels), which shows up as a faint seam line down the
                // middle of every baked frame.
                float clipPad = b.extents.magnitude + 0.5f;
                cam.nearClipPlane = Mathf.Max(0.01f, distance - clipPad);
                cam.farClipPlane = distance + clipPad;

                // Pass 2: render for real with the fixed framing.
                int rows = entries.Count;
                atlas = new Texture2D(_cellWidth * _framesPerState, _cellHeight * rows, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var clearRow = new Color[_cellWidth * _cellHeight];
                for (int i = 0; i < clearRow.Length; i++) clearRow[i] = new Color(0, 0, 0, 0);

                for (int row = 0; row < rows; row++)
                {
                    var entry = entries[row];
                    float[] times = SampleTimes(entry).ToArray();
                    int texRow = rows - 1 - row; // texture space is bottom-up; row 0 (first state) drawn at the top

                    model.transform.position = Vector3.zero;
                    Vector3? baselineXZ = null;

                    for (int col = 0; col < _framesPerState; col++)
                    {
                        if (col < times.Length)
                        {
                            PlayAtRecentered(model, hips, entry.Clip, times[col], ref baselineXZ);
                            RebakeProxies(bakeProxies);
                            Texture2D frame = CaptureFrame(cam, rt);
                            atlas.SetPixels(col * _cellWidth, texRow * _cellHeight, _cellWidth, _cellHeight, frame.GetPixels());
                            Object.DestroyImmediate(frame);
                        }
                        else
                        {
                            atlas.SetPixels(col * _cellWidth, texRow * _cellHeight, _cellWidth, _cellHeight, clearRow);
                        }
                    }
                }
                atlas.Apply();
            }
            finally
            {
                DestroyBakeProxies(bakeProxies);
                if (!animModeAlreadyActive) AnimationMode.StopAnimationMode();
            }

            string outPath = SaveAtlas(atlas, entries);
            if (outPath == null) return; // user chose Skip — don't touch the existing file's clips/manifest either
            if (_generateClips) GenerateClips(outPath, entries);
            if (_generateAtlasJson) WriteAtlasManifest(outPath, entries, atlas.width, atlas.height);
        }
        finally
        {
            if (rigRoot != null) Object.DestroyImmediate(rigRoot);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (atlas != null) Object.DestroyImmediate(atlas);
        }
    }

    IEnumerable<float> SampleTimes(StateEntry entry)
    {
        bool loops = AnimationUtility.GetAnimationClipSettings(entry.Clip).loopTime;
        int n = _framesPerState;
        for (int i = 0; i < n; i++)
            yield return loops || n == 1 ? (float)i / n : (float)i / (n - 1);
    }

    // Samples the clip's actual pose (bones included) at normalizedTime via AnimationMode,
    // then — if a Hips bone was supplied — cancels any X/Z drift baked into that bone relative
    // to where it sat on this state's first sampled frame. Raw Mixamo drops that weren't
    // re-baked with "Bake Into Pose" can carry real walk/turn displacement directly on the
    // Hips curve, so this keeps the character in place for a fixed camera while leaving
    // rotation and vertical movement (falling, crouching, jumping) intact.
    static void PlayAtRecentered(GameObject model, Transform hips, AnimationClip clip, float normalizedTime, ref Vector3? baselineXZ)
    {
        AnimationMode.SampleAnimationClip(model, clip, normalizedTime * clip.length);

        if (hips == null) return;

        Vector3 hipsPos = hips.position;
        if (!baselineXZ.HasValue)
        {
            baselineXZ = new Vector3(hipsPos.x, 0f, hipsPos.z);
            return;
        }

        Vector3 delta = new Vector3(hipsPos.x - baselineXZ.Value.x, 0f, hipsPos.z - baselineXZ.Value.z);
        model.transform.position -= delta;
    }

    static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds b = default;
        bool first = true;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static Bounds Encapsulate(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

    // Raw Mixamo loop clips frequently have a "seam" glitch: the very first or last sampled
    // frame's root/hips position snaps toward the opposite end of the loop for a single
    // keyframe, producing a frame whose bounds sit far away from every other frame in that same
    // state even though the actual pose range is compact and consistent. A plain union
    // (Encapsulate) across all frames is exactly one bad frame away from wildly overstating how
    // much room a state needs — which is what was inflating the shared camera zoom for every
    // facing despite no single VISIBLE frame ever looking like it needed the extra space.
    // Median-distance outlier rejection catches this regardless of which frame(s) it hits,
    // rather than hardcoding "always drop frame 0 and the last frame".
    static Bounds? AggregateBoundsRejectingOutliers(List<Bounds> frameBounds, string stateNameForLogging)
    {
        if (frameBounds.Count == 0) return null;
        if (frameBounds.Count <= 2)
        {
            Bounds? tiny = null;
            foreach (var b in frameBounds) tiny = tiny.HasValue ? Encapsulate(tiny.Value, b) : b;
            return tiny;
        }

        List<float> xs = frameBounds.Select(b => b.center.x).OrderBy(v => v).ToList();
        List<float> ys = frameBounds.Select(b => b.center.y).OrderBy(v => v).ToList();
        List<float> zs = frameBounds.Select(b => b.center.z).OrderBy(v => v).ToList();
        Vector3 median = new Vector3(xs[xs.Count / 2], ys[ys.Count / 2], zs[zs.Count / 2]);

        List<float> distances = frameBounds.Select(b => Vector3.Distance(b.center, median)).OrderBy(d => d).ToList();
        float medianDistance = distances[distances.Count / 2];
        // Generous multiple (so normal frame-to-frame pose variation is never mistaken for an
        // outlier) plus a small floor (so a near-static state with almost-zero natural spread
        // doesn't make the threshold itself near-zero and start rejecting ordinary frames).
        float threshold = Mathf.Max(medianDistance * 4f, 0.05f);

        Bounds? result = null;
        int rejected = 0;
        foreach (var b in frameBounds)
        {
            if (Vector3.Distance(b.center, median) > threshold) { rejected++; continue; }
            result = result.HasValue ? Encapsulate(result.Value, b) : b;
        }
        if (rejected > 0)
            Debug.Log($"[SpritesheetBaker] '{stateNameForLogging}': rejected {rejected} outlier frame(s) (likely a loop-seam glitch in the raw clip) out of {frameBounds.Count} when computing framing size.");
        return result ?? frameBounds[0];
    }

    // The old framing math used extents.magnitude — the AABB's diagonal half-length — as the
    // horizontal half-size, which is "safe for any camera angle" but a real over-estimate for
    // the specific fixed yaw/pitch actually being baked at (it only matches the true footprint
    // if the camera happens to view exactly along that diagonal). That slack showed up as
    // margin around the character even with Frame Margin set to 0. This instead projects the
    // AABB's 8 corners onto the camera's own right/up axes and takes the true half-extent along
    // each — the tightest orthographic fit that still guarantees no clipping at this exact
    // camera orientation.
    static void ComputeOrthographicFit(Bounds b, Quaternion camRot, out float horizontalHalf, out float verticalHalf)
    {
        Vector3 right = camRot * Vector3.right;
        Vector3 up = camRot * Vector3.up;
        Vector3 e = b.extents;

        float maxH = 0f, maxV = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Vector3 corner = new Vector3(sx * e.x, sy * e.y, sz * e.z);
            maxH = Mathf.Max(maxH, Mathf.Abs(Vector3.Dot(corner, right)));
            maxV = Mathf.Max(maxV, Mathf.Abs(Vector3.Dot(corner, up)));
        }
        horizontalHalf = maxH;
        verticalHalf = maxV;
    }

    static Texture2D CaptureFrame(Camera cam, RenderTexture rt)
    {
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }

    // Matches the "default" label CharacterAnimatorControllers/Illustrations already scan for
    // other per-character art, so CharacterSpritesheets finds these with no separate label setup.
    const string AddressableSpritesheetLabel = "default";

    // Moving a baked PNG/atlas.json in the Project window after baking (e.g. into a
    // [RaceOrName] subfolder to match the layout other characters use) does NOT update its
    // Addressable entry's stored address — Unity tracks the file itself by GUID (always
    // correct), but the address string CharacterSpritesheets keys its lookups by is whatever was
    // set at bake time, and goes stale. Resets every AnimationSpritesheets entry's address back
    // to AddressableAssetEntry.AssetPath (which always resolves live from the GUID), so a moved
    // file's address matches where it actually lives again.
    [MenuItem("Tools/Runeboard/Animation/Spritesheet Baker/Resync Addressable Paths")]
    static void ResyncSpritesheetAddresses()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[SpritesheetBaker] No AddressableAssetSettings found.");
            return;
        }

        int fixedCount = 0;
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null) continue;
            bool groupChanged = false;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
                if (!entry.AssetPath.StartsWith(CharacterSpritesheets.AddressRoot)) continue;
                if (entry.address == entry.AssetPath) continue;

                Debug.Log($"[SpritesheetBaker] Resyncing address '{entry.address}' -> '{entry.AssetPath}'.");
                entry.address = entry.AssetPath;
                groupChanged = true;
                fixedCount++;
            }
            if (groupChanged) EditorUtility.SetDirty(group);
        }

        if (fixedCount > 0)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"[SpritesheetBaker] Resynced {fixedCount} AnimationSpritesheets Addressable entr{(fixedCount == 1 ? "y" : "ies")}.");
    }

    // No-ops (with a warning) if this project has no AddressableAssetSettings yet, rather than
    // throwing and aborting an otherwise-successful bake.
    static void MarkAddressable(string assetPath, string label)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning($"[SpritesheetBaker] No AddressableAssetSettings found — '{assetPath}' was NOT marked Addressable. Mark it manually (label '{label}') so CharacterSpritesheets can load it at runtime.");
            return;
        }

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return;

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = assetPath;
        if (!settings.GetLabels().Contains(label)) settings.AddLabel(label);
        entry.SetLabel(label, true, true);

        // Labels live on the entry, which is serialized as part of its GROUP asset (e.g.
        // "Default Local Group.asset"), not AddressableAssetSettings.asset itself — dirtying
        // only the top-level settings previously left the group's on-disk YAML holding an empty
        // m_SerializedLabels for every baked sprite/atlas, so CharacterSpritesheets' label-based
        // Addressables scan found nothing. Dirty the group explicitly and save both immediately
        // rather than hoping an eventual autosave picks it up.
        if (entry.parentGroup != null) EditorUtility.SetDirty(entry.parentGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
    }

    // Parents each enabled prop under its chosen Humanoid bone on this specific rig instance.
    // Must run BEFORE CreateBakeProxies(model, ...) so a prop with its own SkinnedMeshRenderer
    // gets caught by that same recursive scan; a static-mesh prop needs no proxy and is already
    // picked up by the existing "every non-skinned Renderer under model" sweep once parented in.
    // Instances are plain children of the model, so they're destroyed for free when the caller
    // tears down its rigRoot — no separate cleanup path needed.
    //
    // Returns every Renderer belonging to a prop whose UseForFraming is OFF (the default) — the
    // caller must exclude these from whatever renderer set it measures bounds from, while still
    // letting them render normally (rendering goes through the camera's layer mask, not this
    // set, so excluding a renderer here only hides it from the ZOOM calculation, never from the
    // actual captured pixels).
    HashSet<Renderer> AttachProps(GameObject model, Animator animator, int layer)
    {
        var excludedFromFraming = new HashSet<Renderer>();
        foreach (var prop in _props)
        {
            if (!prop.Include || prop.Prefab == null) continue;

            if (!animator.isHuman)
            {
                Debug.LogWarning($"[SpritesheetBaker] Skipping prop '{prop.Prefab.name}' — Animator is not Humanoid, so bone '{prop.Bone}' can't be resolved.");
                continue;
            }

            Transform bone = animator.GetBoneTransform(prop.Bone);
            if (bone == null)
            {
                Debug.LogWarning($"[SpritesheetBaker] Skipping prop '{prop.Prefab.name}' — Avatar has no '{prop.Bone}' bone mapped.");
                continue;
            }

            // Rotation lives on this anchor, not on the prop mesh itself — the anchor's own
            // position/rotation relative to the bone never changes, so spinning the prop via
            // RotationOffsetEuler orbits it around a fixed point instead of moving that point.
            // The mesh's own (possibly off-grip) pivot is absorbed into PivotOffset below, one
            // level further down the hierarchy.
            var anchor = new GameObject($"__PropAnchor_{prop.Prefab.name}__");
            anchor.transform.SetParent(bone, false);
            anchor.transform.localPosition = prop.PositionOffset;
            anchor.transform.localRotation = Quaternion.Euler(prop.RotationOffsetEuler);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prop.Prefab);
            if (instance == null) instance = Object.Instantiate(prop.Prefab);
            instance.transform.SetParent(anchor.transform, false);
            instance.transform.localPosition = prop.PivotOffset;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, prop.ScaleMultiplier);
            SetLayerRecursive(anchor.transform, layer);

            if (!prop.UseForFraming)
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                    excludedFromFraming.Add(r);
        }
        return excludedFromFraming;
    }

    // A SkinnedMeshRenderer's GPU-skin deformation is dispatched on the player loop's own frame
    // tick, not on demand when Camera.Render() is called. Calling cam.Render() repeatedly inside
    // one script method — with no real frame boundary in between — can leave the GPU-skinned
    // mesh frozen on whatever pose it last actually deformed to, even though the bone Transforms
    // underneath are being correctly updated by AnimationMode.SampleAnimationClip every call
    // (confirmed separately by the bone-rotation diagnostic in Bake()). The fix is to sidestep
    // GPU skinning for capture entirely: SkinnedMeshRenderer.BakeMesh() produces a plain static
    // Mesh reflecting the CURRENT bone pose synchronously, no frame-timing dependency, which
    // gets rendered through an ordinary MeshRenderer instead of the live SkinnedMeshRenderer
    // (disabled here so it doesn't also render and double up).
    static List<BakeProxy> CreateBakeProxies(GameObject model, int layer)
    {
        var proxies = new List<BakeProxy>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.enabled = false;

            // Parented with identity local transform: BakeMesh's output vertices are already in
            // the renderer's own local space, so this reproduces the exact world transform the
            // SkinnedMeshRenderer itself would have rendered at, including through root motion.
            var proxyGo = new GameObject($"__BakeProxy_{smr.name}__") { hideFlags = HideFlags.HideAndDontSave };
            proxyGo.transform.SetParent(smr.transform, false);
            proxyGo.layer = layer;

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var filter = proxyGo.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = proxyGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = smr.sharedMaterials;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            proxies.Add(new BakeProxy { Source = smr, BakedMesh = mesh, Filter = filter });
        }
        return proxies;
    }

    static void RebakeProxies(List<BakeProxy> proxies)
    {
        foreach (var p in proxies) p.Source.BakeMesh(p.BakedMesh, true);
    }

    static void DestroyBakeProxies(List<BakeProxy> proxies)
    {
        foreach (var p in proxies)
        {
            if (p.Filter != null) Object.DestroyImmediate(p.Filter.gameObject);
            if (p.BakedMesh != null) Object.DestroyImmediate(p.BakedMesh);
        }
        proxies.Clear();
    }

    // Each capture is a single, isolated Render() of an unrelated pose from the last one — not
    // consecutive frames of real motion. URP's Temporal Anti-Aliasing (and any Volume-driven
    // post-processing such as motion blur) blends each new frame against that leftover history,
    // which shows up as a double-exposure "ghost" of the previous pose in the exported PNG. It's
    // invisible in the Live Preview because that camera re-renders the *same* sampled pose on
    // every OnGUI repaint, so the temporal history converges to a clean image within a couple of
    // frames — Bake() only ever renders one frame per pose, so it never gets the chance.
    static void DisableTemporalEffects(Camera cam)
    {
        UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
        camData.antialiasing = AntialiasingMode.None;
        camData.renderPostProcessing = false;
    }

    string ComputeAtlasOutPath(string sheetName)
    {
        string safeName = string.Concat(sheetName.Split(Path.GetInvalidFileNameChars()));
        return $"{_outFolder}/{safeName}.png";
    }

    // Asks at most one Overwrite/Stop dialog for a whole Bake() call, up front, before any of
    // the (potentially 4x, for a full facing set) expensive rendering work starts — rather than
    // one dialog per facing, which asked the same question up to 4 times for one decision.
    // Rejecting stops the entire bake (not just the facing(s) that already existed): a per-file
    // Skip would silently leave a mismatched set of old/new facing sheets.
    bool ConfirmOverwrite(IEnumerable<string> outPaths)
    {
        List<string> existing = outPaths.Where(File.Exists).ToList();
        if (existing.Count == 0) return true;

        bool overwrite = EditorUtility.DisplayDialog(
            "Spritesheet Already Exists",
            existing.Count == 1
                ? $"'{existing[0]}' already exists. Overwrite it?"
                : $"{existing.Count} output spritesheet(s) already exist:\n\n{string.Join("\n", existing)}\n\nOverwrite them?",
            "Overwrite", "Stop");
        _overwriteConfirmed = overwrite;
        if (!overwrite) Debug.Log("[SpritesheetBaker] Bake stopped — output already exists.");
        return overwrite;
    }

    // Returns null if the file already existed and the user chose Skip — callers must check for
    // that before touching the result (see BakeOneFacing). Reached only as a fallback: the normal
    // path already resolved this via ConfirmOverwrite before any baking started.
    string SaveAtlas(Texture2D atlas, List<StateEntry> entries)
    {
        if (!Directory.Exists(_outFolder)) Directory.CreateDirectory(_outFolder);
        string safeName = string.Concat(_sheetName.Split(Path.GetInvalidFileNameChars()));
        string outPath = ComputeAtlasOutPath(_sheetName);

        if (File.Exists(outPath) && !_overwriteConfirmed)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Spritesheet Already Exists",
                $"'{outPath}' already exists. Overwrite it?",
                "Overwrite", "Skip");
            if (!overwrite)
            {
                Debug.Log($"[SpritesheetBaker] Skipped — {outPath} already exists.");
                return null;
            }
        }

        File.WriteAllBytes(outPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = _pointFilter ? FilterMode.Point : FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(atlas.width, atlas.height)), 32, SystemInfo.maxTextureSize);

        // Reimport now, BEFORE computing/assigning per-frame sprite rects. The very first
        // ImportAsset() call above ran with whatever import settings were already on disk
        // (typically the Editor's default max texture size, e.g. 2048), so at this point the
        // texture can still be sitting in memory scaled down well below atlas.width/height —
        // e.g. a 3520px-tall atlas gets shrunk to fit a 2048 cap. Assigning rects computed for
        // the FULL-size canvas against that still-shrunken texture is exactly what made every
        // single frame fail as "rect lies outside texture": none were being sliced at all.
        importer.SaveAndReimport();

        int rows = entries.Count;

        // TextureImporter.spritesheet is the deprecated legacy sprite-slicing API — its own
        // obsolete-warning says support "has been removed" in favor of
        // ISpriteEditorDataProvider, and in practice it proved not just deprecated but actually
        // broken for bulk custom-named slicing here: across an 11-state, 176-sprite bake, only
        // ONE state's sprites ever came out under their requested names (the rest silently
        // didn't get sliced/named as asked, which is what was starving GenerateClips). This
        // uses the actively-supported data provider API instead, which Unity's own Sprite
        // Editor Window is built on.
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        var spriteRects = new List<SpriteRect>();
        for (int row = 0; row < rows; row++)
        {
            var entry = entries[row];
            int texRow = rows - 1 - row;
            string stateSafe = string.Concat(entry.State.name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
            for (int col = 0; col < _framesPerState; col++)
            {
                spriteRects.Add(new SpriteRect
                {
                    name = $"{safeName}_{stateSafe}_{col:D2}",
                    rect = new Rect(col * _cellWidth, texRow * _cellHeight, _cellWidth, _cellHeight),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = GUID.Generate(),
                });
            }
        }
        dataProvider.SetSpriteRects(spriteRects.ToArray());

        var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameFileIdProvider.SetNameFileIdPairs(spriteRects.Select(sr => new SpriteNameFileIdPair(sr.name, sr.spriteID)));

        dataProvider.Apply();
        importer.SaveAndReimport();

        // For a large sub-sprite count (16 cols × 11 rows = 176 here), SaveAndReimport()
        // reimporting the file doesn't guarantee every Sprite sub-asset is already materialized
        // and loadable via AssetDatabase.LoadAllAssetsAtPath the instant it returns — GenerateClips
        // querying immediately afterward in this same call stack was only finding whichever
        // sub-assets happened to be ready, which is why only 1 of 11 states' .anim clips were
        // getting generated instead of all of them. A forced synchronous refresh here ensures
        // the whole sprite set is actually queryable before GenerateClips runs.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Marking the TEXTURE Addressable exposes every one of its Sprite sub-assets as
        // individually loadable/enumerable too (the same mechanism Illustrations.cs already
        // relies on for other multi-sprite character art) — so CharacterSpritesheets can find
        // these frames at runtime with no further manual step.
        MarkAddressable(outPath, AddressableSpritesheetLabel);

        var texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        Debug.Log($"[SpritesheetBaker] Baked {rows} state(s) × {_framesPerState} frame(s) → {outPath} ({atlas.width}×{atlas.height})");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = texAsset;
        EditorGUIUtility.PingObject(texAsset);
        return outPath;
    }

    void GenerateClips(string sheetPath, List<StateEntry> entries)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
            .OfType<Sprite>()
            .ToDictionary(s => s.name);

        const string clipFolder = "Assets/Art/Characters/Animations";
        if (!Directory.Exists(clipFolder)) Directory.CreateDirectory(clipFolder);

        string safeName = string.Concat(_sheetName.Split(Path.GetInvalidFileNameChars()));
        int saved = 0;

        foreach (var entry in entries)
        {
            string stateSafe = string.Concat(entry.State.name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
            var frameSprites = new List<Sprite>();
            for (int col = 0; col < _framesPerState; col++)
            {
                if (sprites.TryGetValue($"{safeName}_{stateSafe}_{col:D2}", out Sprite s))
                    frameSprites.Add(s);
            }
            if (frameSprites.Count == 0) continue;

            bool loops = AnimationUtility.GetAnimationClipSettings(entry.Clip).loopTime;
            float fps = _framesPerState / Mathf.Max(entry.Clip.length, 0.01f);

            var clip = new AnimationClip { frameRate = fps };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
            var kf = new ObjectReferenceKeyframe[frameSprites.Count];
            for (int i = 0; i < frameSprites.Count; i++)
                kf[i] = new ObjectReferenceKeyframe { time = i / fps, value = frameSprites[i] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, kf);

            string dest = $"{clipFolder}/{safeName}_{stateSafe}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dest) != null)
                AssetDatabase.DeleteAsset(dest);
            AssetDatabase.CreateAsset(clip, dest);
            saved++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SpritesheetBaker] Generated {saved} .anim sprite clip(s) in {clipFolder}.");
    }

    // Plain data-only classes for JsonUtility — describes which pixel rects/frame ranges in the
    // baked PNG correspond to which AnimatorController state ("movement"), for any external
    // tooling/runtime that wants to read the atlas without going through Unity's own
    // Sprite/AnimationClip assets (e.g. a non-Unity viewer, or a custom in-game 2D renderer).
    [System.Serializable]
    private class AtlasFrame
    {
        public int index;
        public int x, y, width, height;
    }

    [System.Serializable]
    private class AtlasState
    {
        public string name;
        public string spriteNamePrefix;
        public int row;
        public int frameCount;
        public bool loop;
        public float clipLength;
        public float fps;
        public List<AtlasFrame> frames;
    }

    [System.Serializable]
    private class AtlasManifest
    {
        public string texture;
        public int textureWidth, textureHeight;
        public int cellWidth, cellHeight;
        public int framesPerState;
        public List<AtlasState> states;
    }

    // Row 0 = first included state = the TOP of the image as normally displayed (see the "texture
    // space is bottom-up; row 0 drawn at the top" comment in Bake()'s Pass 2) — so top-left pixel
    // coordinates here are simply row*cellHeight / col*cellWidth, no bottom-up flip needed.
    void WriteAtlasManifest(string sheetPath, List<StateEntry> entries, int textureWidth, int textureHeight)
    {
        string safeName = string.Concat(_sheetName.Split(Path.GetInvalidFileNameChars()));
        var manifest = new AtlasManifest
        {
            texture = Path.GetFileName(sheetPath),
            textureWidth = textureWidth,
            textureHeight = textureHeight,
            cellWidth = _cellWidth,
            cellHeight = _cellHeight,
            framesPerState = _framesPerState,
            states = new List<AtlasState>()
        };

        for (int row = 0; row < entries.Count; row++)
        {
            var entry = entries[row];
            string stateSafe = string.Concat(entry.State.name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
            bool loops = AnimationUtility.GetAnimationClipSettings(entry.Clip).loopTime;
            float fps = _framesPerState / Mathf.Max(entry.Clip.length, 0.01f);

            var state = new AtlasState
            {
                name = entry.State.name,
                spriteNamePrefix = $"{safeName}_{stateSafe}",
                row = row,
                frameCount = _framesPerState,
                loop = loops,
                clipLength = entry.Clip.length,
                fps = fps,
                frames = new List<AtlasFrame>()
            };
            for (int col = 0; col < _framesPerState; col++)
            {
                state.frames.Add(new AtlasFrame
                {
                    index = col,
                    x = col * _cellWidth,
                    y = row * _cellHeight,
                    width = _cellWidth,
                    height = _cellHeight
                });
            }
            manifest.states.Add(state);
        }

        string jsonPath = $"{_outFolder}/{safeName}.atlas.json";
        File.WriteAllText(jsonPath, JsonUtility.ToJson(manifest, true));
        AssetDatabase.ImportAsset(jsonPath);
        MarkAddressable(jsonPath, AddressableSpritesheetLabel);
        Debug.Log($"[SpritesheetBaker] Wrote atlas manifest → {jsonPath}");
    }
}
