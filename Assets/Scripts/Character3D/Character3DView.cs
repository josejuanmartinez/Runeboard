using System.Collections.Generic;
using UnityEngine;

// Prototype: shows a hex's character as a live, animated 3D model instead of (Replace) or next to
// (Side By Side) its baked spritesheet sprite. Added by CharacterAnimationController to every
// board controller. The sprite controller keeps running unchanged (state machine, turn-in-place
// timing, outline/gold ring, hover collider); this reads its resolved name, state and facing each
// frame, and on top of that acts out gameplay events from UnitAnimations — spells, blows, wounds,
// deaths, level-ups — with the full Humanoid clip library, picking its own base loop (idle, walk,
// run, sneak while hidden). While the model shows in Replace mode the sprite is kept enabled but
// skipped by rendering (forceRenderingOff), so everything still keyed off it (bounds, gold ring,
// Hex's alpha/tint) keeps working. Characters with no FBX keep their sprite.
[DefaultExecutionOrder(100)] // after CharacterAnimationController and Board's walk coroutines
[RequireComponent(typeof(CharacterAnimationController))]
public class Character3DView : MonoBehaviour
{
    // A unit's facing survives moving to the next hex's controller (and so its own view), the
    // same way Character.lastFacingOrientation carries the sprite facing over.
    private static readonly Dictionary<Character, float> lastYawByCharacter = new();
    // Which view currently shows each character — how UnitAnimations finds a unit.
    private static readonly Dictionary<Character, Character3DView> byCharacter = new();

    private const string Idle = "standing idle";
    private const float FacingHold = 2.5f;        // seconds a unit keeps facing whoever it fought
    private const float SpriteEchoSuppress = 1.2f; // ignore the sprite's own copy of a move just played

    private CharacterAnimationController controller;
    private SpriteRenderer sprite;
    private Transform goldRing;
    private SpriteRenderer castShadow;
    private Character3DModel model;
    private string modelFolder;
    private float spriteCellHeight = 5.12f; // sprite-local units, refreshed from the live sprite
    private Character registeredAs;
    private Character outlinedCharacter;

    // The unit in this slot: the spritesheet one, or the one Hex asked for that has no sheet.
    private Character Shown => controller.ShownCharacter ?? controller.RequestedCharacter;

    private Character yawCharacter;
    private float yaw;
    private float targetYaw;
    private bool facingFromMovement;
    private Vector3 lastPosition;
    private CharacterAnimationController.Orientation lastOrientation;
    private float facingOverrideUntil;
    private float facingOverrideYaw;
    private float moveSpeed;               // sprite cells per second, smoothed
    private bool spriteHidden;
    private string lastSpriteState;
    private float suppressSpriteEchoUntil;
    private bool wasSelected;
    private Character armedFor;
    private Character3DWeapons.Loadout loadout = Character3DWeapons.Unarmed;

    public static Character3DView For(Character character) =>
        character != null && byCharacter.TryGetValue(character, out Character3DView view) && view != null ? view : null;

    public CharacterAnimationController Controller => controller;
    public bool ShowsModel => model != null && model.gameObject.activeInHierarchy;

    private void Awake()
    {
        controller = GetComponent<CharacterAnimationController>();
        sprite = GetComponent<SpriteRenderer>();
        goldRing = transform.Find("GoldRing");
    }

    private void OnDisable()
    {
        RestoreGoldRing();
        SetSpriteHidden(false);
        Register(null);
    }

    private void OnDestroy() => Register(null);

    private void Register(Character character)
    {
        if (registeredAs == character) return;
        if (registeredAs != null && byCharacter.TryGetValue(registeredAs, out Character3DView owner) && owner == this)
            byCharacter.Remove(registeredAs);
        registeredAs = character;
        if (character != null) byCharacter[character] = this;
    }

