using UnityEngine;
using UnityEngine.UI;

public class PresenterViewWorldShareButton : MonoBehaviour
{
    [SerializeField] private PresenterViewUIActions uiActions;
    [SerializeField] private Button button;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Text label;
    [SerializeField] private string startSharingText = "Share My View";
    [SerializeField] private string stopSharingText = "Stop Sharing";
    [SerializeField] private Color normalColor = new Color(0.08f, 0.12f, 0.16f, 0.92f);
    [SerializeField] private Color sharingColor = new Color(0.72f, 0.16f, 0.16f, 0.94f);

    private bool listenerRegistered;
    private bool hasVisualState;
    private bool lastSharingState;

    private void Awake()
    {
        ResolveReferences();
        RegisterButtonListener();
        UpdateVisualState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        RegisterButtonListener();
        UpdateVisualState();
    }

    private void OnDisable()
    {
        if (button != null && listenerRegistered)
        {
            button.onClick.RemoveListener(ToggleSharing);
            listenerRegistered = false;
        }
    }

    private void Update()
    {
        ResolveReferences();
        UpdateVisualState();
    }

    public void ToggleSharing()
    {
        ResolveReferences();
        uiActions?.ToggleSharingMyView();
        UpdateVisualState();
    }

    private void ResolveReferences()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
        }

        if (label == null)
        {
            label = GetComponentInChildren<Text>(true);
        }

        if (uiActions == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiActions = FindFirstObjectByType<PresenterViewUIActions>();
#else
            uiActions = FindObjectOfType<PresenterViewUIActions>();
#endif
        }
    }

    private void RegisterButtonListener()
    {
        if (button == null || listenerRegistered)
        {
            return;
        }

        button.onClick.AddListener(ToggleSharing);
        listenerRegistered = true;
    }

    private void UpdateVisualState()
    {
        bool isSharing = uiActions != null && uiActions.IsSharingMyView();
        if (hasVisualState && lastSharingState == isSharing)
        {
            return;
        }

        hasVisualState = true;
        lastSharingState = isSharing;

        if (label != null)
        {
            label.text = isSharing ? stopSharingText : startSharingText;
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = isSharing ? sharingColor : normalColor;
        }

        if (button != null)
        {
            Color baseColor = isSharing ? sharingColor : normalColor;
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.28f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
        }
    }
}
