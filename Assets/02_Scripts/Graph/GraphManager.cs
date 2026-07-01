using System;
using System.Collections.Generic;
using UnityEngine;

// NodeXR 노드그래프의 데이터 등록·조회·삭제·연결 규칙 검사 및 렌더링을 담당한다.
// Singleton 없이 일반 MonoBehaviour 로 유지한다.
//
// subgraph-spec.md 반영:
//   - PROPERTY→PROPERTY 연결 시 부모 PROPERTY 최대 1개 검사 (CanConnect 규칙 1)
//   - RenderGraph: PROPERTY 노드만 SpawnNodeView, 완료 후 ReflowAllSubtrees
//   - RequestDeleteNode: 자손 + 종속 REFERENCE 캐스케이드 삭제
//   - RequestCreatePropertyNode: PROPERTY 자식 노드 즉시 생성 + Reflow
//   - ReflowAllSubtrees: depth 기반 계층형 레이아웃 재계산
public class GraphManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // 렌더링 슬롯
    // ─────────────────────────────────────────────
    [Header("렌더링")]
    [SerializeField] private Transform _graphRoot;
    [SerializeField] private NodeView  _nodePrefab;
    [SerializeField] private EdgeView  _edgePrefab;

    // ─────────────────────────────────────────────
    // 레이아웃 (Inspector에서 조절 가능)
    // ─────────────────────────────────────────────
    [Header("레이아웃 간격 (씬에서 직접 조절)")]
    [SerializeField] private float _layoutHSpacing   = 0.5f;  // depth 단계별 오른쪽 간격 (X축)
    [SerializeField] private float _layoutVSpacing   = 0.6f;  // 형제 노드 세로 간격 (Y축)
    [SerializeField] private float _layoutTreeGap    = 0.2f;  // 독립 서브트리 간 추가 여백

    private const string DefaultPropertyLabel  = "";

    // ─────────────────────────────────────────────
    // 서버 동기화 이벤트 — GraphSyncClient가 구독한다
    // GraphManager는 WS 코드를 알지 못하며 이벤트만 발행한다.
    // ─────────────────────────────────────────────
    public event Action<string>          OnNodeDeleted;      // nodeId
    public event Action<EdgeData>        OnEdgeCreated;      // edge
    public event Action<string>          OnEdgeDeleted;      // edgeId
    public event Action<string, Vector3> OnNodeMoved;        // nodeId, position
    public event Action<string, string>  OnNodeTextUpdated;  // nodeId, newText (서버 기준 node_text)

    // 노드 "+" 발화 요청. GraphManager는 REST를 모르고 이벤트만 발행한다.
    // UtteranceApiClient가 구독해 POST /api/utterances 후 MergeServerGraph로 반영한다.
    public event Action<string, string>  OnUtteranceNodeRequested;  // parentNodeId, utterance

    // ─────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────
    private GraphData    _graphData;
    private NodeRegistry _nodeRegistry;
    private EdgeRegistry _edgeRegistry;

    private Dictionary<string, NodeView> _nodeViewMap;
    private Dictionary<string, EdgeView> _edgeViewMap;  // 신규: EdgeView 추적

    private void Awake()
    {
        _nodeRegistry = new NodeRegistry();
        _edgeRegistry = new EdgeRegistry();
        _graphData    = new GraphData();
        _nodeViewMap  = new Dictionary<string, NodeView>();
        _edgeViewMap  = new Dictionary<string, EdgeView>();
    }

    // ─────────────────────────────────────────────
    // Graph 생명주기
    // ─────────────────────────────────────────────

    public void LoadGraph(GraphData graphData)
    {
        _nodeRegistry.Clear();
        _edgeRegistry.Clear();

        if (graphData == null)
        {
            Debug.LogWarning("[GraphManager] LoadGraph: graphData가 null입니다. 빈 그래프로 초기화합니다.");
            _graphData = new GraphData();
            return;
        }

        _graphData = graphData;
        if (_graphData.nodes == null) _graphData.nodes = new List<NodeData>();
        if (_graphData.edges == null) _graphData.edges = new List<EdgeData>();

        foreach (var node in _graphData.nodes) _nodeRegistry.Register(node);
        foreach (var edge in _graphData.edges) _edgeRegistry.Register(edge);

        BackfillSubGraphIds();
    }

    public GraphData GetGraphData() => _graphData;

    // 현재 그래프로 2D regenerate 요청 JSON 생성(부분 재생성 시 selectedPartNodeIds 지정).
    // 협의 계약: connection = [{ part_node_id, node_id(leaf) }]. 서버 엔드포인트 구현 후 전송에 사용.
    public string BuildRegenerateRequestJson(string assetId, ICollection<string> selectedPartNodeIds = null)
        => RegenerateConnectionBuilder.BuildRequestJson(_graphData, assetId, selectedPartNodeIds);

    // ─────────────────────────────────────────────
    // 렌더링
    // ─────────────────────────────────────────────

    // LoadGraph() 이후 명시적으로 호출한다.
    // PROPERTY 노드만 NodeView 를 생성한다. PART·REFERENCE 는 스킵.
    // SpawnEdgeView 는 양쪽 NodeView 가 존재할 때만(= PROPERTY→PROPERTY) 생성한다.
    public void RenderGraph()
    {
        if (_graphRoot == null || _nodePrefab == null || _edgePrefab == null)
        {
            Debug.LogWarning("[GraphManager] RenderGraph 실패: _graphRoot/_nodePrefab/_edgePrefab 중 하나 이상이 null입니다.");
            return;
        }

        ClearGraphView();

        if (_graphData?.nodes == null || _graphData?.edges == null) return;

        foreach (var node in _graphData.nodes)
        {
            if (node.NodeType == NodeType.PROPERTY)
                SpawnNodeView(node);
        }

        foreach (var edge in _graphData.edges)
            SpawnEdgeView(edge);

        ReflowAllSubtrees();
    }

    public void ClearGraphView()
    {
        if (_graphRoot != null)
        {
            for (int i = _graphRoot.childCount - 1; i >= 0; i--)
                Destroy(_graphRoot.GetChild(i).gameObject);
        }
        _nodeViewMap.Clear();
        _edgeViewMap.Clear();
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
        view.Bind(nodeData, this);

        if (view.ActionPanel != null)
            view.ActionPanel.Bind(nodeData.node_id, this);

        _nodeViewMap[nodeData.node_id] = view;
    }

    // 양쪽 NodeView 가 _nodeViewMap 에 존재할 때만 EdgeView 를 생성한다.
    // PROPERTY→PART, REFERENCE 관련 엣지는 자동으로 스킵된다 (조용히).
    private void SpawnEdgeView(EdgeData edgeData)
    {
        if (edgeData == null) return;

        if (!_nodeViewMap.TryGetValue(edgeData.from_node_id, out NodeView fromView) ||
            !_nodeViewMap.TryGetValue(edgeData.to_node_id,   out NodeView toView))
            return;

        EdgeView view = Instantiate(_edgePrefab, Vector3.zero, Quaternion.identity, _graphRoot);
        view.gameObject.name = $"Edge_{edgeData.edge_id}";
        view.Bind(edgeData, fromView, toView);
        _edgeViewMap[edgeData.edge_id] = view;
    }

    // ─────────────────────────────────────────────
    // 노드 CRUD (data layer)
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

    // 노드와 연결된 엣지를 모두 데이터 레이어에서 삭제한다.
    // View 제거는 RequestDeleteNode 에서 처리한다.
    public bool RemoveNode(string nodeId)
    {
        if (!_nodeRegistry.Contains(nodeId))
        {
            Debug.LogWarning($"[GraphManager] RemoveNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
        var connectedEdges = _edgeRegistry.GetEdgesConnectedTo(nodeId);
        foreach (var edge in connectedEdges)
            RemoveEdge(edge.edge_id);

        _nodeRegistry.Unregister(nodeId);
        _graphData.nodes.RemoveAll(n => n.node_id == nodeId);
        return true;
    }

    public NodeData GetNode(string nodeId) => _nodeRegistry.Get(nodeId);
    public List<NodeData> GetAllNodes()    => _nodeRegistry.GetAll();

    public List<EdgeData> GetEdgesConnectedToNode(string nodeId) => _edgeRegistry.GetEdgesConnectedTo(nodeId);
    public List<EdgeData> GetEdgesIncomingToNode(string nodeId)  => _edgeRegistry.GetEdgesTo(nodeId);

    // ─────────────────────────────────────────────
    // 엣지 CRUD (data layer)
    // ─────────────────────────────────────────────

    public bool AddEdge(EdgeData edge)
    {
        if (edge == null ||
            string.IsNullOrEmpty(edge.edge_id) ||
            string.IsNullOrEmpty(edge.from_node_id) ||
            string.IsNullOrEmpty(edge.to_node_id))
        {
            Debug.LogWarning("[GraphManager] AddEdge 실패: edge 또는 필수 필드가 null입니다.");
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

    public bool CanConnect(string fromNodeId, string toNodeId, out string reason)
    {
        // 1. 노드 존재 확인
        var fromNode = _nodeRegistry.Get(fromNodeId);
        var toNode   = _nodeRegistry.Get(toNodeId);
        if (fromNode == null || toNode == null)
        {
            reason = "노드를 찾을 수 없습니다.";
            return false;
        }

        // 2. 자기 자신 연결 금지
        if (fromNodeId == toNodeId)
        {
            reason = "자기 자신에게 연결할 수 없습니다.";
            return false;
        }

        // 3. 중복 엣지 금지
        if (_edgeRegistry.HasEdge(fromNodeId, toNodeId))
        {
            reason = "이미 존재하는 엣지입니다.";
            return false;
        }

        // 4. 허용된 노드 타입 조합 검사
        if (!IsAllowedConnection(fromNode.NodeType, toNode.NodeType))
        {
            reason = $"허용되지 않는 연결 조합: {fromNode.NodeType} → {toNode.NodeType}";
            return false;
        }

        // 5. 규칙 1: PROPERTY→PROPERTY 시 to 노드의 부모 PROPERTY 최대 1개 (Tree 구조 보장)
        if (fromNode.NodeType == NodeType.PROPERTY && toNode.NodeType == NodeType.PROPERTY)
        {
            foreach (var edge in _edgeRegistry.GetEdgesTo(toNodeId))
            {
                var parent = _nodeRegistry.Get(edge.from_node_id);
                if (parent?.NodeType == NodeType.PROPERTY)
                {
                    reason = $"PROPERTY 부모가 이미 존재합니다. (to={toNodeId})";
                    return false;
                }
            }
        }

        // 6. 사이클 금지
        if (HasPath(toNodeId, fromNodeId))
        {
            reason = "연결 시 사이클이 발생합니다.";
            return false;
        }

        reason = null;
        return true;
    }

    private bool IsAllowedConnection(NodeType from, NodeType to)
    {
        return (from == NodeType.PROPERTY  && to == NodeType.PROPERTY) ||
               (from == NodeType.PROPERTY  && to == NodeType.PART)     ||
               (from == NodeType.REFERENCE && to == NodeType.PROPERTY) ||
               (from == NodeType.REFERENCE && to == NodeType.PART);
    }

    private bool HasPath(string start, string target)
    {
        var visited = new HashSet<string>();
        var stack   = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            string cur = stack.Pop();
            if (cur == target) return true;
            if (!visited.Add(cur)) continue;
            foreach (var edge in _edgeRegistry.GetEdgesFrom(cur))
                stack.Push(edge.to_node_id);
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // Request* 인터페이스 — 개발자 2 / UI 경계
    // ─────────────────────────────────────────────

    public bool RequestSelectNode(string nodeId)
    {
        if (_nodeRegistry.Get(nodeId) == null)
        {
            Debug.LogWarning($"[GraphManager] RequestSelectNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
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
        if (_nodeViewMap.TryGetValue(nodeId, out NodeView view))
            view.transform.position = position;
        OnNodeMoved?.Invoke(nodeId, position);
        return true;
    }

    // 전체 그래프(서브그래프 강체) 이동 — 멤버 노드 하나로 소속 서브그래프를 찾아 이동.
    // 개발자 2가 XR Grab으로 서브그래프를 통째로 드래그할 때 사용.
    public bool RequestMoveSubgraphByMember(string memberNodeId, Vector3 delta)
    {
        string subGraphId = GetSubGraphId(memberNodeId);
        if (string.IsNullOrEmpty(subGraphId))
        {
            Debug.LogWarning($"[GraphManager] RequestMoveSubgraphByMember 실패: sub_graph_id를 찾을 수 없습니다. (node_id={memberNodeId})");
            return false;
        }
        return RequestMoveSubgraph(subGraphId, delta);
    }

    // 같은 sub_graph_id 를 가진 노드 전체를 delta 만큼 강체 이동한다.
    // 루트를 포함해 통째로 옮기므로 상대 배치는 유지되며, 이후 Reflow(루트 고정)와도 일관된다.
    // 서버에는 노드별 개별 NODE_MOVE(OnNodeMoved)로 전달된다. (서버 전체이동 op 미구현 — 2026-07-01 합의)
    public bool RequestMoveSubgraph(string subGraphId, Vector3 delta)
    {
        if (string.IsNullOrEmpty(subGraphId))
        {
            Debug.LogWarning("[GraphManager] RequestMoveSubgraph 실패: subGraphId가 비어 있습니다.");
            return false;
        }

        // 이동 대상 스냅샷 (반복 중 레지스트리 변경 없음)
        var members = new List<NodeData>();
        foreach (var node in _nodeRegistry.GetAll())
        {
            if (node == null || string.IsNullOrEmpty(node.node_id)) continue;
            if (GetSubGraphId(node.node_id) == subGraphId) members.Add(node);
        }

        if (members.Count == 0)
        {
            Debug.LogWarning($"[GraphManager] RequestMoveSubgraph 실패: 해당 서브그래프에 노드가 없습니다. (sub_graph_id={subGraphId})");
            return false;
        }

        foreach (var node in members)
        {
            Vector3 next = node.Position + delta;
            node.SetPosition(next);
            if (_nodeViewMap.TryGetValue(node.node_id, out NodeView view) && view != null)
                view.transform.position = next;
            OnNodeMoved?.Invoke(node.node_id, next);
        }
        return true;
    }

    // Deprecated: RequestCreatePropertyNode(parentId) 를 사용하세요.
    [Obsolete("RequestCreateNode 는 Deprecated 입니다. RequestCreatePropertyNode(parentId) 를 사용하세요.")]
    public void RequestCreateNode(string parentNodeId, string text)
    {
        RequestCreatePropertyNode(parentNodeId);
    }

    // Add 버튼: PROPERTY 자식 노드를 즉시 생성하고 부모와 연결한다.
    // 위치는 ReflowAllSubtrees 에서 계산된다.
    // 반환: 생성된 node_id (실패 시 null)
    public string RequestCreatePropertyNode(string parentId)
    {
        var parentNode = _nodeRegistry.Get(parentId);
        if (parentNode == null)
        {
            Debug.LogWarning($"[GraphManager] RequestCreatePropertyNode 실패: 존재하지 않는 node_id ({parentId})");
            return null;
        }

        var newNode = new NodeData
        {
            node_id  = Guid.NewGuid().ToString(),
            type     = "PROPERTY",
            label    = DefaultPropertyLabel,
            position = new float[] { 0f, 0f, 0f }
        };

        if (!AddNode(newNode)) return null;

        var edge = new EdgeData
        {
            edge_id      = Guid.NewGuid().ToString(),
            from_node_id = parentId,
            to_node_id   = newNode.node_id
        };

        if (!AddEdge(edge))
        {
            RemoveNode(newNode.node_id);
            return null;
        }

        // 부모의 서브그래프에 소속시킨다 (엣지 등록 이후라 부모 탐색 가능).
        newNode.sub_graph_id = GetSubGraphId(newNode.node_id);

        if (_graphRoot != null && _nodePrefab != null)
        {
            SpawnNodeView(newNode);
            SpawnEdgeView(edge);
        }

        ReflowAllSubtrees();
        return newNode.node_id;
    }

    // 키보드 '+' 버튼: 빈 상태(부모 없음)에서 서브그래프의 첫 PROPERTY root 노드를 생성한다.
    //  - RequestCreatePropertyNode 와 대칭이지만 부모/엣지가 없다(새 서브그래프의 루트).
    //  - sub_graph_id 는 자기 자신(GetSubGraphId 가 부모 없는 노드에 대해 자신의 node_id 반환).
    //  - 생성 후 흐름은 자식 노드와 동일: 노드 텍스트칸 입력 = RequestNodeByUtterance(발화 서버 전송).
    // 반환: 생성된 node_id (실패 시 null)
    public string RequestCreateRootPropertyNode()
    {
        var newNode = new NodeData
        {
            node_id  = Guid.NewGuid().ToString(),
            type     = "PROPERTY",
            label    = DefaultPropertyLabel,
            position = new float[] { 0f, 0f, 0f }
        };

        if (!AddNode(newNode)) return null;

        // 부모가 없으므로 자기 자신이 서브그래프 루트가 된다.
        newNode.sub_graph_id = GetSubGraphId(newNode.node_id);

        if (_graphRoot != null && _nodePrefab != null)
            SpawnNodeView(newNode);

        ReflowAllSubtrees();
        return newNode.node_id;
    }

    // 발화 입력칸(노드 텍스트칸) 제출 처리.
    //  - 입력 텍스트를 노드 자신에 로컬 반영(오프라인·즉시). WS는 발행하지 않는다(서버는 utterance로 반영).
    //  - 서버 확장 요청은 이벤트로만 위임한다(REST는 UtteranceApiClient 담당 → parent=이 노드).
    public void RequestNodeByUtterance(string nodeId, string utterance)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestNodeByUtterance 실패: 존재하지 않는 node_id ({nodeId})");
            return;
        }

        string text = (utterance ?? "").Trim();
        node.label     = text;
        node.node_text = text;

        OnUtteranceNodeRequested?.Invoke(nodeId, text);
    }

    // 서버 응답 그래프(/api/utterances 등)를 node_id 기준 upsert 병합한다.
    //  - 새 노드: 추가 + PROPERTY면 NodeView 생성. 서버 position은 신뢰하지 않고 Reflow가 배치한다.
    //  - 기존 노드: node_text/data/parent_node_id 갱신(위치 유지).
    //  - 엣지: edge_id가 없을 때만 추가(+ 양쪽 View 있으면 EdgeView 생성).
    // WS 이벤트는 발행하지 않는다(생성은 REST 경로로 서버에 이미 반영됨).
    public void MergeServerGraph(GraphData incoming)
    {
        if (incoming?.nodes == null)
        {
            Debug.LogWarning("[GraphManager] MergeServerGraph 실패: incoming/nodes가 null입니다.");
            return;
        }

        foreach (var node in incoming.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.node_id)) continue;

            var existing = _nodeRegistry.Get(node.node_id);
            if (existing == null)
            {
                // 표시는 label 우선이므로 label이 비면 node_text로 채운다.
                if (string.IsNullOrEmpty(node.label)) node.label = node.node_text;

                if (!AddNode(node)) continue;
                if (node.NodeType == NodeType.PROPERTY && _graphRoot != null && _nodePrefab != null)
                    SpawnNodeView(node);
            }
            else
            {
                // 위치는 유지, 텍스트/데이터/부모만 갱신.
                existing.node_text      = node.node_text;
                if (!string.IsNullOrEmpty(node.node_text)) existing.label = node.node_text;
                existing.data           = node.data;
                existing.parent_node_id = node.parent_node_id;

                if (existing.NodeType == NodeType.PROPERTY &&
                    _nodeViewMap.TryGetValue(existing.node_id, out NodeView view) && view != null)
                    view.Bind(existing, this);   // 라벨 갱신
            }
        }

        if (incoming.edges != null)
        {
            foreach (var edge in incoming.edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.edge_id)) continue;
                if (_edgeRegistry.Contains(edge.edge_id)) continue;
                if (AddEdge(edge)) SpawnEdgeView(edge);   // SpawnEdgeView는 양쪽 View 없으면 조용히 skip
            }
        }

        BackfillSubGraphIds();
        ReflowAllSubtrees();   // 우리 위치 규칙으로 재배치
    }

    // 메인 그래프 UI (AddPartPort) 전용 PART 노드 즉시 생성
    public string RequestCreatePartNode(string label, bool isGlobal = false)
        => RequestCreatePartNode(label, isGlobal, null);

    // nodeId 지정 오버로드. 서버(part_node/generate)가 발급한 UUID로 로컬 PART를 생성할 때 사용한다.
    // nodeId 가 null/blank 면 기존처럼 로컬 GUID 를 발급한다.
    // (PART은 REST 동기화이므로 이 메서드는 서버로 아무 이벤트도 발행하지 않는다 — 로컬 생성 전용.)
    public string RequestCreatePartNode(string label, bool isGlobal, string nodeId)
    {
        var node = new NodeData
        {
            node_id   = string.IsNullOrEmpty(nodeId) ? Guid.NewGuid().ToString() : nodeId,
            type      = "PART",
            label     = label,
            position  = new float[] { 0f, 0f, 0f },
            is_global = isGlobal
        };
        return AddNode(node) ? node.node_id : null;
    }

    public bool RequestConnectNodes(string fromNodeId, string toNodeId)
    {
        var edge = new EdgeData
        {
            edge_id      = Guid.NewGuid().ToString(),
            from_node_id = fromNodeId,
            to_node_id   = toNodeId
        };
        if (!AddEdge(edge)) return false;
        SpawnEdgeView(edge);
        OnEdgeCreated?.Invoke(edge);
        return true;
    }

    // 메인 그래프 포트(ALL/PART) → 서브그래프 노드 드래그 연결용 어댑터.
    // 제스처 방향은 항상 포트(시작) → 서브그래프(드롭)지만,
    // 데이터 방향은 항상 서브그래프(PROPERTY/REFERENCE) → PART 로 저장한다.
    // 개발자 2는 시작 포트의 node_id와 드롭된 서브그래프 노드의 node_id를 순서 그대로 넘긴다.
    // (인자를 뒤바꿔 넘겨도 CanConnect 가 PART→... 조합을 막아 안전하게 실패한다.)
    public bool RequestConnectFromPort(string portNodeId, string subgraphNodeId)
        => RequestConnectNodes(subgraphNodeId, portNodeId);

    // 자손 PROPERTY + 종속 REFERENCE 를 post-order 로 캐스케이드 삭제한 뒤 Reflow.
    // emitSync=false 이면 OnNodeDeleted(→ WS NODE_DELETE) 를 발행하지 않는다(로컬 전용 삭제).
    //   PART 노드는 REST(part_node/delete)로 동기화하므로 PartNodeApiClient 가 emitSync:false 로 호출한다.
    //   PROPERTY 등 기존 호출부는 기본값(true) 유지 → 동작 무변경.
    public bool RequestDeleteNode(string nodeId, bool emitSync = true)
    {
        if (!_nodeRegistry.Contains(nodeId))
        {
            Debug.LogWarning($"[GraphManager] RequestDeleteNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }

        var targets = CollectCascadeTargets(nodeId);

        foreach (var targetId in targets)
        {
            // EdgeView 제거 (RemoveNode 가 데이터를 지우기 전에 먼저 처리)
            var connectedEdges = new List<EdgeData>(_edgeRegistry.GetEdgesConnectedTo(targetId));
            foreach (var edge in connectedEdges)
            {
                if (_edgeViewMap.TryGetValue(edge.edge_id, out EdgeView ev))
                {
                    if (ev != null) Destroy(ev.gameObject);
                    _edgeViewMap.Remove(edge.edge_id);
                }
            }

            // NodeView 제거 (PROPERTY 만 View 존재)
            if (_nodeViewMap.TryGetValue(targetId, out NodeView nv))
            {
                if (nv != null) Destroy(nv.gameObject);
                _nodeViewMap.Remove(targetId);
            }

            // 데이터 삭제 (연결 엣지 데이터도 내부에서 정리)
            RemoveNode(targetId);
            if (emitSync) OnNodeDeleted?.Invoke(targetId);
        }

        ReflowAllSubtrees();
        return true;
    }

    // AllPort Pressed X 버튼: 엣지만 삭제, 노드 유지.
    // PROPERTY→PART 엣지가 주 대상이므로 Reflow 는 호출하지 않는다.
    public bool RequestDeleteEdge(string edgeId)
    {
        if (_edgeViewMap.TryGetValue(edgeId, out EdgeView ev))
        {
            if (ev != null) Destroy(ev.gameObject);
            _edgeViewMap.Remove(edgeId);
        }
        bool ok = RemoveEdge(edgeId);
        if (!ok)
            Debug.LogWarning($"[GraphManager] RequestDeleteEdge 실패: edge_id={edgeId}");
        else
            OnEdgeDeleted?.Invoke(edgeId);
        return ok;
    }

    // PartPort 인라인 rename — 로컬 label 갱신 전용.
    // PART 노드는 REST(part_node/modify)로 서버 동기화하므로 여기서 WS 이벤트를 발행하지 않는다.
    // (서버 반영은 PartNodeApiClient.ModifyPart 가 담당. 이 메서드는 modify 성공 후 로컬 반영용.)
    public bool RequestRenamePartNode(string nodeId, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            Debug.LogWarning("[GraphManager] RequestRenamePartNode 실패: newLabel이 비어 있습니다.");
            return false;
        }
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestRenamePartNode 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
        node.label = newLabel.Trim();
        return true;
    }

    // PROPERTY 등 일반 노드의 텍스트 수정 → 서버 NODE_TEXT_UPDATE 동기화용.
    // 서버 기준 필드는 node_text, 로컬 표시는 label 우선이므로 둘 다 갱신한다.
    // 서버가 blank 텍스트를 거부하므로(GRAPH400), blank면 로컬만 갱신하고 이벤트는 발행하지 않는다.
    public bool RequestUpdateNodeText(string nodeId, string text)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestUpdateNodeText 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }

        string trimmed = (text ?? "").Trim();
        node.label     = trimmed;
        node.node_text = trimmed;

        if (!string.IsNullOrEmpty(trimmed))
            OnNodeTextUpdated?.Invoke(nodeId, trimmed);

        return true;
    }

    // ─────────────────────────────────────────────
    // 캐스케이드 삭제 헬퍼
    // ─────────────────────────────────────────────

    // 삭제 대상 목록을 post-order(자식 먼저, 자신 마지막)로 반환한다.
    private List<string> CollectCascadeTargets(string nodeId)
    {
        var targets = new List<string>();
        var visited = new HashSet<string>();
        CollectCascadeTargetsRecursive(nodeId, targets, visited);
        return targets;
    }

    private void CollectCascadeTargetsRecursive(string nodeId, List<string> targets, HashSet<string> visited)
    {
        if (visited.Contains(nodeId)) return;

        // 1. PROPERTY 자식 먼저 재귀
        foreach (var childId in GetPropertyChildren(nodeId))
            CollectCascadeTargetsRecursive(childId, targets, visited);

        // 2. 이 노드에 종속된 REFERENCE (REFERENCE → 이 노드 엣지)
        foreach (var edge in _edgeRegistry.GetEdgesTo(nodeId))
        {
            var fromNode = _nodeRegistry.Get(edge.from_node_id);
            if (fromNode?.NodeType == NodeType.REFERENCE && !visited.Contains(edge.from_node_id))
            {
                visited.Add(edge.from_node_id);
                targets.Add(edge.from_node_id);
            }
        }

        // 3. 자신
        visited.Add(nodeId);
        targets.Add(nodeId);
    }

    // ─────────────────────────────────────────────
    // R 버튼 컨텍스트 수집
    // ─────────────────────────────────────────────

    // 현재 노드에서 레이아웃 루트까지 올라가며 PROPERTY 체인을 수집하고
    // 루트와 직접 연결된 PART 정보를 반환한다.
    // ReferenceSearchPanel.Open(context) 에 전달 예정 (Phase 2).
    public ReferenceContext CollectReferenceContext(string nodeId)
    {
        // 루트까지 올라가며 체인 수집 (루트 → 현재 순서)
        var chain = new List<string>();
        string cur = nodeId;
        while (cur != null)
        {
            chain.Insert(0, cur);
            cur = GetPropertyParent(cur);
        }

        // 루트 PROPERTY 에서 직접 연결된 PART 탐색
        string partNodeId = null;
        string partLabel  = null;
        if (chain.Count > 0)
        {
            foreach (var edge in _edgeRegistry.GetEdgesFrom(chain[0]))
            {
                var toNode = _nodeRegistry.Get(edge.to_node_id);
                if (toNode?.NodeType == NodeType.PART)
                {
                    partNodeId = toNode.node_id;
                    partLabel  = toNode.DisplayText;
                    break;
                }
            }
        }

        var chainLabels = new List<string>();
        foreach (var id in chain)
        {
            var node = _nodeRegistry.Get(id);
            if (node != null) chainLabels.Add(node.DisplayText);
        }

        return new ReferenceContext
        {
            chainLabels = chainLabels,
            partNodeId  = partNodeId,
            partLabel   = partLabel
        };
    }

    // ─────────────────────────────────────────────
    // 레이아웃 — ReflowAllSubtrees
    // ─────────────────────────────────────────────

    // 모든 PROPERTY 서브트리를 재배치한다.
    // NodeData.position 갱신 → NodeView.transform.position 반영 → depth 머티리얼 적용.
    public void ReflowAllSubtrees()
    {
        var roots = FindLayoutRoots();

        foreach (var rootId in roots)
        {
            var rootNode = _nodeRegistry.Get(rootId);
            if (rootNode == null) continue;
            // 루트 위치는 건드리지 않고 자식들만 루트의 X(depth기준)/Y(중심) 기준으로 재배치
            AssignChildPositions(rootId, rootNode.Position.x, rootNode.Position.y);
        }

        var depthMap = CalculateDepthMap();

        foreach (var kv in _nodeViewMap)
        {
            var node = _nodeRegistry.Get(kv.Key);
            if (node == null) continue;

            kv.Value.transform.position = node.Position;

            int depth = depthMap.TryGetValue(kv.Key, out int d) ? d : 0;
            kv.Value.SetDepth(depth);
        }
    }

    // 들어오는 PROPERTY 엣지가 없는 PROPERTY 노드 = 레이아웃 루트
    private List<string> FindLayoutRoots()
    {
        var roots = new List<string>();
        foreach (var node in _nodeRegistry.GetAll())
        {
            if (node.NodeType != NodeType.PROPERTY) continue;
            if (GetPropertyParent(node.node_id) == null)
                roots.Add(node.node_id);
        }
        return roots;
    }

    // 서브트리 너비 계산: 리프=1, 비리프=자식 너비의 합 (bottom-up)
    private float CalculateSubtreeWidth(string nodeId)
    {
        var children = GetPropertyChildren(nodeId);
        if (children.Count == 0) return 1f;
        float total = 0f;
        foreach (var child in children)
            total += CalculateSubtreeWidth(child);
        return total;
    }

    // 이 노드를 x/centerY 에 배치하고 자식도 재귀 배치 (오른쪽 방향 트리)
    private void AssignPositions(string nodeId, float x, float centerY)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null) return;
        node.SetPosition(new Vector3(x, centerY, 0f));
        AssignChildPositions(nodeId, x, centerY);
    }

    // 이 노드 자체는 움직이지 않고 자식들만 x/centerY 기준으로 배치
    // x: 현재 depth의 X 위치, centerY: 이 서브트리 전체의 Y 중심
    private void AssignChildPositions(string nodeId, float x, float centerY)
    {
        var children = GetPropertyChildren(nodeId);
        if (children.Count == 0) return;

        float totalHeight = 0f;
        foreach (var child in children)
            totalHeight += CalculateSubtreeWidth(child);

        float cursor = centerY - (totalHeight / 2f) * _layoutVSpacing;
        foreach (var child in children)
        {
            float childHeight  = CalculateSubtreeWidth(child);
            float childCenterY = cursor + (childHeight / 2f) * _layoutVSpacing;
            AssignPositions(child, x + _layoutHSpacing, childCenterY);
            cursor += childHeight * _layoutVSpacing;
        }
    }

    // depth map 계산 (루트=0, 자식=부모+1)
    private Dictionary<string, int> CalculateDepthMap()
    {
        var map = new Dictionary<string, int>();
        foreach (var rootId in FindLayoutRoots())
            AssignDepths(rootId, 0, map);
        return map;
    }

    private void AssignDepths(string nodeId, int depth, Dictionary<string, int> map)
    {
        map[nodeId] = depth;
        foreach (var child in GetPropertyChildren(nodeId))
            AssignDepths(child, depth + 1, map);
    }

    // ─────────────────────────────────────────────
    // 그래프 탐색 헬퍼
    // ─────────────────────────────────────────────

    // 노드가 속한 서브그래프 식별자.
    // 자신 또는 PROPERTY 부모 체인에 서버가 준 sub_graph_id 가 있으면 그것을,
    // 없으면 레이아웃 루트(PROPERTY 부모가 없는 최상위)의 node_id 를 사용한다.
    // REFERENCE 는 PROPERTY 부모가 없으므로 서버 값이 없으면 단독(자기 node_id) 서브그래프가 된다.
    public string GetSubGraphId(string nodeId)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null) return null;

        var visited = new HashSet<string>();
        string cur = nodeId;
        while (visited.Add(cur))
        {
            var curNode = _nodeRegistry.Get(cur);
            if (curNode != null && !string.IsNullOrEmpty(curNode.sub_graph_id))
                return curNode.sub_graph_id;

            string parent = GetPropertyParent(cur);
            if (parent == null) return cur;  // cur = 레이아웃 루트 → 그 node_id 를 서브그래프 id 로
            cur = parent;
        }
        return cur;
    }

    // sub_graph_id 가 비어 있는 노드를 레이아웃 루트 기준으로 채운다. LoadGraph 직후 1회 호출.
    private void BackfillSubGraphIds()
    {
        foreach (var node in _nodeRegistry.GetAll())
        {
            if (node == null || string.IsNullOrEmpty(node.node_id)) continue;
            if (string.IsNullOrEmpty(node.sub_graph_id))
                node.sub_graph_id = GetSubGraphId(node.node_id);
        }
    }

    // 이 노드의 PROPERTY 부모 node_id 반환 (없으면 null)
    private string GetPropertyParent(string nodeId)
    {
        foreach (var edge in _edgeRegistry.GetEdgesTo(nodeId))
        {
            var fromNode = _nodeRegistry.Get(edge.from_node_id);
            if (fromNode?.NodeType == NodeType.PROPERTY)
                return edge.from_node_id;
        }
        return null;
    }

    // 이 노드의 직접 PROPERTY 자식 node_id 목록 반환
    private List<string> GetPropertyChildren(string nodeId)
    {
        var result = new List<string>();
        foreach (var edge in _edgeRegistry.GetEdgesFrom(nodeId))
        {
            var toNode = _nodeRegistry.Get(edge.to_node_id);
            if (toNode?.NodeType == NodeType.PROPERTY)
                result.Add(edge.to_node_id);
        }
        return result;
    }
}