    private void LateUpdate()
    {
        bool spriteVisible = sprite != null && sprite.enabled && sprite.sprite != null;
        // Units with no baked spritesheet (drawn with their static illustration as a sprite) are
        // still 3D: the model resolves on its own tiers (Character3DModel.TryResolve).
        Character character = Shown;
        Register(spriteVisible ? character : null);
        if (character != outlinedCharacter)
        {
            outlinedCharacter = character;
            // Hex only sets the alignment outline for spritesheet units; the 3D outline needs it always.
            if (character != null && !controller.IsShowing) controller.SetOutlineForCharacter(character);
        }

        Character3DSettings settings = Character3DSettings.Instance;
        Character3DSettings.Mode mode = Character3DPrototype.Mode;
        string folder = null;
        if (settings != null && mode != Character3DSettings.Mode.Off) Character3DModel.TryResolve(character, out folder);

        if (folder != modelFolder)
        {
            if (model != null) Destroy(model.gameObject);
            model = null;
            modelFolder = folder;
        }
        if (folder != null && model == null)
        {
            GameObject prefab = Character3DModel.RequestPrefab(folder);
            if (prefab != null) model = Character3DModel.Create(prefab, folder, transform);
        }

        SetSpriteHidden(model != null && mode == Character3DSettings.Mode.Replace);
        if (model == null)
        {
            RestoreGoldRing();
            token?.Hide();
            return;
        }

        if (model.gameObject.activeSelf != spriteVisible) model.gameObject.SetActive(spriteVisible);
        if (!spriteVisible)
        {
            token?.Hide();
            return;
        }

        // Only spritesheet frames share the baked cell; a static illustration has its own size.
        if (controller.IsShowing && sprite.sprite != null) spriteCellHeight = sprite.sprite.bounds.size.y;
        UpdateFacing(settings);
        Place(settings, mode);
        Leader owner = character != null ? character.GetOwner() : null;
        model.SetBase(settings.showBase ? owner != null ? owner.nationColor : Color.clear : null, settings.baseRadius);
        UpdateBaseFocus();
        if (character != armedFor)
        {
            armedFor = character;
            loadout = Character3DWeapons.For(character);
        }
        model.Arm(loadout);
        UpdateRegiment(character);
        Animate(settings);
        KickUpDust();

        model.Tint = FigureTint(sprite.color);
        model.RimColor = controller.PresentationRimColor;
        model.SortingLayerId = sprite.sortingLayerID;
        model.SortingOrder = sprite.sortingOrder;
        UpdateToken(settings, character, owner);
    }

    // Strategic zoom: the figure dithers away and its token takes its place on the base.
    private UnitToken token;

    // An army commander leads a small regiment on one oval stand (Character3DRegiment); a lone
    // character is one figure on its round base.
    private Character3DRegiment regiment;
    private Character regimentFor;
    private int troopCount;

    private void UpdateRegiment(Character character)
    {
        Army army = character != null && character.IsArmyCommander() ? character.GetArmy() : null;
        troopCount = Character3DRegiment.TroopCount(army);
        bool sameUnit = character == regimentFor;
        regimentFor = character;
        if (regiment == null || regiment.gameObject != model.gameObject)
        {
            if (troopCount == 0) return;
            regiment = model.GetComponent<Character3DRegiment>();
            if (regiment == null) regiment = model.gameObject.AddComponent<Character3DRegiment>();
        }
        if (troopCount == 0) regiment.Sync(null, TroopsTypeEnum.ma, 0, sameUnit);
        else regiment.Sync(Character3DRegiment.TroopFolder(character), Character3DRegiment.MainTroopType(army), troopCount, sameUnit);
    }

    private void UpdateToken(Character3DSettings settings, Character character, Leader owner)
    {
        float amount = model.BaseWorldRadius > 0f ? UnitToken.Amount(settings) : 0f;
        model.FigureAlpha = 1f - amount;
        if (amount <= 0f)
        {
            token?.Hide();
            return;
        }
        if (token == null) token = UnitToken.Create(transform);
        token.Show(character, owner != null ? owner.nationColor : Color.clear, model.transform.position, model.BaseWorldRadius,
            amount * sprite.color.a, sprite.sortingLayerID, sprite.sortingOrder, settings, Mathf.Min(troopCount, 2));
    }

    // ── Animation ───────────────────────────────────────────────────────────────────────────

