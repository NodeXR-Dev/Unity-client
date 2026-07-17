using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 기술 용어 대신 지금 해야 할 한 가지 행동만 보여 주는 MVP 작업판 안내기.
// 기존 플로우를 바꾸지 않고 화면 문구와 다음 버튼의 준비 상태만 학생 눈높이로 정리한다.
[DefaultExecutionOrder(850)]
public class MvpStudentWorkspaceGuide : MonoBehaviour
{
    private const string MvpScenePath =
        "Assets/00_Scenes/MVP/MVP.unity";

    private MvpWaterRocketGraphController _graph;
    private GraphManager _graphManager;
    private MvpSpatialNodeGestureController _gesture;
    private TMP_Text _status;
    private Button _pictureButton;
    private string _lastGuideText;
    private string _lastObservedText;
    private float _keepFlowMessageUntil;
    private float _nextScanTime;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForMvp()
    {
        if (SceneManager.GetActiveScene().path != MvpScenePath)
            return;

        GameObject app = GameObject.Find("MvpApp");
        if (app == null)
            app = new GameObject("MvpApp");
        if (app.GetComponent<MvpStudentWorkspaceGuide>() == null)
            app.AddComponent<MvpStudentWorkspaceGuide>();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeGesture();
        _nextScanTime = 0f;
    }

    private void OnDisable()
    {
        if (_gesture != null)
            _gesture.OnRootNodeCreated -= HandleRootNodeCreated;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + 0.25f;
        ResolveReferences();
        ResolveWorkspaceUi();
        SimplifyVisibleTerms();
        PumpPendingMessages();
        SuggestRecenterIfNodesStray();
        RefreshGuide();
    }

    // ─────────────────────────────────────────────
    // 노드가 보드에서 멀리 흩어지면(잃어버리기 쉬움) '작업판 맞추기'를 제안한다
    // ─────────────────────────────────────────────
    private const float StrayDistance = 1.6f;      // 보드 중심에서 이 이상이면 '멀다'
    private const float StrayHoldSeconds = 2.5f;   // 이 시간 이상 지속돼야 제안
    private const float StrayHintCooldown = 45f;   // 제안 반복 최소 간격

    private MvpWorkspaceLayout _workspaceLayoutRef;
    private float _straySince = -1f;
    private float _nextStrayHintTime;

    private void SuggestRecenterIfNodesStray()
    {
        if (_workspaceLayoutRef == null)
            _workspaceLayoutRef =
                FindFirstObjectByType<MvpWorkspaceLayout>();
        Transform board =
            _workspaceLayoutRef != null
                ? _workspaceLayoutRef.MainSketchPanel
                : null;
        if (board == null)
        {
            _straySince = -1f;
            return;
        }

        bool anyStray = false;
        foreach (NodeView node in FindObjectsByType<NodeView>(
                     FindObjectsSortMode.None))
        {
            if (node == null) continue;
            if ((node.transform.position - board.position).sqrMagnitude >
                StrayDistance * StrayDistance)
            {
                anyStray = true;
                break;
            }
        }

        if (!anyStray)
        {
            _straySince = -1f;
            return;
        }

        if (_straySince < 0f)
            _straySince = Time.unscaledTime;

        if (Time.unscaledTime - _straySince >= StrayHoldSeconds &&
            Time.unscaledTime >= _nextStrayHintTime)
        {
            _nextStrayHintTime = Time.unscaledTime + StrayHintCooldown;
            ShowLinkFeedback(
                "노드가 멀리 흩어져 있어요 — 설정의 '작업판 맞추기'로 한 번에 모을 수 있어요.",
                new Color(0.95f, 0.73f, 0.29f, 1f),
                4f);
        }
    }

    private void ResolveReferences()
    {
        if (_graph == null)
            _graph =
                FindFirstObjectByType<MvpWaterRocketGraphController>();
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();

        MvpSpatialNodeGestureController currentGesture =
            FindFirstObjectByType<MvpSpatialNodeGestureController>();
        if (currentGesture != _gesture)
        {
            if (_gesture != null)
                _gesture.OnRootNodeCreated -= HandleRootNodeCreated;
            _gesture = currentGesture;
            SubscribeGesture();
        }
    }

    private void SubscribeGesture()
    {
        if (_gesture == null)
            return;
        _gesture.OnRootNodeCreated -= HandleRootNodeCreated;
        _gesture.OnRootNodeCreated += HandleRootNodeCreated;
    }

    private void ResolveWorkspaceUi()
    {
        if (_status != null && _pictureButton != null)
            return;

        GameObject bar = GameObject.Find("MvpWorkspaceActionBar");
        if (bar == null)
            return;

        Transform status = bar.transform.Find("SpatialStatus");
        _status = status != null
            ? status.GetComponent<TMP_Text>()
            : null;

        Transform button = bar.transform.Find("Review");
        if (button == null)
            button = bar.transform.Find("Generate2D");   // 리뉴얼된 액션바의 그림 생성 버튼
        _pictureButton = button != null
            ? button.GetComponent<Button>()
            : null;
    }

