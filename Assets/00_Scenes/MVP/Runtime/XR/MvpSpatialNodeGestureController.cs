using System;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MvpSpatialNodeGestureController : MonoBehaviour
{
    private enum CreationPhase
    {
        Idle,
        Charging,
        WaitForPalm,
        Preview,
        Cooldown
    }

    [Header("그래프")]
    [SerializeField] private GraphManager _graphManager;

    [Header("제스처")]
    [SerializeField] private float _fistHoldSeconds = 2f;
    [SerializeField] private float _fistDetectionDelay = 0.22f;
    [SerializeField] private float _fistReleaseGrace = 0.18f;
    [SerializeField] private float _commitHoldSeconds = 0.45f;
    [SerializeField] private float _openRearmSeconds = 0.3f;
    [SerializeField] private float _closedTipDistance = 0.065f;
    [SerializeField] private float _palmUpDot = 0.55f;
    // 실측 교정(이슈 3): 온디바이스에서 계산된 손바닥 법선이 실제 손바닥 방향과 반대로 정렬돼
    // "손바닥 아래"에서 발동했다. true 면 최종 법선을 뒤집어 "손바닥 위"에서 발동한다.
    // (하드웨어/SDK 가 바뀌어 반대가 되면 인스펙터에서 이 값을 끄면 된다.)
    [SerializeField] private bool _invertPalmNormal = true;
    [SerializeField] private float _previewPalmOffset = 0.08f;
    [SerializeField] private float _feedbackVerticalOffset = 0.12f;

    [Header("에디터 대체 입력")]
    [SerializeField] private bool _enableEditorFallback = true;
    [SerializeField] private float _fallbackDistance = 0.82f;

    private readonly List<IHand> _hands = new List<IHand>();
    private CreationPhase _phase;
    private IHand _activeHand;
    private Handedness _activeHandedness;
    private float _holdTime;
    private float _fistCandidateTime;
    private float _fistLostTime;
    private float _commitHoldTime;
    private float _openTime;
    private float _nextHandScanTime;
    private bool _stableFist;
    private bool _creationEnabled;

    private Canvas _feedbackCanvas;
    private Image _progressRing;
    private TMP_Text _feedbackText;
    private RectTransform _previewCard;
    private TMP_Text _previewLabel;
    private Vector3 _previewPosition;

    public event Action<string, Vector3> OnRootNodeCreated;
    public event Action<string> OnGuidanceChanged;


    private void Awake()
    {
        ResolveReferences();
        BuildFeedbackVisuals();
        HideAllVisuals();
    }

    private void OnDisable()
    {
        ResetGesture();
    }

    private void Update()
    {
        if (!_creationEnabled)
        {
            HideAllVisuals();
            return;
        }

        if (TryReadTrackedHand(out IHand hand, out Handedness handedness,
                out Pose palmPose, out bool fist, out bool palmUp))
        {
            ProcessGesture(
                hand,
                handedness,
                palmPose.position,
                fist,
                palmUp,
                false);
            return;
        }

#if UNITY_EDITOR
        if (_enableEditorFallback)
            ProcessEditorFallback();
        else
            HideAllVisuals();
#else
        HideAllVisuals();
#endif
    }

    public void SetCreationEnabled(bool enabled)
    {
        _creationEnabled = enabled;
        if (!enabled)
            ResetGesture();
        else
            Announce("원하는 공간에서 주먹을 2초 동안 쥐어 보세요.");
    }

    public void Configure(GraphManager graphManager)
    {
        if (graphManager != null)
            _graphManager = graphManager;
    }


    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
    }

    private bool TryReadTrackedHand(
        out IHand hand,
        out Handedness handedness,
        out Pose palmPose,
        out bool fist,
        out bool palmUp)
    {
        hand = null;
        handedness = Handedness.Left;
        palmPose = default;
        fist = false;
        palmUp = false;

        if (Time.unscaledTime >= _nextHandScanTime)
        {
            ScanHands();
            // 손을 못 찾으면 짧게(0.5s) 재시도, 찾으면 길게(1.5s) 유지.
            // (기존의 `|| _hands.Count == 0` 는 손이 없을 때 매 프레임 전체 씬 스캔을 유발했다.)
            _nextHandScanTime = Time.unscaledTime +
                (_hands.Count == 0 ? 0.5f : 1.5f);
        }

        if (_activeHand != null &&
            _activeHand.IsTrackedDataValid &&
            TryGetPalmPose(_activeHand, out palmPose))
        {
            hand = _activeHand;
            handedness = _activeHandedness;
        }
        else
        {
            _activeHand = null;
            foreach (IHand candidate in _hands)
            {
                if (candidate == null ||
                    !candidate.IsConnected ||
                    !candidate.IsTrackedDataValid ||
                    !TryGetPalmPose(candidate, out palmPose))
                    continue;

                bool candidateFist = IsFist(candidate, palmPose.position);
                if (_phase == CreationPhase.Idle && !candidateFist)
                    continue;

                hand = candidate;
                handedness = candidate.Handedness;
                _activeHand = candidate;
                _activeHandedness = handedness;
                break;
            }
        }

        if (hand == null)
            return false;

        fist = IsFist(hand, palmPose.position);
        palmUp = !fist && IsPalmUp(hand, handedness, palmPose);
        return true;
    }

    private void ScanHands()
    {
        _hands.Clear();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Dictionary<Handedness, IHand> best =
            new Dictionary<Handedness, IHand>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (!(behaviour is IHand candidate))
                continue;

            Handedness side;
            try
            {
                side = candidate.Handedness;
            }
            catch
            {
                continue;
            }

            if (!best.TryGetValue(side, out IHand current) ||
                PreferHand(candidate, current))
                best[side] = candidate;
        }

        foreach (IHand candidate in best.Values)
            _hands.Add(candidate);
    }

    private static bool PreferHand(IHand candidate, IHand current)
    {
        if (candidate == null) return false;
        if (current == null) return true;

        bool candidateValid =
            candidate.IsConnected && candidate.IsTrackedDataValid;
        bool currentValid =
            current.IsConnected && current.IsTrackedDataValid;
        if (candidateValid != currentValid)
            return candidateValid;

        return candidate.GetType().Name == "Hand" &&
               current.GetType().Name != "Hand";
    }

    private void ProcessGesture(
        IHand hand,
        Handedness handedness,
        Vector3 palmPosition,
        bool rawFist,
        bool palmUp,
        bool editorFallback)
    {
        _activeHand = hand;
        _activeHandedness = handedness;

        bool fist = UpdateStableFist(rawFist);

        switch (_phase)
        {
            case CreationPhase.Idle:
                if (fist)
                {
                    _phase = CreationPhase.Charging;
                    _holdTime = _fistCandidateTime;
                    _commitHoldTime = 0f;
                    Announce("주먹이 인식됐어요. 2초 동안 그대로 유지하세요.");
                }
                break;

            case CreationPhase.Charging:
                if (!fist)
                {
                    ResetGesture();
                    Announce("손을 편 뒤 원하는 공간에서 주먹을 천천히 쥐어 주세요.");
                    break;
                }

                _holdTime += Time.unscaledDeltaTime;
                ShowCharging(
                    palmPosition,
                    Mathf.Clamp01(_holdTime /
                                  Mathf.Max(0.2f, _fistHoldSeconds)));

                if (_holdTime >= _fistHoldSeconds)
                {
                    _phase = CreationPhase.WaitForPalm;
                    HideCharging();
                    Announce("준비됐어요. 손바닥을 위로 펼쳐 주세요.");
                }
                break;

            case CreationPhase.WaitForPalm:
                ShowReady(palmPosition);
                if (palmUp || editorFallback && !rawFist)
                {
                    _phase = CreationPhase.Preview;
                    _commitHoldTime = 0f;
                    _previewPosition =
                        GetPreviewPosition(palmPosition, editorFallback);
                    ShowPreview(_previewPosition, 0f);
                    Announce("손을 원하는 위치로 옮긴 뒤 주먹을 잠깐 유지하세요.");
                }
                break;

            case CreationPhase.Preview:
                _previewPosition =
                    GetPreviewPosition(palmPosition, editorFallback);

                if (fist)
                    _commitHoldTime += Time.unscaledDeltaTime;
                else
                    _commitHoldTime = 0f;

                float confirmProgress = Mathf.Clamp01(
                    _commitHoldTime /
                    Mathf.Max(0.15f, _commitHoldSeconds));
                ShowPreview(_previewPosition, confirmProgress);

                if (_commitHoldTime >= _commitHoldSeconds)
                    CommitRootNode(_previewPosition);
                break;

            case CreationPhase.Cooldown:
                HideAllVisuals();
                if (!rawFist && !fist)
                {
                    _openTime += Time.unscaledDeltaTime;
                    if (_openTime >= _openRearmSeconds)
                    {
                        _phase = CreationPhase.Idle;
                        _activeHand = null;
                        _openTime = 0f;
                        Announce("새 노드를 만들려면 주먹을 2초 동안 유지하세요.");
                    }
                }
                else
                {
                    _openTime = 0f;
                }
                break;
        }
    }

    private bool UpdateStableFist(bool detected)
    {
        if (detected)
        {
            _fistCandidateTime += Time.unscaledDeltaTime;
            _fistLostTime = 0f;
            if (!_stableFist &&
                _fistCandidateTime >= _fistDetectionDelay)
                _stableFist = true;
        }
        else
        {
            _fistCandidateTime = 0f;
            if (_stableFist)
            {
                _fistLostTime += Time.unscaledDeltaTime;
                if (_fistLostTime >= _fistReleaseGrace)
                {
                    _stableFist = false;
                    _fistLostTime = 0f;
                }
            }
            else
            {
                _fistLostTime = 0f;
            }
        }

        return _stableFist;
    }


