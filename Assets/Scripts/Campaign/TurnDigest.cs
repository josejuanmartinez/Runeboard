using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "While you waited…": snapshots the world when the human ends their turn and, when their
/// next turn opens, shows a short digest of what changed that they could know about
/// (PCs changing hands on revealed hexes, their own losses, fallen leaders, rival objective
/// swings). Replaces having to read the log or watch camera replays to understand a round.
/// </summary>
public class TurnDigest : MonoBehaviour
{
    private const int MaxLines = 7;
    private const float ShowSeconds = 9f;

    private Game game;
    private readonly Dictionary<PC, Leader> pcOwners = new();
    private readonly HashSet<Character> playerCharacters = new();
    private readonly Dictionary<PlayableLeader, int> scores = new();
    private readonly HashSet<PlayableLeader> aliveLeaders = new();
    private bool hasSnapshot;
    private GameObject panel;
    private float hideAt;

    public static void Attach(Game owner)
    {
        if (owner == null || owner.GetComponent<TurnDigest>() != null) return;
        TurnDigest digest = owner.gameObject.AddComponent<TurnDigest>();
        digest.game = owner;
        owner.HumanTurnEnded += digest.Snapshot;
        owner.HumanTurnStarted += digest.ShowDigest;
    }

    private void OnDestroy()
    {
        if (game == null) return;
        game.HumanTurnEnded -= Snapshot;
        game.HumanTurnStarted -= ShowDigest;
    }

    private void Update()
    {
        if (panel != null && (Time.unscaledTime >= hideAt || game == null || game.GameOver || !game.IsPlayerCurrentlyPlaying()))
            Hide();
    }

    private void Snapshot()
    {
        pcOwners.Clear();
        playerCharacters.Clear();
        scores.Clear();
        aliveLeaders.Clear();
        Board board = Board.Instance;
        if (board?.hexes == null || game?.player == null) return;
        foreach (Hex hex in board.hexes.Values)
        {
            PC pc = hex != null ? hex.GetPC() : null;
            if (pc != null) pcOwners[pc] = pc.owner;
        }
        foreach (Character c in game.player.controlledCharacters) if (c != null && !c.killed) playerCharacters.Add(c);
        CampaignObjectives objectives = CampaignObjectives.Instance;
        foreach (PlayableLeader l in Leaders())
        {
            if (!l.killed) aliveLeaders.Add(l);
            if (objectives != null) scores[l] = objectives.Score(l);
        }
        hasSnapshot = true;
    }

    private IEnumerable<PlayableLeader> Leaders()
    {
        if (game.player != null) yield return game.player;
        if (game.competitors != null) foreach (PlayableLeader c in game.competitors) if (c != null) yield return c;
    }

    private void ShowDigest()
    {
        if (!hasSnapshot || game?.player == null) return;
        hasSnapshot = false;
        PlayableLeader player = game.player;
        List<string> lines = new();

        foreach (PlayableLeader l in Leaders())
            if (aliveLeaders.Contains(l) && l.killed) lines.Add($"<color=#E8C66A>{CampaignObjectives.DisplayLeader(l)} has fallen.</color>");

        foreach (Character c in playerCharacters)
            if (c == null || c.killed) lines.Add($"<color=#E07A5F>{(c != null ? c.characterName : "A companion")} has fallen.</color>");

        foreach (KeyValuePair<PC, Leader> entry in pcOwners)
        {
            PC pc = entry.Key;
            if (pc == null || pc.owner == entry.Value) continue;
            string place = DisplayNames.Format(pc.pcName);
            if (entry.Value == player)
                lines.Add($"<color=#E07A5F>You lost {place}{(pc.owner != null ? $" to {pc.owner.characterName}" : string.Empty)}.</color>");
            else if (pc.owner == player)
                lines.Add($"<color=#9BD37A>{place} is now yours.</color>");
            else if (pc.hex != null && pc.hex.IsHexRevealed())
                lines.Add(pc.owner != null ? $"{pc.owner.characterName} took {place}." : $"{place} was abandoned.");
        }

        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives != null)
        {
            foreach (PlayableLeader l in Leaders())
            {
                if (l.killed || !scores.TryGetValue(l, out int before)) continue;
                int now = objectives.Score(l);
                if (now == before) continue;
                string who = l == player ? "Your" : $"{CampaignObjectives.DisplayLeader(l)}'s";
                lines.Add($"{who} objective score {(now > before ? "rose" : "fell")} to <b>{now}</b>.");
            }
        }

        if (lines.Count == 0) return;
        Show(lines);
    }

    private void Show(List<string> lines)
    {
        Hide();
        Layout layout = FindFirstObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return;
        RectTransform root = canvas.rootCanvas.transform as RectTransform;

        panel = new GameObject("TurnDigest", typeof(RectTransform), typeof(CanvasRenderer), typeof(RuneboardPanel),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(Button));
        var rt = panel.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -190f);
        rt.sizeDelta = new Vector2(460f, 0f);
        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 12, 14);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.GetComponent<Button>().onClick.AddListener(Hide);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.GetComponent<RectTransform>().SetParent(rt, false);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.fontSize = 17f;
        text.richText = true;
        text.raycastTarget = false;
        text.color = new Color(0.93f, 0.89f, 0.80f, 1f);
        text.textWrappingMode = TextWrappingModes.Normal;

        var sb = new StringBuilder("<size=75%><color=#C9A35A>WHILE YOU WAITED</color></size>\n");
        foreach (string line in lines.Take(MaxLines)) sb.Append(line).Append('\n');
        if (lines.Count > MaxLines) sb.Append($"<size=80%><color=#8C8371>…and {lines.Count - MaxLines} more in the log.</color></size>");
        text.text = sb.ToString().TrimEnd();
        hideAt = Time.unscaledTime + ShowSeconds;
    }

    private void Hide()
    {
        if (panel != null) Destroy(panel);
        panel = null;
    }
}
