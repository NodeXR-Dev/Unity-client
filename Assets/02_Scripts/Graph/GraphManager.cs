/*
 * 파일명: GraphManager.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: NodeXR 노드그래프의 데이터 등록, 조회, 삭제 및 연결 규칙 검사를 담당한다.
 * 핵심 내용:
 * - Singleton을 사용하지 않는 일반 MonoBehaviour이다.
 * - RenderGraph()로 NodeView/EdgeView 프리팹을 Instantiate해 렌더링한다.
 * - 서버 통신은 포함하지 않는다.
 * - 엣지 타입은 사용하지 않으며, from/to 노드 타입 조합으로 연결 관계를 해석한다.
 *   허용 조합: PROPERTY→PROPERTY, PROPERTY→PART, REFERENCE→PROPERTY, REFERENCE→PART
 * - Request*() 메서드는 데이터 조작만 수행한다. 개별 View 갱신은 추후 단계에서 추가.
 */
using System;
using System.Collections.Generic;
using UnityEngine;

public class GraphManager : MonoBehaviour
{
    private GraphData _graphData;
    private NodeRegistry _nodeRegistry;
    private EdgeRegistry _edgeRegistry;

    // ─────────────────────────────────────────────
    // 렌더링
    // ─────────────────────────────────────────────
    [Header("렌더링")]
    [SerializeField] private Transform _graphRoot;    // 생성된 NodeView/EdgeView의 부모 Transform
    [SerializeField] private NodeView _nodePrefab;    // NodeView가 붙은 노드 프리팹
    [SerializeField] private EdgeView _edgePrefab;    // EdgeView가 붙은 엣지 프리팹

    // node_id → NodeView 매핑. SpawnEdgeView에서 from/to NodeView를 찾는 데 사용.
    private Dictionary<string, NodeView> _nodeViewMap;

    private void Awake()
    {
        _nodeRegistry = new NodeRegistry();
        _edgeRegistry = new EdgeRegistry();
        _graphData = new GraphData();
        _nodeViewMap = new Dictionary<string, NodeView>();
    }

    // ─────────────────────────────────────────────
    // Graph 생명주기
    // ─────────────────────────────────────────────

    // 서버 응답 GraphData를 반영한다.
    // _graphData를 직접 교체하고, registry를 새로 등록한다.
    // graphData.nodes/edges 리스트에 재추가하지 않도록 주의.
    public void LoadGraph(GraphData graphData)
    {
        _nodeRegistry.Clear();
        _edgeRegistry.Clear();

        if (graphData == null)
        {
            Debug.LogWarning("[GraphManager] LoadGraph 실패: graphData가 null입니다. 빈 그래프로 초기화합니다.");
            _graphData = new GraphData();
            return;
        }

        _graphData = graphData;

        // nodes/edges가 null이면 빈 리스트로 초기화하여 이후 AddNode/AddEdge에서 null reference 방지
        if (_graphData.nodes == null) _graphData.nodes = new List<NodeData>();
        if (_graphData.edges == null) _graphData.edges = new List<EdgeData>();

        foreach (var node in _graphData.nodes)
            _nodeRegistry.Register(node);

        foreach (var edge in _graphData.edges)
            _edgeRegistry.Register(edge);

    }

    // 현재 GraphData를 반환한다 (읽기 전용 접근용).
    public GraphData GetGraphData() => _graphData;

    // ─────────────────────────────────────────────
    // 렌더링
    // ─────────────────────────────────────────────

    // 현재 GraphData를 기반으로 NodeView/EdgeView를 생성한다.
    // LoadGraph() 이후 명시적으로 호출해야 한다.
    public void RenderGraph()
    {
        if (_graphRoot == null)
        {
            Debug.LogWarning("[GraphManager] RenderGraph 실패: _graphRoot가 연결되지 않았습니다.");
            return;
        }
        if (_nodePrefab == null)
        {
            Debug.LogWarning("[GraphManager] RenderGraph 실패: _nodePrefab이 연결되지 않았습니다.");
            return;
        }
        if (_edgePrefab == null)
        {
            Debug.LogWarning("[GraphManager] RenderGraph 실패: _edgePrefab이 연결되지 않았습니다.");
            return;
        }

        ClearGraphView();

        if (_graphData == null || _graphData.nodes == null || _graphData.edges == null)
        {
            Debug.LogWarning("[GraphManager] RenderGraph: GraphData 또는 nodes/edges가 null입니다.");
            return;
        }

        foreach (var node in _graphData.nodes)
            SpawnNodeView(node);

        foreach (var edge in _graphData.edges)
            SpawnEdgeView(edge);
    }

    // 씬에 생성된 모든 NodeView/EdgeView를 삭제하고 매핑을 초기화한다.
    public void ClearGraphView()
    {
        if (_graphRoot == null) return;

        for (int i = _graphRoot.childCount - 1; i >= 0; i--)
            Destroy(_graphRoot.GetChild(i).gameObject);

        _nodeViewMap.Clear();
    }

