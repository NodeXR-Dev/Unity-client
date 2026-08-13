using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;

// 동적으로 생성되는 모든 PROPERTY 노드에 이름 편집, 포인터 이동,
// 손 핀치와 컨트롤러 Grip 이동을 배선한다.
[DefaultExecutionOrder(780)]
[DisallowMultipleComponent]
public class MvpNodeInteractionController : MonoBehaviour
{
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private MvpWorkspaceLayout _workspaceLayout;
    [SerializeField] private float _scanInterval = 0.2f;

    private float _nextScanTime;

    private MvpXrGraphLinkController _linkController;
    private GraphNetworkManager _network;          // 멀티플레이 노드 잠금(있을 때만)
    private bool _networkEventsHooked;
    private string _editLockNodeId;                // 키보드 편집으로 잠근 노드
    private float _nextLockDeniedMessageTime;
    private bool _manualPlacementAnnounced;

    private void Awake()
    {
        ResolveReferences();
        WireNodes();
    }

    private void OnEnable()
    {
        _nextScanTime = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime =
            Time.unscaledTime + Mathf.Max(0.08f, _scanInterval);
        ResolveReferences();
        WireNodes();

        // 키보드가 닫히면 편집용 잠금을 해제한다(잠금이 영구 보유되는 것 방지).
        if (!string.IsNullOrEmpty(_editLockNodeId) && !MvpWorldKeyboard.IsOpen)
        {
            _network?.RequestUnlockNode(_editLockNodeId);
            _editLockNodeId = null;
        }
    }

    // 다른 참가자가 잠근 노드인지. (오프라인/세션 없음이면 항상 조작 가능)
    public bool CanManipulate(NodeView node)
    {
        if (node == null || string.IsNullOrEmpty(node.NodeId))
            return false;
        return _network == null ||
               !_network.ShouldDisableNodeInteraction(node.NodeId);
    }

    public void NotifyLockBlocked(NodeView node)
    {
        if (Time.unscaledTime < _nextLockDeniedMessageTime)
            return;
        _nextLockDeniedMessageTime = Time.unscaledTime + 1.5f;
        ShowFeedback(
            "다른 친구가 이 노드를 편집하고 있어요. 잠시만 기다려 주세요.",
            MvpStudentUiFactory.Amber);
        if (MvpAudioCue.Instance != null)
            MvpAudioCue.Instance.Play(MvpAudioCue.Cue.Error);
    }

    public void WireNodes()
    {
        if (_graphManager == null)
            return;

        NodeView[] nodes = FindObjectsByType<NodeView>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (NodeView node in nodes)
            WireNode(node);

        _linkController?.RefreshNodeControls();
    }

    public void FocusNodeForRename(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            StartCoroutine(FocusWhenRendered(nodeId));
    }

    public bool TryOpenNodeKeyboard(NodeView node, TMP_InputField input)
    {
        if (node == null || input == null)
        return false;

        if (!CanManipulate(node))
        {
            NotifyLockBlocked(node);
            return false;
        }

        // 편집하는 동안 다른 참가자가 같은 노드를 고치지 못하게 잠근다.
        // 키보드가 닫히면 LateUpdate 가 해제한다.
        if (_network != null && !string.IsNullOrEmpty(node.NodeId))
        {
            // 키보드가 열린 채 다른 노드로 옮겨 타면 이전 편집 잠금부터 해제한다(누수 방지).
            if (!string.IsNullOrEmpty(_editLockNodeId) &&
            _editLockNodeId != node.NodeId)
            _network.RequestUnlockNode(_editLockNodeId);

            _network.RequestLockNode(node.NodeId);
            _editLockNodeId = node.NodeId;
        }

        // 노드 글씨(입력칸)를 직접 포크/클릭하면 바로 키보드를 연다(접촉 게이트로 막지 않음 — 이슈 3).
        MvpWorldKeyboard.Open(input);
        ShowFeedback(
        "이 노드의 이름을 바꿔 보세요.",
        MvpStudentUiFactory.HoloCyan);
        return true;
    }


