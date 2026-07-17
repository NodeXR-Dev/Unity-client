using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction.Input;

// 개인 설정과 생성 그림 기록은 로컬 왼손목에서만 보여 준다.
// 손바닥이 위를 향한 왼손목을 보면 원형 버튼만 나타나며, 버튼을 눌러야 개인 메뉴가 열린다.
[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public class MvpWristSettingsMenu : MonoBehaviour
{
    private const float WristWorldScale = 0.00060f;
    private const float MinWristDistance = 0.16f;
    private const float MaxWristDistance = 0.72f;
    private const float WristGazeDot = 0.72f;
    private const float PalmUpDot = 0.55f;
    private const float HideDelay = 0.42f;

    private readonly List<IHand> _hands = new List<IHand>();
    private Canvas _canvas;
    private RectTransform _rootRect;
    private GameObject _settingsPanel;
    private GameObject _historyPanel;
    private RawImage _historyImage;
    private TMP_Text _historyTitle;
    private TMP_Text _historyIndex;
    private Button _previousButton;
    private Button _nextButton;
    private Button _wristToggleButton;
    private TMP_Text _wristToggleLabel;
    private bool _menuOpen;
    private static Sprite _wristCircleSprite;
    private IHand _leftHand;
    private int _currentHistoryIndex;
    private float _nextHandScan;
    private float _lastLookTime = -99f;

    private void Awake()
    {
        BuildVisuals();
    }

    private void OnEnable()
    {
        _nextHandScan = 0f;
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }


    private void Update()
    {
        if (Time.unscaledTime >= _nextHandScan)
        {
            _nextHandScan = Time.unscaledTime + 0.45f;
            FindLeftHand();
        }

        if (_canvas == null)
            return;

        Camera camera = Camera.main;
        if (camera == null ||
            !TryGetWristAndPalmPose(out Pose wrist, out Pose palm) ||
            !IsPalmFacingUp(palm))
        {
            SetVisible(false);
            return;
        }

        Vector3 toWrist = wrist.position - camera.transform.position;
        float distance = toWrist.magnitude;
        bool lookingAtRaisedWrist =
            distance >= MinWristDistance &&
            distance <= MaxWristDistance &&
            Vector3.Dot(camera.transform.forward, toWrist.normalized) >=
                WristGazeDot;
        if (lookingAtRaisedWrist)
            _lastLookTime = Time.unscaledTime;

        bool visible =
            Time.unscaledTime - _lastLookTime <= HideDelay;
        SetVisible(visible);
        if (!visible)
            return;

        Vector3 towardCamera =
            (camera.transform.position - wrist.position).normalized;
        _canvas.transform.position =
            wrist.position + towardCamera * 0.040f + Vector3.up * 0.024f;
        Vector3 panelForward =
            _canvas.transform.position - camera.transform.position;
        if (panelForward.sqrMagnitude > 0.0001f)
            _canvas.transform.rotation = Quaternion.LookRotation(
                panelForward.normalized,
                Vector3.up);
    }

    private void BuildVisuals()
    {
        GameObject canvasRoot = new GameObject(
            "MvpWristSettingsCanvas", typeof(RectTransform));
        _canvas = canvasRoot.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 820;
        canvasRoot.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasRoot.AddComponent<GraphicRaycaster>();

        _rootRect = canvasRoot.GetComponent<RectTransform>();
        _rootRect.sizeDelta = new Vector2(110f, 110f);
        _rootRect.localScale = Vector3.one * WristWorldScale;

        BuildSettingsPanel(canvasRoot.transform);
        BuildHistoryPanel(canvasRoot.transform);
        BuildWristButton(canvasRoot.transform);

        _menuOpen = false;
        _settingsPanel.SetActive(false);
        _historyPanel.SetActive(false);
        SyncMenuVisuals();
        _canvas.enabled = false;
        RewireCanvas();
    }

    private void BuildWristButton(Transform parent)
    {
        _wristToggleButton = MvpStudentUiFactory.CreateButton(
            parent,
            "WristToggleButton",
            "≡",
            Vector2.zero,
            new Vector2(82f, 82f),
            new Color(0.22f, 0.46f, 0.88f, 1f),
            ToggleMenu,
            30f);
        _wristToggleLabel =
            _wristToggleButton.GetComponentInChildren<TMP_Text>();
        Image toggleImage = _wristToggleButton.GetComponent<Image>();
        if (toggleImage != null)
        {
            toggleImage.sprite = WristCircleSprite;
            toggleImage.type = Image.Type.Simple;
            toggleImage.preserveAspect = true;
        }
        _wristToggleButton.transform.SetAsLastSibling();

        Outline rim =
            _wristToggleButton.GetComponent<Outline>();
        if (rim != null)
        {
            rim.effectColor =
                new Color(0.52f, 0.88f, 1f, 0.72f);
            rim.effectDistance = new Vector2(2f, -2f);
        }
    }

    private void ToggleMenu()
    {
        if (_canvas == null || !_canvas.enabled)
            return;

        _menuOpen = !_menuOpen;
        _historyPanel.SetActive(false);
        _settingsPanel.SetActive(_menuOpen);
        _rootRect.sizeDelta = _menuOpen
            ? new Vector2(560f, 310f)
            : new Vector2(110f, 110f);
        SyncMenuVisuals();
        RewireCanvas();
    }

    private void SyncMenuVisuals()
    {
        if (_wristToggleButton == null)
            return;

        bool historyOpen =
            _menuOpen && _historyPanel != null &&
            _historyPanel.activeSelf;
        _wristToggleButton.gameObject.SetActive(true);
        _wristToggleButton.transform.SetAsLastSibling();

        RectTransform buttonRect =
            _wristToggleButton.transform as RectTransform;
        if (buttonRect != null)
            buttonRect.anchoredPosition = !_menuOpen
                ? Vector2.zero
                : historyOpen
                    ? new Vector2(282f, 190f)
                    : new Vector2(242f, 116f);

        if (_wristToggleLabel != null)
            _wristToggleLabel.text = _menuOpen ? "×" : "≡";
    }

    private void ResetMenuState()
    {
        _menuOpen = false;
        if (_settingsPanel != null)
            _settingsPanel.SetActive(false);
        if (_historyPanel != null)
            _historyPanel.SetActive(false);
        if (_rootRect != null)
            _rootRect.sizeDelta = new Vector2(110f, 110f);
        SyncMenuVisuals();
    }
    private void BuildSettingsPanel(Transform parent)
    {
        Image panel = MvpStudentUiFactory.CreatePanel(
            parent,
            "WristSettingsPanel",
            Vector2.zero,
            new Vector2(560f, 310f),
            new Color(0.022f, 0.08f, 0.18f, 0.97f),
            true);
        _settingsPanel = panel.gameObject;
        AddPanelRim(panel);

        MvpStudentUiFactory.CreateText(
            panel.transform,
            "Title",
            "내 손목 메뉴",
            new Vector2(0f, 116f),
            new Vector2(500f, 44f),
            25f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "ReturnToSeat",
            "의자로 돌아가기",
            new Vector2(-130f, 36f),
            new Vector2(240f, 66f),
            MvpStudentUiFactory.ElectricBlue,
            ReturnToSeat,
            18f);
        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "RecenterWorkspace",
            "작업판 맞추기",
            new Vector2(130f, 36f),
            new Vector2(240f, 66f),
            MvpStudentUiFactory.GlassBlue,
            RecenterWorkspace,
            18f);
        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "OpenHistory",
            "내 그림 기록",
            new Vector2(0f, -54f),
            new Vector2(500f, 68f),
            MvpStudentUiFactory.Cyan,
            ShowHistory,
            19f);
    }

    private void BuildHistoryPanel(Transform parent)
    {
        Image panel = MvpStudentUiFactory.CreatePanel(
            parent,
            "WristHistoryPanel",
            Vector2.zero,
            new Vector2(640f, 440f),
            new Color(0.018f, 0.065f, 0.15f, 0.985f),
            true);
        _historyPanel = panel.gameObject;
        AddPanelRim(panel);

        _historyTitle = MvpStudentUiFactory.CreateText(
            panel.transform,
            "HistoryTitle",
            "내 그림 기록",
            new Vector2(0f, 190f),
            new Vector2(560f, 46f),
            24f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);

        _historyImage = MvpStudentUiFactory.CreateRawImage(
            panel.transform,
            "PrivateSketch",
            new Vector2(0f, 30f),
            new Vector2(470f, 270f));
        _historyImage.color = new Color(0.10f, 0.15f, 0.24f, 1f);

        _previousButton = MvpStudentUiFactory.CreateButton(
            panel.transform,
            "Previous",
            "이전",
            new Vector2(-225f, -158f),
            new Vector2(140f, 60f),
            MvpStudentUiFactory.GlassBlue,
            ShowPreviousHistory,
            18f);
        _historyIndex = MvpStudentUiFactory.CreateText(
            panel.transform,
            "HistoryIndex",
            "0 / 0",
            new Vector2(-45f, -158f),
            new Vector2(190f, 54f),
            18f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.HoloCyan,
            1);
        _nextButton = MvpStudentUiFactory.CreateButton(
            panel.transform,
            "Next",
            "다음",
            new Vector2(135f, -158f),
            new Vector2(140f, 60f),
            MvpStudentUiFactory.GlassBlue,
            ShowNextHistory,
            18f);
        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "Back",
            "메뉴로",
            new Vector2(265f, 190f),
            new Vector2(110f, 48f),
            MvpStudentUiFactory.Mint,
            CloseHistory,
            16f);
    }

    private static void AddPanelRim(Image panel)
    {
        Outline rim = panel.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(
            MvpStudentUiFactory.HoloCyan.r,
            MvpStudentUiFactory.HoloCyan.g,
            MvpStudentUiFactory.HoloCyan.b,
            0.68f);
        rim.effectDistance = new Vector2(2f, -2f);
    }

    private void ShowHistory()
    {
        if (!_menuOpen)
            return;

        MvpClassroomFlow flow =
            FindFirstObjectByType<MvpClassroomFlow>();
        int count = flow != null ? flow.GetPersonalSketchCount() : 0;
        _currentHistoryIndex = Mathf.Max(0, count - 1);
        _settingsPanel.SetActive(false);
        _historyPanel.SetActive(true);
        _rootRect.sizeDelta = new Vector2(640f, 440f);
        RefreshHistory();
        SyncMenuVisuals();
        RewireCanvas();
    }

    private void CloseHistory()
    {
        if (!_menuOpen)
            return;

        _historyPanel.SetActive(false);
        _settingsPanel.SetActive(true);
        _rootRect.sizeDelta = new Vector2(560f, 310f);
        SyncMenuVisuals();
        RewireCanvas();
    }

    private void ShowPreviousHistory()
    {
        _currentHistoryIndex = Mathf.Max(0, _currentHistoryIndex - 1);
        RefreshHistory();
    }

    private void ShowNextHistory()
    {
        MvpClassroomFlow flow =
            FindFirstObjectByType<MvpClassroomFlow>();
        int count = flow != null ? flow.GetPersonalSketchCount() : 0;
        _currentHistoryIndex = Mathf.Min(
            Mathf.Max(0, count - 1),
            _currentHistoryIndex + 1);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        MvpClassroomFlow flow =
            FindFirstObjectByType<MvpClassroomFlow>();
        int count = flow != null ? flow.GetPersonalSketchCount() : 0;
        bool hasHistory = count > 0;
        if (hasHistory)
            _currentHistoryIndex = Mathf.Clamp(
                _currentHistoryIndex, 0, count - 1);
        else
            _currentHistoryIndex = 0;

        Texture texture = hasHistory
            ? flow.GetPersonalSketchTexture(_currentHistoryIndex)
            : null;
        _historyImage.texture = texture;
        _historyImage.color = texture != null
            ? Color.white
            : new Color(0.10f, 0.15f, 0.24f, 1f);
        _historyTitle.text = hasHistory
            ? flow.GetPersonalSketchTitle(_currentHistoryIndex)
            : "저장된 그림이 없어요";
        _historyIndex.text = hasHistory
            ? (_currentHistoryIndex + 1) + " / " + count
            : "0 / 0";
        _previousButton.interactable =
            hasHistory && _currentHistoryIndex > 0;
        _nextButton.interactable =
            hasHistory && _currentHistoryIndex < count - 1;
    }

    private void SetVisible(bool visible)
    {
        if (_canvas == null)
            return;

        if (!visible)
        {
            ResetMenuState();
            _canvas.enabled = false;
            return;
        }

        if (!_canvas.enabled)
        {
            _canvas.enabled = true;
            ResetMenuState();
            RewireCanvas();
        }
    }

    private void HideNow()
    {
        _lastLookTime = -99f;
        SetVisible(false);
    }

    private void ReturnToSeat()
    {
        MvpMeetingRoomPlayerController player =
            FindFirstObjectByType<MvpMeetingRoomPlayerController>();
        player?.RespawnAtAssignedSeat();
        HideNow();
    }

    private void RecenterWorkspace()
    {
        MvpClassroomFlow flow =
            FindFirstObjectByType<MvpClassroomFlow>();
        flow?.RecenterWorkspaceFromWrist();
        HideNow();
    }

    private bool TryGetWristAndPalmPose(
        out Pose wrist,
        out Pose palm)
    {
        wrist = default;
        palm = default;
        if (_leftHand == null ||
            !_leftHand.IsConnected ||
            !_leftHand.IsTrackedDataValid)
            return false;

        bool hasWrist = _leftHand.GetJointPose(
            HandJointId.HandWristRoot, out wrist);
        bool hasPalm = _leftHand.GetJointPose(
            HandJointId.HandPalm, out palm);
        if (!hasWrist && hasPalm)
            wrist = palm;
        if (!hasPalm && hasWrist)
            palm = wrist;
        return hasWrist || hasPalm;
    }

    private bool IsPalmFacingUp(Pose palm)
    {
        Vector3 normal = -(palm.rotation * Vector3.up);
        if (_leftHand != null &&
            _leftHand.GetJointPose(
                HandJointId.HandWristRoot, out Pose wrist) &&
            _leftHand.GetJointPose(
                HandJointId.HandIndex0, out Pose index) &&
            _leftHand.GetJointPose(
                HandJointId.HandPinky0, out Pose pinky))
        {
            normal = Vector3.Cross(
                index.position - wrist.position,
                pinky.position - wrist.position).normalized;
        }

        return Vector3.Dot(normal, Vector3.up) >= PalmUpDot;
    }

    private void FindLeftHand()
    {
        _hands.Clear();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            IHand candidate = behaviour as IHand;
            if (candidate == null)
                continue;

            try
            {
                if (candidate.Handedness == Handedness.Left)
                    _hands.Add(candidate);
            }
            catch
            {
                // 초기화 중인 보조 hand source는 다음 스캔에서 다시 확인한다.
            }
        }

        _leftHand = null;
        foreach (IHand candidate in _hands)
        {
            if (_leftHand == null || PreferHand(candidate, _leftHand))
                _leftHand = candidate;
        }
    }

    private static bool PreferHand(IHand candidate, IHand current)
    {
        bool candidateValid =
            candidate.IsConnected && candidate.IsTrackedDataValid;
        bool currentValid =
            current.IsConnected && current.IsTrackedDataValid;
        if (candidateValid != currentValid)
            return candidateValid;

        return candidate.GetType().Name == "Hand" &&
            current.GetType().Name != "Hand";
    }

    private void RewireCanvas()
    {
        MvpXrInteractionBridge bridge =
            FindFirstObjectByType<MvpXrInteractionBridge>();
        bridge?.WireScene();
    }

    private static Sprite WristCircleSprite
    {
        get
        {
            if (_wristCircleSprite != null)
                return _wristCircleSprite;

            const int size = 64;
            float center = (size - 1f) * 0.5f;
            float radius = center - 1f;
            Texture2D texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false);
            texture.name = "MvpWristCircleTexture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        new Vector2(center, center));
                    float alpha = Mathf.Clamp01(radius + 1f - distance);
                    pixels[y * size + x] = new Color32(
                        255, 255, 255,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _wristCircleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
            _wristCircleSprite.name = "MvpWristCircleSprite";
            return _wristCircleSprite;
        }
    }
}