    private void RefreshGuide()
    {
        if (_status == null || _graph == null)
            return;

        string current = _status.text ?? "";
        if (current != _lastObservedText && current != _lastGuideText)
            _keepFlowMessageUntil = Time.unscaledTime + 1.8f;
        _lastObservedText = current;

        int parts = _graph.PartCount;
        int ideas = _graph.RequirementCount;
        int attached = _graph.AppliedConnectionCount;

        string guide;
        if (parts == 0)
            guide = "먼저 마음에 드는 부품을 골라 보세요";
        else if (ideas == 0)
            guide = "주먹 2초 → 손바닥 펼치기 → 다시 주먹";
        else if (attached == 0)
            guide = "아이디어 카드 손잡이를 잡아 부품 원에 놓아 보세요";
        else
            guide =
                "부품 " + parts + "개  ·  아이디어 " + ideas +
                "개  ·  연결한 아이디어 " + attached + "개";

        if (Time.unscaledTime >= _keepFlowMessageUntil)
        {
            _status.text = guide;
            _status.color = Color.white;
            _lastGuideText = guide;
            _lastObservedText = guide;
        }

        if (_pictureButton != null)
            _pictureButton.interactable =
                parts > 0 && ideas > 0 && attached > 0;
    }

    private void HandleRootNodeCreated(
            string nodeId,
            Vector3 position)
    {
        if (_graphManager != null &&
            !string.IsNullOrEmpty(nodeId))
            _graphManager.RequestUpdateNodeText(
                nodeId,
                "새 아이디어 카드");
    }

    private static void SimplifyVisibleTerms()
    {
        TMP_Text[] texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
        foreach (TMP_Text text in texts)
        {
            if (text == null ||
                !text.gameObject.scene.IsValid() ||
                text.gameObject.scene !=
                    SceneManager.GetActiveScene() ||
                !text.gameObject.activeInHierarchy ||
                text.GetComponentInParent<NodeView>() != null ||
                text.GetComponentInParent<TMP_InputField>() != null)
                continue;

            string value = text.text;
            if (string.IsNullOrEmpty(value))
                continue;

            string simplified = value
                .Replace("2D 스케치", "설계 그림")
                .Replace("스케치", "그림")
                .Replace("요구사항", "아이디어")
                .Replace("속성", "아이디어");
            if (simplified != value)
                text.text = simplified;
        }
    }


    public void ShowLinkFeedback(
            string message,
            Color color,
            float duration = 2.2f)
    {
        ResolveWorkspaceUi();
        if (_status == null)
            return;

        message = message ?? "";

        // 실시간 스트림(음성 부분 자막)은 절대 큐에 넣지 않는다 —
        // 낡은 자막이 나중에 표시되면 오히려 혼란스럽다.
        if (message.StartsWith(LiveMarker))
        {
            ApplyMessage(message, color, duration);
            return;
        }

        // 같은 종류의 메시지(예: 음성 부분 자막 '● 듣는 중 · …')는 즉시 갱신하고,
        // 다른 종류의 새 메시지는 현재 메시지가 최소 노출 시간을 채울 때까지 큐에 둔다.
        // (입장 알림이 조작 안내를 순식간에 지워버리는 문제 방지)
        bool updatesCurrent =
            SamePrefix(_status.text, message) ||
            string.IsNullOrEmpty(_status.text) ||
            _status.text == _lastGuideText;   // 기본 안내 문구는 즉시 대체 가능
        if (!updatesCurrent && Time.unscaledTime < _minHoldUntil)
        {
            if (_pendingMessages.Count < 3 &&
                (_pendingMessages.Count == 0 ||
                 _pendingMessages.Peek().message != message))
                _pendingMessages.Enqueue(
                    new PendingMessage
                    {
                        message = message,
                        color = color,
                        duration = duration
                    });
            return;
        }

        ApplyMessage(message, color, duration);
    }

    // 실시간(큐 우회) 메시지 마커 — 음성 자막 등 스트림성 문구는 이 접두로 시작한다.
    public const string LiveMarker = "<color=#F2606A>●</color>";

    private struct PendingMessage
    {
        public string message;
        public Color color;
        public float duration;
    }

    private readonly System.Collections.Generic.Queue<PendingMessage>
        _pendingMessages = new System.Collections.Generic.Queue<PendingMessage>();
    private float _minHoldUntil;

    private void ApplyMessage(string message, Color color, float duration)
    {
        _status.text = message;
        _status.color = color;
        _lastObservedText = _status.text;
        _keepFlowMessageUntil =
            Time.unscaledTime + Mathf.Max(0.8f, duration);
        _minHoldUntil = Time.unscaledTime + 1.3f;
    }

    // 앞 5자가 같으면 같은 종류의 메시지로 본다(음성 자막 스트림 등).
    private static bool SamePrefix(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        int n = Mathf.Min(5, Mathf.Min(a.Length, b.Length));
        return string.CompareOrdinal(a, 0, b, 0, n) == 0;
    }

    // LateUpdate 틱(0.25s)에서 호출 — 현재 메시지가 시간을 채우면 대기 메시지를 꺼낸다.
    private void PumpPendingMessages()
    {
        if (_pendingMessages.Count == 0 ||
            Time.unscaledTime < _minHoldUntil ||
            _status == null)
            return;
        PendingMessage next = _pendingMessages.Dequeue();
        ApplyMessage(next.message, next.color, next.duration);
    }
}
