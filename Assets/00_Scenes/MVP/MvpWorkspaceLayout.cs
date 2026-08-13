using System;
using System.Collections.Generic;
using UnityEngine;

// MVP 전용 작업공간 배치.
// 중앙 스케치를 시각적 중심으로 두고 PROPERTY root를 파트 순서에 맞춰 위쪽 아치에 배치한다.
// GraphData 변경은 반드시 GraphManager.Request* 인터페이스를 통해 수행한다.
[DefaultExecutionOrder(500)]
public class MvpWorkspaceLayout : MonoBehaviour
{
    private const float RecommendedNodeViewScale = 0.09f;
    private const float MinimumInteractiveNodeScale = 0.09f;

    private const float MinimumReadableArcBaseHeight = 0.34f;
    private const float MaximumReadableArcBaseHeight = 0.42f;
    private const float MaximumInteractiveNodeScale = 0.14f;

    [Header("씬 참조")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private Transform _mainSketchPanel;
    [SerializeField] private Camera _camera;

    [Header("중앙 스케치")]
    [SerializeField] private float _viewDistance = 1.55f;
    [SerializeField] private float _panelHorizontalOffset;
    [SerializeField] private float _panelVerticalOffset = -0.10f;
    [SerializeField] private float _panelScale = 0.0009f;
    [SerializeField] private bool _applyErgonomicProfileAtRuntime = true;

    [Header("속성 노드")]
    [SerializeField] private float _nodeViewScale = RecommendedNodeViewScale;
    [SerializeField] private float _nodePlaneOffset = -0.08f;
    [SerializeField] private float _arcBaseHeight = 0.82f;
    [SerializeField] private float _arcCrownHeight = 0.18f;
    [SerializeField] private float _rootSpacing = 0.58f;
    [SerializeField] private float _maxArcHalfWidth = 1.5f;
    [SerializeField] private int _maxRootsPerRow = 5;
    [SerializeField] private float _rowGap = 0.42f;

    [Header("레퍼런스 영역")]
    [SerializeField] private float _referenceSideOffset = 1.55f;
    [SerializeField] private float _referenceBaseHeight = 0f;
    [SerializeField] private float _referenceRowGap = 0.35f;

    [Header("동작")]
    [SerializeField] private bool _arrangeOnGraphChanged = true;

    private int _lastAppliedFrame = -1;
    private bool _isApplying;
    private int _lastNodeCount = -1;

    private bool _workspacePoseInitialized;
    private float _workspaceScaleMultiplier = 1f;
    private int _lastEdgeCount = -1;
    private float _nextNodeFaceTime;
    private bool _onDesk;   // 설계 보드(+노드)를 책상 위에 평평하게 눕힌 상태

    // 도크가 따라붙을 중앙 보드.
    public Transform MainSketchPanel => _mainSketchPanel;

    private void OnValidate()
    {
        _nodeViewScale = Mathf.Clamp(
            _nodeViewScale,
            MinimumInteractiveNodeScale,
            MaximumInteractiveNodeScale);
        _rootSpacing = Mathf.Max(0.45f, _rootSpacing);
        _maxArcHalfWidth = Mathf.Max(_rootSpacing, _maxArcHalfWidth);
        _maxRootsPerRow = Mathf.Max(1, _maxRootsPerRow);
        _arcBaseHeight = Mathf.Clamp(
            _arcBaseHeight,
            MinimumReadableArcBaseHeight,
            MaximumReadableArcBaseHeight);
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void Start()
    {
        if (_applyErgonomicProfileAtRuntime)
            ApplyRecommendedLayoutProfile();
        ResolveActiveCamera();
        _workspacePoseInitialized = false;
        ArrangeWorkspace();
    }

    private void LateUpdate()
    {
        Camera activeCamera = FindActiveViewCamera();
        if (activeCamera != null && activeCamera != _camera)
        {
            _camera = activeCamera;
            if (!_onDesk)   // 책상 모드에선 유저 앞으로 재정렬하지 않는다.
            {
                _workspacePoseInitialized = false;
                PositionMainSketchPanel();
            }
        }

        // 패널·노드가 항상 "현재" 유저(카메라)를 향하도록 회전을 갱신한다(위치는 유지).
        // 패널 방향이 Start 시점 1회로만 굳으면, 유저가 자리로 이동/착석한 뒤 패널이 유저를
        // 등지고(포크·읽기 반전) 남는다. 배치 모드(자동/수동)와도 무관하게 매 스로틀마다 맞춘다.
        if (_mainSketchPanel != null &&
            Time.unscaledTime >= _nextNodeFaceTime)
        {
            _nextNodeFaceTime = Time.unscaledTime + 0.2f;

            // 책상 모드가 아니면 패널을 현재 유저 방향으로 갱신(책상 모드면 눕힌 자세 유지).
            if (!_onDesk && _camera != null && _camera.isActiveAndEnabled)
            {
                Vector3 away = Vector3.ProjectOnPlane(
                    _mainSketchPanel.position - _camera.transform.position,
                    Vector3.up);
                if (away.sqrMagnitude > 0.0001f)
                    _mainSketchPanel.rotation =
                        Quaternion.LookRotation(away.normalized, Vector3.up);
            }

            // 노드는 책상 모드와 무관하게 '항상 같은 자세'(유저를 향해 세워둠)로 유지한다.
            // 패널이 책상에 누워도 노드까지 눕지 않게, 노드 전용 업라이트 회전을 쓴다.
            Quaternion nodeRot = _mainSketchPanel.rotation;
            if (_onDesk && _camera != null && _camera.isActiveAndEnabled)
            {
                Vector3 nAway = Vector3.ProjectOnPlane(
                    _mainSketchPanel.position - _camera.transform.position,
                    Vector3.up);
                if (nAway.sqrMagnitude > 0.0001f)
                    nodeRot = Quaternion.LookRotation(nAway.normalized, Vector3.up);
            }

            foreach (NodeView view in
                     FindObjectsByType<NodeView>(FindObjectsSortMode.None))
                if (view != null)
                    view.transform.rotation = nodeRot;

            // 보드가 방금 돌아갔을 수 있다 — 다음 노드 생성이 옛 축으로 배치되지 않게 갱신한다.
            ApplyLayoutBasis();
        }

        if (!_arrangeOnGraphChanged || _graphManager == null) return;

        GraphData graph = _graphManager.GetGraphData();
        int nodeCount = graph?.nodes?.Count ?? 0;
        int edgeCount = graph?.edges?.Count ?? 0;
        if (nodeCount != _lastNodeCount || edgeCount != _lastEdgeCount)
            ArrangeWorkspace();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_graphManager != null)
            _graphManager.OnGraphChanged += HandleGraphChanged;
    }

    private void Unsubscribe()
    {
        if (_graphManager != null)
            _graphManager.OnGraphChanged -= HandleGraphChanged;
    }

    private void HandleGraphChanged()
    {
        if (_arrangeOnGraphChanged)
            ArrangeWorkspace();
    }
    public void SetSpatialPlacementMode(bool enabled)
    {
        _arrangeOnGraphChanged = !enabled;
    }


    [ContextMenu("Arrange MVP Workspace")]
    public void ArrangeWorkspace()
    {
        if (_isApplying || _lastAppliedFrame == Time.frameCount) return;

        ResolveReferences();
        PositionMainSketchPanel();

        GraphData graph = _graphManager != null ? _graphManager.GetGraphData() : null;
        RememberGraphShape(graph);
        if (graph?.nodes == null || graph.nodes.Count == 0)
        {
            Debug.Log("[MVP Layout] 중앙 스케치만 배치했습니다. 그래프 로드 후 노드를 정렬합니다.");
            return;
        }

        _isApplying = true;
        _lastAppliedFrame = Time.frameCount;

        var nodeById = new Dictionary<string, NodeData>();
        var partOrder = new Dictionary<string, int>();
        var propertyChildren = new HashSet<string>();
        var propertyChildrenById = new Dictionary<string, List<string>>();

        int nextPartOrder = 0;
        foreach (NodeData node in graph.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.node_id)) continue;
            nodeById[node.node_id] = node;
            if (node.NodeType == NodeType.PART && !node.is_global)
                partOrder[node.node_id] = nextPartOrder++;
        }