    public void MoveNode(NodeView node, Vector3 position)
    {
        if (node == null ||
            _graphManager == null ||
            string.IsNullOrEmpty(node.NodeId))
            return;

        // 사용자가 직접 배치하기 시작한 뒤에는 자동 레이아웃이 위치를 덮지 않는다.
        _workspaceLayout?.SetSpatialPlacementMode(true);
        _graphManager.RequestMoveNode(node.NodeId, position);

        if (!_manualPlacementAnnounced)
        {
            _manualPlacementAnnounced = true;
            ShowFeedback(
                "노드를 원하는 위치로 옮겼어요. 설정의 ‘작업판 맞추기’로 다시 정렬할 수 있어요.",
                MvpStudentUiFactory.HoloCyan);
        }
    }

    public void BeginGrab(NodeView node)
    {
        if (node == null)
            return;

        Debug.Log("[MVPgrab] BeginGrab node=" + node.NodeId +
            " — 노드가 그랩됨(버튼 누르려는데 이게 뜨면 '도망'의 원인)");

        // 잡는 동안 다른 참가자의 동시 편집을 막는다(EndGrab 에서 해제).
        _network?.RequestLockNode(node.NodeId);

        _workspaceLayout?.SetSpatialPlacementMode(true);
        ShowFeedback(
            "손을 펴거나 Grip을 놓으면 이 위치에 고정돼요.",
            MvpStudentUiFactory.HoloCyan);
    }

