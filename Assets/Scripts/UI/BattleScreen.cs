using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The two dice each side rolls in an army battle, and the swing they give its blows.</summary>
public readonly struct BattleDice
{
    public readonly int a1, a2, d1, d2;
    public BattleDice(int a1, int a2, int d1, int d2) { this.a1 = a1; this.a2 = a2; this.d1 = d1; this.d2 = d2; }
    public int AttackerTotal => a1 + a2;
    public int DefenderTotal => d1 + d2;
    public float AttackerSwing => Swing(AttackerTotal);
    public float DefenderSwing => Swing(DefenderTotal);
    /// <summary>2 → ×0.70, 7 → ×1.00, 12 → ×1.30.</summary>
    public static float Swing(int total) => 0.7f + (Mathf.Clamp(total, 2, 12) - 2) * 0.06f;
}

/// <summary>What an army battle came to, for the battle screen.</summary>
public class BattleReport
{
    public BattleDice dice;
    public Army attackerArmy, defenderArmy;
    public int attackerStrength, attackerDefence, defenderStrength, defenderDefence;
    public string attackerLosses, defenderLosses, stalemate;
}

/// <summary>
/// The battle screen. A dark stage fills the screen; the two commanders stand on it as 3D
/// miniatures (rendered by a camera of the screen's own, far from the board), their nation's
/// colours on their bases and plates. Before an army battle the screen opens in a preparing phase
/// (<see cref="PrepareAsync"/>): each side's strength and arms, and for the player's side the
/// chance to re-arm with iron, steel or mithril from the stores, then Fight. The result
/// (<see cref="Play"/>, from CombatBanner's queue) rolls each side's two dice, plays the blows,
/// hits and deaths on the figures and lists the losses.
/// </summary>
public class BattleScreen : MonoBehaviour
{
    public class Fight
    {
        public string title, verb, location;
        public Character attacker, defender;
        public bool attackerWounded, attackerKilled, defenderWounded, defenderKilled;
        public List<string> notices;
        public List<StatusEffectEnum> attackerNewStatus, defenderNewStatus;
        public BattleReport battle;
    }

    private static readonly Vector3 StageOrigin = new(-4000f, -4000f, 0f);
    private static readonly Color Paper = new(0.93f, 0.89f, 0.80f);
    private static readonly Color StageBack = new(0.055f, 0.06f, 0.07f, 1f);

    private static BattleScreen instance;

    /// <summary>Whether a battle is on screen (AutoTour photographs it).</summary>
    public static bool IsShowing => instance != null && instance.canvas != null && instance.canvas.enabled && instance.group.alpha > 0.9f;

    private Canvas canvas;
    private RectTransform root;
    private CanvasGroup group;
    private Camera stageCamera;
    private RenderTexture stageTexture;
    private Transform stage;
    private RawImage stageImage;
    private TextMeshProUGUI title, subtitle, centre, footer;
    private Side left, right;
    private RectTransform diceRow;
    private Button fightButton;
    private TaskCompletionSource<bool> fightPressed;
    // The armies the screen was opened for in the preparing phase: the result of that same
    // battle then plays on in place instead of opening again.
    private Army preparedAttacker, preparedDefender;

    private TextMeshProUGUI verdict;
    private RectTransform noticePanel, diceTray;
    private City3D backdropCity;
    private Vector3 cameraRest;
    private float shake;
    private readonly List<(Transform t, Vector3 velocity, float age, float life, SpriteRenderer sr)> puffs = new();
    private readonly List<(Transform t, float speed, float phase)> motes = new();

    private class Side
    {
        public bool isLeft;
        public RectTransform panel;
        public TextMeshProUGUI name, nation, stats, arms, outcome;
        public Image banner, plate;
        public RectTransform upgrades;
        public Character3DModel model;
        public Character character;
        public RectTransform dice;
    }

    // ── Entry points ────────────────────────────────────────────────────────────────────────

    /// <summary>Before an army battle the player takes part in: the preparing phase, until the
    /// player presses Fight. Computer-led sides re-arm on their own. Returns at once when the
    /// player is not involved (or nobody is at the controls).</summary>
    public static async Task PrepareAsync(Army attacker, Army defender)
    {
        if (attacker == null || defender == null || attacker.commander == null || defender.commander == null) return;
        Game game = Game.Instance;
        PlayableLeader player = game != null ? game.player : null;
        bool attackerMine = attacker.commander.GetOwner() == player;
        bool defenderMine = defender.commander.GetOwner() == player;
        if (!attackerMine) ArmyEquipment.UpgradeForAi(attacker);
        if (!defenderMine) ArmyEquipment.UpgradeForAi(defender);
        if (!attackerMine && !defenderMine) return;
        // Nobody at the controls (autoplay, the tour): the player's side re-arms like the AI.
        if (player == null || game.IsPlayerAutoplayEnabledFor(player) || AutoTour.Active)
        {
            if (attackerMine) ArmyEquipment.UpgradeForAi(attacker);
            if (defenderMine) ArmyEquipment.UpgradeForAi(defender);
            return;
        }
        BattleScreen screen = Ensure();
        if (screen == null) return;
        await screen.Prepare(attacker, defender, attackerMine, defenderMine);
    }

    /// <summary>The result of a fight (CombatBanner's queue holds the centre-screen lock).</summary>
    public static IEnumerator Play(Fight fight)
    {
        BattleScreen screen = Ensure();
        if (screen == null || fight?.attacker == null || fight.defender == null) yield break;
        yield return screen.PlayResult(fight);
    }

    private static BattleScreen Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("BattleScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        instance = go.AddComponent<BattleScreen>();
        instance.Build();
        return instance;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (stageTexture != null) stageTexture.Release();
        if (stage != null) Destroy(stage.gameObject);
    }

    // ── Phases ──────────────────────────────────────────────────────────────────────────────

    private async Task Prepare(Army attacker, Army defender, bool attackerMine, bool defenderMine)
    {
        preparedAttacker = attacker;
        preparedDefender = defender;
        Open(attacker.commander, defender.commander, "Battle", "marches on", attacker.commander.hex?.GetBattleLocationLabel());
        centre.text = $"<color={UiTheme.MutedHex}>Arm your host, then fight.\nEach side rolls two dice: their blows land from ×0.70 to ×1.30.</color>";
        SizeCards(true);
        RefreshArmies(attacker, defender, attackerMine, defenderMine);
        fightButton.gameObject.SetActive(true);
        fightPressed = new TaskCompletionSource<bool>();
        await fightPressed.Task;
        fightButton.gameObject.SetActive(false);
        ClearUpgrades(left);
        ClearUpgrades(right);
        centre.text = string.Empty;
    }