    private void SpawnNodeView(NodeData nodeData)
    {
        if (nodeData == null || string.IsNullOrEmpty(nodeData.node_id))
        {
            Debug.LogWarning("[GraphManager] SpawnNodeView 실패: nodeData가 null이거나 node_id가 비어 있습니다.");
            return;
        }

        NodeView view = Instantiate(_nodePrefab, nodeData.Position, Quaternion.identity, _graphRoot);
        view.gameObject.name = $"Node_{nodeData.node_id}";
        view.Bind(nodeData);
        _nodeViewMap[nodeData.node_id] = view;
    }

    private void SpawnEdgeView(EdgeData edgeData)
    {
        if (edgeData == null)
        {
            Debug.LogWarning("[GraphManager] SpawnEdgeView 실패: edgeData가 null입니다.");
            return;
        }

        if (!_nodeViewMap.TryGetValue(edgeData.from_node_id, out NodeView fromView) ||
            !_nodeViewMap.TryGetValue(edgeData.to_node_id, out NodeView toView))
        {
            Debug.LogWarning($"[GraphManager] SpawnEdgeView 실패: from 또는 to NodeView를 찾을 수 없습니다. edge_id={edgeData.edge_id}");
            return;
        }

        EdgeView view = Instantiate(_edgePrefab, Vector3.zero, Quaternion.identity, _graphRoot);
        view.gameObject.name = $"Edge_{edgeData.edge_id}";
        view.Bind(edgeData, fromView, toView);
    }

    // ─────────────────────────────────────────────
    // 노드 CRUD
    // ─────────────────────────────────────────────

    public bool AddNode(NodeData node)
    {
        if (node == null || string.IsNullOrEmpty(node.node_id))
        {
            Debug.LogWarning("[GraphManager] AddNode 실패: node가 null이거나 node_id가 비어 있습니다.");
            return false;
        }
        if (_nodeRegistry.Contains(node.node_id))
        {
            Debug.LogWarning($"[GraphManager] AddNode 실패: 이미 존재하는 node_id ({node.node_id})");
            return false;
        }
        _nodeRegistry.Register(node);
        _graphData.nodes.Add(node);
        return true;
    }