    public void EndGrab(NodeView node)
    {
        if (node == null)
            return;

        MoveNode(node, node.transform.position);
        _network?.RequestUnlockNode(node.NodeId);
        ShowFeedback(
            "노드 위치를 저장했어요.",
            MvpStudentUiFactory.Mint);
    }

    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
        if (_workspaceLayout == null)
            _workspaceLayout = FindFirstObjectByType<MvpWorkspaceLayout>();
        if (_linkController == null)
            _linkController =
                FindFirstObjectByType<MvpXrGraphLinkController>();
        if (_network == null)
        {
            _network = FindFirstObjectByType<GraphNetworkManager>();
            _networkEventsHooked = false;
        }
        if (_network != null && !_networkEventsHooked)
        {
            _network.NodeLockDenied += HandleNodeLockDenied;
            _networkEventsHooked = true;
        }
    }

    private void OnDestroy()
    {
        if (_network != null && _networkEventsHooked)
            _network.NodeLockDenied -= HandleNodeLockDenied;
    }

    private void HandleNodeLockDenied(
        string nodeId, Fusion.PlayerRef requester, Fusion.PlayerRef owner)
    {
        NotifyLockBlocked(null);
    }

    private void WireNode(NodeView node)
    {
        if (node == null || string.IsNullOrEmpty(node.NodeId))
            return;

        TMP_InputField input =
            node.GetComponentInChildren<TMP_InputField>(true);
        if (input != null)
        {
            MvpXrKeyboardInput keyboard =
                input.GetComponent<MvpXrKeyboardInput>();
            if (keyboard == null)
                keyboard =
                    input.gameObject.AddComponent<MvpXrKeyboardInput>();
            keyboard.Configure(input);

            MvpNodePointerManipulator pointer =
                input.GetComponent<MvpNodePointerManipulator>();
            if (pointer == null)
                pointer =
                    input.gameObject.AddComponent<
                        MvpNodePointerManipulator>();
            pointer.Configure(this, node, input);
        }

        EnsureGrabInteraction(node);
        RefreshLockBadge(node);
    }

    // 다른 참가자가 잠근 노드 위에 '✋ 편집 중' 배지를 띄운다(주기 스캔으로 갱신).
    private void RefreshLockBadge(NodeView node)
    {
        bool lockedByOther =
            _network != null &&
            !string.IsNullOrEmpty(node.NodeId) &&
            _network.ShouldDisableNodeInteraction(node.NodeId);

        Transform existing = node.transform.Find("MvpLockBadge");
        if (!lockedByOther)
        {
            if (existing != null)
                Destroy(existing.gameObject);
            return;
        }

        if (existing != null)
            return;

        GameObject badgeGo = new GameObject("MvpLockBadge");
        badgeGo.transform.SetParent(node.transform, false);

        float top = 0.10f;
        BoxCollider box = node.GetComponent<BoxCollider>();
        if (box != null)
            top = box.center.y + box.size.y * 0.5f + 0.06f;
        badgeGo.transform.localPosition = new Vector3(0f, top, 0f);

        TextMeshPro label = badgeGo.AddComponent<TextMeshPro>();
        label.text = "✋ 친구가 편집 중";
        label.fontSize = 0.9f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.78f, 0.35f, 1f);
        label.raycastTarget = false;
        RectTransform rect = label.rectTransform;
        rect.sizeDelta = new Vector2(2.4f, 0.4f);

        badgeGo.AddComponent<MvpBillboard>();
    }

    private void EnsureGrabInteraction(NodeView node)
    {
        Rigidbody body = node.GetComponent<Rigidbody>();
        if (body == null)
        body = node.gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        EnsureCollider(node);

        MvpNodePlaneTransformer transformer =
        node.GetComponent<MvpNodePlaneTransformer>();
        if (transformer == null)
        transformer = node.gameObject.AddComponent<MvpNodePlaneTransformer>();
        transformer.Configure(this, node);

        Grabbable grabbable = node.GetComponent<Grabbable>();
        if (grabbable == null)
        grabbable = node.gameObject.AddComponent<Grabbable>();
        grabbable.MaxGrabPoints = 1;
        grabbable.InjectOptionalTargetTransform(node.transform);
        grabbable.InjectOptionalRigidbody(body);
        grabbable.InjectOptionalThrowWhenUnselected(false);
        grabbable.InjectOptionalOneGrabTransformer(transformer);

        GrabInteractable controllerGrab =
        node.GetComponent<GrabInteractable>();
        if (controllerGrab == null)
        controllerGrab = node.gameObject.AddComponent<GrabInteractable>();
        controllerGrab.InjectAllGrabInteractable(body);
        controllerGrab.InjectOptionalPointableElement(grabbable);
        controllerGrab.UseClosestPointAsGrabSource = true;
        controllerGrab.ReleaseDistance = 0.16f;

        HandGrabInteractable handGrab =
        node.GetComponent<HandGrabInteractable>();
        if (handGrab == null)
        handGrab = node.gameObject.AddComponent<HandGrabInteractable>();
        handGrab.InjectAllHandGrabInteractable(
        GrabTypeFlags.Pinch,
        body,
        GrabbingRule.DefaultPinchRule,
        GrabbingRule.DefaultPalmRule);
        handGrab.InjectOptionalPointableElement(grabbable);

        MvpNodeContactGate gate = node.GetComponent<MvpNodeContactGate>();
        if (gate == null)
        gate = node.gameObject.AddComponent<MvpNodeContactGate>();
        gate.Configure(node, handGrab, grabbable);
    }

    private static void EnsureCollider(NodeView node)
    {
        if (node == null)
        return;

        BoxCollider box = node.GetComponent<BoxCollider>();
        if (box == null)
        box = node.gameObject.AddComponent<BoxCollider>();

        // 콜라이더(그립/터치 판정)는 보이는 노드 본체 메시(NodeBox)에 맞춘다. (이슈 2)
        // 자식 Canvas 의 rect(481x493)는 실제 내용물보다 ~100배 커서 그걸로 산정하면
        // 월드 ~48m 짜리 콜라이더가 생긴다. UGUI(Image/TMP)는 Renderer 가 아니라 CanvasRenderer 라
        // 아래 GetComponentsInChildren<Renderer> 에는 잡히지 않으므로 본체 메시만 포함된다.
        Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

            Vector3 scale = node.transform.lossyScale;
            scale.x = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            scale.y = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
            scale.z = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

            box.center = node.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
            bounds.size.x / scale.x,
            bounds.size.y / scale.y,
            Mathf.Max(0.04f, bounds.size.z / scale.z));
        }
        else
        {
            // Canvas가 아직 만들어지기 전 한 프레임만 쓰는 보수적인 크기다.
            box.center = Vector3.zero;
            box.size = new Vector3(2.4f, 1.5f, 0.16f);
        }

        box.isTrigger = true;
    }

    private IEnumerator FocusWhenRendered(string nodeId)
    {
        for (int frame = 0; frame < 90; frame++)
        {
            NodeView[] nodes = FindObjectsByType<NodeView>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (NodeView node in nodes)
            {
                if (node == null || node.NodeId != nodeId)
                    continue;

                WireNode(node);
                TMP_InputField input =
                    node.GetComponentInChildren<TMP_InputField>(true);
                if (input != null)
                {
                    MvpWorldKeyboard.Open(input);
                    ShowFeedback(
                        "새 세부 조건의 이름을 입력해 주세요.",
                        MvpStudentUiFactory.HoloCyan);
                    yield break;
                }
            }

            yield return null;
        }

        Debug.LogWarning(
            "[MVP XR] 생성된 자식 노드의 입력칸을 찾지 못했습니다: " +
            nodeId);
    }

    public static void ShowFeedback(string message, Color color)
    {
        MvpStudentWorkspaceGuide guide =
            FindFirstObjectByType<MvpStudentWorkspaceGuide>();
        if (guide != null)
            guide.ShowLinkFeedback(message, color);
        else
            Debug.Log("[MVP XR] " + message);
    }
}

