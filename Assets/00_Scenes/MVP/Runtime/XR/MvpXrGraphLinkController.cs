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
    [SerializeField] private Color _linkedColor =
        new Color(0.27f, 0.92f, 0.63f, 0.95f);

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
            bool active = _graphManager.IsNodeActive(ev.ToNodeId);
            ev.SetLineColor(_linkedColor);
            ev.SetLineVisible(active);
        }

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
        foreach (NodeView node in
             FindObjectsByType<NodeView>(
                 FindObjectsInactive.Include,
                 FindObjectsSortMode.None))
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
            // MVP에서는 숨기고, 아래의 단일 '연결' 점만 사용한다.
            Transform referenceButton = canvas.Find("ReferenceButton");
            if (referenceButton != null && referenceButton.gameObject.activeSelf)
            referenceButton.gameObject.SetActive(false);

            Transform existing = canvas.Find("MvpNodeLinkPort");
            Image linkPoint = existing != null
            ? existing.GetComponent<Image>()
            : null;
            if (linkPoint == null)
            {
                linkPoint = MvpStudentUiFactory.CreatePanel(
                canvas,
                "MvpNodeLinkPort",
                new Vector2(0f, -1.65f),
                new Vector2(170f, 48f),
                // 노드 본체가 반투명 유리 재질(ShaderGraph Transparent)로 바뀌어,
                // 불투명 진청록 패널이 노드 앞에 떠 보였다. 노드와 같은 밝은 반투명
                // 톤으로 맞춰 한 덩어리로 읽히게 한다.
                new Color(0.72f, 0.80f, 0.92f, 0.42f),
                true);
                linkPoint.raycastTarget = true;

                MvpStudentUiFactory.CreateText(
                linkPoint.transform,
                "MvpLinkPortLabel",
                "연결",
                Vector2.zero,
                new Vector2(150f, 38f),
                16f,
                TextAlignmentOptions.Center,
                false,
                // 배경이 밝아졌으므로 흰 글씨는 읽히지 않는다. 노드 라벨과 같은 어두운 톤.
                new Color(0.13f, 0.17f, 0.24f, 1f),
                1);
            }

            linkPoint.raycastTarget = true;

            // 노드 Canvas 자식은 0.02 스케일 규약을 따른다. CreatePanel 은 localScale=1 로 만들어
            // "연결" 패널이 노드보다 ~50배 크게 보이므로(이슈 1), 형제(라벨/버튼) 스케일에 맞춰 축소한다.
            Transform linkSibling =
            canvas.Find("LabelInputField") ?? canvas.Find("AddButton");
            float linkChildScale =
            linkSibling != null ? linkSibling.localScale.x : 0.02f;
            linkPoint.rectTransform.localScale = Vector3.one * linkChildScale;

            NodeActionPanel panel =
            node.GetComponentInChildren<NodeActionPanel>(true);
            MvpIdeaCardDragHandle handle =
            linkPoint.GetComponent<MvpIdeaCardDragHandle>();
            if (handle == null)
            handle = linkPoint.gameObject.AddComponent<MvpIdeaCardDragHandle>();
            handle.Configure(this, node, panel);
            // 자식 PROPERTY(부모가 있는 노드)는 부품 '연결'이 아니라 '활성화' 토글이다.
            // 최상위(부모 없는) 노드만 부품에 연결한다.
            bool isChild = _graphManager != null &&
            !string.IsNullOrEmpty(
                _graphManager.GetPropertyParentId(node.NodeId));
            handle.SetChildMode(isChild);
            if (isChild)
            handle.RefreshChildLabel(
                _graphManager.IsNodeActive(node.NodeId));
            else
            handle.SetConnectionSelected(
                _selectedLinkNodeId == node.NodeId);
        }
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
                nodeMap[nv.NodeId] = nv.transform;

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
        }
    }

    private static Transform FindNodeTransform(string nodeId)
    {
        foreach (NodeView node in
                 FindObjectsByType<NodeView>(
                     FindObjectsSortMode.None))
        {
            if (node != null && node.NodeId == nodeId)
                return node.transform;
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
        _visualsReady = true;
    }

    public void SetConnectionSelected(bool selected)
    {
        if (!_visualsReady)
        Configure(_controller, _node, _panel);

        if (_background != null)
        _background.color = selected
            ? MvpStudentUiFactory.Mint
            : _baseColor;
        if (_label != null)
        {
            _label.fontSize = 16f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            // 선택 중이면 "취소"(다시 누르면 취소), 평소엔 "연결". 어디를 눌러야 하는지는
            // 맥동하는 부품 포트가 안내한다.
            _label.text = selected ? "취소" : "연결";
        }
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

        if (_background != null)
        _background.color = active ? MvpStudentUiFactory.Mint : _baseColor;
        if (_label != null)
        {
            _label.fontSize = 16f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.text = active ? "끄기" : "활성화";
        }
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

