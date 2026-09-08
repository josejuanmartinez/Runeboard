using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Opt-in, isolated visual fixtures. Never saves the open scene or source assets.
[InitializeOnLoad]
public static class UiCompositionVerification
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static UiCompositionVerification() { EditorApplication.update += Check; }
    static void Check()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/ui-composition.request")) return;
        File.Delete("Temp/ui-composition.request");
        try { Render(); File.WriteAllText("Temp/ui-composition-result.txt", "PASS: carousel, board and confirmation rendered; image-to-text layout restored; missing image cleared."); }
        catch (Exception e) { File.WriteAllText("Temp/ui-composition-result.txt", e.ToString()); }
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static Sprite Art(string name) => AssetDatabase.FindAssets(name + " t:Sprite", new[] { "Assets/Art/Cards/Characters" })
        .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>).FirstOrDefault(x => x != null);

    public static void Render()
    {
        Directory.CreateDirectory("Temp/UI-Composition");
        Capture("carousel", 1100, 480, canvas =>
        {
            string[] names = { "Aragorn", "Gandalf", "Saruman" };
            for (int i = 0; i < 3; i++)
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Reusable/CarouselItem.prefab"), canvas);
                var item = go.GetComponent<CarouselItem>();
                item.SetSprite(Art(names[i]));
                item.image.material = null; // Fixture has no runtime skin manager.
                item.SetLabel(names[i] + "<br><size=75%><color=#BEB6A2>" + (i == 1 ? "Disturber of the Peace" : i == 0 ? "Chieftain of the Dunedain" : "The White Wizard") + "</color></size>");
                ((RectTransform)go.transform).anchoredPosition = new Vector2((i - 1) * 285, i == 1 ? 0 : -20);
                go.transform.localScale = Vector3.one * (i == 1 ? 1.1f : .9f);
            }
        });
        Capture("board", 1200, 650, canvas =>
        {
            string[] places = { "Minas Tirith", "Havens of Umbar", "The Grey Havens" };
            for (int i = 0; i < 3; i++)
            {
                var root = new GameObject("Tile fixture");
                root.transform.SetParent(canvas, false);
                root.transform.localPosition = new Vector3((i - 1) * 375, 0, 0);
                root.transform.localScale = Vector3.one * 330;
                var tile = new GameObject("Terrain", typeof(SpriteRenderer)); tile.transform.SetParent(root.transform, false);
                tile.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Hexes/PCs/" + places[i] + ".png");
                tile.GetComponent<SpriteRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/CharacterOutline.mat");
                var layer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/HexParts/HexCharacterLayer.prefab"), root.transform);
                layer.transform.localPosition = new Vector3(0, .13f, 0);
                layer.transform.localScale *= .9f;
                foreach (var sr in layer.GetComponentsInChildren<SpriteRenderer>()) sr.enabled = false;
                var character = layer.transform.Find("character").GetComponent<SpriteRenderer>(); character.enabled = true;
                string kind = i == 1 ? "Orc" : "Gandalf";
                character.sprite = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/Characters/AnimationSpritesheets/{kind}/{kind}_Forward.png").OfType<Sprite>().First(s => s.name.EndsWith("standing_idle_00"));
                character.sortingOrder = 10;
                character.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/CharacterOutline.mat");
                var labelRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/HexParts/HexPcText.prefab"), root.transform);
                var label = labelRoot.GetComponentInChildren<TMP_Text>();
                label.text = "<b><color=#DDC995>" + places[i] + "</color></b><br><size=85%><sprite name=\"pc\">5 <sprite name=\"fort\">4 <sprite name=\"loyalty\">85</size>  <color=#95866C>·</color> <size=75%><sprite name=\"freePeopleCharacter\">2</size>";
                labelRoot.GetComponentInChildren<SpriteRendererFitToTMP>().ConfigureSettlementPresentation();
                label.GetComponent<Renderer>().sortingOrder = 30;
            }
        });
        var original = Object.FindFirstObjectByType<ConfirmationDialog>(FindObjectsInactive.Include);
        if (original == null) throw new Exception("Open the InGame scene to verify its confirmation dialog.");
        foreach (bool showImage in new[] { true, false })
        {
            Capture(showImage ? "confirmation-image" : "confirmation-text", 800, 660, canvas =>
            {
                var clone = Object.Instantiate(original.gameObject, canvas).GetComponent<ConfirmationDialog>();
                clone.transform.localPosition = Vector3.zero; clone.transform.localScale = Vector3.one;
                var content = Field<GameObject>(clone, "content"); content.SetActive(true);
                var c = content.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.overrideSorting = false;
                var scaler = content.GetComponent<CanvasScaler>(); if (scaler != null) scaler.enabled = false;
                var rect = (RectTransform)content.transform;
                rect.localScale = Vector3.one; rect.localPosition = Vector3.zero;
                rect.pivot = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(800,660);
                var label = Field<TextMeshProUGUI>(clone, "messageLabel"); label.text = "Do you want to start as Gandalf (Disturber of the Peace)?";
                var panel = (RectTransform)label.transform.parent;
                var originalSize = panel.sizeDelta;
                var type = typeof(ConfirmationDialog).GetNestedType("DialogRequest", BindingFlags.NonPublic);
                var request = Activator.CreateInstance(type);
                type.GetField("image").SetValue(request, Art("Gandalf"));
                var apply = typeof(ConfirmationDialog).GetMethod("ApplyRequestImage", Private);
                apply.Invoke(clone, new[] { request });
                if (!showImage)
                {
                    type.GetField("image").SetValue(request, null);
                    type.GetField("imageName").SetValue(request, "missing-verification-artwork");
                    apply.Invoke(clone, new[] { request });
                    if (panel.sizeDelta != originalSize || Field<Image>(clone, "requestImage").sprite != null)
                        throw new Exception("Text-only confirmation failed to restore layout or clear artwork.");
                }
                Field<Button>(clone,"previousButton").gameObject.SetActive(false);
                Field<Button>(clone,"nextButton").gameObject.SetActive(false);
            });
        }
    }

    static void Capture(string name, int width, int height, Action<Transform> build)
    {
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previous = RenderTexture.active;
        RenderTexture target = null; Texture2D output = null;
        try
        {
            var camera = new GameObject("Fixture camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = height / 2f;
            camera.transform.position = new Vector3(0,0,-1000);
            camera.nearClipPlane = 1; camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f,.16f,.15f);
            camera.cullingMask = 1 << 31;
            var go = new GameObject("Fixture canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            build(go.transform);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true)) graphic.Rebuild(CanvasUpdate.PreRender);
            target = new RenderTexture(width,height,24); camera.targetTexture = target; camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            output = new Texture2D(width,height,TextureFormat.RGB24,false);
            output.ReadPixels(new Rect(0,0,width,height),0,0); output.Apply();
            File.WriteAllBytes("Temp/UI-Composition/" + name + ".png",output.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (output != null) Object.DestroyImmediate(output);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            SceneManager.SetActiveScene(active); EditorSceneManager.CloseScene(scene,true);
        }
    }
}