// 노드 라벨을 짧게 누르면 이름 편집, 누른 채 움직이면 노드 이동으로 해석한다.
// PointableCanvas가 손 핀치와 컨트롤러 Ray를 PointerEventData로 변환한다.
[DisallowMultipleComponent]
public class MvpNodePointerManipulator :
    MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IPointerClickHandler
{
    private MvpNodeInteractionController _controller;
    private NodeView _node;
    private TMP_InputField _input;
    private Vector3 _pointerOffset;
    private bool _dragging;
    private float _suppressClickUntil;

    public void Configure(
        MvpNodeInteractionController controller,
        NodeView node,
        TMP_InputField input)
    {
        if (controller != null)
            _controller = controller;
        if (node != null)
            _node = node;
        if (input != null)
            _input = input;
    }


    public void OnBeginDrag(PointerEventData eventData)
    {
        // 노드 글씨 위 poke 는 노드 이동으로 쓰지 않는다. (poke 흔들림이 드래그로 오인돼
        // 노드가 손을 따라 움직이고 키보드가 안 열리던 문제 — 노드 이동은 본체 핀치 그랩 사용)
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 의도적으로 비움 — 이 경로로 노드를 움직이지 않는다.
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 손을 떼면(드래그로 처리됐든 클릭이든) 키보드를 연다.
        if (Time.unscaledTime < _suppressClickUntil)
        return;
        _suppressClickUntil = Time.unscaledTime + 0.25f;
        Debug.Log("[MVPkbd] text poke(endDrag) -> open keyboard node=" +
        (_node != null ? _node.NodeId : "null"));
        _controller?.TryOpenNodeKeyboard(_node, _input);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Time.unscaledTime < _suppressClickUntil)
        return;
        _suppressClickUntil = Time.unscaledTime + 0.25f;
        Debug.Log("[MVPkbd] text click -> open keyboard node=" +
        (_node != null ? _node.NodeId : "null"));
        _controller?.TryOpenNodeKeyboard(_node, _input);
    }


}

[DefaultExecutionOrder(775)]
[DisallowMultipleComponent]
public class MvpNodeContactGate : MonoBehaviour
{
    // 손끝이 노드 가까이 왔을 때 새 Pinch 를 연다.
    //
    // 예전에는 1mm 였다. ClosestPoint 는 콜라이더 '안'이면 그 점을 그대로 돌려주므로
    // 사실상 손끝이 상자 안에 들어가야만 핀치가 열렸다. 노드 콜라이더는
    // 0.26 x 0.12 x 0.13m 인데 손 추적 오차가 1~2cm 라 거의 잡히지 않았다.
    //
    // 손가락 한 마디만큼 여유를 준다. 이 정도면 허공에서 잘못 잡히지는 않으면서
    // 노드 표면 근처에서 핀치가 자연스럽게 열린다.
    private const float ContactSurfaceTolerance = 0.035f;