#if UNITY_EDITOR
    private void ProcessEditorFallback()
    {
        bool keyPressed = ReadEditorGestureKey();
        Vector3 cursorPosition = GetEditorCursorPosition();

        if (_phase == CreationPhase.Idle ||
            _phase == CreationPhase.Charging)
        {
            ProcessGesture(
                null,
                Handedness.Right,
                cursorPosition,
                keyPressed,
                false,
                true);
            return;
        }

        if (_phase == CreationPhase.WaitForPalm)
        {
            ProcessGesture(
                null,
                Handedness.Right,
                cursorPosition,
                keyPressed,
                !keyPressed,
                true);
            return;
        }

        ProcessGesture(
            null,
            Handedness.Right,
            cursorPosition,
            keyPressed,
            true,
            true);
    }

    private static bool ReadEditorGestureKey()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null &&
               Keyboard.current.nKey.isPressed;
#else
        return Input.GetKey(KeyCode.N);
#endif
    }

    private Vector3 GetEditorCursorPosition()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return transform.position + Vector3.forward * _fallbackDistance;

        Vector2 screenPosition;
#if ENABLE_INPUT_SYSTEM
        screenPosition = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        screenPosition = Input.mousePosition;
#endif
        Ray ray = camera.ScreenPointToRay(screenPosition);
        return ray.GetPoint(_fallbackDistance);
    }
