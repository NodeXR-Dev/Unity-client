using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// MVP에서 아이디어 카드를 부품에 놓는 연결 UX를 제공한다.
// 데이터 변경은 GraphManager.RequestConnectFromPort()만 사용한다.
[DefaultExecutionOrder(760)]
public class MvpXrGraphLinkController : MonoBehaviour
{
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private float _lineWidth = 0.009f;
    [SerializeField] private Color _dragColor =
        new Color(0.25f, 0.88f, 1f, 0.95f);
    // 활성화된 연결선 색. 구체(Sphere_Grabbed)의 _EmissionColor 와 맞춘다.
    //   _Color(0, 0.934, 0.162)는 어두워서, 빛나는 구체 옆에 두면 선만 탁해 보인다.
    //   구체가 화면에서 실제로 내는 밝은 연두가 이 값이다.
    [SerializeField] private Color _linkedColor =
        new Color(0.51806414f, 1f, 0.495283f, 1f);

    [Tooltip("아직 활성화하지 않은 연결선 색. 선 자체는 항상 보인다.")]
    [SerializeField] private Color _inactiveLineColor = Color.white;

    [Tooltip("PART/ALL 연결선 끝 구체 크기의 폴백. 씬에 EdgeView 가 있으면 " +
             "그쪽 _connectorScale(EdgePrefab 0.0009)을 우선 쓴다.")]
    [SerializeField] private float _fallbackSphereScale = 0.0009f;

    private readonly Dictionary<string, LineRenderer> _edgeLines =
        new Dictionary<string, LineRenderer>();
    private readonly List<MvpPartDropTargetFeedback> _portFeedback =
        new List<MvpPartDropTargetFeedback>();

    private Material _lineMaterial;
    private LineRenderer _dragLine;
    private MvpIdeaCardDragHandle _activeHandle;
    private NodeView _activeNode;
    private Vector3 _originalPosition;
    private MvpIdeaCardDragHandle _selectedLinkHandle;
    private NodeView _selectedLinkNode;
    private string _selectedLinkNodeId;
    private Vector3 _originalScale;
    private Vector3 _pointerOffset;
    private float _nextScanTime;

    private void Awake()
    {
        ResolveReferences();
        CreateLineResources();
        DisableLegacyMouseConnector();
        WireIdeaHandles();
        WirePortFeedback();
    }

    private void OnEnable()
    {
        _nextScanTime = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + 0.35f;
            ResolveReferences();
            DisableLegacyMouseConnector();
            WireIdeaHandles();
            WirePortFeedback();
            RefreshChildActivation();
        }