    private readonly List<IHand> _hands = new List<IHand>();
    private NodeView _node;
    private HandGrabInteractable _handGrab;
    private Grabbable _grabbable;
    private bool _hasTrackedHand;
    private float _nextScanTime;

    public bool IsTouching { get; private set; }

    public void Configure(
        NodeView node,
        HandGrabInteractable handGrab,
        Grabbable grabbable)
    {
        if (node != null)
            _node = node;
        if (handGrab != null)
            _handGrab = handGrab;
        if (grabbable != null)
            _grabbable = grabbable;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + 0.08f;
            RefreshHands();
        }

        // 손 위치 판정은 매 프레임 갱신해 노드 경계를 벗어난 뒤에도
        // 이전 접촉 상태로 허공 Pinch가 시작되는 지연을 없앤다.
        RefreshTouchState();
        AnimateTouchHighlight();
    }

    // 노드가 여러 개 겹쳐 있을 때 "지금 Pinch 하면 잡히는 노드"를 미리 보여 준다.
    // 손끝이 닿아 있는 노드만 살짝 커진다(위치는 건드리지 않아 동기화에 안전).
    // 하이라이트 중이 아닐 때는 스케일을 전혀 건드리지 않아 레이아웃 등
    // 외부 스케일 변경과 충돌하지 않는다.
    private Vector3 _restScale;
    private bool _touchHighlighted;

    private void AnimateTouchHighlight()
    {
        if (_node == null)
            return;

        Transform t = _node.transform;

        if (IsTouching)
        {
            if (!_touchHighlighted)
            {
                _restScale = t.localScale;   // 커지기 직전 스케일을 기준으로
                _touchHighlighted = true;
            }
            t.localScale = Vector3.Lerp(
                t.localScale,
                _restScale * 1.06f,
                12f * Time.unscaledDeltaTime);
            return;
        }

        if (!_touchHighlighted)
            return;

        t.localScale = Vector3.Lerp(
            t.localScale,
            _restScale,
            12f * Time.unscaledDeltaTime);
        if ((t.localScale - _restScale).sqrMagnitude < 0.00001f)
        {
            t.localScale = _restScale;
            _touchHighlighted = false;
        }
    }


    private void RefreshHands()
    {
        _hands.Clear();

        // HandRef, Filter, Synthetic 등 같은 손을 가리키는 컴포넌트가 여럿이다.
        // 손마다 실제 Hand를 우선해 한 개만 남기지 않으면 판정이 프레임마다 바뀐다.
        Dictionary<Handedness, IHand> best =
            new Dictionary<Handedness, IHand>();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            IHand candidate = behaviour as IHand;
            if (candidate == null)
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

        foreach (IHand hand in best.Values)
        {
            if (hand.IsConnected && hand.IsTrackedDataValid)
                _hands.Add(hand);
        }

        _hasTrackedHand = _hands.Count > 0;
    }

    private static bool PreferHand(IHand candidate, IHand current)
    {
        if (candidate == null)
            return false;
        if (current == null)
            return true;

        bool candidateValid =
            candidate.IsConnected && candidate.IsTrackedDataValid;
        bool currentValid =
            current.IsConnected && current.IsTrackedDataValid;
        if (candidateValid != currentValid)
            return candidateValid;

        return candidate.GetType().Name == "Hand" &&
               current.GetType().Name != "Hand";
    }

    private void RefreshTouchState()
    {
        bool rawTouching = false;
        foreach (IHand hand in _hands)
        {
            Pose thumbPose;
            bool hasThumb =
                hand.GetJointPose(HandJointId.HandThumbTip, out thumbPose);
            if (hasThumb && IsPointTouching(thumbPose.position))
            {
                rawTouching = true;
                break;
            }

            Pose indexPose;
            bool hasIndex =
                hand.GetJointPose(HandJointId.HandIndexTip, out indexPose);
            if (hasIndex && IsPointTouching(indexPose.position))
            {
                rawTouching = true;
                break;
            }

            // 사용자가 실제로 겨냥하는 곳은 두 손끝 사이다. 손끝 하나하나보다
            // 이 지점이 노드에 먼저 닿는 경우가 많아 함께 본다.
            if (hasThumb && hasIndex &&
                IsPointTouching((thumbPose.position + indexPose.position) * 0.5f))
            {
                rawTouching = true;
                break;
            }
        }

        bool isHeld = IsGrabHeld();

        // GrabPoints가 남아 있는 동안은 노드를 이미 잡고 있는 상태다.
        // 이때 손끝이 표면을 스치며 벗어났다고 HandGrabInteractable을 끄면
        // 사용자는 Pinch가 풀린 것처럼 느끼게 된다.
        IsTouching =
            rawTouching ||
            isHeld;

        if (_handGrab != null)
            _handGrab.enabled =
                isHeld || (_hasTrackedHand && IsTouching);
    }

    private bool IsGrabHeld()
    {
        return _grabbable != null &&
               _grabbable.GrabPoints != null &&
               _grabbable.GrabPoints.Count > 0;
    }

    private bool IsPointTouching(Vector3 point)
    {
        if (_node == null)
            return false;

        Collider[] colliders = _node.GetComponents<Collider>();
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled)
                continue;

            Vector3 closest = collider.ClosestPoint(point);
            if ((closest - point).sqrMagnitude <=
                ContactSurfaceTolerance * ContactSurfaceTolerance)
                return true;
        }

        return false;
    }

}
// Meta Interaction SDK의 손 핀치와 컨트롤러 Grip 이동을 작업판 평면으로 제한한다.
[DisallowMultipleComponent]
public class MvpNodePlaneTransformer : MonoBehaviour, ITransformer
{
    private MvpNodeInteractionController _controller;
    private NodeView _node;
    private IGrabbable _grabbable;
    private Vector3 _grabOffset;
    private bool _lockBlocked;   // 다른 참가자가 잠근 노드를 잡은 상태(이동 무시)

