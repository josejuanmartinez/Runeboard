using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the player where and how to pursue their objectives:
///  • a pulsing gold beacon + name plate on every objective target, drawn above the fog
///  • FocusObjective(): camera flies to a target with a caption saying how to achieve it
///    (the objective tracker calls this when an objective is clicked)
///  • on the first turn, a short guided camera tour of all targets
/// </summary>
public class ObjectiveGuide : MonoBehaviour
{
    private static ObjectiveGuide instance;
    private readonly List<(Transform root, SpriteRenderer glow, Hex hex)> beacons = new();
    private Game game;
    private bool tourDone;
    private GameObject caption;
    private Coroutine running;

    public static ObjectiveGuide Instance => instance;

    public static void Attach(Game owner)
    {
        if (instance != null || owner == null) return;
        instance = owner.gameObject.AddComponent<ObjectiveGuide>();
        instance.game = owner;
        owner.HumanTurnStarted += instance.OnHumanTurnStarted;
    }

    private void OnDestroy()
    {
        if (game != null) game.HumanTurnStarted -= OnHumanTurnStarted;
        if (instance == this) instance = null;
    }

    private void OnHumanTurnStarted()
    {
        BuildBeacons();
        if (!tourDone && !AutoTour.Active)
        {
            tourDone = true;
            running = StartCoroutine(Tour());
        }
    }

    private void Update()
    {
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.4f);
        foreach (var b in beacons)
        {
            if (b.glow == null) continue;
            b.glow.color = new Color(1f, 0.80f, 0.42f, 0.30f + 0.35f * pulse);
            b.glow.transform.localScale = Vector3.one * (1.25f + 0.12f * pulse);
        }
        if (caption != null && Input.GetMouseButtonDown(0) && running != null)
        {
            StopCoroutine(running);
            running = null;
            HideCaption();
        }
    }

    private void BuildBeacons()
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives == null || game?.player == null) return;
        foreach (var b in beacons) if (b.root != null) Destroy(b.root.gameObject);
        beacons.Clear();
        foreach (CampaignObjective o in objectives.GetObjectives(game.player))
        {
            foreach (Hex hex in objectives.GetTargetHexes(o))
            {
                if (beacons.Exists(b => b.hex == hex)) continue;
                beacons.Add(CreateBeacon(hex, o));
            }
        }
    }

    private (Transform, SpriteRenderer, Hex) CreateBeacon(Hex hex, CampaignObjective o)
    {
        var root = new GameObject($"ObjectiveBeacon_{o.id}").transform;
        root.position = hex.transform.position + new Vector3(0f, 0f, -0.1f);

        var glowGo = new GameObject("Glow", typeof(SpriteRenderer));
        glowGo.transform.SetParent(root, false);
        var glow = glowGo.GetComponent<SpriteRenderer>();
        glow.sprite = BoardLook.SoftEllipseSprite();
        glow.sortingLayerName = "Hexes";
        glow.sortingOrder = 5;

        var labelGo = new GameObject("Label", typeof(TextMeshPro));
        labelGo.transform.SetParent(root, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        var label = labelGo.GetComponent<TextMeshPro>();
        label.font = UiTheme.Display;
        label.fontSize = 2.6f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = UiTheme.Gold;
        label.outlineWidth = 0.25f;
        label.outlineColor = new Color32(0, 0, 0, 220);
        label.text = $"★ {DisplayNames.Format(hex.GetPC()?.pcName ?? o.pc)}";
        label.rectTransform.sizeDelta = new Vector2(6f, 1f);
        var mr = label.GetComponent<MeshRenderer>();
        mr.sortingLayerID = glow.sortingLayerID;
        mr.sortingOrder = 30000;
        return (root, glow, hex);
    }

    /// <summary>Flies the camera to an objective's first target and explains how to achieve it.</summary>
    public void FocusObjective(CampaignObjective o)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Visit(o, 3.2f, returnAfter: false));
    }

    private IEnumerator Tour()
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives == null) yield break;
        yield return new WaitForSecondsRealtime(0.8f);
        foreach (CampaignObjective o in objectives.GetObjectives(game.player))
        {
            if (objectives.GetTargetHexes(o).Count == 0) continue;
            yield return Visit(o, 2.6f, returnAfter: false);
        }
        Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
        if (selected?.hex != null) BoardNavigator.Instance?.LookAt(selected.hex.transform.position, 0.8f);
        running = null;
    }

    private IEnumerator Visit(CampaignObjective o, float hold, bool returnAfter)
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        List<Hex> targets = objectives != null ? objectives.GetTargetHexes(o) : null;
        if (targets == null || targets.Count == 0)
        {
            ShowCaption(o, objectives);
            yield return new WaitForSecondsRealtime(hold);
            HideCaption();
            yield break;
        }
        BoardNavigator.Instance?.LookAt(targets[0].transform.position, 0.9f);
        ShowCaption(o, objectives);
        yield return new WaitForSecondsRealtime(hold);
        HideCaption();
    }

    private void ShowCaption(CampaignObjective o, CampaignObjectives objectives)
    {
        HideCaption();
        Layout layout = FindFirstObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return;
        RectTransform root = canvas.rootCanvas.transform as RectTransform;
        float k = Mathf.Max(0.5f, root.rect.height / 1080f);

        caption = new GameObject("ObjectiveCaption", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var rt = caption.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.localScale = Vector3.one * k;
        rt.anchoredPosition = new Vector2(0f, -150f * k);
        rt.sizeDelta = new Vector2(520f, 0f);
        var vlg = caption.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 12, 14);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        caption.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.GetComponent<RectTransform>().SetParent(rt, false);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(text, 15f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        string reward = o.decisive ? "Wins the war" : $"+{o.points} points";
        string how = objectives != null ? objectives.HowTo(game.player, o) : string.Empty;
        text.text = $"{UiTheme.Eyebrow("Objective")}\n<size=125%><color={UiTheme.GoldHex}>{o.title}</color></size>  <color={UiTheme.MutedHex}>· {reward}</color>\n{o.description}\n<color={UiTheme.GoodHex}>{how}</color>";
    }

    private void HideCaption()
    {
        if (caption != null) Destroy(caption);
        caption = null;
    }
}