#endif

    private bool IsFist(IHand hand, Vector3 palmPosition)
    {
        if (hand == null) return false;

        float scale = Mathf.Clamp(hand.Scale, 0.7f, 1.5f);
        float threshold = _closedTipDistance * scale;
        HandJointId[] tips =
        {
            HandJointId.HandIndexTip,
            HandJointId.HandMiddleTip,
            HandJointId.HandRingTip,
            HandJointId.HandPinkyTip
        };

        int closed = 0;
        bool indexClosed = false;
        foreach (HandJointId tip in tips)
        {
            if (!hand.GetJointPose(tip, out Pose tipPose) ||
                Vector3.Distance(tipPose.position, palmPosition) > threshold)
                continue;

            closed++;
            if (tip == HandJointId.HandIndexTip)
                indexClosed = true;
        }

        // 가리키기처럼 나머지 세 손가락만 접힌 자세는 주먹으로 보지 않는다.
        return indexClosed && closed >= 3;
    }


    private bool IsPalmUp(
        IHand hand,
        Handedness handedness,
        Pose palmPose)
    {
        Vector3 normal = palmPose.rotation * Vector3.up;

        if (hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist) &&
            hand.GetJointPose(HandJointId.HandIndex0, out Pose index) &&
            hand.GetJointPose(HandJointId.HandPinky0, out Pose pinky))
        {
            normal = Vector3.Cross(
                index.position - wrist.position,
                pinky.position - wrist.position).normalized;
            if (handedness == Handedness.Left)
                normal = -normal;
        }

        // 양손 모두 동일 부호로 어긋나므로 최종 법선을 한 번 뒤집어 교정한다(이슈 3).
        if (_invertPalmNormal)
            normal = -normal;

        return Vector3.Dot(normal, Vector3.up) >= _palmUpDot;
    }

    private static bool TryGetPalmPose(IHand hand, out Pose pose)
    {
        if (hand.GetJointPose(HandJointId.HandPalm, out pose))
            return true;
        return hand.GetJointPose(HandJointId.HandWristRoot, out pose);
    }

    private Vector3 GetPreviewPosition(
        Vector3 palmPosition,
        bool editorFallback)
    {
        if (editorFallback)
            return palmPosition;

        return palmPosition + Vector3.up * _previewPalmOffset;
    }

    private void CommitRootNode(Vector3 position)
    {
        ResolveReferences();
        if (_graphManager == null)
        {
            Announce("그래프를 찾지 못했어요. 잠시 뒤 다시 시도해 주세요.");
            ResetGesture();
            return;
        }

        string nodeId = _graphManager.RequestCreateRootPropertyNode();
        if (string.IsNullOrEmpty(nodeId))
        {
            Announce("노드를 만들지 못했어요. 다시 시도해 주세요.");
            ResetGesture();
            return;
        }

        _graphManager.RequestUpdateNodeText(nodeId, "아이디어");
        _graphManager.RequestMoveNode(nodeId, position);
        OnRootNodeCreated?.Invoke(nodeId, position);

        _phase = CreationPhase.Cooldown;
        HideAllVisuals();
        Announce("노드가 생성됐어요. 이름을 바꾸거나 +로 세부 조건을 추가하세요.");
    }

    private void BuildFeedbackVisuals()
    {
        GameObject root = new GameObject(
            "MvpGestureFeedback",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        root.transform.SetParent(transform, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(440f, 190f);

        _feedbackCanvas = root.GetComponent<Canvas>();
        _feedbackCanvas.renderMode = RenderMode.WorldSpace;
        _feedbackCanvas.sortingOrder = 420;
        _feedbackCanvas.worldCamera = Camera.main;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 2f;

        Image back = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "RingBack",
            new Vector2(0f, 32f),
            new Vector2(112f, 112f),
            new Color(0.12f, 0.20f, 0.38f, 0.82f),
            false);
        back.raycastTarget = false;

        _progressRing = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "RingProgress",
            new Vector2(0f, 32f),
            new Vector2(100f, 100f),
            MvpStudentUiFactory.HoloCyan,
            false);
        _progressRing.type = Image.Type.Filled;
        _progressRing.fillMethod = Image.FillMethod.Radial360;
        _progressRing.fillOrigin = 2;
        _progressRing.fillClockwise = true;
        _progressRing.fillAmount = 0f;
        _progressRing.raycastTarget = false;

        _feedbackText = MvpStudentUiFactory.CreateText(
            root.transform,
            "GestureText",
            "주먹 유지",
            new Vector2(0f, -54f),
            new Vector2(340f, 62f),
            25f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            2);

        Image preview = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "NodePreview",
            Vector2.zero,
            new Vector2(420f, 116f),
            new Color(0.08f, 0.16f, 0.34f, 0.96f),
            true);
        Outline outline = preview.gameObject.AddComponent<Outline>();
        outline.effectColor = MvpStudentUiFactory.HoloCyan;
        outline.effectDistance = new Vector2(3f, -3f);
        _previewCard = preview.rectTransform;

        _previewLabel = MvpStudentUiFactory.CreateText(
            preview.transform,
            "PreviewLabel",
            "원하는 위치에서 주먹을 쥐세요",
            Vector2.zero,
            new Vector2(300f, 112f),
            24f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            2);

        root.transform.localScale = Vector3.one * 0.00085f;
    }

    private void ShowCharging(Vector3 position, float progress)
    {
        if (_feedbackCanvas == null) return;
        _feedbackCanvas.gameObject.SetActive(true);
        _progressRing.gameObject.SetActive(true);
        _progressRing.fillAmount = progress;
        _feedbackText.text =
            progress >= 0.98f
                ? "준비 완료"
                : "주먹 유지  " + Mathf.CeilToInt(
                    Mathf.Max(0f, _fistHoldSeconds - _holdTime)) + "초";
        _previewCard.gameObject.SetActive(false);
        PositionFeedback(position);
    }

    private void ShowReady(Vector3 position)
    {
        if (_feedbackCanvas == null) return;
        _feedbackCanvas.gameObject.SetActive(true);
        _progressRing.gameObject.SetActive(true);
        _progressRing.fillAmount = 1f;
        _feedbackText.text = "손바닥을 위로 펼쳐 주세요";
        _previewCard.gameObject.SetActive(false);
        PositionFeedback(position);
    }

    private void ShowPreview(Vector3 position, float confirmProgress)
    {
        if (_feedbackCanvas == null) return;
        _feedbackCanvas.gameObject.SetActive(true);
        _progressRing.gameObject.SetActive(false);
        _feedbackText.gameObject.SetActive(false);
        _previewCard.gameObject.SetActive(true);

        if (_previewLabel != null)
            _previewLabel.text = "원하는 위치에서 주먹을 쥐세요";

        PositionFeedback(position);
    }


    private void HideCharging()
    {
        if (_progressRing != null)
            _progressRing.gameObject.SetActive(false);
    }

    private void PositionFeedback(Vector3 position)
    {
        if (_feedbackCanvas == null) return;

        Transform root = _feedbackCanvas.transform;
        root.position =
            position +
            (_phase == CreationPhase.Preview
                ? Vector3.zero
                : Vector3.up * _feedbackVerticalOffset);
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 forward = root.position - camera.transform.position;
            if (forward.sqrMagnitude > 0.0001f)
                root.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    camera.transform.up);
        }
        root.localScale = Vector3.one * 0.00085f;

        if (_feedbackText != null)
            _feedbackText.gameObject.SetActive(
                _phase != CreationPhase.Preview);
    }

    private void HideAllVisuals()
    {
        if (_feedbackCanvas != null)
            _feedbackCanvas.gameObject.SetActive(false);
    }

    private void ResetGesture()
    {
        _phase = CreationPhase.Idle;
        _activeHand = null;
        _holdTime = 0f;
        _fistCandidateTime = 0f;
        _fistLostTime = 0f;
        _commitHoldTime = 0f;
        _openTime = 0f;
        _stableFist = false;
        HideAllVisuals();
    }


    private void Announce(string message)
    {
        OnGuidanceChanged?.Invoke(message);
    }
}