    public void Configure(
        MvpNodeInteractionController controller,
        NodeView node)
    {
        if (controller != null)
            _controller = controller;
        if (node != null)
            _node = node;
    }

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    public void BeginTransform()
    {
        if (_grabbable == null ||
        _grabbable.GrabPoints.Count == 0 ||
        _node == null)
        return;

        // 다른 참가자가 잠근 노드는 잡아도 움직이지 않는다.
        _lockBlocked =
        _controller != null && !_controller.CanManipulate(_node);
        if (_lockBlocked)
        {
            _controller?.NotifyLockBlocked(_node);
            return;
        }

        // 컨트롤러/손의 실제 3D 잡기점과 노드의 상대 거리만 보존한다.
        // 평면에 투영하지 않으므로 앞·뒤·위·아래 모든 방향으로 움직인다.
        Vector3 grabPoint = _grabbable.GrabPoints[0].position;
        _grabOffset = _node.transform.position - grabPoint;
        _controller?.BeginGrab(_node);
    }

    public void UpdateTransform()
    {
        if (_lockBlocked ||
        _grabbable == null ||
        _grabbable.GrabPoints.Count == 0 ||
        _node == null)
        return;

        Vector3 grabPoint = _grabbable.GrabPoints[0].position;
        _controller?.MoveNode(_node, grabPoint + _grabOffset);
    }

    public void EndTransform()
    {
        if (_lockBlocked)
        {
            _lockBlocked = false;
            return;
        }

        if (_node != null)
            _controller?.EndGrab(_node);
    }
}

// 항상 카메라를 바라보는 간단한 빌보드(잠금 배지용).
[DisallowMultipleComponent]
public class MvpBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        transform.rotation = Quaternion.LookRotation(
            transform.position - cam.transform.position);
    }
}