    private void Animate(Character3DSettings settings)
    {
        Character3DAnimator anim = model.Animation;
        anim.IdleVariety = UnitAnimations.IsHumans(Shown); // other nations' units just stand idle
        Character character = Shown;
        string spriteState = controller.CurrentStateName;

        // Base loop: walk / run while moving (crouched if hidden), otherwise idle or crouch idle.
        bool hidden = character != null && character.IsHidden();
        bool moving = spriteState == "Standing Walk Forward" || moveSpeed > 0.05f;
        string loop = moving
            ? hidden ? "Crouch Walk Forward" : moveSpeed > settings.runSpeed ? "Standing Run Forward" : "Standing Walk Forward"
            : hidden ? "Crouch Idle" : Idle;
        if (!anim.IsHolding) anim.SetBaseLoop(loop, 0.2f);
        if (regiment != null) regiment.Mirror(loop);

        // Gameplay events (UnitAnimations) take priority over the sprite's own one-shot states.
        if (UnitAnimations.TryTake(character, out UnitAnimations.Move move, out string clip, out Vector3? facing))
        {
            if (facing.HasValue) FaceToward(facing.Value);
            UnitAnimations.Perform(anim, move, clip, hidden);
            if (regiment != null) regiment.Follow(move, hidden);
            suppressSpriteEchoUntil = Time.time + SpriteEchoSuppress;
        }

        // One-shots set straight on the sprite controller by game code (e.g. a human card play).
        if (spriteState != lastSpriteState)
        {
            lastSpriteState = spriteState;
            if (Time.time > suppressSpriteEchoUntil && UnitAnimations.IsHumans(character) && SpriteStateMove(spriteState, out UnitAnimations.Move echo))
            {
                UnitAnimations.Perform(anim, echo, UnitAnimations.Pick(echo), hidden);
                if (regiment != null) regiment.Follow(echo, hidden);
            }
        }

        // Selecting a unit gets a "ready" acknowledgement — it raises its guard and lowers it again.
        bool selected = character != null && Board.Instance != null && Board.Instance.selectedCharacter == character;
        if (selected && !wasSelected && !anim.IsBusy && !moving && UnitAnimations.IsHumans(character))
        {
            UnitAnimations.Perform(anim, UnitAnimations.Move.Rally, null, hidden);
            if (regiment != null) regiment.Follow(UnitAnimations.Move.Rally, hidden);
        }
        wasSelected = selected;
    }

    private static bool SpriteStateMove(string state, out UnitAnimations.Move move)
    {
        switch (state)
        {
            case "Action": move = UnitAnimations.Move.Gesture; return true;
            case "Magic": move = UnitAnimations.Move.Cast; return true;
            case "Attack": move = UnitAnimations.Move.Attack; return true;
            case "Hit": move = UnitAnimations.Move.Hit; return true;
            case "Block": move = UnitAnimations.Move.Block; return true;
            default: move = default; return false;
        }
    }

    // Called by UnitAnimations.Killed before the unit leaves its hex: the model is let go into the
    // world where it stands, plays the death fall, lies a moment and fades out on its own.
    public bool PlayDeathAndDetach(string clip)
    {
        if (model == null || !model.gameObject.activeInHierarchy || clip == null) return false;
        Character3DModel body = model;
        model = null;
        modelFolder = null; // a fresh model if this slot shows a unit again
        regiment = null;    // goes with the body (Character3DCorpse fells it)
        body.transform.SetParent(null, true);
        body.gameObject.AddComponent<Character3DCorpse>().Begin(clip);
        SetSpriteHidden(false);
        return true;
    }

    private void FaceToward(Vector3 worldPosition)
    {
        Vector2 delta = worldPosition - transform.position;
        if (delta.sqrMagnitude < 1e-6f) return;
        facingOverrideYaw = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
        facingOverrideUntil = Time.time + FacingHold;
    }

    // ── Facing and placement ────────────────────────────────────────────────────────────────

