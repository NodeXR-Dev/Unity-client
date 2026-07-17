using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MeetingRoomCommonUI : MonoBehaviour
{
    private const string DefaultLoadingKey = "default";

    private static MeetingRoomCommonUI instance;

    [SerializeField] private Vector2Int canvasSize = new Vector2Int(960, 540);
    [SerializeField] private float widthInMeters = 1.28f;
    [SerializeField] private float distanceFromCamera = 1.65f;
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private float verticalOffset = 0.1f;
    [SerializeField] private Vector3 fallbackWorldPosition = new Vector3(5.7f, 1.45f, 1.65f);
    [SerializeField] private Vector3 fallbackWorldEulerAngles = Vector3.zero;
    [SerializeField] private float noticeVisibleSeconds = 2.4f;
    [SerializeField] private float noticeFadeSeconds = 0.25f;

    private readonly Dictionary<string, string> loadingRequests = new Dictionary<string, string>();
    private readonly HashSet<string> shownAlertKeys = new HashSet<string>();

    private Canvas canvas;
    private CanvasGroup alertGroup;
    private CanvasGroup noticeGroup;
    private CanvasGroup loadingGroup;
    private RectTransform spinnerPivot;
    private Text alertTitleText;
    private Text alertMessageText;
    private Text alertConfirmText;
    private Text noticeText;
    private Text loadingText;
    private Coroutine noticeRoutine;
    private Action alertConfirmed;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[MeetingRoomCommonUI] Another instance already exists. Disabling duplicate.");
            enabled = false;
            return;
        }

        instance = this;
        CreateCanvas();
        SetLayerVisible(alertGroup, false);
        SetLayerVisible(noticeGroup, false);
        SetLayerVisible(loadingGroup, false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        UpdateCanvasPose();

        if (loadingGroup != null && loadingGroup.gameObject.activeSelf && spinnerPivot != null)
        {
            spinnerPivot.Rotate(0f, 0f, -280f * Time.unscaledDeltaTime);
        }
    }

    public static void Alert(string message)
    {
        Alert("알림", message);
    }

    public static void Alert(string title, string message)
    {
        Alert(title, message, "확인", null);
    }

    public static void Alert(string title, string message, string confirmText, Action onConfirmed = null)
    {
        GetOrCreate().ShowAlert(title, message, confirmText, onConfirmed);
    }

    public static void AlertOnce(string key, string title, string message)
    {
        AlertOnce(key, title, message, "확인");
    }

    public static void AlertOnce(string key, string title, string message, string confirmText)
    {
        var ui = GetOrCreate();
        if (string.IsNullOrWhiteSpace(key))
        {
            ui.ShowAlert(title, message, confirmText, null);
            return;
        }

        if (!ui.shownAlertKeys.Add(key))
        {
            return;
        }

        ui.ShowAlert(title, message, confirmText, null);
    }

    public static void ResetAlertOnce(string key)
    {
        if (instance == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        instance.shownAlertKeys.Remove(key);
    }

    public static void Notice(string message)
    {
        Notice(message, -1f);
    }

    public static void Notice(string message, float visibleSeconds)
    {
        GetOrCreate().ShowNotice(message, visibleSeconds);
    }

    public static void ShowLoading(string message = "처리 중...", string key = DefaultLoadingKey)
    {
        GetOrCreate().ShowLoadingFrame(message, key);
    }

    public static void HideLoading(string key = DefaultLoadingKey)
    {
        if (instance == null)
        {
            return;
        }

        instance.HideLoadingFrame(key);
    }

    public static void HideAllLoading()
    {
        if (instance == null)
        {
            return;
        }

        instance.loadingRequests.Clear();
        instance.SetLayerVisible(instance.loadingGroup, false);
    }

    public static void HideAlert()
    {
        if (instance == null)
        {
            return;
        }

        instance.CloseAlert();
    }

    private static MeetingRoomCommonUI GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<MeetingRoomCommonUI>();
        if (instance != null)
        {
            return instance;
        }

        var commonUiObject = new GameObject("MeetingRoom Common UI");
        return commonUiObject.AddComponent<MeetingRoomCommonUI>();
    }

    private void ShowAlert(string title, string message, string confirmText, Action onConfirmed)
    {
        alertConfirmed = onConfirmed;
        alertTitleText.text = string.IsNullOrWhiteSpace(title) ? "알림" : title;
        alertMessageText.text = string.IsNullOrWhiteSpace(message) ? "" : message;
        alertConfirmText.text = string.IsNullOrWhiteSpace(confirmText) ? "확인" : confirmText;
        alertGroup.transform.SetAsLastSibling();
        SetLayerVisible(alertGroup, true);
    }

    private void CloseAlert()
    {
        SetLayerVisible(alertGroup, false);

        var callback = alertConfirmed;
        alertConfirmed = null;
        callback?.Invoke();
    }

    private void ShowNotice(string message, float visibleSeconds)
    {
        if (noticeRoutine != null)
        {
            StopCoroutine(noticeRoutine);
        }

        noticeRoutine = StartCoroutine(ShowNoticeRoutine(message, visibleSeconds));
    }

    private IEnumerator ShowNoticeRoutine(string message, float visibleSeconds)
    {
        noticeText.text = string.IsNullOrWhiteSpace(message) ? "" : message;
        noticeGroup.transform.SetAsLastSibling();
        SetLayerVisible(noticeGroup, true);
        noticeGroup.alpha = 1f;

        var holdSeconds = visibleSeconds > 0f ? visibleSeconds : noticeVisibleSeconds;
        yield return new WaitForSecondsRealtime(holdSeconds);

        var elapsed = 0f;
        while (elapsed < noticeFadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            noticeGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, noticeFadeSeconds)));
            yield return null;
        }

        SetLayerVisible(noticeGroup, false);
        noticeRoutine = null;
    }

    private void ShowLoadingFrame(string message, string key)
    {
        var requestKey = string.IsNullOrWhiteSpace(key) ? DefaultLoadingKey : key;
        loadingRequests[requestKey] = string.IsNullOrWhiteSpace(message) ? "처리 중..." : message;
        loadingText.text = loadingRequests[requestKey];
        loadingGroup.transform.SetAsLastSibling();
        SetLayerVisible(loadingGroup, true);
    }

    private void HideLoadingFrame(string key)
    {
        var requestKey = string.IsNullOrWhiteSpace(key) ? DefaultLoadingKey : key;
        loadingRequests.Remove(requestKey);

        if (loadingRequests.Count == 0)
        {
            SetLayerVisible(loadingGroup, false);
            return;
        }

        foreach (var request in loadingRequests)
        {
            loadingText.text = request.Value;
        }
    }

    private void CreateCanvas()
    {
        var canvasObject = new GameObject(
            "MeetingRoom Common UI Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 500;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(canvasSize.x, canvasSize.y);

        var metersPerPixel = widthInMeters / Mathf.Max(1, canvasSize.x);
        canvasObject.transform.localScale = new Vector3(metersPerPixel, metersPerPixel, metersPerPixel);

        CreateLoadingLayer(canvasRect);
        CreateAlertLayer(canvasRect);
        CreateNoticeLayer(canvasRect);
        UpdateCanvasPose();
    }

    private void CreateAlertLayer(RectTransform canvasRect)
    {
        alertGroup = CreateLayerGroup("Alert Frame", canvasRect);

        var dim = CreateImage("Dim", alertGroup.transform, new Color(0f, 0f, 0f, 0.58f));
        Stretch(dim.rectTransform, 0f, 0f, 0f, 0f);

        var panel = CreateImage("Panel", alertGroup.transform, new Color(0.055f, 0.065f, 0.08f, 0.98f));
        SetFixedRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(620f, 260f));

        alertTitleText = CreateText("Title", panel.transform, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(alertTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -70f), new Vector2(-32f, -24f));

        alertMessageText = CreateText("Message", panel.transform, 22, FontStyle.Normal, TextAnchor.UpperLeft);
        alertMessageText.color = new Color(0.9f, 0.93f, 0.96f, 1f);
        SetRect(alertMessageText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 84f), new Vector2(-32f, -84f));

        var confirmImage = CreateImage("Confirm Button", panel.transform, new Color(0.1f, 0.42f, 0.92f, 1f));
        SetFixedRect(confirmImage.rectTransform, new Vector2(1f, 0f), new Vector2(-118f, 44f), new Vector2(156f, 52f));

        var confirmButton = confirmImage.gameObject.AddComponent<Button>();
        confirmButton.targetGraphic = confirmImage;
        confirmButton.onClick.AddListener(CloseAlert);

        var colors = confirmButton.colors;
        colors.highlightedColor = new Color(0.18f, 0.52f, 1f, 1f);
        colors.pressedColor = new Color(0.08f, 0.28f, 0.68f, 1f);
        colors.selectedColor = colors.highlightedColor;
        confirmButton.colors = colors;

        alertConfirmText = CreateText("Label", confirmImage.transform, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(alertConfirmText.rectTransform, 0f, 0f, 0f, 0f);
    }

    private void CreateNoticeLayer(RectTransform canvasRect)
    {
        noticeGroup = CreateLayerGroup("Notice Toast", canvasRect);

        var panel = CreateImage("Panel", noticeGroup.transform, new Color(0.07f, 0.08f, 0.1f, 0.94f));
        SetFixedRect(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(640f, 76f));

        var accent = CreateImage("Accent", panel.transform, new Color(0.26f, 0.68f, 1f, 1f));
        SetRect(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(7f, 0f));

        noticeText = CreateText("Message", panel.transform, 22, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(noticeText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 0f), new Vector2(-24f, 0f));
    }

    private void CreateLoadingLayer(RectTransform canvasRect)
    {
        loadingGroup = CreateLayerGroup("Loading Frame", canvasRect);

        var dim = CreateImage("Dim", loadingGroup.transform, new Color(0f, 0f, 0f, 0.46f));
        Stretch(dim.rectTransform, 0f, 0f, 0f, 0f);

        var panel = CreateImage("Panel", loadingGroup.transform, new Color(0.045f, 0.052f, 0.065f, 0.96f));
        SetFixedRect(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 196f));

        spinnerPivot = CreateRect("Spinner", panel.transform);
        SetFixedRect(spinnerPivot, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(88f, 88f));

        var spinnerBar = CreateImage("Bar", spinnerPivot, new Color(0.3f, 0.72f, 1f, 1f));
        SetFixedRect(spinnerBar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(16f, 48f));

        loadingText = CreateText("Message", panel.transform, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(loadingText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(34f, 26f), new Vector2(-34f, 74f));
    }

    private CanvasGroup CreateLayerGroup(string objectName, Transform parent)
    {
        var layerObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasGroup));
        layerObject.transform.SetParent(parent, false);

        var rectTransform = (RectTransform)layerObject.transform;
        Stretch(rectTransform, 0f, 0f, 0f, 0f);

        return layerObject.GetComponent<CanvasGroup>();
    }

    private void UpdateCanvasPose()
    {
        if (canvas == null)
        {
            return;
        }

        var canvasTransform = canvas.transform;
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var cameraTransform = mainCamera.transform;
            var targetPosition = cameraTransform.position
                + cameraTransform.forward * distanceFromCamera
                + cameraTransform.right * horizontalOffset
                + Vector3.up * verticalOffset;

            canvasTransform.position = targetPosition;
            canvasTransform.rotation = Quaternion.LookRotation(targetPosition - cameraTransform.position, Vector3.up);
            return;
        }

        canvasTransform.position = fallbackWorldPosition;
        canvasTransform.rotation = Quaternion.Euler(fallbackWorldEulerAngles);
    }

    private void SetLayerVisible(CanvasGroup group, bool visible)
    {
        if (group == null)
        {
            return;
        }

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        group.gameObject.SetActive(visible);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var rectObject = new GameObject(objectName, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        return (RectTransform)rectObject.transform;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        var image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateText(string objectName, Transform parent, int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        var text = textObject.GetComponent<Text>();
        text.font = GetUIFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, fontSize - 6);
        text.resizeTextMaxSize = fontSize;
        text.color = Color.white;
        return text;
    }

    private static Font GetUIFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static void Stretch(RectTransform rectTransform, float left, float bottom, float right, float top)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(left, bottom);
        rectTransform.offsetMax = new Vector2(-right, -top);
    }

    private static void SetFixedRect(RectTransform rectTransform, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
    }

    private static void SetRect(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
    }
}