    // 노드를 삭제하고, 연결된 엣지도 함께 제거한다.
    public bool RemoveNode(string nodeId)
    {
        if (!_nodeRegistry.Contains(nodeId))
        {
            Debug.LogWarning($"[GraphManager] RemoveNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }

        // 순회 중 컬렉션 수정 방지를 위해 복사본을 먼저 확보
        var connectedEdges = _edgeRegistry.GetEdgesConnectedTo(nodeId);
        foreach (var edge in connectedEdges)
            RemoveEdge(edge.edge_id);

        _nodeRegistry.Unregister(nodeId);
        _graphData.nodes.RemoveAll(n => n.node_id == nodeId);
        return true;
    }

    public NodeData GetNode(string nodeId) => _nodeRegistry.Get(nodeId);

    // 외부 UI(MainSketchView 등)가 GraphData를 읽기 위한 read-only 조회 메서드.
    // Registry 자체는 노출하지 않고, Registry.GetAll() / GetEdgesConnectedTo() 의 복사본 List를 그대로 위임 반환한다.
    public List<NodeData> GetAllNodes() => _nodeRegistry.GetAll();
    public List<EdgeData> GetEdgesConnectedToNode(string nodeId) => _edgeRegistry.GetEdgesConnectedTo(nodeId);

    // ─────────────────────────────────────────────
    // 엣지 CRUD
    // ─────────────────────────────────────────────

    // 엣지를 추가하기 전에 내부적으로 CanConnect를 검사한다.
    public bool AddEdge(EdgeData edge)
    {
        if (edge == null ||
            string.IsNullOrEmpty(edge.edge_id) ||
            string.IsNullOrEmpty(edge.from_node_id) ||
            string.IsNullOrEmpty(edge.to_node_id))
        {
            Debug.LogWarning("[GraphManager] AddEdge 실패: edge가 null이거나 필수 필드(edge_id, from_node_id, to_node_id)가 비어 있습니다.");
            return false;
        }
        if (!CanConnect(edge.from_node_id, edge.to_node_id, out string reason))
        {
            Debug.LogWarning($"[GraphManager] AddEdge 실패: {reason}");
            return false;
        }
        _edgeRegistry.Register(edge);
        _graphData.edges.Add(edge);
        return true;
    }

    public bool RemoveEdge(string edgeId)
    {
        if (!_edgeRegistry.Contains(edgeId))
        {
            Debug.LogWarning($"[GraphManager] RemoveEdge 실패: 존재하지 않는 edge_id ({edgeId})");
            return false;
        }
        _edgeRegistry.Unregister(edgeId);
        _graphData.edges.RemoveAll(e => e.edge_id == edgeId);
        return true;
    }

    // ─────────────────────────────────────────────
    // 연결 규칙 검사
    // ─────────────────────────────────────────────

    // 두 노드를 연결할 수 있는지 검사한다.
    // 실패 시 reason에 거부 이유를 담아 반환한다. 나중에 UI Toast/Warning과 연결 가능.
    public bool CanConnect(string fromNodeId, string toNodeId, out string reason)
    {
        // 1. 노드 존재 확인
        var fromNode = _nodeRegistry.Get(fromNodeId);
        var toNode = _nodeRegistry.Get(toNodeId);
        if (fromNode == null || toNode == null)
        {
            reason = "Node not found.";
            return false;
        }

        // 2. 자기 자신 연결 금지
        if (fromNodeId == toNodeId)
        {
            reason = "Cannot connect a node to itself.";
            return false;
        }

        // 3. 중복 엣지 금지
        if (_edgeRegistry.HasEdge(fromNodeId, toNodeId))
        {
            reason = "Edge already exists.";
            return false;
        }

        // 4. 허용된 노드 타입 조합 검사
        //    PROPERTY→PROPERTY, PROPERTY→PART, REFERENCE→PROPERTY, REFERENCE→PART 만 허용
        NodeType fromType = fromNode.NodeType;
        NodeType toType = toNode.NodeType;
        if (!IsAllowedConnection(fromType, toType))
        {
            reason = $"Connection not allowed: {fromType} → {toType}";
            return false;
        }

        // 5. 순환 구조 금지 (to에서 from으로 도달 가능하면 사이클 발생)
        if (HasPath(toNodeId, fromNodeId))
        {
            reason = "Connection would create a cycle.";
            return false;
        }

        reason = null;
        return true;
    }

    // 허용된 노드 타입 조합인지 확인한다.
    private bool IsAllowedConnection(NodeType from, NodeType to)
    {
        return (from == NodeType.PROPERTY && to == NodeType.PROPERTY) ||
               (from == NodeType.PROPERTY && to == NodeType.PART)     ||
               (from == NodeType.REFERENCE && to == NodeType.PROPERTY) ||
               (from == NodeType.REFERENCE && to == NodeType.PART);
    }

    // start 노드에서 forward edge를 따라 target 노드에 도달 가능한지 DFS로 확인한다.
    // 순환 감지에 사용: CanConnect(from, to)에서 HasPath(to, from)이 true이면 사이클.
    private bool HasPath(string start, string target)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (current == target) return true;
            if (visited.Contains(current)) continue;
            visited.Add(current);

            foreach (var edge in _edgeRegistry.GetEdgesFrom(current))
                stack.Push(edge.to_node_id);
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // Dev 2 인터페이스 (데이터 조작만 수행. 렌더링은 다음 단계에서 추가)
    // ─────────────────────────────────────────────

    public bool RequestSelectNode(string nodeId)
    {
        if (_nodeRegistry.Get(nodeId) == null)
        {
            Debug.LogWarning($"[GraphManager] RequestSelectNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
        // TODO: 해당 NodeView 하이라이트 처리 추가 예정
        return true;
    }

    public bool RequestMoveNode(string nodeId, Vector3 position)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestMoveNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
        node.SetPosition(position);
        // TODO: NodeView 위치 갱신 추가 예정
        return true;
    }

    public void RequestCreateNode(string parentNodeId, string text)
    {
        // TODO: 서버 응답으로 NodeData를 받은 뒤 AddNode 호출 예정
        // parentNodeId는 새 노드와 연결할 부모 노드 식별자
    }

    // 메인 그래프 UI(MainSketchView/AddPartPort)에서 PART 또는 ALL 노드를 즉시 생성할 때 사용한다.
    // 서버 연동 전까지는 클라이언트에서 GUID를 생성해 AddNode로 등록하고 생성된 node_id를 반환한다.
    // 실패 시 null. 서버 연동 시 POST /api/nodes 응답 ID로 교체 예정.
    public string RequestCreatePartNode(string label, bool isGlobal = false)
    {
        var node = new NodeData
        {
            node_id = Guid.NewGuid().ToString(),
            type = "PART",
            label = label,
            position = new float[] { 0f, 0f, 0f },
            is_global = isGlobal
        };
        return AddNode(node) ? node.node_id : null;
    }

    public bool RequestConnectNodes(string fromNodeId, string toNodeId)
    {
        var edge = new EdgeData
        {
            edge_id = Guid.NewGuid().ToString(),
            from_node_id = fromNodeId,
            to_node_id = toNodeId
        };
        // TODO: EdgeView 렌더링 추가 예정
        return AddEdge(edge);
    }

    public bool RequestDeleteNode(string nodeId)
    {
        // TODO: NodeView/EdgeView 제거 처리 추가 예정
        return RemoveNode(nodeId);
    }
}
