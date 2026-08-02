using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PresenterViewTestHud : MonoBehaviour
{
    [SerializeField] private PresenterViewUIActions uiActions;
    [SerializeField] private PresenterViewTestBootstrap bootstrap;
    [SerializeField] private PresenterViewRenderer rendererController;
    [SerializeField] private bool showHudOnlyForFirstPlayer = true;

    private Button shareButton;
    private Text shareButtonText;
    private Text statusText;
    private Font defaultFont;

    private void Awake()
    {
        ResolveReferences();
        EnsureEventSystem();
        CreateHud();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateHud();
    }

    private void ResolveReferences()
    {
        if (uiActions == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiActions = FindFirstObjectByType<PresenterViewUIActions>();
#else
            uiActions = FindObjectOfType<PresenterViewUIActions>();
#endif
        }

        if (bootstrap == null)
        {
#if UNITY_2023_1_OR_NEWER
            bootstrap = FindFirstObjectByType<PresenterViewTestBootstrap>();
#else
            bootstrap = FindObjectOfType<PresenterViewTestBootstrap>();
#endif
        }

        if (rendererController == null)
        {
#if UNITY_2023_1_OR_NEWER
            rendererController = FindFirstObjectByType<PresenterViewRenderer>();
#else
            rendererController = FindObjectOfType<PresenterViewRenderer>();
#endif
        }
    }

    private void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindFirstObjectByType<EventSystem>() != null)
#else
        if (FindObjectOfType<EventSystem>() != null)
#endif
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void CreateHud()
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("Presenter View Test HUD");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        shareButton = CreateButton(canvasRect, "ShareToggleButton", new Vector2(16f, -16f), new Vector2(190f, 46f));
        shareButton.onClick.AddListener(ToggleSharing);
        shareButtonText = shareButton.GetComponentInChildren<Text>();

        statusText = CreateText(canvasRect, "PresenterViewTestStatus", new Vector2(16f, -70f), new Vector2(360f, 68f), 14);
        statusText.alignment = TextAnchor.UpperLeft;
        statusText.color = Color.white;
    }

    private Button CreateButton(RectTransform parent, string objectName, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = new GameObject(objectName);
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.08f, 0.12f, 0.16f, 0.92f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.08f, 0.12f, 0.16f, 0.92f);
        colors.highlightedColor = new Color(0.14f, 0.22f, 0.28f, 0.95f);
        colors.pressedColor = new Color(0.04f, 0.08f, 0.1f, 0.95f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        Text text = CreateText(rectTransform, "Text", Vector2.zero, Vector2.zero, 15);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        return button;
    }

    private Text CreateText(RectTransform parent, string objectName, Vector2 anchoredPosition, Vector2 size, int fontSize)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Text text = textObject.AddComponent<Text>();
        text.font = defaultFont;
        text.fontSize = fontSize;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    private void ToggleSharing()
    {
        if (!ShouldShowHud())
        {
            return;
        }

        ResolveReferences();
        uiActions?.ToggleSharingMyView();
    }

    private void UpdateHud()
    {
        bool shouldShowHud = ShouldShowHud();

        if (shareButton != null && shareButton.gameObject.activeSelf != shouldShowHud)
        {
            shareButton.gameObject.SetActive(shouldShowHud);
        }

        if (statusText != null && statusText.gameObject.activeSelf != shouldShowHud)
        {
            statusText.gameObject.SetActive(shouldShowHud);
        }

        if (!shouldShowHud)
        {
            return;
        }

        bool isSharing = uiActions != null && uiActions.IsSharingMyView();
        bool isWatching = rendererController != null && rendererController.IsShowingPresenterView;

        if (shareButtonText != null)
        {
            shareButtonText.text = isSharing ? "Stop Sharing" : "Share My View";
        }

        if (shareButton != null)
        {
            Image image = shareButton.GetComponent<Image>();
            if (image != null)
            {
                image.color = isSharing
                    ? new Color(0.72f, 0.16f, 0.16f, 0.94f)
                    : new Color(0.08f, 0.12f, 0.16f, 0.92f);
            }
        }

        if (statusText != null)
        {
            string photonStatus = bootstrap != null
                ? bootstrap.LastStatus
                : "No test bootstrap";
            string viewStatus = isSharing
                ? "Sharing my camera"
                : isWatching
                    ? "Watching shared camera"
                    : "Personal camera";

            statusText.text = $"{photonStatus}\n{viewStatus}\nWASD/QE move, hold Right Mouse to look";
        }
    }

    private bool ShouldShowHud()
    {
        if (!showHudOnlyForFirstPlayer)
        {
            return true;
        }

        NetworkRunner runner = bootstrap != null ? bootstrap.Runner : NetworkManager.runnerInsatance;
        return runner != null &&
               runner.IsRunning &&
               runner.LocalPlayer != PlayerRef.None &&
               runner.LocalPlayer.PlayerId == 1;
    }
}