        if (graph.edges != null)
        {
            foreach (EdgeData edge in graph.edges)
            {
                if (edge == null) continue;
                if (!nodeById.TryGetValue(edge.from_node_id, out NodeData fromNode) ||
                    !nodeById.TryGetValue(edge.to_node_id, out NodeData toNode))
                    continue;
                if (fromNode.NodeType == NodeType.PROPERTY && toNode.NodeType == NodeType.PROPERTY)
                {
                    propertyChildren.Add(toNode.node_id);
                    if (!propertyChildrenById.TryGetValue(fromNode.node_id, out List<string> children))
                    {
                        children = new List<string>();
                        propertyChildrenById[fromNode.node_id] = children;
                    }
                    children.Add(toNode.node_id);
                }
            }
        }

        var partRoots = new List<NodeData>();
        var referenceRoots = new List<NodeData>();

        foreach (NodeData node in graph.nodes)
        {
            if (node == null || node.NodeType != NodeType.PROPERTY) continue;
            if (propertyChildren.Contains(node.node_id)) continue;

            NodeData parent = null;
            if (!string.IsNullOrEmpty(node.parent_node_id))
                nodeById.TryGetValue(node.parent_node_id, out parent);

            if (parent != null && parent.NodeType == NodeType.REFERENCE)
                referenceRoots.Add(node);
            else
                partRoots.Add(node);
        }