    private IEnumerator PlayResult(Fight fight)
    {
        BattleReport battle = fight.battle;
        bool continuing = group.alpha > 0.5f && battle != null && battle.attackerArmy == preparedAttacker && battle.defenderArmy == preparedDefender;
        preparedAttacker = preparedDefender = null;
        if (!continuing) Open(fight.attacker, fight.defender, fight.title, fight.verb, fight.location);
        if (battle != null) ShowArmies(battle);
        else { left.stats.text = right.stats.text = left.arms.text = right.arms.text = string.Empty; }
        SizeCards(battle != null || preparedAttacker != null);
        bool tray = battle != null;
        verdict.rectTransform.anchoredPosition = new Vector2(0f, tray ? -400f : -330f);
        noticePanel.anchoredPosition = new Vector2(0f, tray ? -452f : -390f);

        // What comes into it: abilities, fortifications, allies, command.
        if (fight.notices != null && fight.notices.Count > 0)
        {
            SetNotice(string.Join("\n", fight.notices.Take(6)));
            yield return Wait(Mathf.Min(3.2f, 1f + 0.45f * fight.notices.Count));
        }
        else yield return Wait(0.5f);

        // The dice.
        if (battle != null)
        {
            SetNotice(string.Empty);
            diceTray.gameObject.SetActive(true);
            yield return RollDice(battle.dice);
            yield return Wait(0.9f);
        }

        // The blows.
        PlayMove(left, UnitAnimations.Move.Attack);
        yield return Wait(0.35f);
        yield return Wait(0.25f);
        PlayMove(right, fight.defenderKilled ? UnitAnimations.Move.Death
            : fight.defenderWounded ? UnitAnimations.Move.HitLarge : UnitAnimations.Move.Block);
        Impact(right, fight.defenderWounded || fight.defenderKilled);
        yield return Wait(0.6f);
        if (!fight.defenderKilled && fight.defenderWounded)
        {
            PlayMove(right, UnitAnimations.Move.Attack);
            yield return Wait(0.35f);
        }
        if (fight.attackerKilled) PlayMove(left, UnitAnimations.Move.Death);
        else if (fight.attackerWounded) PlayMove(left, UnitAnimations.Move.Hit);
        if (fight.attackerKilled || fight.attackerWounded) Impact(left, true);
        yield return Wait(0.8f);

        // The outcome.
        left.outcome.text = Outcome(fight.attackerKilled, battle?.attackerLosses, fight.attackerNewStatus);
        right.outcome.text = Outcome(fight.defenderKilled, battle?.defenderLosses, fight.defenderNewStatus);
        verdict.text = Verdict(fight);
        if (!string.IsNullOrEmpty(battle?.stalemate)) SetNotice($"<color={UiTheme.MutedHex}>{battle.stalemate}</color>");
        footer.text = $"<color={UiTheme.MutedHex}>Click to continue</color>";
        float until = Time.unscaledTime + 3.2f;
        while (Time.unscaledTime < until && !Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Space) && !Input.GetKeyDown(KeyCode.Escape))
            yield return null;
        yield return Close();
    }

    // Who carried the field, in a line.
    private static string Verdict(Fight fight)
    {
        string a = DisplayNames.Format(fight.attacker.characterName), d = DisplayNames.Format(fight.defender.characterName);
        if (fight.defenderKilled && !fight.attackerKilled) return $"{a} carries the field";
        if (fight.attackerKilled && !fight.defenderKilled) return $"{d} holds the field";
        if (fight.attackerKilled && fight.defenderKilled) return "Both fall";
        if (fight.defenderWounded && !fight.attackerWounded) return $"{a} has the better of it";
        if (fight.attackerWounded && !fight.defenderWounded) return $"{d} has the better of it";
        return "Neither side breaks";
    }

    private const float CardWidth = 440f;

    // Army battles fill the cards with the numbers and arms; a duel card shows the fighter's
    // craft and health. Either way the card is only as tall as what it says.
    private void SizeCards(bool armies)
    {
        foreach (Side side in new[] { left, right })
        {
            float height = armies ? 264f : 132f;
            float top = height * 0.5f;
            side.panel.sizeDelta = new Vector2(CardWidth, height);
            side.panel.anchoredPosition = new Vector2(side.panel.anchoredPosition.x, -470f + height * 0.5f);
            float inset = side.isLeft ? 96f : -96f;
            side.name.rectTransform.anchoredPosition = new Vector2(inset, top - 36f);
            side.nation.rectTransform.anchoredPosition = new Vector2(inset, top - 64f);
            side.stats.rectTransform.anchoredPosition = new Vector2(inset, top - 100f);
            side.arms.rectTransform.anchoredPosition = new Vector2(inset, top - 160f);
            side.outcome.rectTransform.anchoredPosition = new Vector2(0f, -top + 12f);
            side.outcome.rectTransform.sizeDelta = new Vector2(CardWidth - 40f, armies ? 58f : 50f);
            if (!armies && side.character != null) side.stats.text = Craft(side.character);
        }
    }

    // A duellist's craft and health in one line of icons.
    private static string Craft(Character c)
    {
        var parts = new List<string>();
        void Add(string sprite, int level) { if (level > 0) parts.Add($"<sprite name=\"{sprite}\"> <color={UiTheme.GoldHex}>{level}</color>"); }
        Add("commander", c.GetCommander());
        Add("agent", c.GetAgent());
        Add("emmissary", c.GetEmmissary());
        Add("mage", c.GetMage());
        parts.Add($"<sprite name=\"health\"> <color={(c.health >= 50 ? UiTheme.GoodHex : "#E06A55")}>{Mathf.Max(0, c.health)}%</color>");
        return string.Join("    ", parts);
    }

    private void SetNotice(string text)
    {
        centre.text = text;
        noticePanel.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    private static string Outcome(bool killed, string losses, List<StatusEffectEnum> newStatus)
    {
        var lines = new List<string>();
        if (killed) lines.Add("<color=#E06A55>Fallen</color>");
        if (!string.IsNullOrEmpty(losses)) lines.Add($"<color=#E06A55>Losses:</color> {losses}");
        else if (!killed) lines.Add($"<color={UiTheme.GoodHex}>No losses</color>");
        if (newStatus != null && newStatus.Count > 0) lines.Add($"<color={UiTheme.MutedHex}>{string.Join(", ", newStatus)}</color>");
        return string.Join("\n", lines);
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
    }

    // ── Opening and closing ─────────────────────────────────────────────────────────────────

    private void Open(Character attacker, Character defender, string heading, string verb, string location)
    {
        canvas.enabled = true;
        stageCamera.enabled = true;
        group.alpha = 1f;
        group.blocksRaycasts = true;
        title.text = string.IsNullOrWhiteSpace(location) ? heading : $"{heading} at {location}";
        subtitle.text = $"{DisplayNames.Format(attacker.characterName)} {verb} {DisplayNames.Format(defender.characterName)}";
        footer.text = verdict.text = string.Empty;
        SetNotice(string.Empty);
        diceTray.gameObject.SetActive(false);
        SetSide(left, attacker);
        SetSide(right, defender);
        Hex where = defender.hex ?? attacker.hex;
        SetTerrain(where != null ? where.terrainType : TerrainEnum.grasslands);
        SetBackdropCity(where);
        foreach (Transform child in diceRow) Destroy(child.gameObject);
        stageCamera.transform.localPosition = cameraRest;
    }

    private IEnumerator Close()
    {
        float t = 0f;
        while (t < 0.25f)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = 1f - t / 0.25f;
            yield return null;
        }
        group.alpha = 0f;
        group.blocksRaycasts = false;
        canvas.enabled = false;
        stageCamera.enabled = false;
        if (backdropCity != null) Destroy(backdropCity.gameObject);
        backdropCity = null;
        foreach (Side side in new[] { left, right })
        {
            if (side.model != null) Destroy(side.model.gameObject);
            side.model = null;
            side.character = null;
        }
    }

    // ── Sides ───────────────────────────────────────────────────────────────────────────────

    private void SetSide(Side side, Character character)
    {
        Leader owner = character.GetOwner();
        Color nation = owner != null ? owner.nationColor : new Color(0.6f, 0.6f, 0.6f);
        side.name.text = DisplayNames.Format(character.characterName);
        side.nation.text = owner != null && owner != character ? DisplayNames.Format(owner.characterName) : string.Empty;
        side.plate.color = new Color(nation.r, nation.g, nation.b, 0.9f);
        Sprite banner = CombatBanner.ResolveBannerSprite(owner);
        side.banner.sprite = banner;
        side.banner.enabled = banner != null;
        side.stats.text = side.arms.text = side.outcome.text = string.Empty;
        ClearUpgrades(side);
        if (side.character != character)
        {
            if (side.model != null) Destroy(side.model.gameObject);
            side.model = null;
            side.character = character;
            StartCoroutine(Stand(side, character, nation));
        }
    }

    // The commander's 3D figure on the stage (loaded on demand, like the board's).
    private IEnumerator Stand(Side side, Character character, Color nation)
    {
        if (!Character3DModel.TryResolve(character, out string folder)) yield break;
        GameObject prefab = null;
        float deadline = Time.realtimeSinceStartup + 5f;
        while ((prefab = Character3DModel.RequestPrefab(folder)) == null && !Character3DModel.IsMissing(folder) && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (prefab == null || side.character != character) yield break;
        Character3DModel model = Character3DModel.Create(prefab, folder, stage);
        side.model = model;
        Transform t = model.transform;
        t.localScale = Vector3.one * (1.9f / Mathf.Max(0.01f, model.ModelHeight));
        t.position = StageOrigin + new Vector3(side.isLeft ? -1.25f : 1.25f, -0.85f, 0f);
        // Facing each other, turned a little toward the camera.
        t.rotation = Quaternion.Euler(0f, side.isLeft ? 115f : 245f, 0f);
        model.Animation.IdleVariety = false;
        model.Animation.SetBaseLoop("RB Ready Idle", 0f);
        model.Arm(Character3DWeapons.For(character));
        model.SetBase(Color.Lerp(nation, new Color(0.3f, 0.24f, 0.17f), 0.45f), 0.32f);
    }

    private static void PlayMove(Side side, UnitAnimations.Move move)
    {
        if (side.model == null) return;
        string clip = UnitAnimations.Pick(move);
        if (clip == null) return;
        if (move == UnitAnimations.Move.Attack)
        {
            side.model.Swing();
            return;
        }
        bool hold = move == UnitAnimations.Move.Death;
        if (hold) side.model.Fallen = true;
        side.model.Animation.PlayOnce(clip, 0.12f, 1f, hold);
    }

    private void RefreshArmies(Army attacker, Army defender, bool attackerMine, bool defenderMine)
    {
        ShowArmy(left, attacker, attacker.GetStrengthAgainst(defender), attacker.GetDefenceAgainst(defender));
        ShowArmy(right, defender, defender.GetStrengthAgainst(attacker), defender.GetDefenceAgainst(attacker));
        ClearUpgrades(left);
        ClearUpgrades(right);
        if (attackerMine) AddUpgrades(left, attacker, () => RefreshArmies(attacker, defender, attackerMine, defenderMine));
        if (defenderMine) AddUpgrades(right, defender, () => RefreshArmies(attacker, defender, attackerMine, defenderMine));
    }

    private void ShowArmies(BattleReport battle)
    {
        ShowArmy(left, battle.attackerArmy, battle.attackerStrength, battle.attackerDefence);
        ShowArmy(right, battle.defenderArmy, battle.defenderStrength, battle.defenderDefence);
    }

    private static void ShowArmy(Side side, Army army, int strength, int defence)
    {
        side.stats.text = $"<sprite name=\"sword\"> <size=140%><color={UiTheme.GoldHex}>{strength}</color></size>   <size=70%><color={UiTheme.MutedHex}>ATTACK</color></size>        <sprite name=\"shield\"> <size=140%><color={UiTheme.GoldHex}>{defence}</color></size>   <size=70%><color={UiTheme.MutedHex}>DEFENCE</color></size>";
        if (army == null) { side.arms.text = string.Empty; return; }
        int troops = ArmyEquipment.Troops(army);
        side.arms.text = $"<color={UiTheme.MutedHex}>{troops} troops</color>\n{Arms(true, army.weaponTier)}\n{Arms(false, army.armourTier)}";
    }

    private static string Arms(bool weapons, int tier)
    {
        string name = weapons ? ArmyEquipment.WeaponNames[tier] : ArmyEquipment.ArmourNames[tier];
        string icon = tier > 0 ? $"<sprite name=\"{ArmyEquipment.Metals[tier]}\"> " : string.Empty;
        return $"{icon}{name} <color={UiTheme.MutedHex}><size=85%>({ArmyEquipment.Bonus(tier)} {(weapons ? "attack" : "defence")})</size></color>";
    }

    // The re-arming buttons for the player's side: each metal they could afford for weapons and
    // for armour, with what it costs.
    private void AddUpgrades(Side side, Army army, System.Action changed)
    {
        Leader owner = army.commander.GetOwner();
        int troops = ArmyEquipment.Troops(army);
        int row = 0;
        foreach (bool weapons in new[] { true, false })
        {
            for (int tier = ArmyEquipment.CurrentTier(army, weapons) + 1; tier <= ArmyEquipment.MaxTier; tier++)
            {
                bool affordable = ArmyEquipment.CanUpgrade(army, weapons, tier);
                int stock = ArmyEquipment.Stock(owner, tier);
                string metal = ArmyEquipment.Metals[tier];
                string label = $"<sprite name=\"{metal}\"> {char.ToUpperInvariant(metal[0])}{metal.Substring(1)} {(weapons ? "weapons" : "armour")}  <size=80%><color={(affordable ? UiTheme.TextHex : UiTheme.MutedHex)}>{troops}/{stock}</color></size>";
                int capturedTier = tier;
                bool capturedWeapons = weapons;
                AddButton(side.upgrades, label, new Vector2(0f, row * 36f), new Vector2(320f, 32f), 14f, affordable, () =>
                {
                    if (ArmyEquipment.Upgrade(army, capturedWeapons, capturedTier))
                    {
                        Sounds.Instance?.PlayUiClick();
                        changed();
                    }
                });
                row++;
            }
        }
        if (row == 0)
        {
            TextMeshProUGUI note = NewText(side.upgrades, "Note", 13f, TextAlignmentOptions.Top);
            note.text = $"<color={UiTheme.MutedHex}>Best arms already</color>";
            Place(note.rectTransform, new Vector2(0f, 0f), new Vector2(300f, 24f));
        }
    }

    private static void ClearUpgrades(Side side)
    {
        if (side?.upgrades == null) return;
        foreach (Transform child in side.upgrades) Destroy(child.gameObject);
    }

    // ── Dice ────────────────────────────────────────────────────────────────────────────────

    private IEnumerator RollDice(BattleDice dice)
    {
        foreach (Transform child in diceRow) Destroy(child.gameObject);
        Die[] mine = { NewDie(left, -46f), NewDie(left, 46f), NewDie(right, -46f), NewDie(right, 46f) };
        int[] faces = { dice.a1, dice.a2, dice.d1, dice.d2 };
        Sounds.Instance?.PlayUiClick();
        float t = 0f;
        const float tumble = 1.1f;
        while (t < tumble)
        {
            t += Time.unscaledDeltaTime;
            float p = t / tumble;
            for (int i = 0; i < mine.Length; i++)
            {
                // Faces flicker fast, then slow and settle; the die spins down to rest.
                if (Random.value < Mathf.Lerp(0.9f, 0.1f, p)) mine[i].Show(Random.Range(1, 7));
                mine[i].rect.localEulerAngles = new Vector3(0f, 0f, (1f - p) * (1f - p) * 540f * (i % 2 == 0 ? 1f : -1f));
                mine[i].rect.anchoredPosition = mine[i].rest + new Vector2(0f, Mathf.Abs(Mathf.Sin(p * Mathf.PI * 3f)) * 26f * (1f - p));
            }
            yield return null;
        }
        for (int i = 0; i < mine.Length; i++)
        {
            mine[i].Show(faces[i]);
            mine[i].rect.localEulerAngles = Vector3.zero;
            mine[i].rect.anchoredPosition = mine[i].rest;
        }
        left.dice = SwingLabel(left, dice.AttackerTotal, dice.AttackerSwing);
        right.dice = SwingLabel(right, dice.DefenderTotal, dice.DefenderSwing);
    }

    private class Die
    {
        public RectTransform rect;
        public Vector2 rest;
        public Image[] pips;
        private static readonly int[][] Layout =
        {
            new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 }, new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 }, new[] { 0, 2, 3, 5, 6, 8 },
        };

        public void Show(int face)
        {
            int[] on = Layout[Mathf.Clamp(face, 1, 6) - 1];
            for (int i = 0; i < pips.Length; i++) pips[i].enabled = System.Array.IndexOf(on, i) >= 0;
        }
    }

    private Die NewDie(Side side, float offset)
    {
        var face = new GameObject("Die", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = face.GetComponent<RectTransform>();
        rt.SetParent(diceRow, false);
        rt.sizeDelta = new Vector2(64f, 64f);
        var die = new Die { rect = rt, rest = new Vector2((side.isLeft ? -130f : 130f) + offset * 0.85f, 6f), pips = new Image[9] };
        rt.anchoredPosition = die.rest;
        var img = face.GetComponent<Image>();
        img.sprite = RoundedSquare();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.93f, 0.89f, 0.80f);
        for (int i = 0; i < 9; i++)
        {
            var pip = new GameObject("Pip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var pr = pip.GetComponent<RectTransform>();
            pr.SetParent(rt, false);
            pr.sizeDelta = new Vector2(11f, 11f);
            pr.anchoredPosition = new Vector2((i % 3 - 1) * 17f, (1 - i / 3) * 17f);
            var pi = pip.GetComponent<Image>();
            pi.sprite = HudSprites.Circle;
            pi.color = new Color(0.16f, 0.1f, 0.06f);
            die.pips[i] = pi;
        }
        return die;
    }

    private RectTransform SwingLabel(Side side, int total, float swing)
    {
        TextMeshProUGUI label = NewText(diceRow, "Swing", 18f, TextAlignmentOptions.Center);
        string colour = swing >= 1.0001f ? UiTheme.GoodHex : swing <= 0.9999f ? "#E06A55" : UiTheme.TextHex;
        label.text = $"<color={UiTheme.MutedHex}>rolled</color> {total}   <color={colour}>×{swing:0.00}</color>";
        Place(label.rectTransform, new Vector2(side.isLeft ? -130f : 130f, -50f), new Vector2(240f, 28f));
        return label.rectTransform;
    }

    // ── Building ────────────────────────────────────────────────────────────────────────────

    private void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // over the whole HUD (the old banner sat at 32767)
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        root = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        canvas.enabled = false;

        // Opaque: nothing of the board or the HUD shows through.
        Image back = NewImage(root, "Backdrop", null, StageBack);
        Stretch(back.rectTransform);
        back.raycastTarget = true;
        Image vignette = NewImage(root, "Vignette", HudSprites.FadeUp, new Color(0f, 0f, 0f, 0.6f));
        vignette.rectTransform.anchorMin = Vector2.zero;
        vignette.rectTransform.anchorMax = new Vector2(1f, 0.35f);
        vignette.rectTransform.offsetMin = vignette.rectTransform.offsetMax = Vector2.zero;

        BuildStage();
        stageImage = new GameObject("Stage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        stageImage.rectTransform.SetParent(root, false);
        stageImage.texture = stageTexture;
        stageImage.raycastTarget = false;
        Stretch(stageImage.rectTransform);
        // Washes behind the title and the two side columns, so the text reads over the scene.
        Image top = NewImage(root, "TopWash", HudSprites.FadeDown, new Color(0f, 0f, 0f, 0.75f));
        top.rectTransform.anchorMin = new Vector2(0f, 0.8f);
        top.rectTransform.anchorMax = Vector2.one;
        top.rectTransform.offsetMin = top.rectTransform.offsetMax = Vector2.zero;
        Image bottom = NewImage(root, "BottomWash", HudSprites.FadeUp, new Color(0f, 0f, 0f, 0.8f));
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = new Vector2(1f, 0.4f);
        bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;

        title = NewText(root, "Title", 46f, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(title, 46f, UiTheme.Gold);
        title.fontSharedMaterial = UiTheme.PlainMaterial(title.font);
        Place(title.rectTransform, new Vector2(0f, 470f), new Vector2(1400f, 64f));
        Image rule = NewImage(root, "Rule", null, new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, 0.7f));
        Place(rule.rectTransform, new Vector2(0f, 436f), new Vector2(520f, 1.5f));
        subtitle = NewText(root, "Subtitle", 21f, TextAlignmentOptions.Center);
        subtitle.color = Paper;
        Place(subtitle.rectTransform, new Vector2(0f, 414f), new Vector2(1400f, 32f));

        // The centre column at the bottom, between the two cards: what comes into the fight
        // (abilities, forts, allies, command) as plain lines, then the verdict over them. The
        // fighters above are left clear.
        noticePanel = new GameObject("Notices", typeof(RectTransform)).GetComponent<RectTransform>();
        noticePanel.SetParent(root, false);
        Place(noticePanel, new Vector2(0f, -440f), new Vector2(700f, 96f));
        centre = NewText(noticePanel, "Text", 17f, TextAlignmentOptions.Top);
        centre.color = Paper;
        centre.lineSpacing = 4f;
        Stretch(centre.rectTransform);

        verdict = NewText(root, "Verdict", 44f, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(verdict, 44f, UiTheme.Gold);
        verdict.fontSharedMaterial = UiTheme.PlainMaterial(verdict.font);
        verdict.enableAutoSizing = true;
        verdict.fontSizeMin = 28f;
        verdict.fontSizeMax = 44f;
        Place(verdict.rectTransform, new Vector2(0f, -385f), new Vector2(760f, 60f));

        // The dice tray, between the two cards.
        diceTray = Panel(root, "DiceTray", new Vector2(0f, -290f), new Vector2(560f, 150f));
        TextMeshProUGUI trayTitle = NewText(diceTray, "Heading", 13f, TextAlignmentOptions.Center);
        trayTitle.text = UiTheme.Eyebrow("The dice");
        Place(trayTitle.rectTransform, new Vector2(0f, 56f), new Vector2(400f, 22f));
        diceRow = new GameObject("Dice", typeof(RectTransform)).GetComponent<RectTransform>();
        diceRow.SetParent(diceTray, false);
        Place(diceRow, new Vector2(0f, 0f), new Vector2(10f, 10f));

        left = BuildSide(true);
        right = BuildSide(false);

        fightButton = AddButton(root, "Fight", new Vector2(0f, -360f), new Vector2(240f, 58f), 28f, true, () => fightPressed?.TrySetResult(true));
        fightButton.gameObject.SetActive(false);
        footer = NewText(root, "Footer", 14f, TextAlignmentOptions.Center);
        Place(footer.rectTransform, new Vector2(0f, -505f), new Vector2(600f, 24f));
    }

    private static RectTransform Panel(RectTransform parent, string name, Vector2 at, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Place(rt, at, size);
        var panel = go.GetComponent<CinematicPanel>();
        panel.fillTop = new Color(0.07f, 0.06f, 0.05f, 0.88f);
        panel.fillBottom = new Color(0.025f, 0.022f, 0.02f, 0.92f);
        panel.raycastTarget = false;
        return rt;
    }

    // A side's card in its lower corner: the nation's stripe and banner, the name, the numbers,
    // the arms, and at the end what the fight cost; the re-arming buttons stand above it.
    private Side BuildSide(bool isLeft)
    {
        float x = isLeft ? -700f : 700f;
        var side = new Side { isLeft = isLeft };
        side.panel = Panel(root, isLeft ? "Attacker" : "Defender", new Vector2(x, -330f), new Vector2(CardWidth, 250f));

        side.plate = NewImage(side.panel, "Stripe", null, Color.white);
        side.plate.rectTransform.anchorMin = new Vector2(0f, 1f);
        side.plate.rectTransform.anchorMax = new Vector2(1f, 1f);
        side.plate.rectTransform.pivot = new Vector2(0.5f, 1f);
        side.plate.rectTransform.offsetMin = new Vector2(3f, -6f);
        side.plate.rectTransform.offsetMax = new Vector2(-3f, -3f);

        // The banner hangs from the card's top at its outer edge, half above it.
        side.banner = NewImage(side.panel, "Banner", null, Color.white);
        side.banner.preserveAspect = true;
        side.banner.rectTransform.anchorMin = side.banner.rectTransform.anchorMax = new Vector2(isLeft ? 0f : 1f, 1f);
        side.banner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        side.banner.rectTransform.sizeDelta = new Vector2(64f, 108f);
        side.banner.rectTransform.anchoredPosition = new Vector2(isLeft ? 50f : -50f, 0f);

        float textX = isLeft ? 100f : -100f;
        side.name = NewText(side.panel, "Name", 32f, isLeft ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        UiTheme.StyleTitle(side.name, 32f, Paper);
        side.name.fontSharedMaterial = UiTheme.PlainMaterial(side.name.font);
        Place(side.name.rectTransform, new Vector2(textX, 88f), new Vector2(CardWidth - 160f, 44f));
        side.nation = NewText(side.panel, "Nation", 14f, isLeft ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        side.nation.color = UiTheme.Muted;
        Place(side.nation.rectTransform, new Vector2(textX, 56f), new Vector2(CardWidth - 160f, 22f));

        side.stats = NewText(side.panel, "Stats", 19f, isLeft ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        side.stats.color = Paper;
        Place(side.stats.rectTransform, new Vector2(textX, 14f), new Vector2(CardWidth - 160f, 40f));
        side.arms = NewText(side.panel, "Arms", 14f, isLeft ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight);
        side.arms.color = Paper;
        Place(side.arms.rectTransform, new Vector2(textX, -42f), new Vector2(CardWidth - 160f, 66f));
        side.outcome = NewText(side.panel, "Outcome", 17f, TextAlignmentOptions.Center);
        side.outcome.color = Paper;
        Place(side.outcome.rectTransform, new Vector2(0f, -100f), new Vector2(CardWidth - 40f, 44f));
        side.outcome.enableAutoSizing = true;
        side.outcome.fontSizeMin = 12f;
        side.outcome.fontSizeMax = 17f;
        side.outcome.rectTransform.pivot = new Vector2(0.5f, 0f);
        side.outcome.alignment = TextAlignmentOptions.Bottom;

        side.upgrades = new GameObject("Upgrades", typeof(RectTransform)).GetComponent<RectTransform>();
        side.upgrades.SetParent(root, false);
        Place(side.upgrades, new Vector2(x, -175f), new Vector2(300f, 10f));
        foreach (TextMeshProUGUI t in new[] { side.name, side.nation, side.stats, side.arms })
        {
            RectTransform r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(isLeft ? 0f : 1f, 0.5f);
            r.pivot = new Vector2(isLeft ? 0f : 1f, 0.5f);
        }
        return side;
    }

    private void BuildStage()
    {
        stage = new GameObject("BattleStage").transform;
        stage.position = StageOrigin;
        stageTexture = new RenderTexture(1920, 1080, 24) { name = "BattleStage", antiAliasing = 4 };
        var camGo = new GameObject("BattleStageCamera");
        camGo.transform.SetParent(stage, false);
        stageCamera = camGo.AddComponent<Camera>();
        stageCamera.targetTexture = stageTexture;
        stageCamera.clearFlags = CameraClearFlags.SolidColor;
        stageCamera.backgroundColor = StageBack;
        stageCamera.orthographic = false;
        stageCamera.fieldOfView = 34f;
        stageCamera.nearClipPlane = 0.5f;
        stageCamera.farClipPlane = 160f;
        // A little above the figures, looking slightly down onto the field.
        camGo.transform.position = StageOrigin + new Vector3(0f, 0.45f, -6.0f);
        camGo.transform.rotation = Quaternion.Euler(7f, 0f, 0f);
        cameraRest = camGo.transform.localPosition;
        stageCamera.enabled = false;
        BuildScenery();
    }

    // ── Scenery ─────────────────────────────────────────────────────────────────────────────
    // A field to fight on: ground the colour of the battle's terrain, running back to a horizon
    // haze, two ranges of hills, and an evening sky. All flat sprites, drawn behind the figures.

    private SpriteRenderer ground, hillsNear, hillsFar, sky;

    private SpriteRenderer hillsMid, sun, haze;
    private readonly List<SpriteRenderer> trees = new();

    private void BuildScenery()
    {
        sky = Layer("Sky", Gradient("BattleSky", new Color(0.86f, 0.56f, 0.34f), new Color(0.07f, 0.09f, 0.17f)), -112);
        // The gradient sprite is 32 units tall: from the horizon (y -1) to the top of the view.
        sky.transform.localPosition = new Vector3(0f, 31f, 100f);
        sky.transform.localScale = new Vector3(260f, 2f, 1f);

        sun = Layer("Sun", Glow(), -111);
        sun.transform.localPosition = new Vector3(6f, 6f, 95f);
        sun.transform.localScale = Vector3.one * 26f;
        sun.color = new Color(1f, 0.78f, 0.5f, 0.55f);

        hillsFar = Layer("FarHills", Ridge("BattleFarHills", 0.75f, 3, 0.25f), -110);
        hillsFar.transform.localPosition = new Vector3(0f, -0.9f, 70f);
        hillsFar.transform.localScale = new Vector3(200f, 35f, 1f);

        hillsMid = Layer("MidHills", Ridge("BattleMidHills", 0.55f, 11, 0.4f), -109);
        hillsMid.transform.localPosition = new Vector3(0f, -0.9f, 55f);
        hillsMid.transform.localScale = new Vector3(170f, 24f, 1f);

        hillsNear = Layer("NearHills", Ridge("BattleNearHills", 0.4f, 7, 0.55f), -108);
        hillsNear.transform.localPosition = new Vector3(0f, -0.9f, 42f);
        hillsNear.transform.localScale = new Vector3(140f, 15f, 1f);

        // A tree line for wooded ground, behind the field.
        Sprite tree = TreeSprite();
        var rng = new System.Random(7);
        for (int i = 0; i < 46; i++)
        {
            SpriteRenderer t = Layer("Tree", tree, -107);
            float z = 18f + (float)rng.NextDouble() * 9f;
            t.transform.localPosition = new Vector3(-26f + i * 1.15f + (float)rng.NextDouble() * 0.6f, -0.86f, z);
            float h = 2.2f + (float)rng.NextDouble() * 1.8f;
            t.transform.localScale = new Vector3(h * 0.55f, h, 1f);
            trees.Add(t);
        }

        ground = Layer("Ground", Noise("BattleGround"), -106);
        ground.transform.localPosition = new Vector3(0f, -0.86f, 40f);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(220f, 100f, 1f);

        haze = Layer("Haze", Gradient("BattleHaze", new Color(1f, 1f, 1f, 0f), Color.white), -105);
        // A low band of haze over the horizon (it was hundreds of units tall and tinted the whole view).
        haze.transform.localPosition = new Vector3(0f, 3.5f, 75f);
        haze.transform.localScale = new Vector3(200f, 0.28f, 1f);

        // Dust drifting in the air between the fighters and the hills.
        Sprite mote = Glow();
        for (int i = 0; i < 40; i++)
        {
            SpriteRenderer m = Layer("Mote", mote, -103);
            m.transform.localPosition = new Vector3(((float)rng.NextDouble() - 0.5f) * 14f, -0.6f + (float)rng.NextDouble() * 3f, 1f + (float)rng.NextDouble() * 10f);
            m.transform.localScale = Vector3.one * (0.03f + (float)rng.NextDouble() * 0.05f);
            m.color = new Color(1f, 0.85f, 0.6f, 0.35f);
            motes.Add((m.transform, 0.05f + (float)rng.NextDouble() * 0.1f, (float)rng.NextDouble() * 6f));
        }
    }

    private void SetTerrain(TerrainEnum terrain)
    {
        (Color earth, Color hills) = terrain switch
        {
            TerrainEnum.forest => (new Color(0.2f, 0.27f, 0.13f), new Color(0.1f, 0.16f, 0.1f)),
            TerrainEnum.hills => (new Color(0.4f, 0.37f, 0.22f), new Color(0.26f, 0.25f, 0.18f)),
            TerrainEnum.mountains => (new Color(0.36f, 0.35f, 0.33f), new Color(0.3f, 0.31f, 0.36f)),
            TerrainEnum.snow => (new Color(0.82f, 0.84f, 0.88f), new Color(0.6f, 0.66f, 0.76f)),
            TerrainEnum.desert => (new Color(0.7f, 0.56f, 0.34f), new Color(0.55f, 0.4f, 0.26f)),
            TerrainEnum.wastelands => (new Color(0.32f, 0.25f, 0.2f), new Color(0.22f, 0.14f, 0.12f)),
            TerrainEnum.swamp => (new Color(0.22f, 0.26f, 0.18f), new Color(0.14f, 0.18f, 0.14f)),
            TerrainEnum.shore or TerrainEnum.shallowWater or TerrainEnum.deepWater => (new Color(0.52f, 0.47f, 0.34f), new Color(0.22f, 0.3f, 0.36f)),
            TerrainEnum.plains => (new Color(0.45f, 0.43f, 0.22f), new Color(0.26f, 0.3f, 0.18f)),
            _ => (new Color(0.3f, 0.4f, 0.18f), new Color(0.16f, 0.24f, 0.13f)),
        };
        bool mountains = terrain is TerrainEnum.mountains or TerrainEnum.snow;
        ground.color = earth;
        hillsNear.color = hills;
        hillsMid.color = Color.Lerp(hills, new Color(0.42f, 0.38f, 0.42f), 0.35f);
        hillsFar.color = Color.Lerp(hills, new Color(0.5f, 0.45f, 0.5f), 0.6f);
        // Mountains stand tall behind mountain fights; a tree line stands behind forest ones.
        hillsFar.transform.localScale = new Vector3(200f, mountains ? 75f : 35f, 1f);
        hillsMid.transform.localScale = new Vector3(170f, mountains ? 45f : 24f, 1f);
        bool wooded = terrain is TerrainEnum.forest or TerrainEnum.swamp;
        Color treeColour = Color.Lerp(hills, Color.black, 0.25f);
        foreach (SpriteRenderer t in trees) { t.enabled = wooded; t.color = treeColour; }
        (Color horizon, Color zenith, Color glow) = terrain switch
        {
            TerrainEnum.snow or TerrainEnum.mountains => (new Color(0.78f, 0.82f, 0.88f), new Color(0.24f, 0.32f, 0.45f), new Color(1f, 0.97f, 0.9f, 0.35f)),
            TerrainEnum.desert => (new Color(0.96f, 0.86f, 0.62f), new Color(0.36f, 0.55f, 0.78f), new Color(1f, 0.92f, 0.7f, 0.6f)),
            TerrainEnum.wastelands => (new Color(0.62f, 0.25f, 0.14f), new Color(0.08f, 0.05f, 0.06f), new Color(1f, 0.4f, 0.15f, 0.5f)),
            TerrainEnum.swamp => (new Color(0.62f, 0.66f, 0.58f), new Color(0.2f, 0.25f, 0.24f), new Color(0.9f, 0.95f, 0.8f, 0.2f)),
            TerrainEnum.forest => (new Color(0.74f, 0.82f, 0.74f), new Color(0.22f, 0.36f, 0.5f), new Color(1f, 0.95f, 0.8f, 0.35f)),
            TerrainEnum.shore or TerrainEnum.shallowWater or TerrainEnum.deepWater => (new Color(0.8f, 0.86f, 0.9f), new Color(0.22f, 0.42f, 0.66f), new Color(1f, 0.95f, 0.85f, 0.45f)),
            TerrainEnum.hills => (new Color(0.9f, 0.78f, 0.62f), new Color(0.26f, 0.38f, 0.6f), new Color(1f, 0.85f, 0.6f, 0.5f)),
            _ => (new Color(0.84f, 0.88f, 0.86f), new Color(0.26f, 0.45f, 0.72f), new Color(1f, 0.95f, 0.82f, 0.45f)),
        };
        sky.sprite = SkyFor(terrain, horizon, zenith);
        sun.color = glow;
        haze.color = new Color(horizon.r, horizon.g, horizon.b, terrain == TerrainEnum.wastelands ? 0.4f : 0.3f);
    }

    private readonly Dictionary<TerrainEnum, Sprite> skies = new();

    private Sprite SkyFor(TerrainEnum terrain, Color horizon, Color zenith)
    {
        if (!skies.TryGetValue(terrain, out Sprite sprite) || sprite == null)
            skies[terrain] = sprite = Gradient($"BattleSky{terrain}", zenith, horizon); // (top row, bottom row)
        return sprite;
    }

    // The settlement the fight is at, or the nearest within two hexes, stands behind the field.
    private void SetBackdropCity(Hex where)
    {
        if (backdropCity != null) Destroy(backdropCity.gameObject);
        backdropCity = null;
        if (where == null) return;
        PC pc = where.GetHexesInRadius(2)
            .Where(h => h != null && h.GetPCData() != null && h.GetPCData().citySize != PCSizeEnum.NONE && h.IsHexRevealed())
            .OrderBy(h => (h.transform.position - where.transform.position).sqrMagnitude)
            .Select(h => h.GetPCData())
            .FirstOrDefault();
        if (pc == null) return;
        try
        {
            var anchor = new GameObject("CityAnchor").transform;
            anchor.SetParent(stage, false);
            anchor.localPosition = new Vector3(where.GetPCData() == pc ? 1.2f : 5f, -0.86f, 17f);
            Character owner = pc.owner;
            backdropCity = City3D.BuildStandalone(pc.pcName, owner != null ? owner.race : RacesEnum.Common, pc.citySize, pc.fortSize, pc.isCapital,
                owner is Leader leader ? leader.nationColor : Color.clear, 6f, anchor);
            backdropCity.SetSorting(0, -104);
            // Built in the board's tilted frame: stood upright on the field instead, and kept
            // under the stage (the anchor only placed it).
            backdropCity.transform.localRotation = Quaternion.identity;
            backdropCity.transform.SetParent(stage, true);
            Destroy(anchor.gameObject);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            backdropCity = null;
        }
    }

    private void LateUpdate()
    {
        if (stageCamera == null || !stageCamera.enabled) return;
        float dt = Time.unscaledDeltaTime;
        // The camera settles back to rest after a heavy blow's shake.
        shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
        Vector3 jitter = shake > 0f ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * shake * 0.06f : Vector3.zero;
        stageCamera.transform.localPosition = Vector3.Lerp(stageCamera.transform.localPosition, cameraRest, dt * 2f) + jitter;
        foreach (var (t, speed, phase) in motes)
        {
            Vector3 p = t.localPosition;
            p.x += speed * dt;
            p.y += Mathf.Sin(Time.unscaledTime * 0.7f + phase) * 0.05f * dt;
            if (p.x > 7f) p.x -= 14f;
            t.localPosition = p;
        }
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            var (t, velocity, age, life, sr) = puffs[i];
            age += dt;
            if (t == null || age >= life) { if (t != null) Destroy(t.gameObject); puffs.RemoveAt(i); continue; }
            t.localPosition += velocity * dt;
            t.localScale *= 1f + dt * 0.9f;
            Color c = sr.color;
            c.a = 0.55f * (1f - age / life);
            sr.color = c;
            puffs[i] = (t, velocity * (1f - dt * 1.5f), age, life, sr);
        }
    }

    // Dust bursting where a blow lands (and the camera shaking with a heavy one).
    private void Impact(Side side, bool heavy)
    {
        if (heavy) shake = 1f;
        Vector3 at = new Vector3(side.isLeft ? -1.25f : 1.25f, -0.5f, 0f);
        Sprite puff = Glow();
        for (int i = 0; i < (heavy ? 14 : 6); i++)
        {
            SpriteRenderer sr = Layer("Dust", puff, 10);
            sr.transform.localPosition = at + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.4f), -0.4f);
            sr.transform.localScale = Vector3.one * Random.Range(0.25f, 0.5f);
            sr.color = new Color(0.72f, 0.62f, 0.5f, 0.55f);
            Vector3 velocity = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.2f, 0.9f), Random.Range(-0.3f, 0.1f));
            puffs.Add((sr.transform, velocity, 0f, Random.Range(0.6f, 1.1f), sr));
        }
    }

    private static Sprite glowSprite, treeSprite;

    private static Sprite Glow()
    {
        if (glowSprite != null) return glowSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "BattleGlow", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply(false, true);
        return glowSprite = Unit(tex);
    }

    // A dark conifer: a tall, jagged triangle on a short trunk, pivot at its foot.
    private static Sprite TreeSprite()
    {
        if (treeSprite != null) return treeSprite;
        const int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "BattleTree", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f, v = (y + 0.5f) / h;
                float jag = 0.06f * Mathf.Sin(v * 40f);
                bool canopy = v > 0.12f && u < (1f - (v - 0.12f) / 0.88f) * 0.95f + jag;
                bool trunk = v <= 0.14f && u < 0.12f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, canopy || trunk ? 1f : 0f));
            }
        tex.Apply(false, true);
        return treeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), h);
    }

    private SpriteRenderer Layer(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(stage, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    // A unit-sized sprite from a texture (its pixels per unit = its width, so scale sets the size).
    private static Sprite Unit(Texture2D tex) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);

    // Note the order: `bottom` lands on the sprite's top row and `top` on its bottom row.
    private static Sprite Gradient(string name, Color bottom, Color top)
    {
        var tex = new Texture2D(4, 128, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 128; y++)
        {
            Color c = Color.Lerp(top, bottom, Mathf.SmoothStep(0f, 1f, y / 127f));
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, 4, 128), new Vector2(0.5f, 0.5f), 4f);
    }

    // A hill line: white below a wandering ridge, clear above; tinted per terrain.
    private static Sprite Ridge(string name, float height, int seed, float sharpness)
    {
        const int w = 1024, h = 256; // fine enough that the skyline is smooth, not stepped
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[w * h];
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)w;
            float ridge = height * (0.22f + 0.55f * Mathf.PerlinNoise(u * 14f + seed, seed * 0.7f)
                + 0.3f * Mathf.PerlinNoise(u * 38f + seed, 3.1f) + sharpness * 0.35f * (Mathf.PerlinNoise(u * 110f + seed, 7.3f) - 0.5f));
            for (int y = 0; y < h; y++)
                pixels[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((ridge * h - y) * 0.8f));
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
    }

    // Mottled ground: soft noise in light and dark, tinted per terrain.
    private static Sprite Noise(string name)
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f) + 0.12f * (Mathf.PerlinNoise(x * 0.3f, y * 0.3f) - 0.5f);
                pixels[y * n + x] = new Color(v, v, v, 1f);
            }
        tex.SetPixels(pixels);
        tex.Apply(true, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static Sprite roundedSquare;

    private static Sprite RoundedSquare()
    {
        if (roundedSquare != null) return roundedSquare;
        const int n = 48;
        const float radius = 10f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "DieFace", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - radius));
                float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - radius));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - d);
                // A soft shade toward the bottom edge reads as a little cube.
                float shade = Mathf.Lerp(0.82f, 1f, y / (float)n);
                pixels[y * n + x] = new Color(shade, shade, shade, a);
            }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return roundedSquare = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private static Image NewImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI NewText(RectTransform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(text, size);
        text.alignment = alignment;
        text.richText = true;
        text.raycastTarget = false;
        return text;
    }

    private static Button AddButton(RectTransform parent, string label, Vector2 position, Vector2 size, float fontSize, bool live, System.Action onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Place(rt, position, size);
        var face = go.GetComponent<CinematicPanel>();
        face.ornament = 0f;
        face.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.interactable = live;
        button.onClick.AddListener(() => onClick());
        TextMeshProUGUI text = NewText(rt, "Label", fontSize, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(text, fontSize, live ? UiTheme.Gold : UiTheme.Muted);
        text.richText = true;
        text.text = label;
        Stretch(text.rectTransform);
        return button;
    }

    private static void Place(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
