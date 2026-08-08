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
    private Button _shareButton;
    private bool _shareHighlighted;   // 공유 중이면 hover 없이도 아이콘을 밝게 유지
    private PresenterViewUIActions _shareActions;
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
        BuildPalmHint();
    }

    private void OnEnable()
    {
        _nextHandScan = 0f;
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        if (_palmHintCanvas != null)
            Destroy(_palmHintCanvas.gameObject);
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
            !TryGetWristAndPalmPose(out Pose wrist, out Pose palm))
        {
            SetVisible(false);
            SetPalmHint(false, Vector3.zero, null);
            return;
        }

        Vector3 toWrist = wrist.position - camera.transform.position;
        float distance = toWrist.magnitude;
        bool lookingAtRaisedWrist =
            distance >= MinWristDistance &&
            distance <= MaxWristDistance &&
            Vector3.Dot(camera.transform.forward, toWrist.normalized) >=
                WristGazeDot;

        // 손목은 보고 있는데 손바닥이 아직 위를 향하지 않았다면, 메뉴가 왜 안 열리는지
        // 알 길이 없다(예전엔 그냥 아무것도 안 떴다). 이때만 안내를 띄운다.
        if (!IsPalmFacingUp(palm))
        {
            SetVisible(false);
            SetPalmHint(lookingAtRaisedWrist, wrist.position, camera);
            return;
        }
        SetPalmHint(false, Vector3.zero, null);

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

    // ── 손바닥 안내 알약 ──────────────────────────────────────
    // 손목은 쳐다보는데 손바닥이 아래를 향하면 메뉴가 안 열린다. 그 순간에만 뜬다.
    // 배경은 발화 상태 바와 같은 디자이너 셰이더(둥근 알약 + 그라디언트)를 재사용한다.
    private Canvas _palmHintCanvas;
    private RectTransform _palmHintRect;
    private Image _palmHintIcon;

    private void BuildPalmHint()
    {
        GameObject root = new GameObject(
            "MvpPalmHintCanvas", typeof(RectTransform));
        _palmHintCanvas = root.AddComponent<Canvas>();
        _palmHintCanvas.renderMode = RenderMode.WorldSpace;
        _palmHintCanvas.sortingOrder = 815;
        root.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ConstantPixelSize;
        root.AddComponent<GraphicRaycaster>();

        _palmHintRect = root.GetComponent<RectTransform>();
        _palmHintRect.sizeDelta = new Vector2(520f, 108f);
        _palmHintRect.localScale = Vector3.one * WristWorldScale;

        Image pill = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "PalmHintPill",
            Vector2.zero,
            new Vector2(520f, 108f),
            new Color(0.30f, 0.29f, 0.25f, 0.96f),
            true);
        pill.raycastTarget = false;

        Shader border = Shader.Find("UI/RotatingGradientBorder");
        if (border != null)
        {
            RotatingGradientBorderUI gradient =
                pill.gameObject.AddComponent<RotatingGradientBorderUI>();
            gradient.shader = border;
            gradient.rotationSpeed = 0.08f;   // 거의 정지 — 시안엔 회전이 없다
            gradient.borderWidth = 2f;
            gradient.cornerRadius = 54f;      // 높이의 절반 = 완전한 알약
            gradient.bgAngle = 270f;          // 위(올리브) → 아래(금빛)
            gradient.gradientResolution = 200;
            gradient.borderGradient = MakeHintGradient(
                new Color(0.72f, 0.70f, 0.62f, 1f),
                new Color(0.86f, 0.78f, 0.58f, 1f), 0.55f, 0.55f);
            gradient.bgGradient = MakeHintGradient(
                new Color(0.49f, 0.48f, 0.42f, 1f),
                new Color(0.62f, 0.55f, 0.36f, 1f), 1f, 1f);
        }

        // 손 아이콘 자리. 폰트에 손 글리프(✋)가 없어 스프라이트를 받으면 넣는다.
        _palmHintIcon = MvpStudentUiFactory.CreatePanel(
            pill.transform,
            "HandIcon",
            new Vector2(-196f, 0f),
            new Vector2(76f, 76f),
            Color.white,
            false);
        _palmHintIcon.raycastTarget = false;
        _palmHintIcon.preserveAspect = true;
        Sprite hand = Resources.Load<Sprite>("HandPanel/hand_open");
        if (hand != null)
            _palmHintIcon.sprite = hand;
        else
            _palmHintIcon.color = new Color(1f, 1f, 1f, 0f);   // 아직 없으면 숨긴다

        MvpStudentUiFactory.CreateText(
            pill.transform,
            "PalmHintLabel",
            "손바닥을 위로 펼쳐 주세요",
            new Vector2(28f, 0f),
            new Vector2(400f, 56f),
            30f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);

        root.SetActive(false);
    }

    // 안내를 손목 옆에 띄우거나 감춘다.
    private void SetPalmHint(bool visible, Vector3 wristPosition, Camera camera)
    {
        if (_palmHintCanvas == null)
            return;

        if (!visible || camera == null)
        {
            if (_palmHintCanvas.gameObject.activeSelf)
                _palmHintCanvas.gameObject.SetActive(false);
            return;
        }

        if (!_palmHintCanvas.gameObject.activeSelf)
            _palmHintCanvas.gameObject.SetActive(true);

        Vector3 towardCamera =
            (camera.transform.position - wristPosition).normalized;
        _palmHintCanvas.transform.position =
            wristPosition + towardCamera * 0.040f + Vector3.up * 0.030f;
        Vector3 forward =
            _palmHintCanvas.transform.position - camera.transform.position;
        if (forward.sqrMagnitude > 0.0001f)
            _palmHintCanvas.transform.rotation =
                Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static Gradient MakeHintGradient(
        Color a, Color b, float alphaA, float alphaB)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
            new[] { new GradientAlphaKey(alphaA, 0f), new GradientAlphaKey(alphaB, 1f) });
        return g;
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
            ? new Vector2(PanelWidth, PanelHeight)
            : new Vector2(110f, 110f);
        if (_menuOpen)
            RefreshShareButton();   // 열 때마다 공유 상태를 다시 읽는다
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
        // 손 패널은 6개 아이콘이 판을 꽉 채우므로, 닫기 버튼은 판 바깥(아래)에 둔다.
        // 예전 좌표(242,116)는 옛 가로형 패널 기준이라 지금은 판 한가운데를 덮었다.
        if (buttonRect != null)
            buttonRect.anchoredPosition = !_menuOpen
                ? Vector2.zero
                : historyOpen
                    ? new Vector2(282f, 190f)
                    : new Vector2(0f, -(PanelHeight * 0.5f + 52f));

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
    // 디자이너 손 패널(319x439)을 그대로 쓴다. 6개 버튼이 배경 이미지에 이미 그려져 있어서,
    // 우리는 그 위에 투명한 히트 영역만 얹고 hover 시 배경 스프라이트를 해당 강조본으로 바꾼다.
    // 원 중심 좌표는 강조본과 기본본의 픽셀 차이로 실측했다(아래 표는 패널 스케일 반영값).
    private const float PanelWidth  = 400f;
    private const float PanelHeight = 550f;
    private const float PanelScale  = PanelWidth / 319f;   // 소스 스프라이트 → 패널 크기 비율
    private const float HitDiameter = 105f * PanelScale;   // 원 지름 실측 105px

    private Image _panelImage;
    private Sprite _panelDefault;
    private Sprite _panelNodeArray;
    private Sprite _panelRespawn;
    private Sprite _panelShare;
    private Sprite _panelHistory;
    private Sprite _panelMic;

    private void BuildSettingsPanel(Transform parent)
    {
        LoadPanelSprites();

        GameObject go = new GameObject(
            "WristSettingsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        _panelImage = go.GetComponent<Image>();
        _panelImage.sprite = _panelDefault;
        _panelImage.type = Image.Type.Simple;
        _panelImage.preserveAspect = true;
        _panelImage.raycastTarget = true;   // 판 자체가 포크 표면이 된다
        _settingsPanel = go;

        // (라벨, 위치, hover 스프라이트, 동작)
        CreatePanelHit("NodeArray", new Vector2(-82f, 166f), _panelNodeArray, RecenterWorkspace);
        CreatePanelHit("Respawn",   new Vector2( 81f, 166f), _panelRespawn,   ReturnToSeat);
        _shareButton =
        CreatePanelHit("Share",     new Vector2(-83f,   4f), _panelShare,     ToggleShareView);
        CreatePanelHit("History",   new Vector2( 81f,   3f), _panelHistory,   ShowHistory);
        CreatePanelHit("Mic",       new Vector2(-83f,-161f), _panelMic,       ToggleVoice);
        CreatePanelHit("LeaveRoom", new Vector2( 81f,-161f), null,            LeaveRoomFromWrist);
    }

    private void LoadPanelSprites()
    {
        if (_panelDefault != null)
            return;
        _panelDefault   = Resources.Load<Sprite>("HandPanel/pannel_default");
        _panelNodeArray = Resources.Load<Sprite>("HandPanel/pannel_nodearray");
        _panelRespawn   = Resources.Load<Sprite>("HandPanel/pannel_respawn");
        _panelShare     = Resources.Load<Sprite>("HandPanel/pannel_share");
        _panelHistory   = Resources.Load<Sprite>("HandPanel/pannel_history");
        _panelMic       = Resources.Load<Sprite>("HandPanel/pannel_mic");
    }

    // 배경 그림 위의 투명 버튼. 눌리는 영역은 원 그림과 같은 크기다.
    private Button CreatePanelHit(
        string name, Vector2 position, Sprite hoverSprite, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(
            "Hit_" + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(_settingsPanel.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(HitDiameter, HitDiameter);

        Image image = go.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);   // 보이지 않지만 포크/레이는 받는다
        image.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;   // 강조는 배경 스프라이트 교체로 한다
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (MvpAudioCue.Instance != null)
                MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick, 0.7f);
            action?.Invoke();
        });

        MvpWristPanelHover hover = go.AddComponent<MvpWristPanelHover>();
        hover.Configure(this, hoverSprite);
        return button;
    }

    // 히트 영역이 hover/exit 될 때 배경 스프라이트를 갈아 끼운다.
    internal void SetPanelHighlight(Sprite sprite)
    {
        if (_panelImage == null)
            return;

        // 손을 뗐는데(sprite=null) 공유가 켜져 있으면 공유 아이콘 강조를 유지한다.
        if (sprite == null && _shareHighlighted)
        {
            _panelImage.sprite = _panelShare;
            return;
        }
        _panelImage.sprite = sprite != null ? sprite : _panelDefault;
    }

    private void ToggleVoice()
    {
        MvpClassroomFlow flow = FindFirstObjectByType<MvpClassroomFlow>();
        if (flow != null)
            flow.ToggleVoiceInput();
    }

    private void LeaveRoomFromWrist()
    {
        MvpClassroomFlow flow = FindFirstObjectByType<MvpClassroomFlow>();
        if (flow != null)
            flow.RequestLeaveRoom();
    }

    // ── 화면 공유 ─────────────────────────────────────────────
    // 실제 동작은 PresenterViewUIActions 가 맡는다(발표자 지정·시점 동기화·렌더).
    // 손 패널은 아이콘만 있어서 안내 문구는 작업판 메시지로 대신 띄운다.
    private void ToggleShareView()
    {
        PresenterViewUIActions actions = ResolveShareActions();
        if (actions == null)
        {
            FindFirstObjectByType<MvpClassroomFlow>()?.SetWorkspaceMessage(
                "화면 공유를 아직 쓸 수 없어요.",
                MvpStudentUiFactory.Amber);
            return;
        }

        actions.ToggleSharingMyView();
        RefreshShareButton();
    }

    // Fusion 세션이 실제로 Spawn 되어 네트워크 상태를 읽을 수 있는 상태인지.
    private static bool IsPresenterSessionReady()
    {
        PresenterViewSession session = PresenterViewSession.Instance;
        return session != null &&
               session.Object != null &&
               session.Object.IsValid &&
               session.Runner != null &&
               session.Runner.IsRunning;
    }

    private PresenterViewUIActions ResolveShareActions()
    {
        if (_shareActions == null)
            _shareActions = FindFirstObjectByType<PresenterViewUIActions>();
        return _shareActions;
    }

    // 손 패널은 라벨이 없는 아이콘 판이라, 상태는 "누를 수 있는지 + 공유 중 강조"로만 보여 준다.
    private void RefreshShareButton()
    {
        if (_shareButton == null)
            return;

        PresenterViewUIActions actions = ResolveShareActions();
        // 씬에 PresenterViewSession 오브젝트가 있어도 Fusion 이 Spawn 하기 전엔
        // [Networked] 프로퍼티를 읽을 수 없다(InvalidOperationException). Instance 존재만으로
        // 판단하면 오프라인에서 IsSharingMyView() 가 그대로 터진다 → Spawn 여부까지 확인한다.
        bool online = IsPresenterSessionReady();
        if (actions == null || !online)
        {
            _shareButton.interactable = false;
            _shareHighlighted = false;
            SetPanelHighlight(null);
            return;
        }

        _shareButton.interactable = true;
        // 공유 중이면 hover 가 아니어도 공유 아이콘을 계속 밝게 둔다(지금 켜져 있다는 표시).
        _shareHighlighted = actions.IsSharingMyView();
        SetPanelHighlight(_shareHighlighted ? _panelShare : null);
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
// 손 패널의 원형 히트 영역 하나. 손끝/레이가 올라오면 배경 스프라이트를 자기 강조본으로 바꾼다.
// 아이콘이 배경 그림에 통째로 그려져 있어서, 개별 버튼 색을 바꾸는 대신 판 전체를 교체한다.
[DisallowMultipleComponent]
public class MvpWristPanelHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler
{
    private MvpWristSettingsMenu _menu;
    private Sprite _hoverSprite;

    public void Configure(MvpWristSettingsMenu menu, Sprite hoverSprite)
    {
        _menu = menu;
        _hoverSprite = hoverSprite;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_menu != null)
            _menu.SetPanelHighlight(_hoverSprite);
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_menu != null)
            _menu.SetPanelHighlight(null);
    }

    private void OnDisable()
    {
        if (_menu != null)
            _menu.SetPanelHighlight(null);
    }
}