    // Faces the actual direction of travel while the unit walks (diagonals included — the sprites
    // only have 4 facings), whoever it is fighting for a moment after a clash, otherwise the
    // controller's facing. Reached by turning, never snapped.
    private void UpdateFacing(Character3DSettings settings)
    {
        Character character = Shown;
        CharacterAnimationController.Orientation orientation = controller.ActiveOrientation;
        Vector3 position = transform.position;

        if (character != yawCharacter)
        {
            yawCharacter = character;
            yaw = character != null && lastYawByCharacter.TryGetValue(character, out float saved)
                ? saved : OrientationYaw(orientation);
            targetYaw = yaw;
            facingFromMovement = true; // keep the carried-over yaw until the facing changes
            lastOrientation = orientation;
            lastPosition = position;
            moveSpeed = 0f;
        }

        Vector2 delta = position - lastPosition;
        lastPosition = position;
        float worldCell = spriteCellHeight * transform.lossyScale.y;
        float step = delta.magnitude;
        // The upper bound skips the snap back to the hex slot when a walk ends.
        bool walkingStep = step > worldCell * 0.001f && step < worldCell * 0.5f;
        float speed = walkingStep && Time.deltaTime > 0f ? step / worldCell / Time.deltaTime : 0f;
        moveSpeed = Mathf.Lerp(moveSpeed, speed, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (moveSpeed < 0.02f) moveSpeed = 0f;

        if (walkingStep)
        {
            targetYaw = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
            facingFromMovement = true;
        }
        else if (Time.time < facingOverrideUntil)
        {
            targetYaw = facingOverrideYaw;
            facingFromMovement = true;
        }
        else if (orientation != lastOrientation)
        {
            facingFromMovement = false;
        }
        lastOrientation = orientation;
        if (!facingFromMovement) targetYaw = OrientationYaw(orientation);

        yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, settings.turnSpeed * Time.deltaTime);
        if (character != null) lastYawByCharacter[character] = yaw;
    }

    // Yaw 0 faces up the board (away from the viewer). Matches the sprite controller's mapping,
    // where moving toward -X shows the "Right" facing.
    private static float OrientationYaw(CharacterAnimationController.Orientation orientation) => orientation switch
    {
        CharacterAnimationController.Orientation.Back => 0f,
        CharacterAnimationController.Orientation.Right => -90f,
        CharacterAnimationController.Orientation.Left => 90f,
        _ => 180f,
    };

    // The baked sprite's actual figure (feet and top, sprite-local units) per character, measured
    // from its idle frames. The sheets are imported with tight meshes, so a frame's vertices trace
    // the opaque silhouette — the baker framed every character differently (widest pose, props),
    // so a single "figure fills N% of the cell" fraction left many models visibly smaller.
    private static readonly Dictionary<string, Vector2> figureByFolder = new();

    private void MeasureSpriteFigure()
    {
        if (!controller.IsShowing || controller.CurrentStateName != Idle || sprite.sprite == null) return;
        string sheet = controller.ResolvedRaceOrName; // keyed by the spritesheet on screen
        Vector2[] vertices = sprite.sprite.vertices;
        if (vertices == null || vertices.Length == 0) return;
        float min = float.MaxValue, max = float.MinValue;
        foreach (Vector2 v in vertices) { min = Mathf.Min(min, v.y); max = Mathf.Max(max, v.y); }
        // Idle frames breathe a little; keep the full extent seen across them.
        if (figureByFolder.TryGetValue(sheet, out Vector2 seen)) { min = Mathf.Min(min, seen.x); max = Mathf.Max(max, seen.y); }
        figureByFolder[sheet] = new Vector2(min, max);
    }

    private void Place(Character3DSettings settings, Character3DSettings.Mode mode)
    {
        MeasureSpriteFigure();
        float h = spriteCellHeight;
        float feetY, targetHeight;
        if (controller.IsShowing && controller.ResolvedRaceOrName != null && figureByFolder.TryGetValue(controller.ResolvedRaceOrName, out Vector2 figure) && figure.y > figure.x)
        {
            // Stand on the sprite's own feet, as tall on screen as the sprite figure. The model is
            // pitched toward the camera, so its on-screen height is ModelHeight · cos(pitch).
            feetY = figure.x;
            targetHeight = (figure.y - figure.x) / Mathf.Cos(settings.pitch * Mathf.Deg2Rad);
        }
        else
        {
            // Not measured yet (first frames, or no idle shown so far): the gold ring marks the feet.
            feetY = goldRing != null ? goldRing.localPosition.y : sprite.sprite.bounds.min.y + 0.15f * h;
            targetHeight = settings.heightFraction * h;
        }
        feetY += settings.feetOffset * h;
        float x = mode == Character3DSettings.Mode.SideBySide ? settings.sideBySideOffset * h : 0f;

        Transform root = model.transform;
        root.localPosition = new Vector3(x, feetY, 0f);
        root.rotation = Character3DLighting.BoardFrame(settings) * Quaternion.Euler(0f, yaw, 0f);
        root.localScale = Vector3.one * (targetHeight * settings.figureScale * ZoomBoost(settings) / model.ModelHeight);
    }

