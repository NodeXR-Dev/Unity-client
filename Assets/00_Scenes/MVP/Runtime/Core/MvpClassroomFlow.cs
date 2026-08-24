using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Fusion;   // 로비가 띄운 Fusion 세션을 이어받기 위해
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public class MvpClassroomFlow : MonoBehaviour
{
    [Header("씬 참조")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private MvpWaterRocketGraphController _waterRocketGraph;
    [SerializeField] private GraphSyncClient _graphSyncClient;

    [Tooltip("'설계 마치기' 때 띄울 회의 리포트 패널 " +
             "(Assets/01_Prefabs/Graph/Designer/ReportPanel). " +
             "씬에 미리 놓아 두었다면 비워 둬도 된다.")]
    [SerializeField] private GameObject _reportPanelPrefab;

    private GameObject _reportPanel;

    // 회의 목표('GoalPannel')·마치기 확인('ConfirmPanel') 패널은 프리팹이 아니라
    // 씬의 MainSketchPanel 아래에 꺼진 채로 놓여 있다. 필요할 때 찾아 켠다.
    // (FindBoardPanel 주석에 씬에 두는 이유가 적혀 있다)
    [Tooltip("따라다니는 캔버스 위에서의 확인 패널 크기. " +
             "ConfirmPanel 프리팹이 원래 갖고 있던 값(0.5)이 기본이다. " +
             "씬에 놓인 localScale 은 무시하고 이 값을 쓴다.")]
    [SerializeField] private float _confirmPanelScale = 0.5f;

    private GameObject _finishConfirmPanel;
    private GameObject _goalPanel;
    private Canvas _confirmFollowCanvas;   // 확인 패널을 얹는, 시야를 따라오는 캔버스
    [SerializeField] private Generate2DController _generate2DController;
    [SerializeField] private Generate3DController _generate3DController;
    [SerializeField] private MainSketchView _mainSketchView;
    [SerializeField] private MvpWorkspaceLayout _workspaceLayout;

    [Header("백엔드")]
    [SerializeField] private string _backendHost = "127.0.0.1:8000";
    [SerializeField] private bool _tryBackendFirst = true;
    [SerializeField] private float _imageWaitSeconds = 9f;

    // 서버 AI 이미지 대기 상한(초). 실제 대기는 Generate2DController.IsGenerating 이 끝나면
    // 함께 끝나므로, 이 값은 컨트롤러가 응답 없이 멈춘 경우를 대비한 안전장치다.
    // 컨트롤러 자체 타임아웃(_generationTimeoutSeconds, 기본 120초)보다 넉넉하게 잡는다.
    private const float ServerImageWaitCapSeconds = 150f;

    // 서버 3D(Meshy) 대기 상한(초). Meshy 폴링 상한이 서버 기본 900초라 그보다 넉넉히 잡는다.
    // 실제 대기는 Generate3DController.IsGenerating 이 끝나면 함께 끝난다.
    private const float ServerModelWaitCapSeconds = 960f;

    // 서버 3D 모델 받침 원판의 반지름(m). 모델 최장변(_targetSize 0.45)보다 조금 크게.
    private const float ServerStagePadRadius = 0.28f;

    private readonly MvpSessionData _session = new MvpSessionData();
    private readonly List<MvpSketchHistoryItem> _history =
        new List<MvpSketchHistoryItem>();

    private Canvas _toolCanvas;
    private Canvas _flowCanvas;
    private MvpXrCanvasAnchor _flowAnchor;   // 가운데 패널을 유저 앞에 고정하는 앵커
    private MvpRocket3DStage _rocketStage;   // 3D 단계에서 유저 앞에 조립되는 mock 3D 로켓
    private Transform _serverModelStage;     // 서버 3D(GLB)가 놓이는 스테이지(받침 원판 포함)
    private Texture2D _workspaceMockTexture; // 워크스페이스 '2D 만들기'가 만든 로컬 mock(교체 시 파괴)
    private bool _workspaceGenBusy;
    [SerializeField] private MvpNetworkSession _networkSession; // room_id 기반 Fusion 멀티플레이 세션
    private RectTransform _contentRoot;
    private TMP_Text _modeBadge;
    private Canvas _mainSketchCanvas;
    private GameObject _graphRoot;
    private TMP_Text _workspaceStatus;
    private Button _reviewButton;
    private Button _threeDViewButton;
    private Button _voiceButton;   // '말로 추가' 음성 입력 토글 (시안 버튼을 쓸 땐 null)

    // MainSketchPanel 프리팹(디자이너 시안)의 버튼을 쓰고 있는지.
    // true 면 버튼 색·배경을 코드가 덮어쓰지 않는다(스프라이트 위 덧칠 방지).
    private bool _usingDesignerButtons;
    private RawImage _centerSketchImage;
    private MvpFlowState _state;
    private bool _requestBusy;
    private bool _subscribed;
    private readonly Dictionary<string, Button> _recommendationButtons =
        new Dictionary<string, Button>();
    private readonly HashSet<string> _resolvedRecommendations =
        new HashSet<string>();


    private MvpVoiceRequirementController _voiceController;
    private MvpSpatialNodeGestureController _spatialGesture;
    private TMP_InputField _customPartInput;
    private bool _recommendationsDismissed;
    // 로비(MvpLobby)에서 넘어왔는가. 참여 안내 문구가 갈린다
    // (로비 = 방 목록에서 찾기 / 단독 = 초대 코드).
    private bool _adoptedFromLobby;


    private void Awake()
    {
        ResolveReferences();
        PrepareExistingScene();
        BuildFlowCanvas();

        // 로비(MvpLobby)에서 넘어왔다면 방 생성·입장이 이미 끝났다.
        // 그걸 모르고 Welcome 부터 시작하면 방을 한 번 더 만들고 Fusion 세션도 둘이 된다.
        //
        // 브리핑('오늘의 설계 미션')은 건너뛴다. 주제·목표는 로비에서 이미 입력받아
        // 같은 내용을 한 번 더 읽히고 '설계 시작'을 누르게 할 뿐이다.
        // 대신 시안의 회의 목표 패널을 먼저 띄우고, '회의 시작하기'로 설계에 들어간다.
        if (TryAdoptLobbySession())
        {
            // 설계 화면은 한 프레임 뒤에 연다.
            // 여기(Awake)에서 바로 StartDesign 을 부르면 보드·시안 프리팹 등 다른
            // 컴포넌트의 Awake/Start 가 아직 안 돌아 빈 판만 뜬다.
            // (원래 이 함수는 브리핑의 '설계 시작' 버튼이 훨씬 뒤에 부르던 것이다)
            StartCoroutine(ShowGoalPanelWhenReady());
        }
        else
        {
            ShowState(MvpFlowState.Welcome);
        }

        // 노드 X(캐스케이드 삭제)는 되돌릴 수 없으므로 MVP 에서는 확인을 거친다.
        NodeActionPanel.ConfirmDeleteHook = HandleConfirmNodeDelete;
        // (아래 훅 등록은 이어받기 여부와 무관하다)
        // 노드 R 버튼 → 비슷한 느낌의 레퍼런스 디자인 이미지 검색(웹뷰).
        NodeActionPanel.ReferenceHook = HandleReferenceRequest;
    }

    // 로비(MvpLobby)가 만든 Fusion 세션을 그대로 이어받는다.
    //   - 로비는 room_id 를 SessionName 으로 StartGame 하고, 같은 값을 PlayerPrefs 에 남긴다.
    //   - 그래서 "실행 중인 러너가 있는가"만 보면 로비 경유인지 판별할 수 있다.
    //   - 이어받으면 방 생성/입장 화면(Welcome~JoinRoom)을 건너뛰고 바로 Briefing 으로 간다.
    // 러너가 없으면(MVP_SH 단독 실행) false 를 돌려 기존 흐름을 그대로 쓴다.
    private bool TryAdoptLobbySession()
    {
        NetworkRunner runner = null;
        foreach (NetworkRunner candidate in
                 FindObjectsByType<NetworkRunner>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.IsRunning)
            {
                runner = candidate;
                break;
            }
        }

        if (runner == null)
            return false;

        string roomId = runner.SessionInfo != null ? runner.SessionInfo.Name : null;
        if (string.IsNullOrEmpty(roomId))
            roomId = PlayerPrefs.GetString("Lobby.LastSessionName", "");
        if (string.IsNullOrEmpty(roomId))
            return false;

        _session.roomId = roomId;
        _session.userId = PlayerPrefs.GetString("Lobby.LastSessionUserId", "");
        _session.nickname = PlayerPrefs.GetString("Lobby.LastSessionNickname", "학생");
        _session.roomName = ReadSessionProperty(runner, "DisplayTopic", "함께하는 수업");
        _session.topic = _session.roomName;
        _session.goal = "우리 팀만의 해결책 만들기";
        _session.online = true;
        _adoptedFromLobby = true;

        // 그래프 WebSocket 은 MVP 가 직접 붙는다(로비는 Photon 만 담당).
        // Fusion 러너는 이미 돌고 있으므로 MvpNetworkSession 은 재시작하지 않고 붙기만 한다.
        ConfigureGraphSocket();

        Debug.Log(
            "[MVP Flow] 로비 세션 이어받음 — room_id=" + _session.roomId +
            ", nickname=" + _session.nickname);

        // 로비에서 요구사항을 말하면 서버가 그때 초기 2D 스케치를 만들기 시작한다.
        // 완성 통보(WS 2D_GENERATED)는 요청자에게만 가는데 씬을 갈아타며 연결이 끊겨 놓치므로,
        // 회의실에 들어온 지금 서버에 이미 만들어진 그림을 직접 가져와 보드에 올린다.
        // (아직 생성 중이면 컨트롤러가 생길 때까지 기다렸다 표시한다.)
        if (_generate2DController == null)
            _generate2DController = FindFirstObjectByType<Generate2DController>();
        if (_generate2DController != null)
            _generate2DController.RestoreExistingSketch();

        return true;
    }

    private static string ReadSessionProperty(
        NetworkRunner runner, string key, string fallback)
    {
        if (runner == null || runner.SessionInfo == null ||
            runner.SessionInfo.Properties == null)
            return fallback;
        if (runner.SessionInfo.Properties.TryGetValue(key, out SessionProperty value) &&
            value.IsString)
            return (string)value;
        return fallback;
    }

    private void Start()
    {
        // EventSystem의 Awake가 끝난 뒤 ISDK 모듈을 만든다.
        // MvpClassroomFlow의 실행 순서가 빨라 PointableCanvas.Start보다 먼저 실행된다.
        EnsurePointableCanvasModule();
    }


    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        if (NodeActionPanel.ConfirmDeleteHook == HandleConfirmNodeDelete)
            NodeActionPanel.ConfirmDeleteHook = null;
        if (NodeActionPanel.ReferenceHook == HandleReferenceRequest)
            NodeActionPanel.ReferenceHook = null;

        foreach (MvpSketchHistoryItem item in _history)
        {
            if (item?.texture != null)
                Destroy(item.texture);
        }
        _history.Clear();
    }

    // ─────────────────────────────────────────────
    // 확인 팝업 (삭제·설계 마치기 등 되돌릴 수 없는 행동 앞에서)
    // ─────────────────────────────────────────────

    private Canvas _confirmCanvas;

    private bool HandleConfirmNodeDelete(string nodeId, Action doDelete)
    {
        ShowConfirmDialog(
            "이 노드를 지울까요?",
            "아래에 달린 세부 조건도 함께 지워져요.\n지운 노드는 되돌릴 수 없어요.",
            "지우기",
            MvpStudentUiFactory.Coral,
            doDelete);
        return true;
    }

    private void ShowConfirmDialog(
        string title,
        string body,
        string confirmLabel,
        Color confirmColor,
        Action onConfirm,
        bool showCancel = true)
    {
        CloseConfirmDialog();

        GameObject root = new GameObject(
            "MvpConfirmCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        // 캔버스의 남는 영역이 모달 블로커가 된다. 다만 너무 키우면 안 된다.
        // 예전에는 2400x1500 이라 0.85m 앞에서 2.16 x 1.35m — 시야각 103도로
        // 검은 판이 화면을 통째로 덮었다(대화상자 0.65 x 0.34m 의 세 배 폭).
        // 대화상자를 넉넉히 감싸는 정도로만 둔다.
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1200f, 700f);

        _confirmCanvas = root.GetComponent<Canvas>();
        _confirmCanvas.renderMode = RenderMode.WorldSpace;
        _confirmCanvas.sortingOrder = 400;   // 다른 MVP 패널 위
        _confirmCanvas.worldCamera = Camera.main;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            root.transform.position =
                cam.transform.position + forward * 0.85f;
            root.transform.rotation =
                Quaternion.LookRotation(forward);
        }
        root.transform.localScale = Vector3.one * 0.0009f;
        // 사용자가 몸을 돌려도 응답을 요구하는 팝업은 시야를 따라온다.
        root.AddComponent<MvpGentleFollow>().Configure(0.85f);

        // 모달 블로커: 뒤 UI 클릭을 막고, 바깥을 누르면 취소로 처리한다.
        Image blocker = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "ModalBlocker",
            Vector2.zero,
            rect.sizeDelta,
            // 뒤를 가리기만 하면 된다. 0.55 는 VR 에서 검은 벽처럼 보였다.
            new Color(0.01f, 0.02f, 0.05f, 0.32f),
            false);
        blocker.raycastTarget = true;
        Button blockerButton = blocker.gameObject.AddComponent<Button>();
        blockerButton.transition = Selectable.Transition.None;
        blockerButton.onClick.AddListener(CloseConfirmDialog);

        Image panel = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "ConfirmPanel",
            Vector2.zero,
            new Vector2(720f, 380f),
            new Color(0.045f, 0.06f, 0.09f, 0.99f),
            true);
        Outline rim = panel.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.48f, 0.58f, 0.76f, 0.30f);
        rim.effectDistance = new Vector2(2f, -2f);

        MvpStudentUiFactory.CreateText(
            panel.transform, "Title", title,
            new Vector2(0f, 118f), new Vector2(640f, 56f),
            30f, TextAlignmentOptions.Center, true,
            new Color(0.96f, 0.98f, 1f, 1f), 1);

        MvpStudentUiFactory.CreateText(
            panel.transform, "Body", body,
            new Vector2(0f, 22f), new Vector2(620f, 110f),
            21f, TextAlignmentOptions.Center, false,
            new Color(0.72f, 0.78f, 0.88f, 1f), 3);

        if (showCancel)
            MvpStudentUiFactory.CreateButton(
                panel.transform, "Cancel", "취소",
                new Vector2(-160f, -110f), new Vector2(250f, 74f),
                new Color(0.14f, 0.18f, 0.26f, 1f),
                CloseConfirmDialog, 24f);

        MvpStudentUiFactory.CreateButton(
            panel.transform, "Confirm", confirmLabel,
            new Vector2(showCancel ? 160f : 0f, -110f),
            new Vector2(showCancel ? 250f : 320f, 74f),
            confirmColor,
            () =>
            {
                CloseConfirmDialog();
                onConfirm?.Invoke();
            }, 24f);

        AttachPointableCanvas(_confirmCanvas);
    }

    private void CloseConfirmDialog()
    {
        if (_confirmCanvas != null)
        {
            Destroy(_confirmCanvas.gameObject);
            _confirmCanvas = null;
        }
    }

    // ─────────────────────────────────────────────
    // 노드 R 버튼 → 레퍼런스 디자인 검색 패널
    // ─────────────────────────────────────────────
    private MvpReferencePanel _referencePanel;
    private ReferenceApiClient _referenceApi;

    private bool HandleReferenceRequest(string nodeId)
    {
        if (_graphManager == null || string.IsNullOrEmpty(nodeId))
            return false;

        // 1) 즉시: 부모 체인 기반 로컬 키워드로 연다(오프라인에서도 동작).
        string keyword = BuildLocalReferenceKeyword(nodeId);

        if (_referencePanel == null)
        {
            _referencePanel = GetComponent<MvpReferencePanel>();
            if (_referencePanel == null)
                _referencePanel =
                    gameObject.AddComponent<MvpReferencePanel>();
        }
        _referencePanel.OpenForNode(nodeId, keyword, AttachPointableCanvas);
        SetWorkspaceMessage(
            "비슷한 느낌의 디자인을 찾아볼게요. 검색어는 위 칸에서 바꿀 수 있어요.",
            MvpStudentUiFactory.HoloCyan);

        // 2) 온라인이면 서버 추천 키워드(부모 체인 반영 LLM)로 갱신을 시도한다.
        if (_session.online &&
            _graphSyncClient != null && _graphSyncClient.IsConnected)
        {
            ReferenceApiClient api = EnsureReferenceApi();
            api?.RequestKeyword(nodeId, serverKeyword =>
            {
                if (!string.IsNullOrWhiteSpace(serverKeyword) &&
                    _referencePanel != null)
                    _referencePanel.SuggestKeyword(
                        nodeId, serverKeyword.Trim() + " 디자인");
            });
        }
        return true;
    }

    // "물로켓 {부품} {가장 구체적인 체인 라벨 최대 2개} 디자인" 형태의 검색어.
    private string BuildLocalReferenceKeyword(string nodeId)
    {
        ReferenceContext context =
            _graphManager.CollectReferenceContext(nodeId);

        var words = new List<string> { "물로켓" };
        if (!string.IsNullOrWhiteSpace(context.partLabel))
            words.Add(context.partLabel.Trim());
        if (context.chainLabels != null)
        {
            int start = Mathf.Max(0, context.chainLabels.Count - 2);
            for (int i = start; i < context.chainLabels.Count; i++)
            {
                string label = context.chainLabels[i];
                if (!string.IsNullOrWhiteSpace(label) &&
                    !words.Contains(label.Trim()))
                    words.Add(label.Trim());
            }
        }
        words.Add("디자인");
        return string.Join(" ", words);
    }

    private ReferenceApiClient EnsureReferenceApi()
    {
        if (_referenceApi != null)
            return _referenceApi;

        _referenceApi = FindFirstObjectByType<ReferenceApiClient>();
        if (_referenceApi == null && _graphSyncClient != null)
        {
            _referenceApi =
                gameObject.AddComponent<ReferenceApiClient>();
            SetPrivateField(_referenceApi, "_syncClient", _graphSyncClient);
        }
        return _referenceApi;
    }

    // '방 나가기'(테이블 도크에서 호출) — 확인 후 세션을 정리하고 처음 화면으로 돌아간다.
    public void RequestLeaveRoom()
    {
        ShowConfirmDialog(
            "방에서 나갈까요?",
            "함께 만든 그래프는 방에 남고,\n내 화면은 처음으로 돌아가요.",
            "나가기",
            MvpStudentUiFactory.Coral,
            LeaveRoom);
    }

    private void LeaveRoom()
    {
        _networkSession?.EndSession();   // Fusion 종료 + 내 노드 잠금 반납
        RestartFlow();                   // 그래프/기록/소켓 정리 + Welcome 으로
    }

    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
        if (_waterRocketGraph == null)
            _waterRocketGraph =
                GetComponent<MvpWaterRocketGraphController>();
        if (_graphSyncClient == null)
            _graphSyncClient =
                FindFirstObjectByType<GraphSyncClient>();
        if (_generate2DController == null)
            _generate2DController =
                FindFirstObjectByType<Generate2DController>();
        if (_generate3DController == null)
            _generate3DController =
                FindFirstObjectByType<Generate3DController>();
        if (_mainSketchView == null)
            _mainSketchView =
                FindFirstObjectByType<MainSketchView>();
        if (_workspaceLayout == null)
            _workspaceLayout =
                FindFirstObjectByType<MvpWorkspaceLayout>();

        if (_spatialGesture == null)
            _spatialGesture =
                GetComponent<MvpSpatialNodeGestureController>();
        if (_spatialGesture == null)
            _spatialGesture =
                gameObject.AddComponent<
                    MvpSpatialNodeGestureController>();


        if (_voiceController == null)
            _voiceController = GetComponent<MvpVoiceRequirementController>();
        if (_voiceController == null)
            _voiceController = gameObject.AddComponent<MvpVoiceRequirementController>();
        _voiceController.Configure(_graphManager);
        _spatialGesture.Configure(_graphManager);

        if (_mainSketchView != null)
            _mainSketchCanvas =
                _mainSketchView.GetComponentInParent<Canvas>();

        _graphRoot = GameObject.Find("GraphRoot");
        _centerSketchImage = FindCenterSketchImage();
    }

    private void PrepareExistingScene()
    {
        SeedGraphLoader seed =
            FindFirstObjectByType<SeedGraphLoader>();
        if (seed != null)
            seed.enabled = false;

        SetPrivateField(_graphSyncClient, "_autoConnect", false);
        SetPrivateField(_graphSyncClient, "_sendToServer", false);
        if (_graphSyncClient != null)
            _graphSyncClient.enabled = false;

        // PointableCanvas는 활성화되는 순간 모듈을 찾으므로 먼저 준비한다.
        EnsurePointableCanvasModule();
        if (_mainSketchCanvas != null)
            AttachPointableCanvas(_mainSketchCanvas);

        HideLegacyHistoryDots();
        SetMainWorkspaceVisible(false);
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        if (_waterRocketGraph != null)
            _waterRocketGraph.OnDesignChanged +=
                RefreshWorkspaceDock;
        if (_spatialGesture != null)
        {
            _spatialGesture.OnRootNodeCreated +=
                HandleSpatialRootCreated;
            _spatialGesture.OnGuidanceChanged +=
                HandleGestureGuidance;
        }
        if (_voiceController != null)
            _voiceController.OnStatusChanged += HandleVoiceStatusChanged;
        if (_networkSession != null)
            _networkSession.OnSessionNotice += HandleSessionNotice;
        MvpWorldKeyboard.OnOpenedGlobal += HandleKeyboardOpened;
        MvpWorldKeyboard.OnClosedGlobal += HandleKeyboardClosed;
        _subscribed = true;
    }

    // ─────────────────────────────────────────────
    // 정면 레이어 겹침 중재 — 키보드가 열리면 같은 공간의 추천 패널이 비켜난다
    // ─────────────────────────────────────────────
    private bool _toolCanvasHiddenForKeyboard;

    private void HandleKeyboardOpened()
    {
        if (_toolCanvas == null || !_toolCanvas.gameObject.activeSelf)
            return;

        // 추천 패널 안의 입력칸(직접 부품 추가)을 편집 중이면 패널을 유지한다.
        TMP_InputField target = MvpWorldKeyboard.CurrentTarget;
        if (target != null &&
            target.transform.IsChildOf(_toolCanvas.transform))
            return;

        _toolCanvas.gameObject.SetActive(false);
        _toolCanvasHiddenForKeyboard = true;
    }

    private void HandleKeyboardClosed()
    {
        if (!_toolCanvasHiddenForKeyboard)
            return;
        _toolCanvasHiddenForKeyboard = false;
        if (_toolCanvas != null && !_recommendationsDismissed)
            _toolCanvas.gameObject.SetActive(true);
    }

    // 음성 상태 문구를 안내 칩에 보여 주고, 마이크 버튼 라벨도 함께 갱신한다.
    private void HandleVoiceStatusChanged(string message, Color color)
    {
        SetWorkspaceMessage(message, color);
        RefreshVoiceButtonLabel();
    }

    // 액션바 버튼과 손목 메뉴(마이크 아이콘) 양쪽에서 부른다.
    public void ToggleVoiceInput()
    {
        if (_voiceController == null)
        {
            SetWorkspaceMessage(
                "음성 입력을 아직 사용할 수 없어요.",
                MvpStudentUiFactory.Coral);
            return;
        }

        _voiceController.ToggleListening();
        RefreshVoiceButtonLabel();
    }

    private MvpVoiceIndicator _voiceIndicator;

    private void RefreshVoiceButtonLabel()
    {
        if (_voiceButton == null)
            return;

        bool listening =
            _voiceController != null && _voiceController.IsListening;
        bool preparing =
            !listening &&
            _voiceController != null && _voiceController.IsPreparingVoice;

        TMP_Text label = _voiceButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = listening
                ? "듣기 멈추기"
                : preparing ? "준비 중…" : "말로 추가";
        MvpStudentUiFactory.SetButtonColor(
            _voiceButton,
            listening
                ? new Color(0.72f, 0.30f, 0.38f, 1f)   // 듣는 중엔 경고 레드로 강조
                : MvpStudentUiFactory.GlassAction); // 평소엔 보조 글래스 톤

        if (_voiceIndicator != null)
            _voiceIndicator.SetState(
                listening
                    ? MvpVoiceIndicator.State.Listening
                    : preparing
                        ? MvpVoiceIndicator.State.Preparing
                        : MvpVoiceIndicator.State.Idle,
                _voiceController != null ? _voiceController.Dictation : null);
    }

    // 입장/퇴장·원격 편집 알림을 작업판 안내 칩으로 보여 준다.
    private void HandleSessionNotice(string message)
    {
        SetWorkspaceMessage(message, MvpStudentUiFactory.HoloCyan);
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (_waterRocketGraph != null)
            _waterRocketGraph.OnDesignChanged -=
                RefreshWorkspaceDock;
        if (_spatialGesture != null)
        {
            _spatialGesture.OnRootNodeCreated -=
                HandleSpatialRootCreated;
            _spatialGesture.OnGuidanceChanged -=
                HandleGestureGuidance;
        }
        if (_voiceController != null)
            _voiceController.OnStatusChanged -= HandleVoiceStatusChanged;
        if (_networkSession != null)
            _networkSession.OnSessionNotice -= HandleSessionNotice;
        MvpWorldKeyboard.OnOpenedGlobal -= HandleKeyboardOpened;
        MvpWorldKeyboard.OnClosedGlobal -= HandleKeyboardClosed;
        _subscribed = false;
    }

    private void BuildFlowCanvas()
    {
        Camera camera = Camera.main;
        GameObject root = new GameObject(
            "MvpFlowCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1500f, 840f);

        _flowCanvas = root.GetComponent<Canvas>();
        _flowCanvas.renderMode = RenderMode.WorldSpace;
        _flowCanvas.sortingOrder = 220;
        _flowCanvas.worldCamera = camera;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 1.4f;
        scaler.referencePixelsPerUnit = 100f;

        if (camera != null)
        {
            root.transform.position =
                camera.transform.position +
                camera.transform.forward * 1.22f +
                camera.transform.up * -0.02f;
            root.transform.rotation = Quaternion.LookRotation(
                camera.transform.forward,
                camera.transform.up);
        }
        root.transform.localScale = Vector3.one * 0.00135f;
        _flowAnchor =
            root.AddComponent<MvpXrCanvasAnchor>();
        _flowAnchor.Configure(1.22f, -0.02f, 0.00135f);

        MvpStudentUiFactory.CreatePanel(
            rect,
            "Backdrop",
            Vector2.zero,
            new Vector2(1500f, 840f),
            MvpStudentUiFactory.Surface,
            true);

        Image topAccent = MvpStudentUiFactory.CreatePanel(
            rect,
            "TopAccent",
            new Vector2(-705f, 356f),
            new Vector2(12f, 68f),
            MvpStudentUiFactory.Primary,
            false);
        topAccent.raycastTarget = false;

        MvpStudentUiFactory.CreateText(
            rect,
            "Brand",
            "NodeXR  ·  COLLABORATIVE DESIGN",
            new Vector2(-435f, 356f),
            new Vector2(510f, 62f),
            27f,
            TextAlignmentOptions.MidlineLeft,
            true,
            MvpStudentUiFactory.Ink,
            1);

        _modeBadge = MvpStudentUiFactory.CreateText(
            rect,
            "ModeBadge",
            "함께 설계하기",
            new Vector2(610f, 356f),
            new Vector2(220f, 52f),
            21f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Primary,
            1);

        _contentRoot = MvpStudentUiFactory.CreateRect(
            rect,
            "Content",
            new Vector2(0f, -24f),
            new Vector2(1410f, 720f));

        AttachPointableCanvas(_flowCanvas);
        // 위치 변경은 설계 보드의 단일 작업판 버튼으로 제공한다.
    }


    private void ShowState(MvpFlowState state)
    {
        _state = state;
        RestoreFlowCanvasGroup();   // 3D 조립 페이드 등 잔여 투명도 방어 복원
        bool design = state == MvpFlowState.Design;

        if (state != MvpFlowState.Design &&
            state != MvpFlowState.ThreeD &&
            state != MvpFlowState.Complete)
            ClearRocketStage();

        if (!design && _spatialGesture != null)
            _spatialGesture.SetCreationEnabled(false);
        if (_workspaceLayout != null)
            _workspaceLayout.SetSpatialPlacementMode(design);

        if (_flowCanvas != null)
            _flowCanvas.gameObject.SetActive(!design);

        SetMainWorkspaceVisible(design);

        if (design)
        {
            BuildRequirementDock();
            return;
        }

        ClearContent();
        UpdateModeBadge();

        switch (state)
        {
            case MvpFlowState.Welcome:
                BuildWelcomePage();
                break;
            case MvpFlowState.CreateRoom:
                BuildCreateRoomPage();
                break;
            case MvpFlowState.JoinRoom:
                BuildJoinRoomPage();
                break;
            case MvpFlowState.Briefing:
                BuildBriefingPage();
                break;
            case MvpFlowState.Review:
                BuildReviewPage();
                break;
            case MvpFlowState.Generating:
                BuildGeneratingPage();
                break;
            case MvpFlowState.Result:
                BuildResultPage();
                break;
            case MvpFlowState.History:
                BuildHistoryPage();
                break;
            case MvpFlowState.ThreeD:
                BuildThreeDPage();
                break;
            case MvpFlowState.Complete:
                BuildCompletePage();
                break;
        }

        ApplyFlowVisualPass();
    }

    private void ApplyFlowVisualPass()
    {
        if (_flowCanvas == null || _contentRoot == null)
            return;

        Transform backdropTransform = _flowCanvas.transform.Find("Backdrop");
        Image backdrop = backdropTransform != null
            ? backdropTransform.GetComponent<Image>()
            : null;
        if (backdrop != null)
        {
            backdrop.color = new Color(0.025f, 0.055f, 0.13f, 0.975f);
            backdrop.raycastTarget = false;

            Outline frame = backdrop.GetComponent<Outline>();
            if (frame == null)
                frame = backdrop.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(0.22f, 0.84f, 1f, 0.42f);
            frame.effectDistance = new Vector2(2f, -2f);
        }

        Transform brandTransform = _flowCanvas.transform.Find("Brand");
        TMP_Text brand = brandTransform != null
            ? brandTransform.GetComponent<TMP_Text>()
            : null;
        if (brand != null)
        {
            brand.color = new Color(0.74f, 0.88f, 1f, 1f);
            brand.fontSize = Mathf.Max(brand.fontSize, 25f);
        }

        if (_modeBadge != null)
            _modeBadge.color = MvpStudentUiFactory.HoloCyan;

        Func<Color, Color, float> colorDistance = (a, b) =>
        {
            float r = a.r - b.r;
            float g = a.g - b.g;
            float blue = a.b - b.b;
            return r * r + g * g + blue * blue;
        };

        TMP_Text[] labels = _contentRoot.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            if (label == null ||
                label.GetComponentInParent<Button>() != null ||
                label.GetComponentInParent<TMP_InputField>() != null)
                continue;

            // 랜딩 인포그래픽은 작은 정보 라벨이므로 일반 본문 최소 크기
            // 규칙을 적용하지 않는다. XR에서도 한 장의 다이어그램으로 읽힌다.
            if (IsLandingDiagramLabel(label))
            {
                label.extraPadding = false;
                label.raycastTarget = false;
                continue;
            }

            Color current = label.color;
            if (colorDistance(current, MvpStudentUiFactory.MutedInk) < 0.012f)
                label.color = new Color(0.72f, 0.82f, 0.96f, 1f);
            else if (colorDistance(current, MvpStudentUiFactory.SuccessInk) < 0.018f)
                label.color = MvpStudentUiFactory.Mint;
            else if (colorDistance(current, MvpStudentUiFactory.WarningInk) < 0.018f)
                label.color = MvpStudentUiFactory.Amber;
            else if (colorDistance(current, MvpStudentUiFactory.DangerInk) < 0.018f)
                label.color = MvpStudentUiFactory.Coral;
            else if (colorDistance(current, MvpStudentUiFactory.InfoInk) < 0.018f)
                label.color = MvpStudentUiFactory.HoloCyan;
            else
                label.color = new Color(0.95f, 0.98f, 1f, 1f);

            bool compactCopy =
                label.name == "Body" ||
                label.name == "Label";
            if (compactCopy)
            {
                label.fontSize = Mathf.Max(label.fontSize, 17f);
                label.fontSizeMin = 16f;
                label.margin = new Vector4(4f, 2f, 4f, 2f);
                label.lineSpacing = 0f;
            }
            else
            {
                label.fontSize = Mathf.Max(label.fontSize, 22f);
                label.fontSizeMin =
                    Mathf.Max(label.fontSizeMin, 18f);
                label.margin = new Vector4(10f, 6f, 10f, 6f);
                label.lineSpacing = 2f;
            }
            label.extraPadding = true;
            label.raycastTarget = false;
        }

        Image[] surfaces = _contentRoot.GetComponentsInChildren<Image>(true);
        foreach (Image surface in surfaces)
        {
            if (surface == null ||
                surface.GetComponent<Button>() != null ||
                surface.GetComponentInParent<TMP_InputField>() != null)
                continue;

            bool illustration = false;
            Transform cursor = surface.transform;
            while (cursor != null && cursor != _contentRoot)
            {
                if (cursor.name == "RocketIllustration")
                {
                    illustration = true;
                    break;
                }
                cursor = cursor.parent;
            }
            if (illustration)
                continue;

            Color current = surface.color;
            bool lightSurface =
                current.r > 0.72f &&
                current.g > 0.72f &&
                current.b > 0.72f;
            if (!lightSurface)
                continue;

            surface.color = new Color(0.075f, 0.14f, 0.27f, 0.97f);
            surface.raycastTarget = false;

            Outline outline = surface.GetComponent<Outline>();
            if (outline == null)
                outline = surface.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.26f, 0.76f, 1f, 0.25f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }


    private static bool IsLandingDiagramLabel(TMP_Text label)
    {
        if (label == null)
            return false;

        Transform current = label.transform;
        while (current != null)
        {
            if (current.name == "DesignFlowIllustration")
                return true;
            current = current.parent;
        }

        return false;
    }
    private void BuildWelcomePage()
    {
        CreateDesignFlowIllustration(
            _contentRoot,
            new Vector2(-390f, -8f),
            1.04f);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "WelcomeEyebrow",
            "말로 나눈 생각이 모두의 설계가 되는 곳",
            new Vector2(305f, 252f),
            new Vector2(640f, 46f),
            20f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.InfoInk,
            1);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "WelcomeTitle",
            "생각을 모아," + System.Environment.NewLine +
            "우리의 설계를 만들어요",
            new Vector2(305f, 145f),
            new Vector2(650f, 150f),
            54f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Ink,
            2);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "WelcomeBody",
            "어떤 주제든 괜찮아요. 친구들과 나눈 생각을" +
            System.Environment.NewLine +
            "아이디어와 특징으로 정리하고, 함께 그림으로 확인해 보세요.",
            new Vector2(305f, 14f),
            new Vector2(650f, 78f),
            24f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "CreateRoomButton",
            "새 설계 시작하기",
            new Vector2(305f, -88f),
            new Vector2(438f, 78f),
            MvpStudentUiFactory.Primary,
            () => ShowState(MvpFlowState.CreateRoom),
            28f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "JoinRoomButton",
            "초대 코드로 참여하기",
            new Vector2(305f, -182f),
            new Vector2(438f, 70f),
            MvpStudentUiFactory.CyanDeep,
            () => ShowState(MvpFlowState.JoinRoom),
            25f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "DemoButton",
            "예시 프로젝트 열기",
            new Vector2(305f, -268f),
            new Vector2(438f, 62f),
            MvpStudentUiFactory.MintDeep,
            StartQuickDemo,
            23f);
    }

    private void BuildCreateRoomPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "설계방 만들기",
            new Vector2(0f, 300f),
            new Vector2(900f, 70f),
            43f,
            TextAlignmentOptions.Center,
            true);

        Image card = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "CreateRoomCard",
            new Vector2(0f, -10f),
            new Vector2(1120f, 530f),
            Color.white,
            true);

        TMP_InputField roomName = CreateLabeledInput(
            card.transform,
            "방 이름",
            "예: 3학년 2반 프로젝트",
            new Vector2(-250f, 130f),
            new Vector2(460f, 66f),
            "우리 팀 설계실");

        TMP_InputField topic = CreateLabeledInput(
            card.transform,
            "설계 주제",
            "예: 새로운 설계 아이디어",
            new Vector2(250f, 130f),
            new Vector2(460f, 66f),
            "새로운 설계 아이디어");

        TMP_InputField goal = CreateLabeledInput(
            card.transform,
            "오늘의 목표",
            "예: 우리 팀만의 해결책 만들기",
            new Vector2(0f, 5f),
            new Vector2(960f, 66f),
            "우리 팀만의 해결책 만들기");

        TMP_InputField nickname = CreateLabeledInput(
            card.transform,
            "내 이름",
            "이름 또는 별명",
            new Vector2(-250f, -120f),
            new Vector2(460f, 66f),
            "선생님");


        TMP_InputField password = CreateLabeledInput(
            card.transform,
            "방 비밀번호",
            "숫자나 쉬운 단어",
            new Vector2(250f, -120f),
            new Vector2(460f, 66f),
            "1234");
        ConfigurePasswordInput(password);

        TMP_Text status = MvpStudentUiFactory.CreateText(
            card.transform,
            "Status",
            "백엔드가 꺼져 있어도 체험 모드로 계속할 수 있어요.",
            new Vector2(0f, -210f),
            new Vector2(900f, 48f),
            19f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Back",
            "이전",
            new Vector2(-230f, -318f),
            new Vector2(210f, 64f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Welcome),
            23f);

        Button create = MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Create",
            "방 만들기",
            new Vector2(110f, -318f),
            new Vector2(430f, 72f),
            MvpStudentUiFactory.Primary,
            null,
            27f);
        create.onClick.AddListener(() =>
            StartCoroutine(CreateRoomRoutine(
                roomName.text,
                topic.text,
                goal.text,
                nickname.text,
                password.text,
                status,
                create)));
    }

    private void BuildJoinRoomPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "설계방 입장하기",
            new Vector2(0f, 290f),
            new Vector2(900f, 70f),
            43f,
            TextAlignmentOptions.Center,
            true);

        Image card = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "JoinCard",
            new Vector2(0f, -5f),
            new Vector2(900f, 500f),
            Color.white,
            true);

        TMP_InputField roomCode = CreateLabeledInput(
            card.transform,
            "방 코드",
            "선생님이 알려준 6글자 코드 (예: KQMWZT)",
            new Vector2(0f, 125f),
            new Vector2(720f, 70f),
            "");

        TMP_InputField nickname = CreateLabeledInput(
            card.transform,
            "내 이름",
            "이름 또는 별명",
            new Vector2(-190f, -10f),
            new Vector2(340f, 66f),
            "학생");


        TMP_InputField password = CreateLabeledInput(
            card.transform,
            "비밀번호",
            "방 비밀번호",
            new Vector2(190f, -10f),
            new Vector2(340f, 66f),
            "1234");
        ConfigurePasswordInput(password);

        TMP_Text status = MvpStudentUiFactory.CreateText(
            card.transform,
            "Status",
            "6글자 코드를 입력하세요. 대소문자는 상관없어요.",
            new Vector2(0f, -135f),
            new Vector2(720f, 54f),
            20f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Back",
            "이전",
            new Vector2(-230f, -300f),
            new Vector2(210f, 64f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Welcome),
            23f);

        Button join = MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Join",
            "입장하기",
            new Vector2(110f, -300f),
            new Vector2(430f, 72f),
            MvpStudentUiFactory.CyanDeep,
            null,
            27f);
        join.onClick.AddListener(() =>
            StartCoroutine(JoinRoomRoutine(
                roomCode.text,
                nickname.text,
                password.text,
                status,
                join)));
    }

    private void BuildBriefingPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "BriefingTitle",
            "오늘의 설계 미션",
            new Vector2(0f, 292f),
            new Vector2(900f, 64f),
            42f,
            TextAlignmentOptions.Center,
            true);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "RoomName",
            string.IsNullOrEmpty(_session.roomName)
                ? "물로켓 설계 수업"
                : _session.roomName,
            new Vector2(0f, 232f),
            new Vector2(900f, 45f),
            23f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Primary,
            1);

        Image mission = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "Mission",
            new Vector2(0f, 140f),
            new Vector2(1120f, 110f),
            MvpStudentUiFactory.SurfaceBlue,
            false);

        MvpStudentUiFactory.CreateText(
            mission.transform,
            "MissionText",
            "주제  " + Safe(_session.topic, "새로운 설계 아이디어") +
            "\n목표  " + Safe(_session.goal, "안전하고 멀리 날아가기"),
            Vector2.zero,
            new Vector2(1050f, 90f),
            25f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Ink,
            2);

        CreateStepCard(-320f, -40f, "1", "부품 고르기", "AI 추천에서 필요한 부품을 골라요");
        CreateStepCard(0f, -40f, "2", "아이디어 붙이기", "주먹을 쥐어 생각 노드를 놓아요");
        CreateStepCard(320f, -40f, "3", "그림으로 보기", "AI가 우리 설계를 그림으로 보여줘요");

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "ModeNotice",
            _session.online
                ? "서버와 연결되었습니다. 팀 활동을 시작할 수 있어요."
                : "체험 모드입니다. 모든 단계를 로컬에서 끝까지 진행할 수 있어요.",
            new Vector2(0f, -185f),
            new Vector2(1000f, 44f),
            21f,
            TextAlignmentOptions.Center,
            false,
            _session.online
                ? MvpStudentUiFactory.SuccessInk
                : MvpStudentUiFactory.WarningInk,
            1);

        // 참여 방법 안내.
        //   로비(MvpLobby) 경유 = 친구가 로비 방 목록에서 찾아 들어온다. 코드는 쓸 곳이 없다.
        //   MVP_SH 단독 실행    = 로비가 없으므로 예전처럼 초대 코드로 들어온다.
        if (_session.online && !string.IsNullOrEmpty(_session.roomId))
        {
            if (_adoptedFromLobby)
            {
                MvpStudentUiFactory.CreateText(
                    _contentRoot,
                    "JoinHint",
                    "친구는 로비 방 목록에서 ‘" + _session.roomName + "’ 을 찾아 들어오면 돼요.",
                    new Vector2(0f, -232f),
                    new Vector2(1000f, 42f),
                    19f,
                    TextAlignmentOptions.Center,
                    false,
                    MvpStudentUiFactory.Primary,
                    1);
            }
            else
            {
                MvpStudentUiFactory.CreateText(
                    _contentRoot,
                    "InviteCode",
                    "초대 코드  " + ShortRoomCode(_session.roomId),
                    new Vector2(-95f, -232f),
                    new Vector2(830f, 42f),
                    19f,
                    TextAlignmentOptions.Center,
                    true,
                    MvpStudentUiFactory.Primary,
                    1);

                Button copy = MvpStudentUiFactory.CreateButton(
                    _contentRoot,
                    "CopyInviteCode",
                    "코드 복사",
                    new Vector2(455f, -232f),
                    new Vector2(170f, 48f),
                    MvpStudentUiFactory.CyanDeep,
                    null,
                    18f);
                copy.onClick.AddListener(() =>
                {
                    GUIUtility.systemCopyBuffer = ShortRoomCode(_session.roomId);
                    TMP_Text label = copy.GetComponentInChildren<TMP_Text>();
                    if (label != null)
                        StartCoroutine(FlashButtonLabel(label, "복사됨!", "코드 복사"));
                });
            }
        }

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "StartDesign",
            "물로켓 설계 시작",
            new Vector2(0f, -302f),
            new Vector2(510f, 76f),
            MvpStudentUiFactory.Primary,
            StartDesign,
            28f);
    }

    private void BuildRequirementDock()
    {
        if (_mainSketchCanvas == null) return;

        Transform oldDock =
            _mainSketchCanvas.transform.Find("MvpRequirementDock");
        if (oldDock != null)
            Destroy(oldDock.gameObject);

        HideOriginalGenerateControls(true);
        HideLegacyHistoryDots();
        BuildWorkspaceActionBar();
        EnsureSpatialToolCanvas();

        if (_recommendationsDismissed)
        {
            if (_toolCanvas != null)
                _toolCanvas.gameObject.SetActive(false);
            _spatialGesture?.SetCreationEnabled(true);
        }
        else
        {
            BuildPartRecommendationPanel();
            _spatialGesture?.SetCreationEnabled(false);
        }

        RefreshWorkspaceDock();
    }

    private void BuildWorkspaceActionBar()
    {
        string[] oldNames =
        {
            "MvpWorkspaceActionBar",
            "MvpFinishDesignButton",
            "MvpWorkspaceStatus",
            "MvpWorkspaceStatusChip"
        };
        foreach (string name in oldNames)
        {
            Transform old = _mainSketchCanvas.transform.Find(name);
            if (old != null)
                Destroy(old.gameObject);
        }

        Image bar = MvpStudentUiFactory.CreatePanel(
            _mainSketchCanvas.transform,
            "MvpWorkspaceActionBar",
            new Vector2(-110f, -410f),
            new Vector2(740f, 94f),
            new Color(0.040f, 0.055f, 0.085f, 0.99f),
            true);
        bar.rectTransform.SetAsLastSibling();

        Outline barRim = bar.gameObject.AddComponent<Outline>();
        barRim.effectColor = new Color(0.48f, 0.58f, 0.76f, 0.20f);
        barRim.effectDistance = new Vector2(1.25f, -1.25f);

        // 안내 칩(SpatialStatus) — MvpStudentWorkspaceGuide 가 이 이름으로 찾아
        // "지금 할 일" 안내와 음성 자막·알림(큐)을 표시한다.
        // 액션바 리뉴얼 때 사라졌던 것을 복구(없으면 모든 안내가 조용히 버려진다).
        Image statusChip = MvpStudentUiFactory.CreatePanel(
            bar.transform,
            "StatusChipBg",
            new Vector2(0f, 74f),
            new Vector2(720f, 42f),
            new Color(0.02f, 0.03f, 0.06f, 0.82f),
            false);
        statusChip.raycastTarget = false;
        TMP_Text statusText = MvpStudentUiFactory.CreateText(
            bar.transform,
            "SpatialStatus",
            "",
            new Vector2(0f, 74f),
            new Vector2(690f, 38f),
            18f,
            TextAlignmentOptions.Center,
            false,
            Color.white,
            1);
        statusText.raycastTarget = false;
        // 메시지는 MvpStudentWorkspaceGuide 의 큐를 거치므로
        // _workspaceStatus 에는 연결하지 않는다(직접 쓰면 큐를 우회한다).

        _workspaceStatus = null;

        // MainSketchPanel 프리팹(디자이너 시안)에 2D / 3D / 리포트 버튼이 들어 있으면 그것을 쓴다.
        //   액션바에 같은 버튼을 또 만들면 화면에 두 벌이 겹치기 때문이다.
        //   프리팹의 onClick 은 씬 오브젝트를 참조할 수 없으므로 여기서 런타임에 연결한다.
        // 시안 버튼을 못 찾으면(프리팹 교체 전 씬 등) 예전처럼 액션바에 직접 만든다 — 그래야
        //   2D·3D 생성 진입점이 사라지지 않는다.
        Button panel2D     = FindSketchPanelButton("Button_2D");
        Button panel3D     = FindSketchPanelButton("Button_3D");
        Button panelReport = FindSketchPanelButton("Report");
        _usingDesignerButtons =
            panel2D != null && panel3D != null && panelReport != null;

        Button finish;

        if (_usingDesignerButtons)
        {
            // 액션바는 안내 칩(SpatialStatus) 전용으로만 남긴다. 배경 띠는 지운다.
            // 칩 배경까지 지워야 시안에 검은 막대가 남지 않는다 — 글자는 그대로 보인다.
            bar.color = new Color(0f, 0f, 0f, 0f);
            bar.raycastTarget = false;
            barRim.enabled = false;
            statusChip.color = new Color(0f, 0f, 0f, 0f);

            _reviewButton = panel2D;
            _reviewButton.onClick.RemoveListener(BeginWorkspaceGenerate);
            _reviewButton.onClick.AddListener(BeginWorkspaceGenerate);

            _threeDViewButton = panel3D;
            _threeDViewButton.onClick.RemoveListener(ToggleOrCreateWorkspace3D);
            _threeDViewButton.onClick.AddListener(ToggleOrCreateWorkspace3D);

            finish = panelReport;
            finish.onClick.RemoveListener(ConfirmFinishDesign);
            finish.onClick.AddListener(ConfirmFinishDesign);

            // '말로 추가'는 시안에서 뺐다. 음성 입력은 노드 키보드의 '음성' 키로 쓴다.
            _voiceButton = null;

            Debug.Log(
                "[MVP Flow] 액션바 대신 MainSketchPanel 시안 버튼(2D/3D/리포트)에 연결했습니다.");
        }
        else
        {
            // 왜 폴백으로 왔는지 남긴다. 셋 중 하나만 없어도 예전 액션바가 통째로 살아난다.
            Transform panelRoot = _workspaceLayout != null
                ? _workspaceLayout.MainSketchPanel
                : null;
            Debug.LogWarning(
                "[MVP Flow] 시안 버튼을 찾지 못해 예전 액션바를 만듭니다 — " +
                $"Button_2D={(panel2D != null)} Button_3D={(panel3D != null)} " +
                $"Report={(panelReport != null)} / " +
                $"탐색 기준={(panelRoot != null ? panelRoot.name : "(MainSketchPanel 없음)")}");

            _reviewButton = MvpStudentUiFactory.CreateButton(
                bar.transform,
                "Generate2D",
                "그림 생성하기",
                new Vector2(-245f, 0f),
                new Vector2(225f, 66f),
                new Color(0.30f, 0.38f, 0.82f, 1f),
                BeginWorkspaceGenerate,
                19f);

            // 위계: 주 행동(그림 생성하기)만 강조색, 보조 행동(3D/말로 추가)은 글래스 톤.
            _threeDViewButton = MvpStudentUiFactory.CreateButton(
                bar.transform,
                "Generate3D",
                "3D 생성하기",
                new Vector2(0f, 0f),
                new Vector2(225f, 66f),
                MvpStudentUiFactory.GlassAction,
                ToggleOrCreateWorkspace3D,
                19f);

            _voiceButton = MvpStudentUiFactory.CreateButton(
                bar.transform,
                "VoiceInput",
                "말로 추가",
                new Vector2(245f, 0f),
                new Vector2(225f, 66f),
                MvpStudentUiFactory.GlassAction,
                ToggleVoiceInput,
                19f);
            _voiceIndicator = MvpVoiceIndicator.Attach(
                _voiceButton.transform, new Vector2(-86f, 0f), 16f);
            RefreshVoiceButtonLabel();

            finish = MvpStudentUiFactory.CreateButton(
                _mainSketchCanvas.transform,
                "MvpFinishDesignButton",
                "설계 마치기",
                new Vector2(625f, -410f),
                new Vector2(230f, 66f),
                new Color(0.12f, 0.40f, 0.34f, 1f),
                ConfirmFinishDesign,
                20f);
        }

        foreach (Button button in new[] { _reviewButton, _threeDViewButton, _voiceButton, finish })
        {
            if (button == null) continue;
            Outline outline = button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(0.78f, 0.86f, 1f, 0.18f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
        }

        RefreshThreeDViewButton();
    }

    // '설계 마치기'(시안에서는 리포트 버튼). 되돌릴 수 없는 행동이라 확인을 한 번 받는다.
    // 람다가 아니라 메서드로 둔 이유: onClick 에 중복 등록되지 않도록 RemoveListener 로 지울 수 있어야 한다.
    // public 인 이유: 손목 패널의 '나가기'도 같은 리포트를 띄운다(MvpWristSettingsMenu).
    public void ConfirmFinishDesign()
    {
        Debug.Log("[MVP Flow] 설계 마치기 확인 요청(손목 '나가기' 또는 보드 리포트 버튼)");

        if (ShowFinishConfirmPanel())
            return;

        // 시안 패널을 못 쓰는 경우(프리팹 미할당 등)에도 확인 단계는 남긴다.
        ShowConfirmDialog(
            "설계를 마칠까요?",
            "마치면 회의 리포트를 보여 줘요.\n계속 수정하려면 취소를 누르세요.",
            "설계 마치기",
            MvpStudentUiFactory.Mint,
            ShowReportPanel);
    }

    // '회의를 마칠까요?' 확인 패널(01_Prefabs/Graph/Main/ConfirmPanel)을 띄운다.
    // 띄웠으면 true — 리포트는 '예'를 눌러야 나온다.
    //
    // 보드는 감추지 않는다. '아니오'를 누르면 하던 설계로 그대로 돌아와야 하는데,
    // 껐다 켜는 사이 노드·엣지 상태가 흔들릴 이유가 없다.
    private bool ShowFinishConfirmPanel()
    {
        // 이미 물어보는 중이면 하나만 띄운다(손목 버튼 연타 방지).
        if (_finishConfirmPanel != null && _finishConfirmPanel.activeSelf)
            return true;

        GameObject panel = FindSceneObject("ConfirmPanel");
        if (panel == null)
        {
            Debug.LogWarning(
                "[MVP Flow] 확인 패널을 찾지 못했습니다 — " +
                "씬에 'ConfirmPanel' 이 있는지 확인하세요.");
            return false;
        }

        // 응답을 요구하는 팝업은 보드에 붙어 있으면 어색하다(보드를 등지면 안 보인다).
        // 노드 삭제 확인창(ShowConfirmDialog)처럼 시야를 따라오는 캔버스에 얹는다.
        _confirmFollowCanvas = CreateFollowCanvas("MvpFinishConfirmCanvas");
        panel.transform.SetParent(_confirmFollowCanvas.transform, false);
        if (panel.transform is RectTransform panelRect)
        {
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition3D = Vector3.zero;
            panelRect.localRotation = Quaternion.identity;
            // 스케일은 반드시 여기서 정한다.
            //   씬에서 이 패널을 보드 밖으로 꺼내면 에디터가 '월드 크기 유지'로 localScale 을
            //   0.0005 같은 값으로 바꿔 놓는다. 그대로 따라오면 캔버스 스케일(0.0009)과 곱해져
            //   4e-7 이 되어 화면에서 사실상 사라진다(로그의 lossyScale=0.0000).
            //   따라다니는 캔버스 기준의 크기는 씬 배치와 무관해야 한다.
            panelRect.localScale = Vector3.one * _confirmPanelScale;
        }
        panel.SetActive(true);
        _finishConfirmPanel = panel;

        MvpPanelButtonBinder.Wire(
            _finishConfirmPanel, "Button_Yes", AcceptFinishDesign);
        MvpPanelButtonBinder.Wire(
            _finishConfirmPanel, "Button_No", CloseFinishConfirmPanel);

        Debug.Log(
            "[MVP Flow] 확인 패널 표시 — activeInHierarchy=" +
            _finishConfirmPanel.activeInHierarchy +
            " cameraMain=" + (Camera.main != null ? Camera.main.name : "(없음)") +
            " canvasWorld=" + _confirmFollowCanvas.transform.position.ToString("F2") +
            " panelWorld=" + _finishConfirmPanel.transform.position.ToString("F2") +
            " lossyScale=" + _finishConfirmPanel.transform.lossyScale.ToString("F4"));
        return true;
    }

    // 시야를 따라오는 월드 캔버스. ShowConfirmDialog 가 쓰는 값과 같게 맞춘다
    // (거리 0.85m, 스케일 0.0009, sortingOrder 400 — 다른 MVP 패널 위).
    private Canvas CreateFollowCanvas(string name)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1200f, 700f);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 400;
        canvas.worldCamera = Camera.main;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            root.transform.position =
                cam.transform.position + forward * 0.85f;
            root.transform.rotation = Quaternion.LookRotation(forward);
        }
        root.transform.localScale = Vector3.one * 0.0009f;
        root.AddComponent<MvpGentleFollow>().Configure(0.85f);
        return canvas;
    }

    // 씬 전체에서 이름으로 찾는다(비활성 포함).
    // 확인 패널은 보드 밖으로 빼 두었으므로 MainSketchPanel 아래만 봐서는 못 찾는다.
    private GameObject FindSceneObject(string objectName)
    {
        foreach (Transform t in
                 FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.name == objectName)
                return t.gameObject;
        }
        return null;
    }

    // 보드(MainSketchPanel) 아래에 미리 놓아 둔 패널을 이름으로 찾는다.
    //
    // 씬에 놓아 두는 방식을 쓰는 이유:
    //   - 위치·스케일을 에디터에서 눈으로 맞출 수 있다(코드로 좌표를 찍어 맞히지 않아도 된다)
    //   - MainSketchPanel 은 MainSketchView 가 붙은 오브젝트라, 그 자식은
    //     MvpWorkspacePolish.KeepOnlyDesignerPanel() 의 '시안 안쪽' 예외에 자동으로 들어간다.
    //     캔버스 바로 밑에 만들면 Graphic 이 전부 꺼져 화면에서만 사라진다.
    //
    // 꺼져 있는 오브젝트를 찾아야 하므로 비활성 자식까지 훑는다.
    private GameObject FindBoardPanel(string panelName)
    {
        Transform root = _workspaceLayout != null
            ? _workspaceLayout.MainSketchPanel
            : null;
        if (root == null && _mainSketchView != null)
            root = _mainSketchView.transform;
        if (root == null && _mainSketchCanvas != null)
            root = _mainSketchCanvas.transform;
        if (root == null)
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == panelName)
                return t.gameObject;
        }
        return null;
    }

    // '예' — 확인 패널을 닫고 회의 리포트로 넘어간다.
    private void AcceptFinishDesign()
    {
        CloseFinishConfirmPanel();
        ShowReportPanel();
    }

    // '아니오' — 패널만 닫고 설계를 계속한다.
    // 씬에 놓아 둔 오브젝트라 파기하지 않고 꺼 두기만 한다(다시 물어볼 때 재사용).
    // 따라다니는 캔버스는 우리가 만든 것이므로 같이 지운다 —
    // 패널은 그 캔버스 밑에 남겨 둔 채 꺼 두면 다음에 다시 붙일 때 그대로 쓸 수 있다.
    private void CloseFinishConfirmPanel()
    {
        if (_finishConfirmPanel != null)
        {
            _finishConfirmPanel.SetActive(false);
            // 캔버스를 지우면 자식인 패널도 함께 사라지므로 먼저 떼어 낸다.
            _finishConfirmPanel.transform.SetParent(transform, false);
            _finishConfirmPanel = null;
        }

        if (_confirmFollowCanvas != null)
        {
            Destroy(_confirmFollowCanvas.gameObject);
            _confirmFollowCanvas = null;
        }
    }

    // 회의 리포트 패널(01_Prefabs/Graph/Designer/ReportPanel)을 띄우고 서버 값을 채운다.
    //   씬에 미리 놓아 둔 게 있으면 그것을 쓰고, 없으면 프리팹에서 만든다.
    private void ShowReportPanel()
    {
        if (_reportPanel == null)
        {
            ReportPanelBinder existing =
                FindFirstObjectByType<ReportPanelBinder>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _reportPanel = existing.gameObject;
            }
            else if (_reportPanelPrefab != null)
            {
                // ReportPanel 프리팹에는 Canvas 가 없다 — 월드 캔버스의 자식으로 쓰도록 만들어졌다.
                // 씬 루트에 그냥 만들면 렌더링 자체가 되지 않으므로(리포트는 받아왔는데 화면에
                // 아무것도 안 보이던 원인) 설계 보드와 같은 캔버스 밑에 붙인다.
                Transform parent = _mainSketchCanvas != null
                    ? _mainSketchCanvas.transform
                    : null;
                _reportPanel = Instantiate(_reportPanelPrefab, parent, false);

                if (_reportPanel.transform is RectTransform rect)
                {
                    rect.anchoredPosition3D = new Vector3(0f, 0f, -30f);   // 보드보다 앞
                    rect.localRotation = Quaternion.identity;
                    rect.localScale = Vector3.one;
                }
            }
        }

        if (_reportPanel == null)
        {
            Debug.LogWarning(
                "[MVP Flow] 리포트 패널을 찾지 못했습니다. " +
                "MvpClassroomFlow 의 Report Panel Prefab 에 " +
                "01_Prefabs/Graph/Designer/ReportPanel 을 넣어 주세요.");
            SetWorkspaceMessage(
                "리포트 화면 연결을 확인해 주세요.", MvpStudentUiFactory.Coral);
            return;
        }

        _reportPanel.SetActive(true);
        _reportPanel.transform.SetAsLastSibling();   // 보드 위에 그린다
        HideWorkspaceForReport();

        // 리포트 조회 클라이언트가 씬에 없으면 붙인다.
        // (ReportApiClient.Awake 가 GraphSyncClient 를 스스로 찾으므로 배선은 필요 없다.)
        if (FindFirstObjectByType<ReportApiClient>() == null)
        {
            GameObject host = _graphSyncClient != null
                ? _graphSyncClient.gameObject
                : _reportPanel;
            host.AddComponent<ReportApiClient>();
        }

        // 월드 캔버스는 GraphicRaycaster 만으로는 XR 에서 안 눌린다(Meta ISDK 배선이 필요).
        // 프리팹에 Canvas 가 없으므로 부모(설계 보드) 캔버스를 다시 배선한다.
        Canvas canvas = _reportPanel.GetComponentInParent<Canvas>();
        if (canvas != null)
            AttachPointableCanvas(canvas);

        WireReportRestartButton();

        ReportPanelBinder binder =
            _reportPanel.GetComponentInChildren<ReportPanelBinder>(true);
        if (binder != null)
            binder.ShowReport();
        else
            Debug.LogWarning("[MVP Flow] ReportPanel 에 ReportPanelBinder 가 없습니다.");
    }

    // 리포트 프리팹의 'Restart' 버튼 — 리포트에서 나가는 유일한 길이다.
    //   HideWorkspaceForReport 가 보드를 통째로 숨기고, MvpTableSettingsDock('방 나가기')은
    //   MVP_SH 씬에 놓여 있지 않다(참조 0). 이 버튼이 없으면 리포트에서 갇힌다.
    // 프리팹 onClick 은 씬 오브젝트를 참조할 수 없으므로 런타임 배선이 유일한 방법이다.
    //   (시안 버튼 Button_2D / Button_3D / Report 와 같은 방식)
    private void WireReportRestartButton()
    {
        if (_reportPanel == null) return;

        Button restart = null;
        foreach (Transform t in
                 _reportPanel.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.name != "Restart") continue;
            restart = t.GetComponent<Button>();
            if (restart != null) break;
        }

        if (restart == null)
        {
            Debug.LogWarning(
                "[MVP Flow] ReportPanel 에서 'Restart' 버튼을 찾지 못했습니다. " +
                "리포트에서 처음으로 돌아갈 방법이 없습니다.");
            return;
        }

        // 리포트를 띄울 때마다 배선하므로 중복 등록을 먼저 지운다.
        restart.onClick.RemoveListener(RestartFromReport);
        restart.onClick.AddListener(RestartFromReport);
        restart.interactable = true;
    }

    // 리포트를 닫고 처음으로 돌아간다.
    // '설계 마치기' 단계에서 이미 확인을 받았으므로 여기서 또 묻지 않는다.
    //
    // 돌아갈 '처음'은 빌드 구성에 따라 다르다.
    //   로비가 함께 빌드돼 있으면 → 로비 씬
    //   회의실만 빌드돼 있으면    → 이 씬의 첫 화면(Welcome)
    private void RestartFromReport()
    {
        // 숨겨 둔 보드를 먼저 되살린다 — 안 그러면 다음에 방에 들어와도
        // 보드·노드·연결선이 꺼진 채로 남는다(_hiddenForReport 가 복원되지 않는다).
        RestoreWorkspaceAfterReport();

        if (TryGetLobbySceneIndex(out int lobbyIndex))
        {
            if (NetworkManager.runnerInsatance != null)
            {
                // Fusion 러너를 정상 종료하면 NetworkManager.OnShutdown 이 로비 씬을 연다.
                // (intentionalShutdown 을 세워 세션 복구가 끼어들지 않게 하는 것도 그쪽이 한다)
                Debug.Log("[MVP Flow] 리포트 Restart → 로비로 돌아갑니다.");
                NetworkManager.ReturnToLobby();
                return;
            }

            // 러너가 없으면(오프라인으로 회의실에 들어온 경우) 씬만 직접 연다.
            // NetworkManager.ReturnToLobby 의 폴백은 "LobbyScene" 이름을 가정하므로 쓰지 않는다.
            Debug.Log("[MVP Flow] 리포트 Restart → 로비 씬을 직접 엽니다(러너 없음).");
            _networkSession?.EndSession();
            SceneManager.LoadScene(lobbyIndex);
            return;
        }

        // 회의실만 빌드된 경우 — 씬을 갈아탈 곳이 없으니 이 씬의 처음 화면으로.
        Debug.Log("[MVP Flow] 리포트 Restart → 이 씬의 처음 화면으로(빌드에 로비 씬 없음).");
        LeaveRoom();
    }

    // 빌드 설정에 로비 씬이 들어 있는지 본다.
    // 이름으로 찾는 이유: 빌드 인덱스는 팀원마다 다르고, 로비 씬 이름이
    // MvpLobby / LobbyScene 두 가지로 쓰인 적이 있다(NetworkManager 참고).
    private static bool TryGetLobbySceneIndex(out int index)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrEmpty(path)) continue;

            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    // 리포트를 볼 때 뒤에 남아 비쳐 보이던 것들을 치운다.
    //   설계 보드(같은 캔버스의 형제들) + 노드 + 연결선.
    //   되돌릴 수 있게 "내가 끈 것"만 기억한다(원래 꺼져 있던 건 건드리지 않는다).
    private readonly List<GameObject> _hiddenForReport = new List<GameObject>();

    private void HideWorkspaceForReport()
    {
        _hiddenForReport.Clear();

        if (_mainSketchCanvas != null)
        {
            foreach (Transform child in _mainSketchCanvas.transform)
            {
                if (child == null) continue;
                if (_reportPanel != null && child == _reportPanel.transform) continue;
                if (!child.gameObject.activeSelf) continue;

                child.gameObject.SetActive(false);
                _hiddenForReport.Add(child.gameObject);
            }
        }

        foreach (NodeView node in
                 FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (node == null || !node.gameObject.activeSelf) continue;
            node.gameObject.SetActive(false);
            _hiddenForReport.Add(node.gameObject);
        }

        foreach (EdgeView edge in
                 FindObjectsByType<EdgeView>(FindObjectsSortMode.None))
        {
            if (edge == null || !edge.gameObject.activeSelf) continue;
            edge.gameObject.SetActive(false);
            _hiddenForReport.Add(edge.gameObject);
        }
    }

    // 리포트를 닫고 설계로 돌아갈 때 쓴다.
    public void RestoreWorkspaceAfterReport()
    {
        foreach (GameObject hidden in _hiddenForReport)
        {
            if (hidden != null)
                hidden.SetActive(true);
        }
        _hiddenForReport.Clear();

        if (_reportPanel != null)
            _reportPanel.SetActive(false);
    }

    // MainSketchPanel(디자이너 시안) 안에서 이름으로 버튼을 찾는다.
    // 비활성 자식도 훑는다 — 시작 시 꺼져 있는 경우가 있다.
    private Button FindSketchPanelButton(string name)
    {
        Transform panel = _workspaceLayout != null
            ? _workspaceLayout.MainSketchPanel
            : null;
        if (panel == null && _mainSketchCanvas != null)
            panel = _mainSketchCanvas.transform;
        if (panel == null)
            return null;

        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == name)
            {
                Button button = t.GetComponent<Button>();
                if (button != null)
                    return button;
            }
        }
        return null;
    }


    private void RecordWorkspaceSketch(Texture2D source)
    {
        if (source == null)
            return;

        Texture2D historyCopy = CloneTexture(source);
        if (historyCopy == null)
            return;

        _history.Add(new MvpSketchHistoryItem
        {
            texture = historyCopy,
            title = "그림 " + (_history.Count + 1),
            summary = _waterRocketGraph != null
                ? _waterRocketGraph.GetDesignSummary()
                : "현재 설계"
        });

        while (_history.Count > 8)
        {
            MvpSketchHistoryItem oldest = _history[0];
            _history.RemoveAt(0);
            if (oldest?.texture != null)
                Destroy(oldest.texture);
        }
    }


    // 중앙 스케치 자리표시자('아직 그림이 없어요')를 숨긴다.
    private void HideSketchPlaceholder()
    {
        if (_mainSketchCanvas == null) return;
        foreach (Transform t in
                 _mainSketchCanvas.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == "SketchPlaceholder")
            {
                t.gameObject.SetActive(false);
                return;
            }
        }
    }

    // 서버 3D 모델이 놓일 스테이지(받침 원판 포함)를 만들고 그 Transform 을 돌려준다.
    // 이미 있으면 재사용한다.
    //
    // 자리는 mock 로켓(MvpRocket3DStage)과 동일하게 메인 보드 기준으로 잡는다. 카메라 기준으로
    // 두면 XR 에서 Camera.main 좌표가 실제 시점과 달라 시야 밖으로 나간다(실측 y=-1.46).
    // 남이 만든 3D 가 공유돼 올 때 받는 쪽도 같은 자리에 받침을 세워야 하므로
    // MvpGenerated3DModelSync 가 호출할 수 있게 공개한다.
    // 3D 결과물을 바닥에서 이만큼 띄운다.
    private const float ServerStageHeightAboveFloor = 0.5f;

    /// <summary>
    /// 회의실 바닥 높이(월드 Y)를 구한다.
    /// 1) 그 자리 위에서 아래로 쏴서 실제 바닥을 찾고,
    /// 2) 안 되면 플레이어 배치가 쓰는 바닥 값을 쓰고,
    /// 3) 그것도 없으면 눈높이에서 역산한다.
    /// </summary>
    private float ResolveFloorY(Vector3 spot)
    {
        Vector3 from = spot + Vector3.up * 3f;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 12f,
                ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        MvpMeetingRoomPlayerController player =
            FindFirstObjectByType<MvpMeetingRoomPlayerController>();
        if (player != null)
            return player.FloorY;

        Camera cam = Camera.main;
        if (cam != null)
            return cam.transform.position.y - 1.55f;

        return spot.y;
    }

    public Transform EnsureServerModelStage()
    {
        if (_serverModelStage != null)
        {
            _serverModelStage.gameObject.SetActive(true);
            return _serverModelStage;
        }

        var go = new GameObject("ServerModelStage");
        _serverModelStage = go.transform;

        Transform board =
            _workspaceLayout != null
                ? _workspaceLayout.MainSketchPanel
                : null;
        if (board != null)
        {
            // 가로 자리는 보드 기준으로 잡되, 높이는 바닥에서 잰다.
            // 예전에는 보드 높이(눈높이)에 붙여 모델이 너무 높이 떴다.
            Vector3 spot =
                board.position +
                board.right * 0.55f -
                board.forward * 0.38f;
            spot.y = ResolveFloorY(spot) + ServerStageHeightAboveFloor;

            go.transform.position = spot;
            go.transform.rotation =
                Quaternion.LookRotation(board.forward, Vector3.up);
            // 보드에 붙여둔다. '내 자리 설정'으로 워크스페이스를 다시 배치하면 보드가 움직이는데,
            // 루트 오브젝트로 두면 모델만 제자리에 남아 보드와 따로 논다.
            go.transform.SetParent(board, true);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    cam.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
                Vector3 spot =
                    cam.transform.position + forward * 1.05f;
                spot.y = ResolveFloorY(spot) + ServerStageHeightAboveFloor;

                go.transform.position = spot;
                go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        // 받침 원판. 기본 실린더는 반지름 0.5·높이 2 라 원하는 치수로 스케일한다.
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        Collider padCollider = pad.GetComponent<Collider>();
        if (padCollider != null)
            Destroy(padCollider);   // 손/레이가 원판에 걸리지 않도록
        pad.transform.SetParent(_serverModelStage, false);
        pad.transform.localPosition = Vector3.zero;
        pad.transform.localScale = new Vector3(ServerStagePadRadius * 2f, 0.004f, ServerStagePadRadius * 2f);

        // [중요] CreatePrimitive 가 붙여주는 머티리얼은 Built-in 파이프라인의 Standard 셰이더라
        // URP 프로젝트에서는 그대로 핑크(셰이더 없음)로 렌더된다. URP 셰이더로 교체해야 한다.
        var renderer = pad.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader padShader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default");   // 최후 수단(Always Included 에 있는 셰이더)
            if (padShader != null)
            {
                var padMaterial = new Material(padShader);
                // 회색이 살짝 도는 흰색. mock 로켓의 어두운 남색(PadColor)은 회의실 조명에서
                // 거의 검게 보여 모델 아래가 구멍처럼 뚫린 느낌을 준다.
                padMaterial.color = new Color(0.90f, 0.90f, 0.92f, 1f);
                renderer.material = padMaterial;
            }
            else
            {
                Debug.LogWarning("[MvpClassroomFlow] 원판용 URP 셰이더를 찾지 못했습니다.");
            }
        }

        return _serverModelStage;
    }

    // 3D는 매번 새로 만들지 않는다. 이미 있으면 토글해 회의 중 비교할 수 있다.
    //
    // 온라인이면 서버 3D(Meshy GLB)를, 오프라인이면 기존 mock(MvpRocket3DStage)을 쓴다.
    // [비용] 서버 3D 는 호출 1회당 Meshy 과금이 발생하고 서버에 중복 방지가 없다.
    //   그래서 "이미 만든 모델이 있으면 절대 재생성하지 않고 토글만" 하는 기존 성격을 그대로 살린다.
    private void ToggleOrCreateWorkspace3D()
    {
        bool useServer3D =
            _session.online &&
            _graphSyncClient != null &&
            _graphSyncClient.IsConnected &&
            _generate3DController != null;

        if (useServer3D)
        {
            // 1) 이미 만든 서버 모델이 있으면 보이기/숨기기만 한다. 재요청하지 않는다.
            if (_generate3DController.HasModel)
            {
                bool show = !_generate3DController.IsModelVisible;
                _generate3DController.SetModelVisible(show);
                // 원판도 같이 숨긴다. 모델만 사라지고 받침만 남으면 어색하다.
                if (_serverModelStage != null)
                    _serverModelStage.gameObject.SetActive(show);
                if (MvpAudioCue.Instance != null)
                    MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick);
                SetWorkspaceMessage(
                    show ? "3D 모델을 보입니다." : "3D 모델을 숨겼어요.",
                    MvpStudentUiFactory.Cyan);
                RefreshThreeDViewButton();
                return;
            }

            // 2) 이미 만드는 중이면 중복 요청하지 않는다(그대로 두면 과금이 두 배가 된다).
            if (_generate3DController.IsGenerating)
            {
                SetWorkspaceMessage(
                    "3D 모델을 만드는 중이에요. 조금만 기다려 주세요.",
                    MvpStudentUiFactory.Cyan);
                return;
            }

            // 3) 3D 는 2D 결과(asset_id)를 입력으로 받는다. 없으면 요청 자체를 보내지 않는다.
            //    (테스트 URL 이 설정돼 있으면 서버를 안 거치므로 이 검사를 건너뛴다.)
            if (!_generate3DController.HasDebugModelUrl &&
                (_generate2DController == null ||
                 string.IsNullOrEmpty(_generate2DController.CurrentAssetId)))
            {
                SetWorkspaceMessage(
                    "먼저 2D 그림을 만들어 주세요.",
                    MvpStudentUiFactory.Amber);
                return;
            }

            StartCoroutine(ServerGenerate3DRoutine());
            return;
        }

        // 오프라인(체험 모드): 서버가 없으므로 기존 mock 로켓을 쓴다.
        if (_rocketStage == null)
        {
            BeginWorkspace3D();
            return;
        }

        ToggleRocketVisibility();
    }

    // 서버 3D 생성을 요청하고 완료까지 기다린다. 기다리는 동안 버튼을 눌러도
    // 위 (2) 분기가 막으므로 중복 과금이 나지 않는다.
    private IEnumerator ServerGenerate3DRoutine()
    {
        SetWorkspaceMessage(
            "AI가 3D 모델을 만들고 있어요. 시간이 걸릴 수 있어요...",
            MvpStudentUiFactory.Cyan);

        // 요청이 들어갔다는 신호로 받침 원판을 먼저 띄운다. 모델은 나중에 이 위에 올라온다.
        // (mock 로켓 MvpRocket3DStage 의 Pad 와 같은 자리·같은 톤)
        Transform stage = EnsureServerModelStage();
        _generate3DController.SetModelParent(stage);

        _generate3DController.RequestGenerate3D();

        // 컨트롤러가 손을 뗄 때까지(성공·실패·자체 타임아웃) 기다린다.
        // 요청이 거부된 경우엔 IsGenerating 이 서지 않아 즉시 빠진다.
        float elapsed = 0f;
        while (_generate3DController.IsGenerating &&
               elapsed < ServerModelWaitCapSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_generate3DController.HasModel)
        {
            if (MvpAudioCue.Instance != null)
                MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Success);
            SetWorkspaceMessage(
                "3D 모델이 만들어졌어요.",
                MvpStudentUiFactory.Mint);
        }
        else
        {
            // 실패하면 받침만 덩그러니 남는다. 같이 치운다.
            if (_serverModelStage != null)
                _serverModelStage.gameObject.SetActive(false);
            SetWorkspaceMessage(
                "3D 모델을 만들지 못했어요. 잠시 후 다시 시도해 주세요.",
                MvpStudentUiFactory.Coral);
        }

        RefreshThreeDViewButton();
    }


    public void RecenterWorkspaceFromWrist()
    {
        _workspaceLayout?.RecenterWorkspaceToActiveView();
        SetWorkspaceMessage(
            "작업판을 현재 자리 기준으로 다시 맞췄어요.",
            MvpStudentUiFactory.Mint);
    }

    public int GetPersonalSketchCount()
    {
        return _history.Count;
    }

    public string GetPersonalSketchTitle(int index)
    {
        if (index < 0 || index >= _history.Count)
            return "저장된 그림이 없어요";
        return string.IsNullOrEmpty(_history[index]?.title)
            ? "그림 " + (index + 1)
            : _history[index].title;
    }


    public Texture GetPersonalSketchTexture(int index)
    {
        if (index < 0 || index >= _history.Count)
            return null;
        return _history[index]?.texture;
    }


    private void RefreshThreeDViewButton()
    {
        if (_threeDViewButton == null)
            return;

        // 서버 3D(GLB)와 오프라인 mock 중 어느 쪽이든 "모델이 있다"로 본다.
        bool hasServerModel =
            _generate3DController != null && _generate3DController.HasModel;
        bool hasModel = _rocketStage != null || hasServerModel;
        bool visible =
            (_rocketStage != null && _rocketStage.gameObject.activeSelf) ||
            (hasServerModel && _generate3DController.IsModelVisible);

        TMP_Text label =
            _threeDViewButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = !hasModel
                ? "3D 생성하기"
                : visible
                    ? "3D 숨기기"
                    : "3D 보기";

        // 시안 버튼은 디자이너 스프라이트를 그대로 살린다. 여기서 색을 칠하면 그림 위에 덧칠된다.
        // (상태는 위의 라벨로만 알린다 — 시안 버튼에 글자가 없으면 label 이 null 이라 그것도 건너뛴다.)
        if (_usingDesignerButtons)
            return;

        Color stateColor = !hasModel
            ? MvpStudentUiFactory.GlassAction   // 기본: 보조 글래스 톤(위계 유지)
            : visible
                ? new Color(0.32f, 0.28f, 0.66f, 1f)
                : new Color(0.10f, 0.48f, 0.42f, 1f);
        MvpStudentUiFactory.SetButtonColor(
            _threeDViewButton,
            stateColor);
    }
    // 생성된 3D 모델 보이기/숨기기 토글.
    private void ToggleRocketVisibility()
    {
        if (_rocketStage == null)
        {
            SetWorkspaceMessage(
                "먼저 '3D 만들기'로 모델을 만들어요.",
                MvpStudentUiFactory.Amber);
            return;
        }
        bool show = !_rocketStage.gameObject.activeSelf;
        _rocketStage.gameObject.SetActive(show);
        if (MvpAudioCue.Instance != null)
            MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick);
        SetWorkspaceMessage(
            show ? "3D 모델을 보입니다." : "3D 모델을 숨겼어요.",
            MvpStudentUiFactory.Cyan);
        RefreshThreeDViewButton();
    }

    // '2D 만들기': 다음 단계로 넘어가지 않고, 지금 연결·활성화된 노드로 중앙 그림을 다시 만든다.
    // 오프라인이면 설계 반영 mock을 즉시 표시하고, 온라인이면 서버 AI 이미지도 함께 요청한다.
    private void BeginWorkspaceGenerate()
    {
        if (_workspaceGenBusy) return;
        StartCoroutine(WorkspaceGenerateRoutine());
    }

    private IEnumerator WorkspaceGenerateRoutine()
    {
        _workspaceGenBusy = true;

        if (_centerSketchImage == null)
            _centerSketchImage = FindCenterSketchImage();

        MvpRocketDesign design =
            _waterRocketGraph != null
                ? _waterRocketGraph.GetRocketDesign(_history.Count)
                : new MvpRocketDesign();

        // 온라인이면 서버 AI 이미지만 보여준다.
        // 예전에는 로컬 mock(MvpFallbackSketchGenerator)을 먼저 띄우고 서버 이미지가 오면 교체했는데,
        // 그 mock 이 설계값으로 그린 물로켓 그림이라 매번 비슷하게 나와 "새로 만든 게 아니라
        // 예전 걸 다시 보여준다"는 인상을 줬다. 서버 요청이 실패하면(예: job_id 누락 422)
        // AI 이미지가 영영 오지 않아 mock 만 남는 것도 구분이 안 됐다.
        bool useServer =
            _session.online &&
            _graphSyncClient != null &&
            _graphSyncClient.IsConnected &&
            _generate2DController != null;

        if (useServer)
        {
            // 이전 결과를 지운다 — 이번에 생성한 이미지만 보이게 하기 위함.
            // 지운 뒤 texture 가 다시 채워지는 것이 곧 "이번 요청의 결과 도착" 신호가 된다.
            if (_centerSketchImage != null)
            {
                _centerSketchImage.texture = null;
                _centerSketchImage.enabled = true;
                _centerSketchImage.color = Color.white;
            }
            HideSketchPlaceholder();
            SetWorkspaceMessage(
                "AI가 새 그림을 그리고 있어요...",
                MvpStudentUiFactory.Cyan);

            // 도착하면 Generate2DController 가 중앙 이미지를 채운다.
            _generate2DController.RequestGenerateGraphAll();

            // 결과를 기다린다. _workspaceGenBusy 가 유지되므로 이 사이 재요청은 막힌다.
            float cap = Mathf.Max(_imageWaitSeconds, ServerImageWaitCapSeconds);
            float elapsed = 0f;
            bool arrived = false;
            while (elapsed < cap)
            {
                if (_centerSketchImage != null &&
                    _centerSketchImage.texture != null)
                {
                    arrived = true;
                    break;
                }
                // 컨트롤러가 손을 뗐는데 이미지가 없으면 실패로 끝난 것이다.
                if (!_generate2DController.IsGenerating)
                    break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (arrived)
            {
                // 손목 히스토리에는 AI 결과를 남긴다(예전엔 여기서 mock 을 남겼다).
                RecordWorkspaceSketch(_centerSketchImage.texture as Texture2D);
                if (MvpAudioCue.Instance != null)
                    MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Success);
                SetWorkspaceMessage(
                    "가운데에 새 그림이 도착했어요.",
                    MvpStudentUiFactory.Mint);
            }
            else
            {
                SetWorkspaceMessage(
                    "그림을 만들지 못했어요. 잠시 후 다시 시도해 주세요.",
                    MvpStudentUiFactory.Coral);
            }
        }
        else
        {
            // 오프라인(체험 모드): 서버가 없으므로 설계 반영 mock 을 그대로 쓴다.
            Texture2D sketch =
                MvpFallbackSketchGenerator.CreateWaterRocketSketch(design);
            if (_centerSketchImage != null)
            {
                _centerSketchImage.texture = sketch;
                _centerSketchImage.enabled = true;
                _centerSketchImage.color = Color.white;
            }
            HideSketchPlaceholder();   // '아직 그림이 없어요' 문구 제거
            RecordWorkspaceSketch(sketch);   // 개인 손목 히스토리에만 저장
            if (_workspaceMockTexture != null)
                Destroy(_workspaceMockTexture);
            _workspaceMockTexture = sketch;

            if (MvpAudioCue.Instance != null)
                MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Success);
            SetWorkspaceMessage(
                "가운데에 새 설계 그림을 만들었어요.",
                MvpStudentUiFactory.Mint);
        }

        yield return null;
        _workspaceGenBusy = false;
    }

    // '3D 만들기': 앞 공간에 지금 설계로 3D 모델을 다시 만든다(반복 가능).
    private void BeginWorkspace3D()
    {
        MvpRocketDesign design =
            _waterRocketGraph != null
                ? _waterRocketGraph.GetRocketDesign(_history.Count)
                : new MvpRocketDesign();

        SpawnRocketStage(design);

        if (MvpAudioCue.Instance != null)
            MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Commit);
        SetWorkspaceMessage(
            "공동 테이블 위에 3D 모델을 만들었어요.",
            MvpStudentUiFactory.Cyan);
    }

    private void BuildPartRecommendationPanel()
    {
        EnsureSpatialToolCanvas();
        if (_toolCanvas == null) return;

        for (int i = _toolCanvas.transform.childCount - 1; i >= 0; i--)
            Destroy(_toolCanvas.transform.GetChild(i).gameObject);

        _toolCanvas.gameObject.SetActive(true);
        PositionSpatialToolCanvas();

        Image panel = MvpStudentUiFactory.CreatePanel(
            _toolCanvas.transform,
            "MvpPartRecommendationPanel",
            Vector2.zero,
            new Vector2(960f, 548f),
            new Color(0.035f, 0.050f, 0.080f, 0.995f),
            true);

        Outline rim = panel.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.48f, 0.58f, 0.76f, 0.28f);
        rim.effectDistance = new Vector2(2f, -2f);

        MvpStudentUiFactory.CreateText(
            panel.transform, "Eyebrow", "AI 부품 제안",
            new Vector2(-365f, 232f), new Vector2(180f, 32f),
            17f, TextAlignmentOptions.MidlineLeft, true,
            new Color(0.50f, 0.72f, 1f, 1f), 1);

        MvpStudentUiFactory.CreateText(
            panel.transform, "Title", "어떤 부품으로 시작할까요?",
            new Vector2(-155f, 190f), new Vector2(600f, 48f),
            29f, TextAlignmentOptions.MidlineLeft, true,
            new Color(0.96f, 0.98f, 1f, 1f), 1);

        MvpStudentUiFactory.CreateText(
            panel.transform, "Topic",
            "주제  ·  " + Safe(_session.topic, "새로운 설계 아이디어"),
            new Vector2(-155f, 151f), new Vector2(600f, 32f),
            17f, TextAlignmentOptions.MidlineLeft, false,
            new Color(0.65f, 0.71f, 0.82f, 1f), 1);

        MvpStudentUiFactory.CreateButton(
            panel.transform, "Close", "나중에",
            new Vector2(393f, 218f), new Vector2(118f, 46f),
            new Color(0.10f, 0.14f, 0.22f, 1f),
            DismissPartRecommendations, 16f);

        _recommendationButtons.Clear();
        CreateRecommendationCard(
            panel.transform, "몸통", "물을 담고 압력을 버티는 중심 부품", -290f);
        CreateRecommendationCard(
            panel.transform, "날개", "흔들림을 줄이고 방향을 잡는 부품", 0f);
        CreateRecommendationCard(
            panel.transform, "노즈콘", "공기 저항을 줄이는 앞쪽 부품", 290f);

        _customPartInput = MvpStudentUiFactory.CreateInput(
            panel.transform, "CustomPartInput", "직접 추가할 부품 이름",
            new Vector2(-210f, -218f), new Vector2(460f, 56f), 19f);

        Image customInputSurface = _customPartInput.GetComponent<Image>();
        if (customInputSurface != null)
            customInputSurface.color = new Color(0.075f, 0.095f, 0.145f, 1f);
        if (_customPartInput.textComponent != null)
            _customPartInput.textComponent.color =
                new Color(0.94f, 0.96f, 1f, 1f);
        if (_customPartInput.placeholder is TMP_Text customPlaceholder)
            customPlaceholder.color =
                new Color(0.55f, 0.62f, 0.74f, 1f);

        MvpXrKeyboardInput customInputBridge =
            _customPartInput.GetComponent<MvpXrKeyboardInput>();
        if (customInputBridge == null)
            customInputBridge =
                _customPartInput.gameObject.AddComponent<MvpXrKeyboardInput>();
        customInputBridge.Configure(_customPartInput);
        _customPartInput.onEndEdit.AddListener(SubmitCustomPartFromWorldKeyboard);

        MvpStudentUiFactory.CreateButton(
            panel.transform, "AddCustomPart", "직접 추가",
            new Vector2(112f, -218f), new Vector2(158f, 56f),
            new Color(0.12f, 0.42f, 0.54f, 1f),
            AddCustomPart, 18f);

        MvpStudentUiFactory.CreateButton(
            panel.transform, "StartSpatialDesign", "설계 시작",
            new Vector2(335f, -218f), new Vector2(240f, 56f),
            new Color(0.30f, 0.38f, 0.82f, 1f),
            DismissPartRecommendations, 19f);
    }

    private void CreateRecommendationCard(
        Transform parent,
        string part,
        string reason,
        float x)
    {
        Image card = MvpStudentUiFactory.CreatePanel(
            parent,
            "Recommendation_" + part,
            new Vector2(x, 5f),
            new Vector2(270f, 218f),
            new Color(0.065f, 0.085f, 0.130f, 1f),
            true);

        Outline border = card.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(0.48f, 0.58f, 0.76f, 0.20f);
        border.effectDistance = new Vector2(1f, -1f);

        MvpStudentUiFactory.CreateText(
            card.transform, "PartName", part,
            new Vector2(0f, 62f), new Vector2(220f, 40f),
            25f, TextAlignmentOptions.Center, true,
            new Color(0.96f, 0.98f, 1f, 1f), 1);

        MvpStudentUiFactory.CreateText(
            card.transform, "Reason", reason,
            new Vector2(0f, 15f), new Vector2(224f, 62f),
            16f, TextAlignmentOptions.Center, false,
            new Color(0.66f, 0.72f, 0.83f, 1f), 2);

        Button add = MvpStudentUiFactory.CreateButton(
            card.transform, "Accept", "추가하기",
            new Vector2(-48f, -69f), new Vector2(150f, 50f),
            new Color(0.30f, 0.38f, 0.82f, 1f),
            () => AcceptRecommendedPart(part), 18f);
        _recommendationButtons[part] = add;

        MvpStudentUiFactory.CreateButton(
            card.transform, "Reject", "제외",
            new Vector2(88f, -69f), new Vector2(92f, 50f),
            new Color(0.12f, 0.15f, 0.22f, 1f),
            () => RejectRecommendedPart(part), 17f);

        if (_resolvedRecommendations.Contains(part))
            UpdateRecommendationButton(part);
    }

    private void AcceptRecommendedPart(string part)
    {
        bool added =
            _waterRocketGraph != null &&
            _waterRocketGraph.AddPart(part);
        if (!added)
        {
            SetWorkspaceMessage(
                part + " 부품을 추가하지 못했어요.",
                MvpStudentUiFactory.Coral);
            return;
        }

        _resolvedRecommendations.Add(part);
        UpdateRecommendationButton(part);
        SetWorkspaceMessage(
            part + " 부품을 설계 보드에 추가했어요.",
            MvpStudentUiFactory.Mint);
        RefreshWorkspaceDock();
    }

    private void RejectRecommendedPart(string part)
    {
        _resolvedRecommendations.Add(part);
        UpdateRecommendationButton(part);
        SetWorkspaceMessage(
            part + " 추천은 제외했어요. 나중에 다시 열 수 있어요.",
            MvpStudentUiFactory.Amber);
    }

    private void UpdateRecommendationButton(string part)
    {
        if (!_recommendationButtons.TryGetValue(
                part,
                out Button button) ||
            button == null)
            return;

        bool added =
            _waterRocketGraph != null &&
            _waterRocketGraph.HasPart(part);
        button.interactable = false;
        MvpStudentUiFactory.SetButtonColor(
            button,
            added
                ? MvpStudentUiFactory.Mint
                : MvpStudentUiFactory.MutedInk);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = added ? "추가됨" : "제외됨";
    }

    private void AddCustomPart()
    {
        if (_customPartInput == null ||
            string.IsNullOrWhiteSpace(_customPartInput.text))
        {
            SetWorkspaceMessage(
                "추가할 부품 이름을 입력해 주세요.",
                MvpStudentUiFactory.Coral);
            return;
        }

        string label = _customPartInput.text.Trim();
        bool added =
            _waterRocketGraph != null &&
            _waterRocketGraph.AddPart(label);
        if (!added)
        {
            SetWorkspaceMessage(
                "부품을 추가하지 못했어요. 다시 시도해 주세요.",
                MvpStudentUiFactory.Coral);
            return;
        }

        _customPartInput.text = "";
        SetWorkspaceMessage(
            label + " 부품을 직접 추가했어요.",
            MvpStudentUiFactory.Mint);
        RefreshWorkspaceDock();
    }

    // 월드 키보드의 enter는 이 입력칸의 onEndEdit을 호출한다.
    // 일반 포인터로 "직접 추가"를 누를 때의 중복 제출은 막는다.
    private void SubmitCustomPartFromWorldKeyboard(string ignored)
    {
        if (MvpWorldKeyboard.IsOpen)
            AddCustomPart();
    }

    private void DismissPartRecommendations()
    {
        _recommendationsDismissed = true;
        if (_toolCanvas != null)
            _toolCanvas.gameObject.SetActive(false);
        _spatialGesture?.SetCreationEnabled(true);
        SetWorkspaceMessage(
            "공간 설계를 시작했어요.",
            MvpStudentUiFactory.HoloCyan);
        ShowGestureCoachOnce();
    }

    // 손 조작(주먹 생성·손목 메뉴)은 눈에 보이지 않는 기능이라, 설계를 처음 시작할 때
    // 한 번만 짧게 알려 준다. (기기당 1회 — PlayerPrefs)
    private void ShowGestureCoachOnce()
    {
        const string coachKey = "Mvp.GestureCoachShown";
        if (PlayerPrefs.GetInt(coachKey, 0) == 1)
            return;

        ShowConfirmDialog(
            "손으로 이렇게 만들어요",
            "주먹을 2초 쥐면 새 아이디어 노드가 생겨요.\n" +
            "왼손바닥을 바라보면 내 메뉴(그림 기록)가 열려요.\n" +
            "아래 '말로 추가' 버튼으로 말해서 만들 수도 있어요.",
            "알겠어요",
            MvpStudentUiFactory.Primary,
            // 실제로 '알겠어요'를 눌러 확인했을 때만 1회 소진 처리한다.
            // (표시 직전에 소진하면, 곧바로 다른 팝업에 덮여 못 본 채 사라질 수 있다.)
            () => PlayerPrefs.SetInt(coachKey, 1),
            false);
    }

    private void HandleSpatialRootCreated(
        string nodeId,
        Vector3 position)
    {
        _mainSketchView?.Refresh();
        SetWorkspaceMessage(
            "새 아이디어가 생겼어요. 이름을 바꾸거나 +로 생각을 더 붙여 보세요.",
            MvpStudentUiFactory.Mint);
        RefreshWorkspaceDock();
    }

    private void HandleGestureGuidance(string message)
    {
        if (_state != MvpFlowState.Design)
            return;
        SetWorkspaceMessage(
            message,
            MvpStudentUiFactory.HoloCyan);
    }

    private void HideLegacyHistoryDots()
    {
        if (_mainSketchCanvas == null) return;

        Transform[] children =
            _mainSketchCanvas.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null ||
                child == _mainSketchCanvas.transform)
                continue;

            if (child.name.IndexOf(
                    "History",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                child.gameObject.SetActive(false);
        }
    }

    private void EnsureSpatialToolCanvas()
    {
        if (_toolCanvas != null)
        {
            PositionSpatialToolCanvas();
            return;
        }

        GameObject root = new GameObject(
            "MvpPartRecommendationCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900f, 540f);

        _toolCanvas = root.GetComponent<Canvas>();
        _toolCanvas.renderMode = RenderMode.WorldSpace;
        _toolCanvas.sortingOrder = 320;
        _toolCanvas.worldCamera = Camera.main;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 2f;
        scaler.referencePixelsPerUnit = 100f;

        PositionSpatialToolCanvas();
        AttachPointableCanvas(_toolCanvas);
        // 손을 패널 너머로 뻗으면 반투명해지며 뒤 노드/보드 조작을 허용한다.
        root.AddComponent<MvpPanelXray>();
    }

    private void PositionSpatialToolCanvas()
    {
        if (_toolCanvas == null)
            return;

        Transform board =
            _workspaceLayout != null
                ? _workspaceLayout.MainSketchPanel
                : null;
        MvpXrCanvasAnchor anchor =
            _toolCanvas.GetComponent<MvpXrCanvasAnchor>();

        if (board != null)
        {
            if (anchor != null)
                anchor.enabled = false;

            MvpWorkspaceSidePanelFollower follower =
                _toolCanvas.GetComponent<
                    MvpWorkspaceSidePanelFollower>();
            if (follower == null)
                follower =
                    _toolCanvas.gameObject.AddComponent<
                        MvpWorkspaceSidePanelFollower>();
            follower.enabled = true;
            follower.Configure(
                board,
                0.00080f,
                0.10f,
                -0.19f);
            return;
        }

        MvpWorkspaceSidePanelFollower existingFollower =
            _toolCanvas.GetComponent<
                MvpWorkspaceSidePanelFollower>();
        if (existingFollower != null)
            existingFollower.enabled = false;

        if (anchor == null)
            anchor =
                _toolCanvas.gameObject.AddComponent<
                    MvpXrCanvasAnchor>();
        anchor.enabled = true;
        anchor.Configure(1.30f, 0.02f, 0.00105f);
        anchor.Recenter();
    }

    private void BuildReviewPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "그림으로 만들기 전에 확인해요",
            new Vector2(0f, 300f),
            new Vector2(900f, 62f),
            42f,
            TextAlignmentOptions.Center,
            true);

        int requirements =
            _waterRocketGraph != null
                ? _waterRocketGraph.RequirementCount
                : 0;
        int parts =
            _waterRocketGraph != null
                ? _waterRocketGraph.CorePartConnectionCount
                : 0;

        CreateMetricCard(-270f, 205f, requirements + "개",
            "모은 아이디어", MvpStudentUiFactory.Primary);
        CreateMetricCard(0f, 205f, parts + "/3",
            "아이디어를 붙인 부품", MvpStudentUiFactory.Cyan);
        CreateMetricCard(270f, 205f,
            _waterRocketGraph != null &&
            _waterRocketGraph.HasMinimumDesign
                ? "준비 완료"
                : "조금 더",
            "그림 준비",
            _waterRocketGraph != null &&
            _waterRocketGraph.HasMinimumDesign
                ? MvpStudentUiFactory.Mint
                : MvpStudentUiFactory.Amber);

        Image summaryCard = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "SummaryCard",
            new Vector2(0f, 15f),
            new Vector2(1080f, 260f),
            Color.white,
            true);

        MvpStudentUiFactory.CreateText(
            summaryCard.transform,
            "SummaryTitle",
            "우리 팀의 최종 설계",
            new Vector2(0f, 95f),
            new Vector2(960f, 42f),
            26f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Ink,
            1);

        MvpStudentUiFactory.CreateText(
            summaryCard.transform,
            "Summary",
            _waterRocketGraph != null
                ? _waterRocketGraph.GetDesignSummary()
                : "설계 정보를 확인할 수 없습니다.",
            new Vector2(0f, -25f),
            new Vector2(940f, 165f),
            23f,
            TextAlignmentOptions.TopLeft,
            false,
            MvpStudentUiFactory.Ink,
            6);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "BackToDesign",
            "설계 수정하기",
            new Vector2(-240f, -265f),
            new Vector2(300f, 68f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Design),
            23f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Generate",
            "물로켓 그림 만들기",
            new Vector2(190f, -265f),
            new Vector2(440f, 74f),
            MvpStudentUiFactory.Primary,
            BeginGenerate,
            27f);
    }

    private void BuildGeneratingPage()
    {
        CreateRocketIllustration(
            _contentRoot,
            new Vector2(-300f, 10f),
            0.95f);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "물로켓 그림을 만들고 있어요",
            new Vector2(260f, 160f),
            new Vector2(700f, 80f),
            39f,
            TextAlignmentOptions.Center,
            true);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Body",
            _session.online
                ? "서버 결과를 기다리는 중이에요.\n시간이 오래 걸리면 체험용 스케치로 이어집니다."
                : "부품과 아이디어를 한 장의 그림으로 바꾸고 있어요.",
            new Vector2(260f, 50f),
            new Vector2(650f, 110f),
            24f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            3);

        Image progress = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "ProgressTrack",
            new Vector2(260f, -75f),
            new Vector2(520f, 24f),
            MvpStudentUiFactory.Border,
            false);

        Image fill = MvpStudentUiFactory.CreatePanel(
            progress.transform,
            "ProgressFill",
            new Vector2(-130f, 0f),
            new Vector2(250f, 18f),
            MvpStudentUiFactory.Cyan,
            false);
        fill.rectTransform.localRotation = Quaternion.identity;
        // 멈춘 것처럼 보이지 않게 좌우 왕복 애니메이션(페이지가 지워지면 함께 종료).
        StartCoroutine(AnimateProgressPulse(fill.rectTransform));

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Tip",
            "Tip  물의 양과 공기 압력은 실제 실험에서 꼭 안전 수칙을 지켜요.",
            new Vector2(260f, -185f),
            new Vector2(700f, 80f),
            21f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.WarningInk,
            2);
    }

    // 버튼 라벨을 잠깐 바꿨다가 되돌린다(복사됨! 등 1회성 피드백).
    private IEnumerator FlashButtonLabel(
        TMP_Text label, string flash, string normal)
    {
        label.text = flash;
        yield return new WaitForSecondsRealtime(1.6f);
        if (label != null)
            label.text = normal;
    }

    // 트랙(520) 안에서 fill(250)을 좌우로 왕복시킨다. 페이지 전환으로 fill 이 파괴되면 종료.
    private IEnumerator AnimateProgressPulse(RectTransform fill)
    {
        const float travel = 130f;   // (520 - 250) / 2
        float t = 0f;
        while (fill != null)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.PingPong(t * 170f, travel * 2f) - travel;
            fill.anchoredPosition = new Vector2(x, 0f);
            yield return null;
        }
    }

    private void BuildResultPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "물로켓 설계 그림 완성",
            new Vector2(0f, 300f),
            new Vector2(900f, 64f),
            42f,
            TextAlignmentOptions.Center,
            true);

        Image imageCard = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "ImageCard",
            new Vector2(-315f, 5f),
            new Vector2(650f, 470f),
            Color.white,
            true);

        RawImage preview = MvpStudentUiFactory.CreateRawImage(
            imageCard.transform,
            "Preview",
            Vector2.zero,
            new Vector2(610f, 420f));
        preview.texture =
            _history.Count > 0
                ? _history[_history.Count - 1].texture
                : _centerSketchImage?.texture;

        Image summaryCard = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "ResultSummary",
            new Vector2(385f, 35f),
            new Vector2(520f, 400f),
            MvpStudentUiFactory.SurfaceBlue,
            false);

        MvpStudentUiFactory.CreateText(
            summaryCard.transform,
            "Badge",
            _history.Count > 0 &&
            _history[_history.Count - 1].fromServer
                ? "AI가 만든 그림"
                : "체험용 설계 그림",
            new Vector2(0f, 145f),
            new Vector2(420f, 42f),
            20f,
            TextAlignmentOptions.Center,
            true,
            _history.Count > 0 &&
            _history[_history.Count - 1].fromServer
                ? MvpStudentUiFactory.SuccessInk
                : MvpStudentUiFactory.WarningInk,
            1);

        MvpStudentUiFactory.CreateText(
            summaryCard.transform,
            "SummaryTitle",
            "그림에 반영된 설계",
            new Vector2(0f, 90f),
            new Vector2(430f, 48f),
            27f,
            TextAlignmentOptions.Center,
            true);

        MvpStudentUiFactory.CreateText(
            summaryCard.transform,
            "Summary",
            _waterRocketGraph != null
                ? _waterRocketGraph.GetDesignSummary()
                : "",
            new Vector2(0f, -35f),
            new Vector2(430f, 205f),
            20f,
            TextAlignmentOptions.TopLeft,
            false,
            MvpStudentUiFactory.Ink,
            7);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Modify",
            "설계 수정",
            new Vector2(-350f, -285f),
            new Vector2(260f, 62f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Design),
            22f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "History",
            "히스토리",
            new Vector2(-35f, -285f),
            new Vector2(260f, 62f),
            MvpStudentUiFactory.CyanDeep,
            () => ShowState(MvpFlowState.History),
            22f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "ThreeD",
            "3D 단계 보기",
            new Vector2(330f, -285f),
            new Vector2(340f, 66f),
            MvpStudentUiFactory.Primary,
            () => ShowState(MvpFlowState.ThreeD),
            24f);
    }

    private void BuildHistoryPage()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "설계 그림 기록",
            new Vector2(0f, 300f),
            new Vector2(900f, 64f),
            42f,
            TextAlignmentOptions.Center,
            true);

        if (_history.Count == 0)
        {
            MvpStudentUiFactory.CreateText(
                _contentRoot,
                "Empty",
                "아직 만든 설계 그림이 없어요.",
                Vector2.zero,
                new Vector2(800f, 80f),
                28f,
                TextAlignmentOptions.Center,
                true,
                MvpStudentUiFactory.MutedInk,
                2);
        }
        else
        {
            MvpSketchHistoryItem current =
                _history[_history.Count - 1];

            Image previewCard = MvpStudentUiFactory.CreatePanel(
                _contentRoot,
                "PreviewCard",
                new Vector2(0f, 45f),
                new Vector2(760f, 440f),
                Color.white,
                true);
            RawImage preview = MvpStudentUiFactory.CreateRawImage(
                previewCard.transform,
                "Preview",
                Vector2.zero,
                new Vector2(720f, 390f));
            preview.texture = current.texture;

            float totalWidth =
                Mathf.Min(1000f, _history.Count * 150f);
            float startX = -totalWidth * 0.5f + 75f;
            for (int i = 0; i < _history.Count; i++)
            {
                int index = i;
                Button thumbButton =
                    MvpStudentUiFactory.CreateButton(
                        _contentRoot,
                        "History_" + i,
                        "",
                        new Vector2(startX + i * 150f, -225f),
                        new Vector2(132f, 92f),
                        i == _history.Count - 1
                            ? MvpStudentUiFactory.Primary
                            : MvpStudentUiFactory.SurfaceBlue,
                        () =>
                        {
                            preview.texture =
                                _history[index].texture;
                        },
                        16f);
                RawImage thumb =
                    MvpStudentUiFactory.CreateRawImage(
                        thumbButton.transform,
                        "Thumbnail",
                        Vector2.zero,
                        new Vector2(116f, 76f));
                thumb.texture = _history[i].texture;
            }
        }

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Back",
            "결과로 돌아가기",
            new Vector2(0f, -315f),
            new Vector2(350f, 62f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Result),
            23f);
    }

    private Coroutine _threeDRoutine;

    private void BuildThreeDPage()
    {
        // 빠른 상태 왕복으로 이전 조립 루틴이 살아 있으면 정리한다(진행바·페이드 이중 구동 방지).
        if (_threeDRoutine != null)
        {
            StopCoroutine(_threeDRoutine);
            RestoreFlowCanvasGroup();
        }
        _threeDRoutine = StartCoroutine(ThreeDGenerateRoutine());
    }

    // 3D "생성" 단계: 유저 앞에 프리미티브 로켓을 조립(팝인)하며 진행률을 보여주고,
    // 완성되면 완료 화면으로 전환한다. (mock — 설계값을 반영한 절차적 3D)
    // 조립되는 로켓이 주인공이므로, 조립 동안 패널은 반투명하게 비켜나
    // "앞쪽 공간을 바라보세요" 안내와 화면이 모순되지 않게 한다.
    private IEnumerator ThreeDGenerateRoutine()
    {
        RectTransform fill = BuildThreeDGeneratingContent();

        MvpRocketDesign design =
            _waterRocketGraph != null
                ? _waterRocketGraph.GetRocketDesign(
                    Mathf.Max(0, _history.Count - 1))
                : new MvpRocketDesign();

        SpawnRocketStage(design);

        CanvasGroup panelGroup = EnsureFlowCanvasGroup();

        float elapsed = 0f;
        float minTime = 2.2f;
        while (elapsed < minTime ||
               (_rocketStage != null && !_rocketStage.BuildComplete))
        {
            if (_state != MvpFlowState.ThreeD)
            {
                RestoreFlowCanvasGroup();
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            if (fill != null)
                fill.sizeDelta = new Vector2(
                    Mathf.Lerp(0f, 460f, Mathf.Clamp01(elapsed / minTime)),
                    fill.sizeDelta.y);

            if (panelGroup != null)
            {
                // 첫 0.9초는 안내 문구를 읽을 시간을 주고, 그 뒤 로켓에게 자리를 내준다.
                float target = elapsed < 0.9f ? 1f : 0.16f;
                panelGroup.alpha = Mathf.Lerp(
                    panelGroup.alpha, target,
                    3.2f * Time.unscaledDeltaTime);
                panelGroup.blocksRaycasts = panelGroup.alpha > 0.7f;
            }
            yield return null;
        }

        RestoreFlowCanvasGroup();

        if (_state != MvpFlowState.ThreeD)
            yield break;

        if (MvpAudioCue.Instance != null)
            MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Success);
        BuildThreeDResultContent(design);
    }

    private CanvasGroup EnsureFlowCanvasGroup()
    {
        if (_flowCanvas == null)
            return null;
        CanvasGroup group = _flowCanvas.GetComponent<CanvasGroup>();
        if (group == null)
            group = _flowCanvas.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private void RestoreFlowCanvasGroup()
    {
        CanvasGroup group =
            _flowCanvas != null
                ? _flowCanvas.GetComponent<CanvasGroup>()
                : null;
        if (group == null)
            return;
        group.alpha = 1f;
        group.blocksRaycasts = true;
    }

    private RectTransform BuildThreeDGeneratingContent()
    {
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Eyebrow",
            "3D 생성",
            new Vector2(0f, 300f),
            new Vector2(620f, 42f),
            20f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.InfoInk,
            1);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "3D 물로켓을 만드는 중…",
            new Vector2(0f, 235f),
            new Vector2(820f, 72f),
            40f,
            TextAlignmentOptions.Center,
            true);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Body",
            "여러분의 설계를 바탕으로 눈앞에서 부품을 조립하고 있어요.\n앞쪽 공간을 바라보세요.",
            new Vector2(0f, -235f),
            new Vector2(780f, 90f),
            23f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);

        MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "ProgressTrack",
            new Vector2(0f, -305f),
            new Vector2(480f, 22f),
            MvpStudentUiFactory.SurfaceBlue,
            false);

        RectTransform fill = MvpStudentUiFactory.CreateRect(
            _contentRoot,
            "ProgressFill",
            new Vector2(-240f, -305f),
            new Vector2(0f, 22f));
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(-240f, -305f);
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = MvpStudentUiFactory.Primary;
        fillImage.raycastTarget = false;
        return fill;
    }

    private void BuildThreeDResultContent(MvpRocketDesign design)
    {
        ClearContent();

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Eyebrow",
            "완성",
            new Vector2(0f, 305f),
            new Vector2(620f, 42f),
            20f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.SuccessInk,
            1);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "우리 팀 3D 물로켓 완성!",
            new Vector2(0f, 240f),
            new Vector2(860f, 72f),
            42f,
            TextAlignmentOptions.Center,
            true);

        int fins = design != null ? design.finCount : 3;
        string noseText =
            design != null && !design.pointedNose ? "둥근 노즈콘" : "뾰족한 노즈콘";
        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Summary",
            $"부품 {(design != null ? design.partCount : 0)}개 · " +
            $"아이디어 {(design != null ? design.requirementCount : 0)}개 · " +
            $"날개 {fins}장 · {noseText}",
            new Vector2(0f, 170f),
            new Vector2(820f, 46f),
            22f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.InfoInk,
            1);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Hint",
            "앞에 떠 있는 3D 모델이 천천히 돌아갑니다. 가까이서 살펴보세요.",
            new Vector2(0f, -235f),
            new Vector2(820f, 60f),
            22f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Back",
            "2D 결과 보기",
            new Vector2(-180f, -305f),
            new Vector2(280f, 62f),
            MvpStudentUiFactory.MutedInk,
            () => ShowState(MvpFlowState.Result),
            22f);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Finish",
            "수업 마치기",
            new Vector2(180f, -305f),
            new Vector2(280f, 66f),
            MvpStudentUiFactory.Primary,
            () => ShowState(MvpFlowState.Complete),
            24f);
    }

    private void SpawnRocketStage(MvpRocketDesign design)
    {
        ClearRocketStage();

        GameObject go = new GameObject("MvpRocket3DStage");
        _rocketStage = go.AddComponent<MvpRocket3DStage>();

        // 카메라 개인 좌표가 아니라 공용 보드 앞 테이블 공간에 둔다.
        // 모든 참가자가 같은 위치에서 보고, 보드나 키보드 뒤에 가리지 않는다.
        Transform board =
            _workspaceLayout != null
                ? _workspaceLayout.MainSketchPanel
                : null;
        if (board != null)
        {
            go.transform.position =
                board.position +
                board.right * 0.55f -
                board.forward * 0.38f +
                Vector3.up * 0.05f;
            go.transform.rotation =
                Quaternion.LookRotation(board.forward, Vector3.up);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    cam.transform.forward,
                    Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;

                go.transform.position =
                    cam.transform.position +
                    forward * 1.05f -
                    Vector3.up * 0.28f;
                go.transform.rotation =
                    Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        go.transform.localScale = Vector3.one * 1.25f;
        go.SetActive(true);
        _rocketStage.Build(design);
        RefreshThreeDViewButton();
    }

    private void ClearRocketStage()
    {
        if (_rocketStage != null)
        {
            Destroy(_rocketStage.gameObject);
            _rocketStage = null;
        }

        RefreshThreeDViewButton();
    }

    private void BuildCompletePage()
    {
        for (int i = 0; i < 7; i++)
        {
            Color color =
                i % 3 == 0
                    ? MvpStudentUiFactory.Primary
                    : i % 3 == 1
                        ? MvpStudentUiFactory.Cyan
                        : MvpStudentUiFactory.Amber;
            Image dot = MvpStudentUiFactory.CreatePanel(
                _contentRoot,
                "Confetti_" + i,
                new Vector2(-500f + i * 165f,
                    220f - (i % 2) * 55f),
                new Vector2(24f, 54f),
                color,
                false);
            dot.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, -25f + i * 8f);
        }

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Title",
            "물로켓 설계 완료!",
            new Vector2(0f, 145f),
            new Vector2(1000f, 90f),
            52f,
            TextAlignmentOptions.Center,
            true);

        MvpStudentUiFactory.CreateText(
            _contentRoot,
            "Body",
            Safe(_session.nickname, "우리 팀") +
            "의 아이디어가 부품과 속성으로 정리되었어요.\n" +
            "실제 제작에서는 보안경을 쓰고 선생님의 안전 지도를 따라요.",
            new Vector2(0f, 35f),
            new Vector2(1000f, 105f),
            25f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            3);

        CreateMetricCard(-220f, -95f,
            (_waterRocketGraph?.RequirementCount ?? 0) + "개",
            "모은 아이디어", MvpStudentUiFactory.Primary);
        CreateMetricCard(220f, -95f,
            _history.Count + "장",
            "만든 설계 그림", MvpStudentUiFactory.Cyan);

        MvpStudentUiFactory.CreateButton(
            _contentRoot,
            "Restart",
            "처음부터 다시하기",
            new Vector2(0f, -280f),
            new Vector2(470f, 72f),
            MvpStudentUiFactory.MintDeep,
            RestartFlow,
            25f);
    }

    private IEnumerator CreateRoomRoutine(
        string roomName,
        string topic,
        string goal,
        string nickname,
        string password,
        TMP_Text status,
        Button submit)
    {
        if (_requestBusy) yield break;

        if (string.IsNullOrWhiteSpace(roomName) ||
            string.IsNullOrWhiteSpace(topic) ||
            string.IsNullOrWhiteSpace(goal) ||
            string.IsNullOrWhiteSpace(nickname) ||
            string.IsNullOrWhiteSpace(password))
        {
            status.text = "모든 칸을 채워 주세요.";
            status.color = MvpStudentUiFactory.DangerInk;
            yield break;
        }

        _requestBusy = true;
        submit.interactable = false;
        status.text = "수업방을 준비하고 있어요...";
        status.color = MvpStudentUiFactory.Primary;

        _session.roomName = roomName.Trim();
        _session.topic = topic.Trim();
        _session.goal = goal.Trim();
        _session.nickname = nickname.Trim();
        _session.password = password.Trim();
        _session.online = false;

        bool created = false;
        if (_tryBackendFirst)
        {
            string roomTopic =
                "[" + _session.roomName + "] " +
                _session.topic + " | 수업 목표: " + _session.goal;
            MvpCreateRoomRequest payload =
                new MvpCreateRoomRequest
                {
                    room_topic = roomTopic,
                    password = _session.password,
                    nickname = _session.nickname
                };

            using (UnityWebRequest request = CreateJsonRequest(
                       ServerAddress.Http(_backendHost) +
                       "/api/rooms/generate",
                       JsonUtility.ToJson(payload)))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result ==
                    UnityWebRequest.Result.Success)
                {
                    MvpCreateRoomEnvelope envelope = null;
                    try
                    {
                        envelope =
                            JsonUtility.FromJson<MvpCreateRoomEnvelope>(
                                request.downloadHandler.text);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "[MVP Flow] 방 생성 응답 파싱 실패: " +
                            exception.Message);
                    }

                    if (envelope != null &&
                        envelope.isSuccess &&
                        envelope.result != null &&
                        !string.IsNullOrEmpty(
                            envelope.result.room_id))
                    {
                        _session.roomId =
                            envelope.result.room_id;
                        created = true;
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "[MVP Flow] 방 생성 실패 → 체험 모드: " +
                        request.error +
                        " (code=" + request.responseCode + ")");
                }
            }
        }

        if (created)
        {
            bool entered = false;
            string serverUserId = "";
            yield return TryEnterRoom(
                _session.roomId,
                _session.nickname,
                _session.password,
                (ok, userId) =>
                {
                    entered = ok;
                    serverUserId = userId;
                });

            _session.online = entered;
            _session.userId = entered
                ? serverUserId
                : Guid.NewGuid().ToString();
        }
        else
        {
            _session.roomId = Guid.NewGuid().ToString();
            _session.userId = Guid.NewGuid().ToString();
            _session.online = false;
        }

        if (_session.online)
            ConfigureGraphSocket();

        _requestBusy = false;
        submit.interactable = true;
        ShowState(MvpFlowState.Briefing);
    }

    // ─────────────────────────────────────────────
    // 초대 코드 단축 (2026-08-01)
    // ─────────────────────────────────────────────
    //
    // 서버 room_id 는 36자 UUID 라 구두/채팅 공유가 어렵다. 서버 변경 없이 클라에서만
    // 6자리 코드를 만들고, 참여 시 GET /api/rooms/list 로 같은 코드를 갖는 방을 찾아 복원한다.
    //
    // [2026-08-01] 알파벳 전용(A~Z)으로 변경.
    //   공간 키보드(MvpWorldKeyboard.BuildEnglishKeys)에 숫자 행이 없어 16진수 코드는
    //   입력 자체가 불가능했다. UUID 앞 7자리(28비트)를 26진수 6자리로 인코딩한다.
    //   28비트 = 268,435,456 < 26^6 = 308,915,776 이라 손실 없이 담긴다.
    //   조합 수도 이전(16^6 ≈ 1,670만)보다 많아 충돌은 더 줄어든다.
    //   - 코드가 여러 방과 겹치면 모호하므로 입장을 거부한다(잘못된 방 입장 방지).
    //   - UUID 를 그대로 붙여넣어도 동작한다(하위호환).
    //   - 대소문자 무관(입력을 대문자로 정규화).

    private const int ShortRoomCodeLength = 6;

    private static string ShortRoomCode(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return string.Empty;

        string compact = roomId.Replace("-", string.Empty);
        if (compact.Length < 7)
            return compact.ToUpperInvariant();

        uint value;
        try
        {
            value = Convert.ToUInt32(compact.Substring(0, 7), 16);
        }
        catch (Exception)
        {
            return compact.Substring(0, ShortRoomCodeLength).ToUpperInvariant();
        }

        char[] buffer = new char[ShortRoomCodeLength];
        for (int i = ShortRoomCodeLength - 1; i >= 0; i--)
        {
            buffer[i] = (char)('A' + (int)(value % 26));
            value /= 26;
        }
        return new string(buffer);
    }

    // 6자리 코드 → 전체 room_id. 못 찾거나 모호하면 null 을 넘긴다.
    private IEnumerator ResolveShortRoomCode(string code, Action<string> onDone)
    {
        // 코드는 A~Z 만 쓴다. 공백·하이픈·오타 문자를 걷어내고 대문자로 맞춘다.
        var sb = new StringBuilder();
        foreach (char c in (code ?? string.Empty).ToUpperInvariant())
            if (c >= 'A' && c <= 'Z') sb.Append(c);
        string normalized = sb.ToString();

        if (string.IsNullOrEmpty(normalized))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string url = ServerAddress.Http(_backendHost) + "/api/rooms/list";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 5;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[MVP Flow] 방 목록 조회 실패(초대 코드 해석 불가): " +
                    request.error);
                onDone?.Invoke(null);
                yield break;
            }

            // 공통 봉투 { isSuccess, code, message, result: { rooms: [ { room_id, ... } ] } }
            // JsonUtility 는 중첩 제네릭에 약해, room_id 값만 정규식으로 뽑아 접두사 비교한다.
            string body = request.downloadHandler.text ?? string.Empty;
            var matches = System.Text.RegularExpressions.Regex.Matches(
                body,
                "\"room_id\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"");

            string found = null;
            int hits = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string candidate = m.Groups[1].Value;
                if (ShortRoomCode(candidate) != normalized) continue;

                hits++;
                found = candidate;
            }

            if (hits > 1)
            {
                Debug.LogWarning(
                    "[MVP Flow] 초대 코드가 여러 방과 일치합니다(모호): " + normalized);
                onDone?.Invoke(null);
                yield break;
            }

            Debug.Log(hits == 1
                ? "[MVP Flow] 초대 코드 해석: " + normalized + " → " + found
                : "[MVP Flow] 초대 코드에 해당하는 방 없음: " + normalized);
            onDone?.Invoke(found);
        }
    }

    private IEnumerator JoinRoomRoutine(
        string roomId,
        string nickname,
        string password,
        TMP_Text status,
        Button submit)
    {
        if (_requestBusy) yield break;

        string typed = roomId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(typed) ||
            string.IsNullOrWhiteSpace(nickname) ||
            string.IsNullOrWhiteSpace(password))
        {
            status.text =
                "올바른 방 코드와 이름, 비밀번호를 입력해 주세요.";
            status.color = MvpStudentUiFactory.DangerInk;
            yield break;
        }

        _requestBusy = true;
        submit.interactable = false;
        status.text = "방에 입장하고 있어요...";
        status.color = MvpStudentUiFactory.Primary;

        // [2026-08-01] 초대 코드를 6자리로 단축(ShortRoomCode). UUID 전체를 붙여넣던 방식은
        //   공유가 불편해 앞 6자리만 쓰고, 여기서 GET /api/rooms/list 로 원래 room_id 를 되찾는다.
        //   UUID 를 그대로 붙여넣어도 동작하도록 둘 다 받는다(하위호환).
        string resolvedRoomId = null;
        if (Guid.TryParse(typed, out Guid parsedFull))
        {
            resolvedRoomId = parsedFull.ToString();
        }
        else
        {
            yield return ResolveShortRoomCode(typed, id => resolvedRoomId = id);
        }

        if (string.IsNullOrEmpty(resolvedRoomId))
        {
            status.text =
                "그 코드의 방을 찾지 못했어요. 코드를 다시 확인해 주세요.";
            status.color = MvpStudentUiFactory.DangerInk;
            _requestBusy = false;
            submit.interactable = true;
            yield break;
        }

        bool entered = false;
        string userId = "";
        yield return TryEnterRoom(
            resolvedRoomId,
            nickname.Trim(),
            password.Trim(),
            (ok, id) =>
            {
                entered = ok;
                userId = id;
            });

        if (!entered)
        {
            status.text =
                "방을 찾지 못했어요. 코드와 비밀번호를 확인해 주세요.";
            status.color = MvpStudentUiFactory.DangerInk;
            _requestBusy = false;
            submit.interactable = true;
            yield break;
        }

        _session.roomId = resolvedRoomId;
        _session.roomName = "함께하는 물로켓 수업";
        _session.topic = "새로운 설계 아이디어";
        _session.goal = "우리 팀만의 해결책 만들기";
        _session.nickname = nickname.Trim();
        _session.password = password.Trim();
        _session.userId = userId;
        _session.online = true;

        ConfigureGraphSocket();
        _requestBusy = false;
        submit.interactable = true;
        ShowState(MvpFlowState.Briefing);
    }

    private IEnumerator TryEnterRoom(
        string roomId,
        string nickname,
        string password,
        Action<bool, string> onDone)
    {
        MvpEnterRoomRequest payload =
            new MvpEnterRoomRequest
            {
                room_id = roomId,
                nickname = nickname,
                password = password
            };

        using (UnityWebRequest request = CreateJsonRequest(
                   ServerAddress.Http(_backendHost) + "/api/rooms/enter",
                   JsonUtility.ToJson(payload)))
        {
            request.timeout = 5;
            yield return request.SendWebRequest();

            if (request.result !=
                UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[MVP Flow] 방 입장 실패: " +
                    request.error +
                    " (code=" + request.responseCode + ")");
                onDone?.Invoke(false, "");
                yield break;
            }

            MvpEnterRoomEnvelope envelope = null;
            try
            {
                envelope =
                    JsonUtility.FromJson<MvpEnterRoomEnvelope>(
                        request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[MVP Flow] 방 입장 응답 파싱 실패: " +
                    exception.Message);
            }

            bool ok =
                envelope != null &&
                envelope.isSuccess &&
                envelope.result != null &&
                !string.IsNullOrEmpty(
                    envelope.result.user_id);
            onDone?.Invoke(
                ok,
                ok ? envelope.result.user_id : "");
        }
    }

    private void StartQuickDemo()
    {
        _session.roomId = Guid.NewGuid().ToString();
        _session.userId = Guid.NewGuid().ToString();
        _session.roomName = "우리 팀 설계실";
        _session.topic = "새로운 설계 아이디어";
        _session.goal = "우리 팀만의 해결책 만들기";
        _session.nickname = "학생";
        _session.password = "1234";
        _session.online = false;
        ShowState(MvpFlowState.Briefing);
    }

    // 로비에서 넘어온 경우 브리핑을 건너뛰고 바로 설계로 들어간다.
    // 다만 Awake 에서 곧장 부르면 다른 컴포넌트가 아직 준비되기 전이라
    // 보드가 빈 판으로 뜬다. 프레임 끝까지 기다린 뒤 연다.
    private IEnumerator StartDesignWhenReady()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        StartDesign();
    }

    // 회의실에 들어오면 설계 보드보다 먼저 '오늘 무엇을 설계하는지'를 보여 준다.
    // 기다리는 이유는 StartDesignWhenReady 와 같다 — 보드 캔버스가 아직 없으면
    // 패널을 붙일 곳을 못 찾는다.
    private IEnumerator ShowGoalPanelWhenReady()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        if (!ShowGoalPanel())
            StartDesign();   // 패널을 못 찾으면 예전처럼 곧장 설계로
    }

    // 회의 목표 패널(MainSketchPanel 아래 'GoalPannel')을 켠다.
    // 켰으면 true — 이때 설계는 '회의 시작하기'를 누를 때까지 시작하지 않는다.
    private bool ShowGoalPanel()
    {
        _goalPanel = FindBoardPanel("GoalPannel");
        if (_goalPanel == null)
        {
            // 헤드셋에서는 화면에 아무것도 안 나오면 원인을 구분할 수 없다.
            // (adb logcat -s Unity:V | grep "MVP Flow")
            Debug.LogWarning(
                "[MVP Flow] 회의 목표 패널을 찾지 못했습니다 — " +
                "MainSketchPanel 아래에 'GoalPannel' 이 있는지 확인하세요.");
            return false;
        }

        // 보드 캔버스를 먼저 켠다.
        //
        // Awake 의 PrepareExistingScene() 이 SetMainWorkspaceVisible(false) 로 보드를 꺼 두고,
        // 원래는 StartDesign → ShowState(Design) 이 다시 켰다. 목표 패널은 그 보드(MainSketchPanel)
        // **안에** 있으므로, 켜지 않으면 패널만 SetActive(true) 해 봐야 꺼진 부모 안이라 안 보인다.
        // 화면에 아무것도 안 나오던 원인이 이것이다.
        SetMainWorkspaceVisible(true);

        // 여기서 보드를 재배치하지 않는다.
        //   RecenterWorkspaceToActiveView() 를 불러 봤더니 보드가 예전보다 멀리 놓였다.
        //   씬 로드 직후라 헤드셋 트래킹이 아직 안정되지 않은 카메라 포즈를 기준으로 잡기 때문이다.
        //   배치는 MvpWorkspaceLayout 의 포즈 추종 창과 StartDesign 에 그대로 맡긴다.
        _goalPanel.SetActive(true);

        // 목표를 읽는 동안에는 보드·노드·엣지를 감춘다.
        //
        // HideWorkspaceForReport() 를 쓰면 안 된다 — 그건 캔버스의 자식,
        // 즉 MainSketchPanel 을 통째로 끄는데 목표 패널이 그 **안에** 들어 있어서
        // 같이 꺼진다. 목표 패널만 남기고 끄는 쪽을 쓴다.
        HideWorkspaceExcept(_goalPanel);

        // 흐름 캔버스(Welcome/브리핑 등을 그리는 판)는 만들어질 때 켜져 있다.
        // 로비에서 넘어온 경우 아무 페이지도 그리지 않아 내용은 비었지만, 어두운 배경판이
        // 카메라 앞 1.22m 에 그대로 떠 목표 패널을 가린다. 설계로 들어갈 때
        // ShowState(Design) 이 어차피 끄는 판이므로 여기서 미리 끈다.
        if (_flowCanvas != null)
            _flowCanvas.gameObject.SetActive(false);

        MvpPanelButtonBinder.Wire(_goalPanel, "Button_Start", BeginMeetingFromGoalPanel);

        // 화면 없이 '보이는지'를 가려내야 하므로 상태를 한 줄 남긴다.
        // activeInHierarchy 가 false 면 부모 어딘가가 꺼져 있다는 뜻이다.
        Debug.Log(
            "[MVP Flow] 회의 목표 패널 표시 — activeInHierarchy=" +
            _goalPanel.activeInHierarchy +
            " 보드캔버스=" + (_mainSketchCanvas != null &&
                              _mainSketchCanvas.gameObject.activeInHierarchy) +
            " world=" + _goalPanel.transform.position.ToString("F2") +
            " lossyScale=" + _goalPanel.transform.lossyScale.ToString("F4"));
        return true;
    }

    // 보드를 감추되 keep(과 그 조상)은 남긴다.
    //
    // HideWorkspaceForReport() 와 목적은 같지만, 남겨야 할 패널이 보드 **안에** 있을 때 쓴다.
    // 조상은 끄지 않고 한 단계 더 들어가 형제만 끄는 식으로 내려간다.
    // 꺼진 것은 _hiddenForReport 에 쌓이므로 RestoreWorkspaceAfterReport() 가 그대로 되살린다.
    private void HideWorkspaceExcept(GameObject keep)
    {
        _hiddenForReport.Clear();

        if (_mainSketchCanvas != null && keep != null)
            HideSiblingsAlongPath(_mainSketchCanvas.transform, keep.transform);

        foreach (NodeView node in
                 FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (node == null || !node.gameObject.activeSelf) continue;
            node.gameObject.SetActive(false);
            _hiddenForReport.Add(node.gameObject);
        }

        foreach (EdgeView edge in
                 FindObjectsByType<EdgeView>(FindObjectsSortMode.None))
        {
            if (edge == null || !edge.gameObject.activeSelf) continue;
            edge.gameObject.SetActive(false);
            _hiddenForReport.Add(edge.gameObject);
        }
    }

    private void HideSiblingsAlongPath(Transform parent, Transform keep)
    {
        foreach (Transform child in parent)
        {
            if (child == null) continue;
            if (child == keep) continue;                 // 남길 패널 자신

            if (keep.IsChildOf(child))
            {
                // 남길 패널의 조상이다. 끄지 말고 한 단계 더 들어간다.
                HideSiblingsAlongPath(child, keep);
                continue;
            }

            if (!child.gameObject.activeSelf) continue;
            child.gameObject.SetActive(false);
            _hiddenForReport.Add(child.gameObject);
        }
    }

    // 목표 패널의 '회의 시작하기'.
    // 씬에 놓아 둔 오브젝트라 파기하지 않고 꺼 두기만 한다.
    private void BeginMeetingFromGoalPanel()
    {
        if (_goalPanel != null)
        {
            _goalPanel.SetActive(false);
            _goalPanel = null;
        }

        RestoreWorkspaceAfterReport();
        StartDesign();
    }

    private void StartDesign()
    {
        // '어떤 부품으로 시작할까요?' 추천 패널은 자동으로 띄우지 않는다.
        // 설계에 들어서자마자 보드를 가려 아무것도 못 하게 만든다.
        // 부품은 보드의 '+'(AddPartPort)로 언제든 직접 추가할 수 있다.
        _recommendationsDismissed = true;
        _resolvedRecommendations.Clear();

        // '내 자리 설정' 도크는 띄우지 않는다.
        // 파빌리온으로 바뀌면서 책상·좌석이 사라져 '자리 복귀'가 의미를 잃었고,
        // 작업판 크기와 방 나가기는 손목 패널에 있다.
        // 이미 만들어져 있으면(이전 단계에서 켜진 경우) 접어 둔다.
        MvpTableSettingsDock dock =
            FindFirstObjectByType<MvpTableSettingsDock>();
        if (dock != null)
            dock.HideDock();

        if (_centerSketchImage != null)
        {
            _centerSketchImage.texture = null;
            _centerSketchImage.color = Color.white;
        }

        if (_waterRocketGraph != null)
            _waterRocketGraph.InitializeWaterRocketGraph();

        if (_workspaceLayout != null)
        {
            _workspaceLayout.RecenterWorkspaceToActiveView();
            _workspaceLayout.ArrangeWorkspace();
        }

        HideLegacyHistoryDots();
        ShowState(MvpFlowState.Design);
    }


    private void BeginGenerate()
    {
        ShowState(MvpFlowState.Generating);
        StartCoroutine(GenerateSketchRoutine());
    }

    private IEnumerator GenerateSketchRoutine()
    {
        Texture before =
            _centerSketchImage != null
                ? _centerSketchImage.texture
                : null;
        bool fromServer = false;

        bool useServer =
            _session.online &&
            _graphSyncClient != null &&
            _graphSyncClient.IsConnected &&
            _generate2DController != null;

        if (useServer)
        {
            _generate2DController.RequestGenerateGraphAll();

            // 서버 생성은 OpenAI(프롬프트) → Gemini(이미지) 2단이라 수십 초가 걸린다.
            // 고정 9초(_imageWaitSeconds)로 기다리면 정상 동작 중에도 거의 항상 시간 초과로
            // 떨어졌다. 컨트롤러가 작업을 끝낼 때까지(성공·실패·자체 타임아웃) 기다린다.
            float cap = Mathf.Max(_imageWaitSeconds, ServerImageWaitCapSeconds);
            float elapsed = 0f;
            while (elapsed < cap)
            {
                Texture current =
                    _centerSketchImage != null
                        ? _centerSketchImage.texture
                        : null;
                if (current != null && current != before)
                {
                    fromServer = true;
                    break;
                }
                // 컨트롤러가 손을 뗐는데 이미지가 안 바뀌었으면 실패로 끝난 것이다.
                // (요청 자체가 거부된 경우엔 애초에 IsGenerating 이 서지 않아 즉시 빠진다.)
                if (!_generate2DController.IsGenerating)
                    break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(1.15f);
        }

        // 노드 그래프에서 이번 설계를 읽어 2D 스케치·3D에 반영한다.
        MvpRocketDesign design =
            _waterRocketGraph != null
                ? _waterRocketGraph.GetRocketDesign(_history.Count)
                : new MvpRocketDesign { accentIndex = _history.Count };

        Texture2D result;
        if (fromServer &&
            _centerSketchImage != null &&
            _centerSketchImage.texture != null)
        {
            result = CloneTexture(_centerSketchImage.texture);
        }
        else if (useServer)
        {
            // 온라인인데 서버 이미지가 안 왔다. mock 으로 덮으면 "생성된 것처럼" 보여
            // 실패를 알아챌 수 없으므로, 화면을 그대로 두고 설계 화면으로 돌려보낸다.
            SetWorkspaceMessage(
                "그림을 만들지 못했어요. 잠시 후 다시 시도해 주세요.",
                MvpStudentUiFactory.Coral);
            ShowState(MvpFlowState.Design);
            yield break;
        }
        else
        {
            result = MvpFallbackSketchGenerator
                .CreateWaterRocketSketch(design);
            if (_centerSketchImage != null)
            {
                _centerSketchImage.texture = result;
                _centerSketchImage.enabled = true;
                _centerSketchImage.color = Color.white;
            }
        }

        Texture2D historyCopy =
            result != null ? CloneTexture(result) : null;
        if (historyCopy == null)
            historyCopy =
                MvpFallbackSketchGenerator
                    .CreateWaterRocketSketch(design);

        _history.Add(new MvpSketchHistoryItem
        {
            texture = historyCopy,
            title = "설계 그림 " + (_history.Count + 1),
            summary = _waterRocketGraph != null
                ? _waterRocketGraph.GetDesignSummary()
                : "",
            createdAt = DateTime.Now,
            fromServer = fromServer
        });

        if (MvpAudioCue.Instance != null)
            MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Success);
        ShowState(MvpFlowState.Result);
    }

    private void RestartFlow()
    {
        // 듣는 중이던 음성 입력이 있으면 정지한다(마이크 점유가 다음 세션까지 남는 것 방지).
        if (_voiceController != null && _voiceController.IsListening)
            _voiceController.ToggleListening();

        _spatialGesture?.SetCreationEnabled(false);
        _workspaceLayout?.SetSpatialPlacementMode(false);
        _waterRocketGraph?.ClearGraph();
        ClearRocketStage();
        if (_serverModelStage != null)
        {
            // 스테이지를 지우면 그 아래 붙은 서버 3D 모델도 함께 사라진다.
            Destroy(_serverModelStage.gameObject);
            _serverModelStage = null;
        }
        if (_workspaceMockTexture != null)
        {
            Destroy(_workspaceMockTexture);
            _workspaceMockTexture = null;
        }
        foreach (MvpSketchHistoryItem item in _history)
        {
            if (item?.texture != null)
                Destroy(item.texture);
        }
        _history.Clear();

        if (_centerSketchImage != null)
            _centerSketchImage.texture = null;

        if (_graphSyncClient != null)
            _graphSyncClient.enabled = false;

        _recommendationsDismissed = true;   // 추천 패널은 자동으로 띄우지 않는다
        _resolvedRecommendations.Clear();
        _session.Reset();
        FindFirstObjectByType<MvpTableSettingsDock>()?.HideDock();
        HideOriginalGenerateControls(false);
        HideLegacyHistoryDots();
        ShowState(MvpFlowState.Welcome);
    }

    private void RefreshWorkspaceDock()
    {
        if (_waterRocketGraph == null)
            return;

        // 생성 버튼 활성 조건은 안내 칩(MvpStudentWorkspaceGuide.RefreshGuide)과
        // 동일하게 유지한다 — 두 곳이 다른 규칙로 쓰면 0.25s 마다 깜빡인다.
        if (_reviewButton != null)
            _reviewButton.interactable =
                _waterRocketGraph.PartCount > 0 &&
                _waterRocketGraph.RequirementCount > 0 &&
                _waterRocketGraph.AppliedConnectionCount > 0;
    }

    // 손목 메뉴처럼 라벨이 없는 UI 가 안내 문구를 대신 띄울 때도 쓴다.
    public void SetWorkspaceMessage(string text, Color color)
    {
        if (_workspaceStatus != null)
        {
            _workspaceStatus.text = text;
            _workspaceStatus.color = color;
            return;
        }

        // 상태 칩이 없는 현 레이아웃에서는 학생 안내 칩으로 보여 준다.
        // (이 폴백이 없으면 음성/네트워크 안내가 전부 조용히 사라진다.)
        MvpStudentWorkspaceGuide guide =
            FindFirstObjectByType<MvpStudentWorkspaceGuide>();
        if (guide != null)
            guide.ShowLinkFeedback(text, color);
    }

    private TMP_InputField CreateLabeledInput(
            Transform parent,
            string label,
            string placeholder,
            Vector2 position,
            Vector2 size,
            string defaultValue)
    {
        MvpStudentUiFactory.CreateText(
            parent,
            "Label_" + label,
            label,
            position + new Vector2(0f, size.y * 0.5f + 31f),
            new Vector2(size.x, 42f),
            22f,
            TextAlignmentOptions.MidlineLeft,
            true,
            MvpStudentUiFactory.Ink,
            1);

        TMP_InputField input =
            MvpStudentUiFactory.CreateInput(
                parent,
                "Input_" + label,
                placeholder,
                position,
                size,
                24f);
        input.text = defaultValue ?? "";
        return input;
    }

    private void CreateStepCard(
        float x,
        float y,
        string number,
        string title,
        string body)
    {
        Image card = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "Step_" + number,
            new Vector2(x, y),
            new Vector2(260f, 190f),
            Color.white,
            true);

        Image badge = MvpStudentUiFactory.CreatePanel(
            card.transform,
            "Badge",
            new Vector2(0f, 58f),
            new Vector2(52f, 52f),
            MvpStudentUiFactory.Primary,
            false);
        MvpStudentUiFactory.CreateText(
            badge.transform,
            "Number",
            number,
            Vector2.zero,
            new Vector2(46f, 46f),
            23f,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);

        MvpStudentUiFactory.CreateText(
            card.transform,
            "Title",
            title,
            new Vector2(0f, 8f),
            new Vector2(220f, 42f),
            23f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Ink,
            1);

        MvpStudentUiFactory.CreateText(
            card.transform,
            "Body",
            body,
            new Vector2(0f, -50f),
            new Vector2(220f, 62f),
            17f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            2);
    }

    private void CreateMetricCard(
        float x,
        float y,
        string value,
        string label,
        Color accent)
    {
        Image card = MvpStudentUiFactory.CreatePanel(
            _contentRoot,
            "Metric_" + label,
            new Vector2(x, y),
            new Vector2(240f, 118f),
            Color.white,
            true);

        MvpStudentUiFactory.CreateText(
            card.transform,
            "Value",
            value,
            new Vector2(0f, 22f),
            new Vector2(210f, 48f),
            29f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.ReadableAccentOnLight(accent),
            1);
        MvpStudentUiFactory.CreateText(
            card.transform,
            "Label",
            label,
            new Vector2(0f, -30f),
            new Vector2(210f, 36f),
            17f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.MutedInk,
            1);
    }

    // 특정 결과물을 그리지 않고, 말한 아이디어가 노드와 스케치로 바뀌는
    // NodeXR의 공통 흐름을 보여주는 랜딩 전용 비주얼이다.
    // 특정 결과물을 그리지 않고, 말한 아이디어가 노드와 스케치로 바뀌는
    // NodeXR의 공통 흐름을 보여주는 랜딩 전용 비주얼이다.
    // 랜딩 전용 마크는 Resources에 두어 MVP 씬만으로도 안전하게 불러온다.
    private static Texture2D LoadLandingBrandMark()
    {
        return Resources.Load<Texture2D>("Brand/node_xr_mark");
    }

    // 복잡한 그래프 예시 대신 한 개의 브랜드 마크와 충분한 여백으로
    // 공동 XR 공간의 첫인상을 전달한다.
    private void CreateDesignFlowIllustration(
        Transform parent,
        Vector2 position,
        float scale)
    {
        RectTransform root = MvpStudentUiFactory.CreateRect(
            parent,
            "DesignFlowIllustration",
            position,
            new Vector2(490f, 510f) * scale);

        Image ambient = MvpStudentUiFactory.CreatePanel(
            root,
            "AmbientHalo",
            new Vector2(-8f, 26f) * scale,
            new Vector2(354f, 354f) * scale,
            new Color(0.12f, 0.34f, 0.62f, 0.18f),
            false);
        ambient.raycastTarget = false;

        Image orbit = MvpStudentUiFactory.CreatePanel(
            root,
            "OrbitFrame",
            new Vector2(0f, 24f) * scale,
            new Vector2(306f, 306f) * scale,
            new Color(0.035f, 0.10f, 0.23f, 0.88f),
            true);
        orbit.raycastTarget = false;
        Outline orbitOutline = orbit.gameObject.AddComponent<Outline>();
        orbitOutline.effectColor = new Color(
            MvpStudentUiFactory.HoloCyan.r,
            MvpStudentUiFactory.HoloCyan.g,
            MvpStudentUiFactory.HoloCyan.b,
            0.56f);
        orbitOutline.effectDistance = new Vector2(2f, -2f) * scale;

        Image markStage = MvpStudentUiFactory.CreatePanel(
            root,
            "BrandMarkStage",
            new Vector2(0f, 24f) * scale,
            new Vector2(256f, 256f) * scale,
            new Color(0.025f, 0.065f, 0.16f, 0.96f),
            false);
        markStage.raycastTarget = false;

        Texture2D markTexture = LoadLandingBrandMark();
        if (markTexture != null)
        {
            RawImage mark = MvpStudentUiFactory.CreateRawImage(
                root,
                "NodeXRBrandMark",
                new Vector2(0f, 24f) * scale,
                new Vector2(246f, 246f) * scale);
            mark.texture = markTexture;
            mark.raycastTarget = false;
        }
        else
        {
            MvpStudentUiFactory.CreateText(
                root,
                "NodeXRFallbackMark",
                "N",
                new Vector2(0f, 24f) * scale,
                new Vector2(220f, 220f) * scale,
                142f * scale,
                TextAlignmentOptions.Center,
                true,
                MvpStudentUiFactory.HoloCyan,
                1);
        }

        Image topRule = MvpStudentUiFactory.CreatePanel(
            root,
            "BrandTopRule",
            new Vector2(0f, 194f) * scale,
            new Vector2(182f, 3f) * scale,
            new Color(0.40f, 0.86f, 1f, 0.75f),
            false);
        topRule.raycastTarget = false;

        MvpStudentUiFactory.CreateText(
            root,
            "MarkEyebrow",
            "NODEXR",
            new Vector2(0f, -157f) * scale,
            new Vector2(310f, 38f) * scale,
            20f * scale,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.HoloCyan,
            1);

        MvpStudentUiFactory.CreateText(
            root,
            "MarkCaption",
            "공동 설계 스튜디오",
            new Vector2(0f, -190f) * scale,
            new Vector2(360f, 38f) * scale,
            21f * scale,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);

        Image beaconA = MvpStudentUiFactory.CreatePanel(
            root,
            "BeaconA",
            new Vector2(-166f, 150f) * scale,
            new Vector2(14f, 14f) * scale,
            MvpStudentUiFactory.Mint,
            false);
        beaconA.raycastTarget = false;

        Image beaconB = MvpStudentUiFactory.CreatePanel(
            root,
            "BeaconB",
            new Vector2(176f, -88f) * scale,
            new Vector2(10f, 10f) * scale,
            MvpStudentUiFactory.Primary,
            false);
        beaconB.raycastTarget = false;
    }

    private static void CreateLandingNode(
        Transform parent,
        string name,
        string label,
        Vector2 position,
        Color accent,
        float scale)
    {
        Image node = MvpStudentUiFactory.CreatePanel(
            parent,
            name,
            position * scale,
            new Vector2(92f, 54f) * scale,
            new Color(0.11f, 0.20f, 0.38f, 1f),
            true);
        Outline outline = node.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(2f, -2f);

        Image marker = MvpStudentUiFactory.CreatePanel(
            node.transform,
            "Marker",
            new Vector2(-29f, 0f) * scale,
            new Vector2(12f, 12f) * scale,
            accent,
            false);
        marker.raycastTarget = false;

        MvpStudentUiFactory.CreateText(
            node.transform,
            "Label",
            label,
            new Vector2(10f, 0f) * scale,
            new Vector2(60f, 42f) * scale,
            15f * scale,
            TextAlignmentOptions.Center,
            true,
            Color.white,
            1);
    }

    private void CreateRocketIllustration(
        Transform parent,
        Vector2 position,
        float scale)
    {
        RectTransform root = MvpStudentUiFactory.CreateRect(
            parent,
            "RocketIllustration",
            position,
            new Vector2(420f, 560f) * scale);

        Image halo = MvpStudentUiFactory.CreatePanel(
            root,
            "Halo",
            new Vector2(0f, 20f) * scale,
            new Vector2(330f, 330f) * scale,
            MvpStudentUiFactory.SurfaceBlue,
            false);
        halo.raycastTarget = false;

        Image body = MvpStudentUiFactory.CreatePanel(
            root,
            "Body",
            new Vector2(0f, 5f) * scale,
            new Vector2(118f, 260f) * scale,
            Color.white,
            true);
        Outline outline = body.gameObject.AddComponent<Outline>();
        outline.effectColor = MvpStudentUiFactory.PrimaryDark;
        outline.effectDistance =
            new Vector2(5f, -5f) * scale;

        Image water = MvpStudentUiFactory.CreatePanel(
            body.transform,
            "Water",
            new Vector2(0f, -65f) * scale,
            new Vector2(94f, 88f) * scale,
            MvpStudentUiFactory.Cyan,
            false);
        water.raycastTarget = false;

        Image stripe = MvpStudentUiFactory.CreatePanel(
            body.transform,
            "Stripe",
            new Vector2(0f, 30f) * scale,
            new Vector2(100f, 30f) * scale,
            MvpStudentUiFactory.Primary,
            false);
        stripe.raycastTarget = false;

        Image nose = MvpStudentUiFactory.CreatePanel(
            root,
            "Nose",
            new Vector2(0f, 162f) * scale,
            new Vector2(92f, 92f) * scale,
            MvpStudentUiFactory.Coral,
            false);
        nose.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, 45f);

        Image leftFin = MvpStudentUiFactory.CreatePanel(
            root,
            "LeftFin",
            new Vector2(-78f, -82f) * scale,
            new Vector2(86f, 36f) * scale,
            MvpStudentUiFactory.Amber,
            false);
        leftFin.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, -42f);

        Image rightFin = MvpStudentUiFactory.CreatePanel(
            root,
            "RightFin",
            new Vector2(78f, -82f) * scale,
            new Vector2(86f, 36f) * scale,
            MvpStudentUiFactory.Amber,
            false);
        rightFin.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, 42f);

        MvpStudentUiFactory.CreateText(
            root,
            "Caption",
            "WATER\nROCKET",
            new Vector2(0f, -210f) * scale,
            new Vector2(300f, 80f) * scale,
            24f * scale,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.Primary,
            2);
    }

    private void SetMainWorkspaceVisible(bool visible)
    {
        if (_mainSketchCanvas != null)
            _mainSketchCanvas.gameObject.SetActive(visible);
        if (_graphRoot != null)
            _graphRoot.SetActive(visible);
        if (_toolCanvas != null)
            _toolCanvas.gameObject.SetActive(
                visible &&
                _state == MvpFlowState.Design &&
                !_recommendationsDismissed);
    }

    private void HideOriginalGenerateControls(bool hide)
    {
        if (_mainSketchCanvas == null) return;
        string[] names =
        {
            "Generate2DControls",
            "GenerateSectionTitle",
            "GenerateSectionHint",
            "GenerateFooterHint",
            "GenerateStatusSurface",
            "GenerateActionCard"
        };

        foreach (string name in names)
        {
            Transform child =
                _mainSketchCanvas.transform.Find(name);
            if (child != null)
                child.gameObject.SetActive(false);
        }
    }

    private RawImage FindCenterSketchImage()
    {
        if (_mainSketchCanvas == null) return null;

        RawImage[] images =
            _mainSketchCanvas.GetComponentsInChildren<RawImage>(true);
        foreach (RawImage image in images)
            if (image != null &&
                image.gameObject.name == "SketchImage")
                return image;

        return images.Length > 0 ? images[0] : null;
    }

    private void ConfigureGraphSocket()
    {
        if (_graphSyncClient == null) return;

        SetPrivateField(
            _graphSyncClient, "_host", _backendHost);
        SetPrivateField(
            _graphSyncClient, "_roomId", _session.roomId);
        SetPrivateField(
            _graphSyncClient, "_userId", _session.userId);
        SetPrivateField(
            _graphSyncClient, "_autoConnect", false);
        SetPrivateField(
            _graphSyncClient, "_sendToServer", true);

        _graphSyncClient.enabled = true;
        ConnectGraphSocket();

        // 같은 room_id로 Fusion Shared 멀티플레이 세션 시작(아바타 + 그래프 협업).
        if (_networkSession != null && !string.IsNullOrEmpty(_session.roomId))
            _networkSession.BeginSession(_session.roomId, null);
    }

    private async void ConnectGraphSocket()
    {
        try
        {
            if (_graphSyncClient != null &&
                !_graphSyncClient.IsConnected)
                await _graphSyncClient.Connect();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[MVP Flow] Graph WebSocket 연결 실패: " +
                exception.Message);
            _session.online = false;
            UpdateModeBadge();
        }
    }

    private void UpdateModeBadge()
    {
        if (_modeBadge == null) return;
        _modeBadge.text =
            string.IsNullOrEmpty(_session.roomId)
                ? "준비됨"
                : _session.ModeLabel;
        _modeBadge.color =
            _session.online
                ? MvpStudentUiFactory.SuccessInk
                : MvpStudentUiFactory.WarningInk;
    }

    private static UnityWebRequest CreateJsonRequest(
        string url, string body)
    {
        UnityWebRequest request =
            new UnityWebRequest(
                url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler =
            new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(body ?? "{}"));
        request.downloadHandler =
            new DownloadHandlerBuffer();
        request.SetRequestHeader(
            "Content-Type", "application/json");
        return request;
    }

    private static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        if (target == null) return;

        Type type = target.GetType();
        while (type != null)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            type = type.BaseType;
        }
    }

    private static string Safe(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private void ClearContent()
    {
        if (_contentRoot == null) return;
        for (int i = _contentRoot.childCount - 1; i >= 0; i--)
            Destroy(_contentRoot.GetChild(i).gameObject);
    }

    private static Texture2D CloneTexture(Texture source)
    {
        if (source == null) return null;

        RenderTexture temporary =
            RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;

        Graphics.Blit(source, temporary);
        RenderTexture.active = temporary;

        Texture2D copy = new Texture2D(
            source.width,
            source.height,
            TextureFormat.RGBA32,
            false);
        copy.ReadPixels(
            new Rect(0f, 0f, source.width, source.height),
            0,
            0);
        copy.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        return copy;
    }

    private static Type FindRuntimeType(string fullName)
    {
        foreach (Assembly assembly in
                 AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName);
            if (type != null) return type;
        }
        return null;
    }

    private static void AttachPointableCanvas(Canvas canvas)
    {
        if (canvas == null) return;

        Type pointableType =
            FindRuntimeType("Oculus.Interaction.PointableCanvas");
        if (pointableType == null) return;

        // UI 오브젝트에 남아 있는 파괴 예약 컴포넌트를 재사용하지 않는다.
        BoxCollider box = canvas.GetComponent<BoxCollider>();
        if (box == null)
            box = canvas.gameObject.AddComponent<BoxCollider>();
        if (box == null)
        {
            Debug.LogWarning(
                "[MVP XR] Canvas 충돌면을 만들지 못했습니다: " +
                canvas.name);
            return;
        }

        RectTransform rect =
            canvas.GetComponent<RectTransform>();
        if (rect != null)
        {
            Vector2 size = rect.rect.size;
            box.center = Vector3.zero;
            box.size = new Vector3(
                Mathf.Max(1f, Mathf.Abs(size.x)),
                Mathf.Max(1f, Mathf.Abs(size.y)),
                4f);
        }

        Component pointable = canvas.GetComponent(pointableType);
        if (pointable == null)
            pointable = canvas.gameObject.AddComponent(pointableType);
        MethodInfo injectCanvas = pointableType.GetMethod(
            "InjectAllPointableCanvas",
            BindingFlags.Instance | BindingFlags.Public);
        injectCanvas?.Invoke(pointable, new object[] { canvas });

        Type surfaceType = FindRuntimeType(
            "Oculus.Interaction.Surfaces.ColliderSurface");
        Type rayType = FindRuntimeType(
            "Oculus.Interaction.RayInteractable");
        if (surfaceType == null || rayType == null)
            return;

        Component surface = canvas.GetComponent(surfaceType);
        if (surface == null)
            surface = canvas.gameObject.AddComponent(surfaceType);
        Component interactable = canvas.GetComponent(rayType);
        if (interactable == null)
            interactable = canvas.gameObject.AddComponent(rayType);

        // Meta ISDK 컴포넌트는 Awake에서 캐시하므로 필드만 바꾸지 않고
        // 공식 런타임 주입 메서드로 캐시와 직렬화 필드를 함께 갱신한다.
        surfaceType.GetMethod(
            "InjectCollider",
            BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(surface, new object[] { box });
        rayType.GetMethod(
            "InjectAllRayInteractable",
            BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(interactable, new object[] { surface });
        rayType.GetMethod(
            "InjectOptionalPointableElement",
            BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(interactable, new object[] { pointable });
    }

    private static void EnsurePointableCanvasModule()
    {
        EventSystem eventSystem =
            EventSystem.current ??
            FindFirstObjectByType<EventSystem>();
        if (eventSystem == null) return;

        Type type = FindRuntimeType(
            "Oculus.Interaction.PointableCanvasModule");
        if (type == null) return;

        // 다른 씬이나 프리팹의 모듈이 아니라 현재 EventSystem에 둔다.
        Component module = eventSystem.GetComponent(type);
        if (module == null)
            module = eventSystem.gameObject.AddComponent(type);
        SetPrivateField(module, "_exclusiveMode", false);
    }


    private static void ConfigurePasswordInput(TMP_InputField input)
    {
        if (input == null) return;
        input.contentType = TMP_InputField.ContentType.Password;
        input.inputType = TMP_InputField.InputType.Password;
        input.characterLimit = 32;
        input.ForceLabelUpdate();
    }
}
