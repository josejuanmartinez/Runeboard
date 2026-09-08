using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject tooltipPanel;
    public TextMeshProUGUI textWidget;
    public Vector2 offset;
    public float exitCheckFrequency = .1f;
    public float readingSize = 20f;
    public float maximumWidth = 440f;
    private RectTransform tooltipRectTransform;
    private float lastExitCheckTime;
    private bool warnedMissingRect;
    private readonly System.Collections.Generic.List<RaycastResult> hits = new();

    private void Awake()
    {
        if (tooltipPanel == null) return;
        EnsureTooltipRect();
        tooltipPanel.SetActive(false);
    }

    // Resolves (and re-resolves) the tooltip's RectTransform. Never use `??=` / `??` here: those
    // use real C# null, so a Unity "fake null" (destroyed object, or a lookup that already failed
    // once) would be kept and every later sizeDelta write would throw. Editor tooling such as
    // StartupLoadingScreenEditor's preload bake calls Initialize() without Awake ever running, so
    // this has to cope with the field being unresolved and with a tooltipPanel that carries no
    // RectTransform at all.
    private bool EnsureTooltipRect()
    {
        if (tooltipPanel == null) return false;
        if (tooltipRectTransform == null) tooltipRectTransform = tooltipPanel.GetComponent<RectTransform>();
        if (tooltipRectTransform != null) return true;
        if (!warnedMissingRect)
        {
            warnedMissingRect = true;
            Debug.LogWarning($"Hover on '{name}': tooltipPanel '{tooltipPanel.name}' has no RectTransform, so the tooltip cannot be laid out.", this);
        }
        return false;
    }
    private void OnDisable() { if (tooltipPanel != null) tooltipPanel.SetActive(false); }
    private void Update()
    {
        if (tooltipPanel == null || !tooltipPanel.activeSelf) return;
        if (ModalOpen()) { tooltipPanel.SetActive(false); return; }
        UpdateTooltipPosition();
        if (Time.unscaledTime - lastExitCheckTime < exitCheckFrequency) return;
        lastExitCheckTime = Time.unscaledTime;
        if (!IsPointOverUI()) tooltipPanel.SetActive(false);
    }
    private static bool ModalOpen() => PopupManager.IsShowing || SelectionDialog.IsShowing || ConfirmationDialog.IsShowing || VideoPopupManager.IsShowing;
    private void Fit()
    {
        if (textWidget == null || !EnsureTooltipRect()) return;
        Canvas canvas = tooltipPanel.GetComponentInParent<Canvas>();
        float scale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f;
        float width = Mathf.Min(maximumWidth, (Screen.width - 32) / scale);
        textWidget.enableAutoSizing = false;
        textWidget.fontSize = readingSize;
        textWidget.alignment = TextAlignmentOptions.TopLeft;
        textWidget.textWrappingMode = TextWrappingModes.Normal;
        textWidget.overflowMode = TextOverflowModes.Overflow;
        textWidget.color = new Color(.92f,.91f,.85f);
        textWidget.raycastTarget = false;
        Vector2 preferred = textWidget.GetPreferredValues(textWidget.text, Mathf.Max(80,width-36), Mathf.Infinity);
        tooltipRectTransform.sizeDelta = new Vector2(Mathf.Min(width,Mathf.Max(100,preferred.x+36)),preferred.y+28);
        tooltipRectTransform.pivot = new Vector2(0,1);
        var rect = textWidget.rectTransform;
        if(rect!=tooltipRectTransform)
        {
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);
            rect.offsetMin=new Vector2(18,14);rect.offsetMax=new Vector2(-18,-14);rect.localScale=Vector3.one;
        }
        else textWidget.margin=new Vector4(18,14,18,14);
        foreach(var graphic in tooltipPanel.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
    }
    private void UpdateTooltipPosition()
    {
        if (tooltipRectTransform == null) return;
        var canvas=tooltipPanel.GetComponentInParent<Canvas>();if(canvas==null)return;
        Vector2 mouse=Input.mousePosition;
        Vector2 size=tooltipRectTransform.rect.size*canvas.scaleFactor;
        Vector2 position=mouse+new Vector2(20,-20);
        if(position.x+size.x>Screen.width-12)position.x=mouse.x-size.x-20;
        position.x=Mathf.Clamp(position.x,12,Mathf.Max(12,Screen.width-size.x-12));
        position.y=Mathf.Clamp(position.y,Mathf.Min(Screen.height-12,size.y+12),Screen.height-12);
        Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        if(tooltipRectTransform.parent is RectTransform parent && RectTransformUtility.ScreenPointToWorldPointInRectangle(parent,position,camera,out var world))tooltipRectTransform.position=world;
    }
    public void Initialize(string text, Vector2 offset, int fontSize, TextAlignmentOptions textAlignment) => Initialize(text);
    public void Initialize(string text, int fontSize) => Initialize(text);
    public void Initialize(string text)
    {
        if(textWidget==null)return;
        textWidget.text=CreateTextWithBackground(text);Fit();
    }
    public string CreateTextWithBackground(string text) => System.Text.RegularExpressions.Regex.Replace(text??string.Empty,@"</?mark\b[^>]*>",string.Empty);
    public void OnPointerEnter(PointerEventData eventData)
    {
        if(ModalOpen()||textWidget==null||string.IsNullOrWhiteSpace(textWidget.text)||!EnsureTooltipRect())return;
        Fit();tooltipPanel.SetActive(true);UpdateTooltipPosition();Sounds.Instance?.PlayUiHover();
    }
    public void OnPointerExit(PointerEventData eventData) { if(tooltipPanel!=null)tooltipPanel.SetActive(false); }
    private bool IsPointOverUI()
    {
        if(EventSystem.current==null)return false;
        hits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=Input.mousePosition},hits);
        if(hits.Count==0)return false;
        var first=hits[0].gameObject;
        return first==gameObject||first.transform.IsChildOf(transform);
    }
}