        partRoots.Sort((a, b) => ComparePartRoots(a, b, partOrder));
        referenceRoots.Sort((a, b) =>
            string.Compare(a.DisplayText, b.DisplayText, StringComparison.Ordinal));

        ArrangePartRoots(partRoots, propertyChildrenById);
        ArrangeReferenceRoots(referenceRoots);

        // 루트를 보드 평면(panel.right/up) 위에 놓았으니 자식도 같은 평면을 따라야 한다.
        // 안 맞추면 보드가 유저를 향해 돌아간 각도만큼 자식이 z 로 어긋난다.
        ApplyLayoutBasis();

        _graphManager.ReflowAllSubtrees();
        ScaleNodeViews();

        Debug.Log($"[MVP Layout] 작업공간 정렬 완료: 중앙 스케치 1, PART 속성 root {partRoots.Count}, REFERENCE root {referenceRoots.Count}");
        _isApplying = false;
    }

    [ContextMenu("Preview Main Sketch Position")]
    public void ApplyPanelPreview()
    {
        ResolveReferences();
        _workspacePoseInitialized = false;
        PositionMainSketchPanel();
    }

    // 기존 MVP 씬에 직렬화된 과거 값을 권장 가독성/조작성 프로필로 교체한다.
    // 에디터 메뉴에서 호출하며 GraphData는 변경하지 않는다.
    public void ApplyRecommendedLayoutProfile()
    {
        // 중앙 설계판은 관찰 거리, 속성 노드는 그 앞의 조작 거리로 분리한다.
        _viewDistance = 1.55f;
        _panelHorizontalOffset = 0f;
        _panelVerticalOffset = -0.10f;
        _panelScale = 0.0009f;

        _nodeViewScale = RecommendedNodeViewScale;
        _nodePlaneOffset = -0.16f;
        _arcBaseHeight = MaximumReadableArcBaseHeight;
        _arcCrownHeight = 0.04f;
        _rootSpacing = 0.60f;
        _maxArcHalfWidth = 1.15f;
        _maxRootsPerRow = 4;
        _rowGap = 0.25f;

        _referenceSideOffset = 0.88f;
        _referenceBaseHeight = 0.00f;
        _referenceRowGap = 0.28f;
    }

    private void RememberGraphShape(GraphData graph)
    {
        _lastNodeCount = graph?.nodes?.Count ?? 0;
        _lastEdgeCount = graph?.edges?.Count ?? 0;
    }

    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
        ResolveActiveCamera();
        if (_mainSketchPanel == null)
        {
            MainSketchView view = FindFirstObjectByType<MainSketchView>();
            Canvas canvas = view != null ? view.GetComponentInParent<Canvas>() : null;
            if (canvas != null) _mainSketchPanel = canvas.transform;
        }
    }

    private void PositionMainSketchPanel()
    {
        if (_workspacePoseInitialized ||
            _mainSketchPanel == null)
            return;

        if (TryGetMeetingTable(
                out Transform table,
                out Bounds tableBounds))
        {
            _mainSketchPanel.position =
                tableBounds.center +
                Vector3.up * (tableBounds.extents.y + 0.62f);

            // 유저(카메라)를 향하게 배치한다. 캔버스는 +Z 가 유저 반대편을 향할 때 읽기가 정상이고
            // 포크도 유저 쪽에서 눌린다(안내 패널 MvpXrCanvasAnchor 와 동일 규약).
            // 기존엔 table.forward 를 썼는데, 좌석이 테이블 +Z 쪽이라 패널 +Z 가 유저를 정면으로
            // 향해(반전) 뒷면을 보고 바깥에서 눌러야 했다. (포크면/키보드 반전 원인)
            Vector3 forward = Vector3.ProjectOnPlane(table.forward, Vector3.up);
            if (_camera != null && _camera.isActiveAndEnabled)
            {
                Vector3 away = Vector3.ProjectOnPlane(
                    _mainSketchPanel.position - _camera.transform.position,
                    Vector3.up);
                if (away.sqrMagnitude > 0.0001f)
                    forward = away;
            }
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            _mainSketchPanel.rotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }
        else
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(
                _camera.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = _camera.transform.forward;
            forward.Normalize();
            Vector3 right =
                Vector3.Cross(Vector3.up, forward).normalized;

            _mainSketchPanel.position =
                _camera.transform.position +
                forward * _viewDistance +
                right * _panelHorizontalOffset +
                Vector3.up * _panelVerticalOffset;
            _mainSketchPanel.rotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }

        _mainSketchPanel.localScale =
            Vector3.one * (_panelScale * _workspaceScaleMultiplier);
        _workspacePoseInitialized = true;
    }

    private bool TryGetMeetingTable(
        out Transform table,
        out Bounds tableBounds)
    {
        table = null;
        tableBounds = default;
        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                candidate.gameObject.scene !=
                    _mainSketchPanel.gameObject.scene ||
                candidate.name != "Table_01")
                continue;

            table = candidate;
            break;
        }

        if (table == null)
            return false;

        Collider collider =
            table.GetComponentInChildren<Collider>(true);
        if (collider != null)
        {
            tableBounds = collider.bounds;
            return true;
        }

        Renderer[] renderers =
            table.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (!found)
            {
                tableBounds = renderer.bounds;
                found = true;
            }
            else
            {
                tableBounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private int ComparePartRoots(
        NodeData a,
        NodeData b,
        Dictionary<string, int> partOrder)
    {
        int orderA = ResolvePartOrder(a, partOrder);
        int orderB = ResolvePartOrder(b, partOrder);
        int orderCompare = orderA.CompareTo(orderB);
        if (orderCompare != 0) return orderCompare;
        return string.Compare(a.DisplayText, b.DisplayText, StringComparison.Ordinal);
    }

    private static int ResolvePartOrder(NodeData node, Dictionary<string, int> partOrder)
    {
        return node != null &&
               !string.IsNullOrEmpty(node.parent_node_id) &&
               partOrder.TryGetValue(node.parent_node_id, out int order)
            ? order
            : int.MaxValue;
    }

    private void ArrangePartRoots(
        List<NodeData> roots,
        Dictionary<string, List<string>> childrenById)
    {
        if (roots.Count == 0) return;

        const float designerNodeWidth = 3.98f;
        float nodeWidth = Mathf.Max(0.2f, designerNodeWidth * _nodeViewScale);
        float clusterGap = Mathf.Max(0.1f, _rootSpacing - nodeWidth);
        float depthStep = _graphManager != null
            ? _graphManager.LayoutHSpacing
            : 0.75f;
        float maxRowWidth = Mathf.Max(nodeWidth, _maxArcHalfWidth * 2f);

        var widthByRootId = new Dictionary<string, float>();
        foreach (NodeData root in roots)
        {
            int depth = CalculateMaxPropertyDepth(
                root.node_id,
                childrenById,
                new HashSet<string>());
            widthByRootId[root.node_id] = nodeWidth + depth * depthStep;
        }

        var rows = new List<List<NodeData>>();
        var currentRow = new List<NodeData>();
        float currentWidth = 0f;

        foreach (NodeData root in roots)
        {
            float clusterWidth = widthByRootId[root.node_id];
            float nextWidth = currentRow.Count == 0
                ? clusterWidth
                : currentWidth + clusterGap + clusterWidth;
            bool rowIsFull = currentRow.Count >= Mathf.Max(1, _maxRootsPerRow);
            bool rowWouldOverflow =
                currentRow.Count > 0 && nextWidth > maxRowWidth;

            if (rowIsFull || rowWouldOverflow)
            {
                rows.Add(currentRow);
                currentRow = new List<NodeData>();
                currentWidth = 0f;
                nextWidth = clusterWidth;
            }

            currentRow.Add(root);
            currentWidth = nextWidth;
        }

        if (currentRow.Count > 0)
            rows.Add(currentRow);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            List<NodeData> row = rows[rowIndex];
            float totalWidth = 0f;
            foreach (NodeData root in row)
                totalWidth += widthByRootId[root.node_id];
            totalWidth += clusterGap * Mathf.Max(0, row.Count - 1);

            float cursor = -totalWidth * 0.5f;
            foreach (NodeData root in row)
            {
                float clusterWidth = widthByRootId[root.node_id];
                float rootX = cursor + nodeWidth * 0.5f;
                float arc = Mathf.Clamp(
                    rootX / Mathf.Max(0.01f, _maxArcHalfWidth),
                    -1f,
                    1f);
                float y =
                    Mathf.Clamp(
                        _arcBaseHeight,
                        MinimumReadableArcBaseHeight,
                        MaximumReadableArcBaseHeight) +
                    _arcCrownHeight * (1f - arc * arc) +
                    rowIndex * _rowGap;

                MoveRoot(root, rootX, y);
                cursor += clusterWidth + clusterGap;
            }
        }
    }

    private static int CalculateMaxPropertyDepth(
        string nodeId,
        Dictionary<string, List<string>> childrenById,
        HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(nodeId) || !visited.Add(nodeId))
            return 0;

        int maxDepth = 0;
        if (childrenById.TryGetValue(nodeId, out List<string> children))
        {
            foreach (string childId in children)
            {
                int childDepth =
                    1 + CalculateMaxPropertyDepth(childId, childrenById, visited);
                maxDepth = Mathf.Max(maxDepth, childDepth);
            }
        }

        visited.Remove(nodeId);
        return maxDepth;
    }

    private void ArrangeReferenceRoots(List<NodeData> roots)
    {
        for (int index = 0; index < roots.Count; index++)
        {
            float side = index % 2 == 0 ? -1f : 1f;
            int row = index / 2;
            float x = side * _referenceSideOffset;
            float y = _referenceBaseHeight - row * _referenceRowGap;
            MoveRoot(roots[index], x, y);
        }
    }

    // 하위 노드 배치가 쓸 좌표축을 보드 평면에 맞춘다.
    // 보드는 LateUpdate 에서 유저를 향해 계속 회전하므로, Reflow 직전뿐 아니라
    // 회전을 갱신한 뒤에도 함께 갱신해야 다음 노드 생성이 옛 축으로 배치되지 않는다.
    private void ApplyLayoutBasis()
    {
        if (_graphManager == null || _mainSketchPanel == null) return;
        _graphManager.SetLayoutBasis(
            _mainSketchPanel.right,
            _mainSketchPanel.up);
    }

    private void MoveRoot(NodeData root, float localX, float localY)
    {
        if (root == null || _mainSketchPanel == null) return;

        Vector3 target =
            _mainSketchPanel.position +
            _mainSketchPanel.right * localX +
            _mainSketchPanel.up * localY +
            _mainSketchPanel.forward * _nodePlaneOffset;

        _graphManager.RequestMoveNode(root.node_id, target);
    }

    private void ScaleNodeViews()
    {
        foreach (NodeView view in FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (view != null)
            {
                view.transform.localScale = Vector3.one * _nodeViewScale;
                if (_mainSketchPanel != null)
                    view.transform.rotation = _mainSketchPanel.rotation;
            }
        }
    }


    public Vector3 GetSuggestedRootPosition()
    {
        ResolveReferences();
        if (_mainSketchPanel == null)
        {
            ResolveActiveCamera();
            return _camera != null
            ? _camera.transform.position +
              _camera.transform.forward * 1.0f +
              Vector3.up * 0.18f
            : new Vector3(0f, 1.35f, 0.8f);
        }

        Vector2[] slots =
        {
            new Vector2(-0.90f, 0.44f),
            new Vector2(-0.30f, 0.44f),
            new Vector2(0.30f, 0.44f),
            new Vector2(0.90f, 0.44f),
            new Vector2(-0.90f, 0.72f),
            new Vector2(-0.30f, 0.72f),
            new Vector2(0.30f, 0.72f),
            new Vector2(0.90f, 0.72f)
        };

        List<NodeData> nodes = _graphManager != null
        ? _graphManager.GetAllNodes()
        : null;
        foreach (Vector2 slot in slots)
        {
            Vector3 candidate =
            _mainSketchPanel.position +
            _mainSketchPanel.right * slot.x +
            _mainSketchPanel.up * slot.y +
            _mainSketchPanel.forward * _nodePlaneOffset;
            bool occupied = false;

            if (nodes != null)
            {
                foreach (NodeData node in nodes)
                {
                    if (node == null ||
                    node.NodeType != NodeType.PROPERTY ||
                    node.Position.sqrMagnitude < 0.0001f)
                    continue;
                    if (Vector3.Distance(node.Position, candidate) < 0.46f)
                    {
                        occupied = true;
                        break;
                    }
                }
            }

            if (!occupied)
            return candidate;
        }

        int propertyCount = 0;
        if (nodes != null)
        foreach (NodeData node in nodes)
            if (node != null && node.NodeType == NodeType.PROPERTY)
                propertyCount++;

        float extraX = ((propertyCount % 4) - 1.5f) * 0.60f;
        float extraY = 0.98f + (propertyCount / 4) * 0.28f;
        return _mainSketchPanel.position +
           _mainSketchPanel.right * extraX +
           _mainSketchPanel.up * extraY +
           _mainSketchPanel.forward * _nodePlaneOffset;
    }

    public void RecenterWorkspaceToActiveView()
    {
        ResolveActiveCamera();
        _workspacePoseInitialized = false;
        PositionMainSketchPanel();
        ArrangeWorkspace();
        // 정렬은 1회성이다. 자동 정렬 모드를 켠 채 두면(기존 버그) 이후 제스처/음성으로 만든
        // 공간 노드가 매 프레임 arc 로 스냅돼 사용자의 수동 배치가 사라진다. 수동 모드로 되돌린다.
        _arrangeOnGraphChanged = false;
    }

    public void SetWorkspaceScaleMultiplier(float multiplier)
    {
        _workspaceScaleMultiplier = Mathf.Clamp(multiplier, 0.9f, 1.2f);
        if (_mainSketchPanel != null)
            _mainSketchPanel.localScale =
                Vector3.one * (_panelScale * _workspaceScaleMultiplier);
    }

    private void ResolveActiveCamera()
    {
        Camera activeCamera = FindActiveViewCamera();
        if (activeCamera != null)
            _camera = activeCamera;
    }

    private static Camera FindActiveViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.isActiveAndEnabled &&
                candidate.CompareTag("MainCamera"))
                return candidate;
        }

        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.isActiveAndEnabled)
                return candidate;
        }
        return null;
    }
}