        RefreshPermanentLines();
    }

    private void OnDisable()
    {
        CancelIdeaDrag(null);
        ClearConnectionSelection(false);
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null)
            Destroy(_lineMaterial);
    }

    public void Configure(GraphManager graphManager)
    {
        if (graphManager != null)
            _graphManager = graphManager;
    }

    public void RefreshNodeControls()
    {
        ResolveReferences();
        DisableLegacyMouseConnector();
        WireIdeaHandles();
        WirePortFeedback();
        RefreshChildActivation();
    }

    // 자식 노드의 활성/비활성 토글. 활성이면 부모와 초록선, 비활성이면 선 없이 흐리게.
    public void ToggleChildActive(NodeView node)
    {
        ResolveReferences();
        if (_graphManager == null || node == null ||
        string.IsNullOrEmpty(node.NodeId))
        return;

        bool now = !_graphManager.IsNodeActive(node.NodeId);

        // 같은 부모 아래 형제 자식은 한 번에 하나만 활성화(라디오식):
        // 이 노드를 켜면 같은 레벨의 다른 형제들은 자동으로 끈다.
        if (now)
        DeactivateSiblings(node.NodeId);

        _graphManager.RequestSetNodeActive(node.NodeId, now);
        RefreshChildActivation();
        WireIdeaHandles();   // 버튼 라벨(활성화/끄기) 즉시 갱신
        ShowMessage(now
            ? "이 조건을 켰어요 — 부모와 초록선으로 이어져요."
            : "이 조건을 껐어요.",
        now ? MvpStudentUiFactory.Mint : MvpStudentUiFactory.Amber);
    }

    // 같은 부모를 공유하는 다른 형제 자식들을 모두 비활성화한다(배타 활성화).
    private void DeactivateSiblings(string nodeId)
    {
        string parentId = _graphManager.GetPropertyParentId(nodeId);
        if (string.IsNullOrEmpty(parentId))
        return;

        foreach (NodeData sibling in _graphManager.GetAllNodes())
        {
            if (sibling == null ||
            string.IsNullOrEmpty(sibling.node_id) ||
            sibling.node_id == nodeId)
            continue;
            if (_graphManager.GetPropertyParentId(sibling.node_id) != parentId)
            continue;
            if (_graphManager.IsNodeActive(sibling.node_id))
            _graphManager.RequestSetNodeActive(sibling.node_id, false);
        }
    }

    // 부모→자식(PROPERTY→PROPERTY) 엣지: 활성 자식만 초록선, 비활성은 숨김 + 노드 흐리게.
    private void RefreshChildActivation()
    {
        if (_graphManager == null)
        return;

        foreach (EdgeView ev in
             FindObjectsByType<EdgeView>(FindObjectsSortMode.None))
        {
            if (ev == null || string.IsNullOrEmpty(ev.ToNodeId))
            continue;
            // to(자식)에 PROPERTY 부모가 있어야 부모→자식 엣지다.
            if (string.IsNullOrEmpty(
                _graphManager.GetPropertyParentId(ev.ToNodeId)))
            continue;
            // 선은 항상 보인다 — 연결 자체가 사라진 것처럼 보이면 안 된다.
            // 활성이면 초록(Sphere_Grabbed), 아니면 기본 흰색으로 구분만 한다.
            bool active = _graphManager.IsNodeActive(ev.ToNodeId);

            // 선 색은 구체 머티리얼에서 직접 가져온다 — 값을 복사해 두면 둘이 미묘하게 어긋난다.
            Color activeColor = ResolveSphereActiveColor(ev);
            ev.SetLineColor(active ? activeColor : _inactiveLineColor);
            ev.SetLineVisible(true);

            // 끝점 구도 함께 바뀌어야 한다 — 선만 초록이고 구는 흰색이면 따로 논다.
            // ConnectorSphereView 의 grabbed 머티리얼이 Sphere_Grabbed(초록) 그 자체다.
            ev.SetFromConnectorPressed(active);
            ev.SetToConnectorPressed(active);
        }

        // 노드에 달린 구체는 포트별로 따로 판단한다(아래 참조).
        RefreshConnectorSpheres();

        foreach (NodeView nv in
             FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (nv == null || string.IsNullOrEmpty(nv.NodeId))
            continue;
            if (string.IsNullOrEmpty(
                _graphManager.GetPropertyParentId(nv.NodeId)))
            continue;   // 자식만 흐림 처리
            SetNodeDimmed(nv, !_graphManager.IsNodeActive(nv.NodeId));
        }
    }

    // 비활성 자식을 비파괴적으로 흐리게(CanvasGroup 알파 + 메시 색 톤다운).
    private static void SetNodeDimmed(NodeView node, bool dim)
    {
        Transform canvas = node.transform.Find("Canvas");
        if (canvas != null)
        {
            CanvasGroup cg = canvas.GetComponent<CanvasGroup>();
            if (cg == null)
            cg = canvas.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = dim ? 0.35f : 1f;
        }

        MeshRenderer mr = node.GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            if (dim)
            {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                Color tint = new Color(0.5f, 0.5f, 0.5f, 1f);
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                mr.SetPropertyBlock(mpb);
            }
            else
            {
                // 활성이면 오버라이드 제거 → 머티리얼(깊이별 색) 원래대로 복원.
                // (white 로 덮으면 노드가 허옇게 뜬다.)
                mr.SetPropertyBlock(new MaterialPropertyBlock());
            }
        }
    }

    // 프리팹 LinkButton 용 진입점. 드래그 핸들 없이 버튼 클릭만으로 연결을 시작한다
    // (버튼을 누르고 → 빛나는 부품 원을 누른다).
    public void ToggleConnectionSelection(NodeView node)
    {
        ResolveReferences();
        if (_graphManager == null || node == null ||
            string.IsNullOrEmpty(node.NodeId))
            return;

        if (_selectedLinkNodeId == node.NodeId)
        {
            ClearConnectionSelection(false);
            return;
        }

        _selectedLinkHandle = null;
        _selectedLinkNodeId = node.NodeId;
        RefreshPortHighlights(node.NodeId, null);
        ShowMessage(
            "연결할 부품을 골라 주세요. 빛나는 원을 한 번 누르면 돼요.",
            MvpStudentUiFactory.HoloCyan);
    }

    public void ToggleConnectionSelection(
        MvpIdeaCardDragHandle handle,
        NodeView node)
    {
        ResolveReferences();
        if (_graphManager == null || handle == null || node == null ||
        string.IsNullOrEmpty(node.NodeId))
        return;

        if (_selectedLinkHandle == handle &&
        _selectedLinkNodeId == node.NodeId)
        {
            ClearConnectionSelection(false);
            ShowMessage("부품 연결 선택을 취소했어요.",
            MvpStudentUiFactory.Amber);
            return;
        }

        ClearConnectionSelection(false);
        _selectedLinkHandle = handle;
        _selectedLinkNode = node;
        _selectedLinkNodeId = node.NodeId;
        handle.SetConnectionSelected(true);
        RefreshPortHighlights(_selectedLinkNodeId, null);
        ShowMessage("연결점을 골랐어요. 빛나는 부품 원을 한 번 누르세요.",
        MvpStudentUiFactory.HoloCyan);
    }

    public void TryConnectSelectedPort(string portId)
    {
        ResolveReferences();
        if (_graphManager == null || string.IsNullOrEmpty(portId))
        return;

        if (string.IsNullOrEmpty(_selectedLinkNodeId))
        {
            ShowMessage("먼저 노드의 '연결' 버튼을 누르세요.",
            MvpStudentUiFactory.Amber);
            return;
        }

        string ideaId = _selectedLinkNodeId;
        string partLabel = ResolvePartLabel(portId);

        // 이미 연결된 부품을 다시 누르면 "연결 끊기"로 해석한다(토글). 엣지를 지우면
        // RefreshPermanentLines 가 다음 프레임에 연결선도 없앤다. (이슈 6)
        if (TryDisconnect(ideaId, portId))
        {
            ClearConnectionSelection(false);
            FindFirstObjectByType<MainSketchView>()?.Refresh();
            RefreshPermanentLines();
            ShowMessage("‘" + partLabel + "’ 연결을 끊었어요.",
            MvpStudentUiFactory.Amber);
            Debug.Log("[MVP XR] node-to-part DISCONNECTED: " +
            ideaId + " -> " + portId);
            return;
        }

        if (!CanAttach(ideaId, portId, out string reason))
        {
            ShowMessage(string.IsNullOrEmpty(reason)
                ? "이 부품에는 연결할 수 없어요."
                : reason,
            MvpStudentUiFactory.Coral);
            return;
        }

        bool connected = _graphManager.RequestConnectFromPort(portId, ideaId);
        ClearConnectionSelection(false);
        if (connected)
        {
            FindFirstObjectByType<MainSketchView>()?.Refresh();
            RefreshPermanentLines();
            ShowMessage(partLabel + "에 연결했어요.",
            MvpStudentUiFactory.Mint);
            Debug.Log("[MVP XR] node-to-part connected: " +
            ideaId + " -> " + portId);
        }
        else
        {
            ShowMessage("연결하지 못했어요. 다른 부품을 눌러 보세요.",
            MvpStudentUiFactory.Coral);
        }
    }

    private void ClearConnectionSelection(bool keepGuide)
    {
        if (_selectedLinkHandle != null)
            _selectedLinkHandle.SetConnectionSelected(false);

        _selectedLinkHandle = null;
        _selectedLinkNode = null;
        _selectedLinkNodeId = null;
        ClearPortHighlights();

        if (!keepGuide)
            return;

        ShowMessage(
            "노드의 ‘연결하기’를 선택하면 연결을 시작할 수 있어요.",
            MvpStudentUiFactory.HoloCyan);
    }


    public void CancelIdeaDrag(MvpIdeaCardDragHandle handle)
    {
        if (handle != null && handle != _activeHandle)
            return;

        if (_activeNode != null)
        {
            _activeNode.transform.position = _originalPosition;
            _activeNode.transform.localScale = _originalScale;
        }

        _activeHandle = null;
        _activeNode = null;

        if (_dragLine != null)
            _dragLine.enabled = false;

        ClearPortHighlights();
    }

    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
    }

    private void DisableLegacyMouseConnector()
    {
        GraphConnectDragController[] legacy =
            FindObjectsByType<GraphConnectDragController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (GraphConnectDragController controller in legacy)
        {
            if (controller != null && controller.enabled)
                controller.enabled = false;
        }
    }

    private void WireIdeaHandles()
    {
        NodeView[] nodes =
            FindObjectsByType<NodeView>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        // 누군가의 부모인 node_id 모음 — 여기 없으면 최하위(leaf)다.
        HashSet<string> parentIds = new HashSet<string>();
        if (_graphManager != null)
        {
            foreach (NodeView n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.NodeId)) continue;
                string parent = _graphManager.GetPropertyParentId(n.NodeId);
                if (!string.IsNullOrEmpty(parent))
                    parentIds.Add(parent);
            }
        }

        foreach (NodeView node in nodes)
        {
            if (node == null)
            continue;

            RectTransform canvas =
            node.transform.Find("Canvas") as RectTransform;
            if (canvas == null)
            continue;

            SyncIdeaTitleIfEmpty(node, canvas);
            WireLocalChildAdd(node, canvas);

            Transform legacyGrip = canvas.Find("MvpIdeaDragGrip");
            if (legacyGrip != null)
            legacyGrip.gameObject.SetActive(false);

            // 원본 R은 레퍼런스 stub이라 남겨 두면 연결점과 중복되어 보인다.
            Transform referenceButton = canvas.Find("ReferenceButton");
            if (referenceButton != null && referenceButton.gameObject.activeSelf)
            referenceButton.gameObject.SetActive(false);

            // [2026-08-13] 코드가 만들던 '연결' 패널(MvpNodeLinkPort)을 걷어낸다.
            //   NewNodebox 프리팹이 LinkButton / ActivateButton 을 이미 갖고 있는데
            //   그 위에 같은 기능의 패널을 얹어 두 겹으로 보이고 서로 클릭을 가로챘다.
            Transform legacyPort = canvas.Find("MvpNodeLinkPort");
            if (legacyPort != null)
            legacyPort.gameObject.SetActive(false);

            // 최상위(부모 없음) = 부품에 연결할 수 있는 노드.
            bool isRoot = _graphManager == null ||
            string.IsNullOrEmpty(
                _graphManager.GetPropertyParentId(node.NodeId));
            // 최하위(자식 없음) = 2D 재생성에 쓸지 고르는 노드.
            bool isLeaf = !parentIds.Contains(node.NodeId);

            // LinkButton — 최상위에만. 누르면 연결 대상 선택이 시작되고, 이어서 부품 원을 누른다.
            Button linkButton = FindNodeButton(canvas, "LinkButton");
            if (linkButton != null)
            {
                linkButton.gameObject.SetActive(isRoot);
                if (isRoot)
                {
                    NodeView captured = node;
                    linkButton.onClick.RemoveAllListeners();
                    linkButton.onClick.AddListener(
                        () => ToggleConnectionSelection(captured));
                }
            }

            // ActivateButton — 최하위에만. 켜진 노드만 2D 재생성에 반영된다.
            Button activateButton = FindNodeButton(canvas, "ActivateButton");
            if (activateButton != null)
            {
                activateButton.gameObject.SetActive(isLeaf && !isRoot);
                if (isLeaf && !isRoot)
                {
                    NodeView captured = node;
                    activateButton.onClick.RemoveAllListeners();
                    activateButton.onClick.AddListener(
                        () => ToggleChildActive(captured));
                }
            }
        }
    }

    // 노드 Canvas 아래에서 이름으로 버튼을 찾는다(꺼져 있어도 찾는다).
    private static Button FindNodeButton(Transform canvas, string name)
    {
        Transform found = canvas.Find(name);
        if (found == null)
        {
            foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == name) { found = t; break; }
            }
        }
        return found != null ? found.GetComponent<Button>() : null;
    }

    private void SyncIdeaTitleIfEmpty(
        NodeView node,
        RectTransform canvas)
    {
        TMP_InputField input =
            canvas.GetComponentInChildren<TMP_InputField>(true);
        if (input == null)
            return;

        string current =
            (input.text ?? string.Empty)
            .Replace("\u200B", string.Empty)
            .Trim();
        if (!string.IsNullOrEmpty(current))
            return;

        NodeData data =
            _graphManager != null && node != null
                ? _graphManager.GetNode(node.NodeId)
                : null;
        string title = data != null ? data.DisplayText : null;
        if (string.IsNullOrWhiteSpace(title))
            return;

        input.SetTextWithoutNotify(title);
        if (input.placeholder != null)
            input.placeholder.gameObject.SetActive(false);
    }


    private void WirePortFeedback()
    {
        _portFeedback.RemoveAll(item => item == null);

        foreach (PartPort port in
                 FindObjectsByType<PartPort>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            EnsurePortFeedback(port.gameObject, port.NodeId);

        foreach (AllPort port in
                 FindObjectsByType<AllPort>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            EnsurePortFeedback(port.gameObject, port.NodeId);
    }

    private void EnsurePortFeedback(
        GameObject target,
        string portId)
    {
        if (target == null)
            return;

        MvpPartDropTargetFeedback feedback =
            target.GetComponent<MvpPartDropTargetFeedback>();
        if (feedback == null)
            feedback =
                target.AddComponent<MvpPartDropTargetFeedback>();

        feedback.Configure(this, portId);
        if (!_portFeedback.Contains(feedback))
            _portFeedback.Add(feedback);
    }


    private void RefreshPortHighlights(
        string ideaId,
        string hoveredPortId)
    {
        foreach (MvpPartDropTargetFeedback feedback in _portFeedback)
        {
            if (feedback == null)
                continue;

            bool eligible =
                CanAttach(ideaId, feedback.PortId, out _);
            bool hovered =
                !string.IsNullOrEmpty(hoveredPortId) &&
                feedback.PortId == hoveredPortId;
            feedback.SetState(
                eligible,
                hovered,
                hovered && !eligible);
        }
    }

    private void ClearPortHighlights()
    {
        _portFeedback.RemoveAll(item => item == null);
        foreach (MvpPartDropTargetFeedback feedback in _portFeedback)
        {
            // Unity의 파괴된 Component는 null 조건 연산자로 걸러지지 않는다.
            if (feedback != null)
                feedback.ClearState();
        }
    }

    // 아이디어(root) → 부품(port) 엣지가 있으면 삭제하고 true 반환. (이슈 6 연결 끊기)
    private bool TryDisconnect(string ideaId, string portId)
    {
        if (_graphManager == null ||
            string.IsNullOrEmpty(ideaId) ||
            string.IsNullOrEmpty(portId))
            return false;

        string rootId = _graphManager.GetPropertyRootNodeId(ideaId);
        if (string.IsNullOrEmpty(rootId))
            rootId = ideaId;

        foreach (EdgeData edge in
                 _graphManager.GetEdgesConnectedToNode(portId))
        {
            if (edge != null &&
                edge.from_node_id == rootId &&
                edge.to_node_id == portId)
                return _graphManager.RequestDeleteEdge(edge.edge_id);
        }
        return false;
    }

    private bool CanAttach(
        string ideaId,
        string portId,
        out string reason)
    {
        reason = "";
        if (_graphManager == null ||
            string.IsNullOrEmpty(ideaId) ||
            string.IsNullOrEmpty(portId))
            return false;

        string rootId =
            _graphManager.GetPropertyRootNodeId(ideaId);
        if (string.IsNullOrEmpty(rootId))
            rootId = ideaId;

        return _graphManager.CanConnect(
            rootId,
            portId,
            out reason);
    }


    private string ResolvePartLabel(string portId)
    {
        NodeData node =
            _graphManager != null
                ? _graphManager.GetNode(portId)
                : null;
        return node != null &&
               !string.IsNullOrWhiteSpace(node.label)
            ? node.label
            : "선택한 부품";
    }

    private void ShowMessage(string message, Color color)
    {
        MvpStudentWorkspaceGuide guide =
            FindFirstObjectByType<MvpStudentWorkspaceGuide>();
        if (guide != null)
            guide.ShowLinkFeedback(message, color);
        else
            Debug.Log("[MVP XR] " + message);
    }

    private void CreateLineResources()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            _lineMaterial = new Material(shader);

        GameObject drag =
            new GameObject("MvpIdeaLinkPreview");
        drag.transform.SetParent(transform, false);
        _dragLine = CreateLineRenderer(drag, _dragColor);
        _dragLine.enabled = false;
    }

    private LineRenderer CreateLineRenderer(
        GameObject target,
        Color color)
    {
        LineRenderer line =
            target.GetComponent<LineRenderer>();
        if (line == null)
            line = target.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = _lineWidth;
        line.numCapVertices = 8;
        line.numCornerVertices = 4;
        line.alignment = LineAlignment.View;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 350;
        if (_lineMaterial != null)
            line.sharedMaterial = _lineMaterial;
        return line;
    }

    private void RefreshPermanentLines()
    {
        if (_graphManager == null)
            return;

        // 매 프레임 edge 별 FindObjectsByType 스캔을 피하기 위해 노드/포트 Transform 을 1회만 인덱싱한다.
        Dictionary<string, Transform> nodeMap =
            new Dictionary<string, Transform>();
        foreach (NodeView nv in
                 FindObjectsByType<NodeView>(FindObjectsSortMode.None))
            if (nv != null && !string.IsNullOrEmpty(nv.NodeId))
                // PART/ALL 은 속성 서브그래프의 위쪽에 붙으므로, 부모→자식 엣지와 똑같이
                // 속성 최상위 노드의 왼쪽 입력 포트로 들어와야 한다.
                // (nv.transform 을 쓰면 선이 노드 한가운데를 관통한다.)
                nodeMap[nv.NodeId] =
                    nv.InputPort != null ? nv.InputPort : nv.transform;

        Dictionary<string, Transform> portMap =
            new Dictionary<string, Transform>();
        foreach (AllPort ap in
                 FindObjectsByType<AllPort>(FindObjectsSortMode.None))
            if (ap != null && !string.IsNullOrEmpty(ap.NodeId))
                portMap[ap.NodeId] = ap.transform;
        foreach (PartPort pp in
                 FindObjectsByType<PartPort>(FindObjectsSortMode.None))
            if (pp != null && !string.IsNullOrEmpty(pp.NodeId))
                portMap[pp.NodeId] = pp.transform;   // PartPort 우선(키 충돌 시 덮어씀)

        GraphData graph = _graphManager.GetGraphData();
        HashSet<string> liveKeys = new HashSet<string>();
        if (graph?.edges != null)
        {
            foreach (EdgeData edge in graph.edges)
            {
                if (edge == null)
                    continue;

                NodeData from =
                    _graphManager.GetNode(edge.from_node_id);
                NodeData to =
                    _graphManager.GetNode(edge.to_node_id);
                if (from == null ||
                    to == null ||
                    from.NodeType != NodeType.PROPERTY ||
                    to.NodeType != NodeType.PART)
                    continue;

                nodeMap.TryGetValue(edge.from_node_id, out Transform node);
                portMap.TryGetValue(edge.to_node_id, out Transform port);
                if (node == null || port == null)
                    continue;

                string key =
                    !string.IsNullOrEmpty(edge.edge_id)
                        ? edge.edge_id
                        : edge.from_node_id + "->" + edge.to_node_id;
                liveKeys.Add(key);

                if (!_edgeLines.TryGetValue(
                        key,
                        out LineRenderer line) ||
                    line == null)
                {
                    GameObject lineObject =
                        new GameObject("MvpLinkedIdea_" + key);
                    lineObject.transform.SetParent(transform, false);
                    line =
                        CreateLineRenderer(
                            lineObject,
                            _linkedColor);
                    _edgeLines[key] = line;
                }

                line.enabled = true;
                line.SetPosition(0, node.position);
                line.SetPosition(1, port.position);

                // 양 끝에 구체를 얹는다. 부품(PART/ALL) 쪽은 테두리만 있는 원형 스프라이트 위에
                // 구체가 덮이면서 "꽂혔다"로 읽힌다. 노드 쪽은 입력 포트에 붙는다.
                PlaceEdgeSphere(key + "#node", node.position);
                PlaceEdgeSphere(key + "#port", port.position);
            }
        }

        List<string> stale = new List<string>();
        foreach (KeyValuePair<string, LineRenderer> pair in _edgeLines)
        {
            if (!liveKeys.Contains(pair.Key))
                stale.Add(pair.Key);
        }

        foreach (string key in stale)
        {
            if (_edgeLines.TryGetValue(
                    key,
                    out LineRenderer line) &&
                line != null)
                Destroy(line.gameObject);
            _edgeLines.Remove(key);

            RemoveEdgeSphere(key + "#node");
            RemoveEdgeSphere(key + "#port");
        }
    }

    // ── PART/ALL 연결선 끝점 구체 ──────────────────────────
    // EdgeView(노드↔노드)는 자기 구체를 갖고 있지만, 이 연결선은 LineRenderer 뿐이라
    // 끝이 허공에서 끊겨 보였다. 속성 노드끼리의 엣지가 쓰는 구체를 그대로 본떠
    // 같은 크기·같은 연두로 얹는다.

    private const string SphereClonePrefix = "MvpLinkSphere_";

    private readonly Dictionary<string, GameObject> _edgeSpheres =
        new Dictionary<string, GameObject>();
    private GameObject _sphereTemplate;
    private float _sphereScale;

    private void PlaceEdgeSphere(string key, Vector3 position)
    {
        if (!_edgeSpheres.TryGetValue(key, out GameObject sphere) ||
            sphere == null)
        {
            GameObject template = ResolveSphereTemplate();
            if (template == null)
                return;

            sphere = Instantiate(template, transform);
            sphere.name = SphereClonePrefix + key;

            // 복제본은 표시 전용이다 — 콜라이더가 있으면 포크/레이가 포트 대신 이걸 집는다.
            foreach (Collider collider in
                     sphere.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            // 이 선은 연결이 성립해 있을 때만 그려진다 → 구체는 항상 활성(연두)이다.
            // 연두색 자체는 ConnectorSphereView 의 grabbed 머티리얼(Sphere_Grabbed)에서 온다.
            ConnectorSphereView view =
                sphere.GetComponentInChildren<ConnectorSphereView>(true);
            if (view != null)
                view.SetGrabbed(true);

            _edgeSpheres[key] = sphere;
        }

        // 크기는 매번 맞춘다 — 템플릿이 늦게 잡히면 첫 프레임의 잘못된 크기가 남는다.
        // EdgeView 는 구체를 엣지 아래(스케일 1)에 두므로 _connectorScale 이 곧 월드 크기다.
        // 이쪽 부모는 이 컨트롤러라 스케일이 1이라는 보장이 없어 나눠서 상쇄한다.
        float scale = _sphereScale > 0f ? _sphereScale : _fallbackSphereScale;
        Vector3 parentScale = transform.lossyScale;
        sphere.transform.localScale = new Vector3(
            scale / SafeDivisor(parentScale.x),
            scale / SafeDivisor(parentScale.y),
            scale / SafeDivisor(parentScale.z));
        sphere.transform.position = position;
        sphere.SetActive(true);
    }

    private static float SafeDivisor(float value) =>
        Mathf.Approximately(value, 0f) ? 1f : value;

    // 속성 노드끼리의 엣지(EdgeView)가 쓰는 구체를 1순위로 본뜬다.
    // 크기까지 그 엣지의 _connectorScale 로 맞춰야 두 종류의 구체가 같아 보인다
    // — 노드에 달린 구체를 본뜨면 lossyScale 이 노드 스케일에 묶여 있어 크기가 어긋난다.
    private GameObject ResolveSphereTemplate()
    {
        if (_sphereTemplate != null)
            return _sphereTemplate;

        foreach (EdgeView edge in
                 FindObjectsByType<EdgeView>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (edge == null)
                continue;
            ConnectorSphereView sphere =
                edge.GetComponentInChildren<ConnectorSphereView>(true);
            if (sphere == null ||
                sphere.name.StartsWith(SphereClonePrefix))
                continue;

            _sphereTemplate = sphere.gameObject;
            _sphereScale = edge.ConnectorScale;
            return _sphereTemplate;
        }

        // 폴백: 엣지가 아직 하나도 없으면 노드에 달린 구체를 본뜬다.
        // 이때는 크기 기준이 없으므로 EdgePrefab 의 값을 그대로 쓴다.
        foreach (ConnectorSphereView sphere in
                 FindObjectsByType<ConnectorSphereView>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            // 내가 만든 복제본을 다시 본뜨지 않는다.
            if (sphere == null ||
                sphere.name.StartsWith(SphereClonePrefix))
                continue;

            _sphereTemplate = sphere.gameObject;
            _sphereScale = _fallbackSphereScale;
            return _sphereTemplate;
        }
        return null;
    }

    private void RemoveEdgeSphere(string key)
    {
        if (_edgeSpheres.TryGetValue(key, out GameObject sphere) &&
            sphere != null)
            Destroy(sphere);
        _edgeSpheres.Remove(key);
    }

    // 이 엣지가 쓰는 구체의 활성 색. 구체를 못 찾으면 인스펙터 색으로 떨어진다.
    private Color ResolveSphereActiveColor(EdgeView edge)
    {
        if (edge != null)
        {
            ConnectorSphereView sphere =
                edge.GetComponentInChildren<ConnectorSphereView>(true);
            if (sphere != null)
                return sphere.ActiveColor;
        }
        return _linkedColor;
    }

    // 노드에 달린 ConnectorSphere(입력/출력)를 포트별로 판단해 색을 맞춘다.
    // 구체가 배선돼 있지 않으면 InputPort/OutputPort 가 그냥 Transform 이라 아무 일도 없다.
    //
    // 예전에는 노드 하나의 In·Out 을 한꺼번에 켜고 껐다. 그래서 자식을 만드는 순간
    // "그 자식은 아직 비활성" → 부모의 In 까지 흰색으로 꺼지는 문제가 있었다
    // (부모가 PART/ALL 에 연결돼 있어도, 들어오는 선은 초록인데 구만 흰색).
    //
    // 규칙:
    //   In  — 이 노드로 들어오는 연결이 살아 있으면 초록.
    //         · PART/ALL 연결은 속성 최상위 노드의 In 으로 들어온다(RefreshPermanentLines 와 같은 규약).
    //           연결이 존재하는 것 자체가 "연결됨"이므로 활성 여부와 무관하게 켠다.
    //         · 부모→자식 연결은 그 자식이 활성일 때만 켠다.
    //   Out — 활성인 자식이 하나라도 있으면 초록.
    private void RefreshConnectorSpheres()
    {
        GraphData graph = _graphManager.GetGraphData();
        if (graph?.edges == null) return;

        HashSet<string> inActive = new HashSet<string>();
        HashSet<string> outActive = new HashSet<string>();

        foreach (EdgeData edge in graph.edges)
        {
            if (edge == null) continue;

            NodeData from = _graphManager.GetNode(edge.from_node_id);
            NodeData to = _graphManager.GetNode(edge.to_node_id);
            if (from == null || to == null) continue;
            if (from.NodeType != NodeType.PROPERTY) continue;

            // 속성 → PART/ALL (보드의 부품에 붙인 연결)
            if (to.NodeType == NodeType.PART)
            {
                inActive.Add(from.node_id);
                continue;
            }

            // 속성 → 속성 (부모→자식)
            if (to.NodeType != NodeType.PROPERTY) continue;
            if (!_graphManager.IsNodeActive(to.node_id)) continue;

            outActive.Add(from.node_id);
            inActive.Add(to.node_id);
        }

        foreach (NodeView node in
                 FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (node == null || string.IsNullOrEmpty(node.NodeId)) continue;
            ApplyConnector(node.InputPort, inActive.Contains(node.NodeId));
            ApplyConnector(node.OutputPort, outActive.Contains(node.NodeId));
        }
    }

    private static void ApplyConnector(Transform port, bool active)
    {
        if (port == null) return;
        ConnectorSphereView sphere = port.GetComponent<ConnectorSphereView>();
        if (sphere != null)
            sphere.SetGrabbed(active);
    }

    private static Transform FindNodeTransform(string nodeId)
    {
        foreach (NodeView node in
                 FindObjectsByType<NodeView>(
                     FindObjectsSortMode.None))
        {
            if (node != null && node.NodeId == nodeId)
                // PART/ALL 은 속성 서브그래프의 "위쪽"에 붙는다. 그래서 부모→자식 엣지와 똑같이
                // 속성 최상위 노드의 왼쪽 입력 포트(Port_In)로 들어와야 한다.
                // (Port_Out 을 쓰면 선이 노드를 가로질러 반대편으로 빠진다.)
                return node.InputPort != null ? node.InputPort : node.transform;
        }
        return null;
    }

    private static Transform FindPortTransform(string nodeId)
    {
        foreach (PartPort port in
                 FindObjectsByType<PartPort>(
                     FindObjectsSortMode.None))
        {
            if (port != null && port.NodeId == nodeId)
                return port.transform;
        }

        foreach (AllPort port in
                 FindObjectsByType<AllPort>(
                     FindObjectsSortMode.None))
        {
            if (port != null && port.NodeId == nodeId)
                return port.transform;
        }

        return null;
    }


    private void WireLocalChildAdd(
        NodeView node,
        RectTransform canvas)
    {
        if (node == null || canvas == null || _graphManager == null)
            return;

        NodeActionPanel panel =
            node.GetComponentInChildren<NodeActionPanel>(true);
        if (panel == null)
            return;

        Button addButton = null;
        Button[] buttons =
            panel.GetComponentsInChildren<Button>(true);
        foreach (Button candidate in buttons)
        {
            if (candidate == null)
                continue;

            if (candidate.gameObject.name.IndexOf(
                    "Add",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                addButton = candidate;
                break;
            }

            TMP_Text label =
                candidate.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text.Trim() == "+")
            {
                addButton = candidate;
                break;
            }
        }

        if (addButton == null)
            return;

        MvpLocalChildAddHandle handle =
            addButton.GetComponent<MvpLocalChildAddHandle>();
        if (handle == null)
            handle =
                addButton.gameObject.AddComponent<
                    MvpLocalChildAddHandle>();

        handle.Configure(
            addButton,
            panel,
            _graphManager,
            node.NodeId,
            node);
    }
}

// 아이디어 카드 아래의 전용 손잡이.
// PointableCanvas가 Meta 손 레이 입력을 PointerEventData로 변환하므로 마우스와 XR을 함께 처리한다.
[DisallowMultipleComponent]
public class MvpIdeaCardDragHandle : MonoBehaviour, IPointerClickHandler
{
    private MvpXrGraphLinkController _controller;
    private NodeView _node;
    private NodeActionPanel _panel;
    private float _nextClickTime;
    private Image _background;
    private TMP_Text _label;
    private Color _baseColor;
    private bool _visualsReady;
    private bool _isChild;   // true면 이 버튼은 '연결'이 아니라 자식 '활성화' 토글

    private static readonly System.Collections.Generic.Dictionary<string, Sprite>
        _spriteCache = new System.Collections.Generic.Dictionary<string, Sprite>();

    public void Configure(
        MvpXrGraphLinkController controller,
        NodeView node,
        NodeActionPanel panel)
    {
        if (controller != null)
        _controller = controller;
        if (node != null)
        _node = node;
        if (panel != null)
        _panel = panel;

        if (_panel != null)
        {
            Button button = GetComponent<Button>();
            if (button != null)
            button.onClick.RemoveListener(_panel.InvokeReference);
        }

        if (_visualsReady)
        return;

        _background = GetComponent<Image>();
        _label = GetComponentInChildren<TMP_Text>(true);
        if (_background != null)
        _baseColor = _background.color;

        // 라벨 글자는 스프라이트에 이미 들어 있다. 남아 있으면 겹쳐 보인다.
        if (_label != null)
        _label.gameObject.SetActive(false);

        _visualsReady = true;
    }

    // 디자이너 스프라이트(Assets/05_Design/JW/UI/Sprites/Buttons/*)를
    // Resources/GraphLink 로 복사해 둔 것을 이름으로 읽는다.
    private static Sprite LoadState(string key)
    {
        if (_spriteCache.TryGetValue(key, out Sprite cached))
        return cached;

        Sprite sprite = Resources.Load<Sprite>("GraphLink/" + key);
        _spriteCache[key] = sprite;
        if (sprite == null)
        Debug.LogWarning("[MVP 연결UI] 스프라이트를 찾지 못했습니다: GraphLink/" + key);
        return sprite;
    }

    private void ApplySprite(string key)
    {
        if (_background == null)
        return;

        Sprite sprite = LoadState(key);
        if (sprite == null)
        return;

        _background.sprite = sprite;
        _background.type = Image.Type.Simple;
        _background.preserveAspect = true;
        _background.color = Color.white;
    }

    public void SetConnectionSelected(bool selected)
    {
        if (!_visualsReady)
        Configure(_controller, _node, _panel);

        // 선택 중이면 Disconnect(다시 누르면 취소), 평소엔 Connect.
        // 어디를 눌러야 하는지는 맥동하는 부품 포트가 안내한다.
        ApplySprite(selected ? "disconnect_default" : "connect_default");
    }

    /// <summary>서버 왕복을 기다리는 동안 Loading 스프라이트를 보여준다.</summary>
    public void SetBusy(bool busy, bool selected)
    {
        if (!_visualsReady)
        Configure(_controller, _node, _panel);

        if (busy)
        {
            ApplySprite(_isChild
                ? "activate_loading"
                : (selected ? "disconnect_loading" : "connect_loading"));
            return;
        }

        if (_isChild)
        RefreshChildLabel(selected);
        else
        SetConnectionSelected(selected);
    }

    // 이 버튼이 자식 '활성화' 토글인지 지정.
    public void SetChildMode(bool isChild)
    {
        _isChild = isChild;
    }

    // 자식 버튼 라벨/색을 활성 상태에 맞춘다. (활성=끄기/민트, 비활성=활성화/기본)
    public void RefreshChildLabel(bool active)
    {
        if (!_visualsReady)
        Configure(_controller, _node, _panel);

        // 활성 상태면 Deactivate(다시 누르면 끔), 아니면 Activate.
        ApplySprite(active ? "deactivate_default" : "activate_default");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null &&
        eventData.button != PointerEventData.InputButton.Left)
        return;
        if (Time.unscaledTime < _nextClickTime)
        return;
        _nextClickTime = Time.unscaledTime + 0.2f;

        if (_node == null)
        return;

        // 자식은 활성화 토글, 최상위는 부품 연결 선택.
        if (_isChild)
        _controller?.ToggleChildActive(_node);
        else
        _controller?.ToggleConnectionSelection(this, _node);
    }


    private void OnDisable()
    {
        if (!_isChild)
        SetConnectionSelected(false);
    }
}