    // Miniatures grow a little as the camera pulls back, so they still read at strategic zoom.
    private static float ZoomBoost(Character3DSettings settings)
    {
        Camera cam = Camera.main;
        if (cam == null || settings.zoomedOutBoost <= 0f) return 1f;
        float t = Mathf.InverseLerp(BoardLook.DefaultZoom, BoardLook.DefaultZoom * 2.5f, cam.orthographicSize);
        return 1f + settings.zoomedOutBoost * Mathf.SmoothStep(0f, 1f, t);
    }

    // A puff of dust at the base when the miniature sets off, and a bigger one when it lands.
    private bool dustMoving;

    private void KickUpDust()
    {
        bool movingNow = moveSpeed > 0.05f;
        if (movingNow == dustMoving) return;
        dustMoving = movingNow;
        AmbientLife.Dust(model.transform.position, model.BaseWorldRadius, landing: !movingNow);
    }

    // A unit standing on a miniature base shows focus on the base itself (its top face lights up,
    // see Character3D.shader's _MINIATURE_BASE), so the flat selection sigil (GoldRing) is not
    // drawn under it. Units without a base keep the sigil.
    private SpriteRenderer goldRingRenderer;
    private bool goldRingHidden;

    // Telling your units from the rest at a glance, without hiding anyone: the human player's own
    // miniatures always have a faint warm light on their base and stand at full brightness; other
    // nations' figures sit a shade dimmer (enemies keep their red outline on top). Hover and
    // selection light the base further and brighten the figure — never a colour tint, and nobody
    // else fades out.
    private const float OwnBaseLight = 0.22f;
    private const float OthersDim = 0.86f;

    private static bool IsMine(Character character) => UnitAnimations.IsHumans(character);

    private void UpdateBaseFocus()
    {
        float focus = controller.FocusAmount;
        model.Focus = Mathf.Max(focus, IsMine(Shown) ? OwnBaseLight : 0f);
        SetGoldRingHidden(model.BaseWorldRadius > 0f);
        AmbientLife.Motes(model.transform.position, model.BaseWorldRadius, focus * model.FigureAlpha);
    }

    private Color FigureTint(Color spriteColor)
    {
        float focus = controller.FocusAmount;
        float brightness = (IsMine(Shown) ? 1f : OthersDim) + 0.16f * focus;
        return new Color(spriteColor.r * brightness, spriteColor.g * brightness, spriteColor.b * brightness, spriteColor.a);
    }

    private void RestoreGoldRing() => SetGoldRingHidden(false);

    private void SetGoldRingHidden(bool hidden)
    {
        if (hidden == goldRingHidden || goldRing == null) return;
        if (goldRingRenderer == null) goldRingRenderer = goldRing.GetComponent<SpriteRenderer>();
        if (goldRingRenderer == null) return;
        goldRingRenderer.forceRenderingOff = hidden;
        goldRingHidden = hidden;
    }

    private void SetSpriteHidden(bool hidden)
    {
        if (sprite != null && (hidden || spriteHidden)) sprite.forceRenderingOff = hidden;
        // The sprite's slanted silhouette shadow (CharacterCastShadow) — the model casts its own.
        if (castShadow == null) castShadow = transform.Find("CastShadow/Scale/Silhouette")?.GetComponent<SpriteRenderer>();
        if (castShadow != null && (hidden || spriteHidden)) castShadow.forceRenderingOff = hidden;
        // Army-size stack copies are flat sprites too; hidden with the main sprite.
        if (controller != null && (hidden || spriteHidden))
            foreach (SpriteRenderer mirror in controller.stackMirrorRenderers)
                if (mirror != null) mirror.forceRenderingOff = hidden;
        spriteHidden = hidden;
    }
}
