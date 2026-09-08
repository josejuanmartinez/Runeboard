using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Offscreen fixtures use prefab clones in a temporary scene; the user's scene is never saved or changed.
[InitializeOnLoad]
public static class UiTargetVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Request = "Temp/ui-target-verification.request";
    static UiTargetVerification() { EditorApplication.delayCall += CheckRequest; }
    static void CheckRequest()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Render(); }
        catch (Exception e) { File.WriteAllText("Temp/ui-target-verification.txt", e.ToString()); }
    }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Private).GetValue(o);
    static void Invoke(object o, string method) => o.GetType().GetMethod(method, Private).Invoke(o, null);

    [MenuItem("Tools/Runeboard/Verify UI targets")]
    public static void Render()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Render verification fixtures in Edit mode.");
        Directory.CreateDirectory("Temp/UI-Targets");
        Capture("preview", 600, 860, canvas =>
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Reusable/Card.prefab"), canvas);
            go.SetActive(true);
            var card = go.GetComponent<Card>();
            Field<TMP_Text>(card, "titleText").text = "Gandalf the Grey";
            Field<TMP_Text>(card, "descriptionText").text = "<b>Wizard</b>\nA traveller and counsellor, watchful against the growing shadow.\n\n<i>All we have to decide is what to do with the time that is given us.</i>";
            Field<TMP_Text>(card, "requirementsText").text = "3<sprite name=\"gold\">   2<sprite name=\"mage\">";
            var art = Field<Image>(card, "cardArtImage");
            var path = AssetDatabase.FindAssets("Gandalf t:Sprite", new[] { "Assets/Art/Cards" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (path != null) art.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            card.ShowRealCard();
            card.ApplyCenterPreviewPresentation();
            ((RectTransform)go.transform).anchorMin = ((RectTransform)go.transform).anchorMax = new Vector2(.5f, .5f);
            ((RectTransform)go.transform).anchoredPosition = Vector2.zero;
            go.transform.localScale = Vector3.one * 1.4f;
        });
        Capture("combat", 1920, 1080, canvas =>
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/CombatBanner.prefab"), canvas);
            var scaler = go.GetComponent<CanvasScaler>(); if (scaler != null) scaler.enabled = false;
            var c = go.GetComponent<Canvas>(); c.overrideSorting = false; c.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
            var banner = go.GetComponent<CombatBanner>();
            Invoke(banner, "ApplyPresentation");
            foreach (var field in new[] { "textRect", "subtitleRect", "nationsRect" }) Field<RectTransform>(banner, field).localScale = Vector3.one;
            foreach (var field in new[] { "attackerBannerImage", "defenderBannerImage" }) Field<Image>(banner, field).enabled = false;
            Field<CanvasGroup>(banner, "rootGroup").alpha = 1;
            Field<TMP_Text>(banner, "titleText").text = "Battle at Minas Tirith";
            Field<TMP_Text>(banner, "subtitleText").text = "Aragorn attacks the Witch-king";
            Field<TMP_Text>(banner, "nationsText").text = "Gondor    /    Mordor";
            Field<TMP_Text>(banner, "noticeText").text = "<color=#73D973>Fortification +3</color>\nAllied reinforcements +2\n<color=#E65959>Weary troops -1</color>";
            foreach (var field in new[] { "attackerStatusText", "defenderStatusText" }) Field<TMP_Text>(banner, field).text = "";
            foreach (var field in new[] { "lineLeftRect", "lineRightRect" }) Field<RectTransform>(banner, field).sizeDelta = new Vector2(320, 1);
            foreach (var field in new[] { "attackerImage", "defenderImage" })
            {
                var image = Field<Image>(banner, field);
                string term = field.StartsWith("attacker") ? "Aragorn" : "Witch";
                string path = AssetDatabase.FindAssets(term + " t:Sprite", new[] { "Assets/Art/Cards" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                if (path != null) image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        });
        Capture("settlement", 800, 400, canvas =>
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/HexParts/HexPcText.prefab"));
            SceneManager.MoveGameObjectToScene(go, canvas.gameObject.scene);
            go.transform.position = new Vector3(0, 0, 0);
            go.transform.localScale = Vector3.one * 150;
            var label = go.GetComponentInChildren<TMP_Text>();
            label.text = "<b><color=#DDC995>Minas Tirith</color></b><br><size=85%><sprite name=\"pc\">5  <sprite name=\"fort\">4  <sprite name=\"loyalty\">85</size><br><sprite name=\"freePeople\">2";
            label.color = new Color(.94f, .92f, .84f);
            label.fontStyle = FontStyles.Normal;
            go.GetComponentInChildren<SpriteRendererFitToTMP>().ConfigureSettlementPresentation();
        });
        Capture("bloom", 960, 560, canvas =>
        {
            var go = new GameObject("Bloom fixture", typeof(RectTransform), typeof(CardBloomWheel));
            go.transform.SetParent(canvas, false);
            ((RectTransform)go.transform).anchoredPosition = new Vector2(0, -130);
            var wheel = go.GetComponent<CardBloomWheel>();
            typeof(CardBloomWheel).GetField("hoveredCardIndex", Private).SetValue(wheel, 2);
            typeof(CardBloomWheel).GetField("<LinesAlpha>k__BackingField", Private).SetValue(wheel, 1f);
            var paths = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Art/Cards/Characters" }).Select(AssetDatabase.GUIDToAssetPath).Take(5).ToArray();
            for (int i = 0; i < 5; i++)
            {
                var token = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Reusable/TokenCard.prefab"), go.transform);
                token.SetActive(true);
                var rect = (RectTransform)token.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                float angle = Mathf.PI - i * Mathf.PI / 4;
                rect.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 280;
                rect.localScale = Vector3.one * (i == 2 ? 1.1f : 1f);
                var card = token.GetComponent<Card>(); card.ShowToken();
                if (i < paths.Length) Field<Image>(card, "tokenImage").sprite = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
                card.SetTokenTint(i == 2 ? 0 : .45f, i == 4 ? .47f : 0);
                var group = token.GetComponent<CanvasGroup>(); group.alpha = 1;
                Field<List<RectTransform>>(wheel, "cardRects").Add(rect);
                Field<List<CanvasGroup>>(wheel, "cardGroups").Add(group);
                Field<List<Color>>(wheel, "cardLineColors").Add(new Color(.79f, .64f, .4f));
            }
            var lines = new GameObject("Bloom lines", typeof(RectTransform), typeof(CardBloomLinesGraphic));
            lines.transform.SetParent(go.transform, false); lines.transform.SetAsFirstSibling();
            lines.GetComponent<CardBloomLinesGraphic>().Init(wheel);
        });
        File.WriteAllText("Temp/ui-target-verification.txt", "Rendered preview, combat, settlement, bloom with Unity.\n");
    }
    static void Capture(string name, int width, int height, Action<Transform> build)
    {
        var activeScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null;
        Texture2D output = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraObject = new GameObject("UI verification camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = height / 2f;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = 1; camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.17f, .19f, .18f);
            var canvasObject = new GameObject("Verification canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            build(canvas.transform);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.Rebuild(CanvasUpdate.PreRender);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            output = new Texture2D(width, height, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply();
            File.WriteAllBytes("Temp/UI-Targets/" + name + ".png", output.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (output != null) Object.DestroyImmediate(output);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            SceneManager.SetActiveScene(activeScene);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