// 연결 후보 부품의 색상과 크기 피드백을 원래 상태를 보존한 채 적용한다.
[DisallowMultipleComponent]
public class MvpPartDropTargetFeedback :
    MonoBehaviour, IPointerClickHandler
{
    private Graphic _graphic;
    private MvpXrGraphLinkController _controller;
    private Color _baseColor;
    private Vector3 _baseScale;
    private bool _initialized;
    private bool _pulse;          // 연결 선택 중 "여기 눌러" 맥동 여부
    private Color _pulseColor;    // 맥동 기준 색

    public string PortId { get; private set; }

    public void Configure(
        MvpXrGraphLinkController controller,
        string portId)
    {
        if (controller != null)
        _controller = controller;
        PortId = portId;
        if (_initialized)
        return;

        _graphic = GetComponent<Graphic>();
        if (_graphic == null)
        _graphic = GetComponentInChildren<Graphic>(true);

        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic != null)
            graphic.raycastTarget = true;
        }

        if (_graphic != null)
        _baseColor = _graphic.color;
        _baseScale = transform.localScale;
        _initialized = true;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("[MVPconnect] 부품포트 OnPointerClick port=" + PortId);
        if (eventData != null &&
            eventData.button != PointerEventData.InputButton.Left)
            return;

        _controller?.TryConnectSelectedPort(PortId);
    }


    public void SetState(
        bool eligible,
        bool hovered,
        bool invalid)
    {
        if (!_initialized)
            Configure(_controller, PortId);

        // 노드가 연결 선택된 상태에서 '연결 가능한' 부품이면 맥동 애니메이션으로 "여기 눌러" 안내.
        // 손을 올린(hover) 상태나 불가(invalid)면 정적 표시. 연결/취소되면 ClearState 로 꺼진다.
        _pulse = eligible && !hovered && !invalid;

        Color target = _baseColor;
        if (invalid)
            target = MvpStudentUiFactory.Coral;
        else if (hovered)
            target = MvpStudentUiFactory.Mint;
        else if (eligible)
            target = Color.Lerp(
                _baseColor,
                MvpStudentUiFactory.HoloCyan,
                0.42f);
        _pulseColor = target;

        if (!_pulse)
        {
            if (_graphic != null)
            {
                target.a = _baseColor.a;
                _graphic.color = target;
            }
            transform.localScale =
                _baseScale * (hovered ? 1.08f : 1f);
        }
    }

    private void Update()
    {
        if (!_initialized || !_pulse)
            return;

        float wave = (Mathf.Sin(Time.unscaledTime * 6.5f) + 1f) * 0.5f; // 0..1
        transform.localScale = _baseScale * (1f + 0.18f * wave);
        if (_graphic != null)
        {
            Color glow = Color.Lerp(
                _pulseColor, MvpStudentUiFactory.HoloCyan, wave * 0.75f);
            glow.a = _baseColor.a;
            _graphic.color = glow;
        }
    }

    public void ClearState()
    {
        if (!_initialized)
            return;

        _pulse = false;
        if (_graphic != null)
            _graphic.color = _baseColor;
        transform.localScale = _baseScale;
    }
}

