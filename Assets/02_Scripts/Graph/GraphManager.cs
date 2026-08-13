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
    // depth 단계별 간격(_layoutRight 방향). 노드 실폭이 3.98 × _nodeViewScale(0.09) ≈ 0.36m 라
    // 0.5 면 노드 사이 빈 공간이 약 0.14m 다. (씬에 직렬화된 값이 이 기본값을 덮는다)
    [SerializeField] private float _layoutHSpacing   = 0.5f;
    [SerializeField] private float _layoutVSpacing   = 0.6f;  // 형제 노드 세로 간격 (Y축)
    [SerializeField] private float _layoutTreeGap    = 0.2f;  // 독립 서브트리 간 추가 여백

    // 자식 배치가 따라갈 좌표축. 기본은 월드 축이다.
    // 보드(MainSketchPanel)처럼 회전한 평면 위에 그래프를 놓을 때는 그 평면의 축을 넣어야 한다.
    //   루트는 MvpWorkspaceLayout.MoveRoot 가 panel.right/up 기준으로 놓는데 자식만 월드 +X 로
    //   밀면, 보드가 유저를 향해 돌아간 각도만큼 자식이 평면을 벗어나 z 로 어긋난다.
    private Vector3 _layoutRight = Vector3.right;
    private Vector3 _layoutUp    = Vector3.up;

    // 회전한 작업 평면 위에 배치할 때 그 평면의 축을 알려준다. 0 벡터는 무시한다.
    public void SetLayoutBasis(Vector3 right, Vector3 up)
    {
        if (right.sqrMagnitude > 0.000001f) _layoutRight = right.normalized;
        if (up.sqrMagnitude    > 0.000001f) _layoutUp    = up.normalized;
    }

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

    // 키보드 직접 노드 생성 → WS NODE_CREATE. GraphSyncClient 가 구독해 송신한다.
    //   jobId(로컬 노드 id) / nodeText / parentNodeId(루트면 빈 문자열) / subGraphId(루트만 서버 발급값, 자식은 빈 문자열) / position
    //   (키보드 NODE_CREATE 는 PROPERTY 전용이라 node_type 은 보내지 않는다 — API 명세 2026-07-10.)
    // 음성 발화(LLM 확장)는 이 경로가 아니라 RequestNodeByUtterance 가 담당한다.
    public event Action<string, string, string, string, Vector3> OnNodeCreated;

    // 루트 노드 첫 제출 시 발행. 서버 서브그래프 발급(/api/sub_graph/generate)을 SubGraphApiClient 에 요청한다.
    // 발급 성공 후 SubGraphApiClient 가 SubmitRootNodeWithSubGraph 로 NODE_CREATE 를 잇는다.
    //   rootNodeId : 서브그래프 루트가 될 로컬 노드 id.
    public event Action<string> OnSubGraphRequested;

    // 로컬 GUID 로 PART 가 생성된 시점 발행. PartNodeApiClient 가 구독해
    //   POST /api/part_node/generate 로 서버 UUID 를 발급받고 ApplyServerNodeId 로 rekey 한다.
    // [2026-08-01] 이 이벤트가 없던 시절엔 RequestCreatePartNode(2-인자) 호출부가 서버에 등록되지 않아
    //   이후 NODE_TEXT_UPDATE / NODE_MOVE 가 전부 [NODE404] 로 거부됐다(퀘스트 실기 확인).
    //   호출부(AddPartPort / MvpWaterRocketGraphController 등)를 일일이 고치는 대신 여기서 일괄 처리한다.
    //   nodeId 를 명시한 3-인자 오버로드(= 서버가 이미 발급했거나 폴백 생성)는 발행하지 않는다.
    //   localNodeId / label / isGlobal / position
    public event Action<string, string, bool, Vector3> OnLocalPartNodeCreated;

    // 그래프 구조가 통째로 바뀐 시점(LoadGraph/RenderGraph, GRAPH_UPDATED 반영 완료 후) 발행.
    // 메인그래프 UI(MainSketchView)가 구독해 PART/ALL 포트를 다시 그린다.
    // (PROPERTY 서브그래프는 SpawnNodeView 로 이미 갱신되므로 이 이벤트는 메인 UI 재동기화용.)
    public event Action  OnGraphChanged;

    // 노드 "+" 발화 요청. GraphManager는 REST를 모르고 이벤트만 발행한다.
    // UtteranceApiClient가 구독해 POST /api/node/generate/utterance 후 응답 { node_id, node_text }로
    //   placeholder 를 ApplyServerNodeId 로 rekey(라벨=서버 node_text)한다.
    //   serverParentId    : 발화를 붙일 "기존(서버-known) 부모" id. 루트(부모 없음)면 null.
    //   utterance         : 입력 텍스트.
    //   placeholderNodeId : 사용자가 입력한 로컬 임시 노드 id. 서버 node_id 로 rekey 대상.
    public event Action<string, string, string> OnUtteranceNodeRequested;

    // 서버 ACK 로 로컬 임시 id 가 서버 발급 id 로 rekey 된 시점 발행.
    // Fusion 피어들은 아직 임시 id 로 노드를 들고 있으므로, 브리지가 이 이벤트를
    // GraphNetworkManager 로 전파해 모든 피어의 id 를 맞춘다(id 불일치로 이후
    // 이동/삭제 RPC 가 유실되는 것 방지).
    //   (oldId, newId, newText?) — newText 는 발화 경로에서 서버가 준 라벨(없으면 null).
    public event Action<string, string, string> OnNodeRekeyed;
    public event Action<string, string> OnEdgeRekeyed;   // (oldId, newId)

    // 자식 노드 활성/비활성 토글 시 발행(값이 실제로 바뀔 때만).
    // 브리지가 Fusion 으로 전파해 다른 참가자 화면의 초록선/흐림도 함께 바뀐다.
    public event Action<string, bool> OnNodeActiveChanged;

    // ─────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────
    private GraphData    _graphData;
    private NodeRegistry _nodeRegistry;
    private EdgeRegistry _edgeRegistry;

    private Dictionary<string, NodeView> _nodeViewMap;
    private Dictionary<string, EdgeView> _edgeViewMap;  // 신규: EdgeView 추적

    // 서버에 실제 등록된 노드 id 집합 (LoadGraph 로 유입되거나 ApplyServerNodeId 로 rekey 된 노드).
    // Reflow 위치 push(NODE_MOVE)는 이 집합의 노드에만 발행한다 → 로컬 전용 노드 NODE404 방지.
    private readonly HashSet<string> _serverKnownNodeIds = new HashSet<string>();

    // NODE_CREATE/서브그래프 생성 요청을 보냈으나 아직 ACK(rekey) 전인 노드. 같은 노드의 중복 생성 요청을 막는다
    // (ACK 전 텍스트를 바꿔 재제출하면 서버에 노드가 이중 생성되는 것 방지). ApplyServerNodeId 성공 또는 RemoveNode 시 해제.
    private readonly HashSet<string> _pendingCreateNodeIds = new HashSet<string>();

    // Reflow 후 위치 변화가 이 값(제곱거리) 미만이면 이동으로 보지 않는다(불필요한 NODE_MOVE 억제).
    private const float ReflowMoveEpsilonSqr = 0.001f * 0.001f;

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
        _serverKnownNodeIds.Clear();

        if (graphData == null)
        {
            Debug.LogWarning("[GraphManager] LoadGraph: graphData가 null입니다. 빈 그래프로 초기화합니다.");
            _graphData = new GraphData();
            return;
        }

        _graphData = graphData;
        if (_graphData.nodes == null) _graphData.nodes = new List<NodeData>();
        if (_graphData.edges == null) _graphData.edges = new List<EdgeData>();

        foreach (var node in _graphData.nodes)
        {
            _nodeRegistry.Register(node);
            if (!string.IsNullOrEmpty(node.node_id)) _serverKnownNodeIds.Add(node.node_id);
        }
        foreach (var edge in _graphData.edges) _edgeRegistry.Register(edge);

        BackfillSubGraphIds();
        BackfillIsGlobal();   // 루트 PART → ALL 유도
    }

    public GraphData GetGraphData() => _graphData;
    public float LayoutHSpacing => _layoutHSpacing;

    // 현재 그래프로 2D 그래프 스케치(/api/2d/generate/graph) 요청 JSON 생성(부분 생성 시 selectedPartNodeIds 지정).
    // 명세: connections = [{ part_node_id, node_id(root) }] + room_id/user_id.
    public string BuildGraphSketchRequestJson(string userId, ICollection<string> selectedPartNodeIds = null)
        => RegenerateConnectionBuilder.BuildGraphRequestJson(_graphData, userId, selectedPartNodeIds);

    // 현재 그래프의 적용 엣지를 connections 목록으로 반환(요청 조립용).
    public System.Collections.Generic.List<ConnectionDto> BuildGraphConnections(ICollection<string> selectedPartNodeIds = null)
        => RegenerateConnectionBuilder.Build(_graphData, selectedPartNodeIds);

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
        OnGraphChanged?.Invoke();   // 메인그래프 UI(PART/ALL) 재동기화
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
        _serverKnownNodeIds.Remove(nodeId);
        _pendingCreateNodeIds.Remove(nodeId);
        return true;
    }

    public NodeData GetNode(string nodeId) => _nodeRegistry.Get(nodeId);
    public EdgeData GetEdge(string edgeId) => _edgeRegistry.Get(edgeId);
    public List<NodeData> GetAllNodes()    => _nodeRegistry.GetAll();

    // 이 노드가 서버에 등록된(LoadGraph 유입 / ApplyServerNodeId rekey) 노드인지.
    // UI 는 이 값으로 "+"(자식 추가) 가능 여부를 판단한다 — 빈 placeholder(로컬 GUID)는 false.
    public bool IsServerKnown(string nodeId) => _serverKnownNodeIds.Contains(nodeId);

    public List<EdgeData> GetEdgesConnectedToNode(string nodeId) => _edgeRegistry.GetEdgesConnectedTo(nodeId);
    public List<EdgeData> GetEdgesIncomingToNode(string nodeId)  => _edgeRegistry.GetEdgesTo(nodeId);
    public List<EdgeData> GetEdgesOutgoingFromNode(string nodeId) => _edgeRegistry.GetEdgesFrom(nodeId);

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

        // 7. ALL 은 부품이 하나라도 있어야 연결할 수 있다 (사용자 확정 2026-08-13).
        //    ALL 은 "모든 부품에 적용되는 속성" 자리라, 적용될 부품이 없으면 의미가 없다.
        //    ALL 노드는 MainSketchView 가 보드에 항상 하나 자동 생성하므로(_autoCreateAllNode)
        //    "노드가 있으니 연결도 된다"가 되어 버린다 → 여기서 막는다.
        if (IsAllNode(toNode) && !HasAnyPartNode())
        {
            reason = "먼저 부품을 추가해 주세요. 부품이 있어야 전체에 연결할 수 있어요.";
            return false;
        }

        reason = null;
        return true;
    }

    // ALL = PART + is_global. (MainSketchView.Refresh 의 분류와 같은 기준)
    private static bool IsAllNode(NodeData node) =>
        node != null && node.NodeType == NodeType.PART && node.is_global;

    // ALL 이 아닌 PART(= 보드의 PartPort)가 하나라도 있는가.
    private bool HasAnyPartNode()
    {
        foreach (var node in _nodeRegistry.GetAll())
        {
            if (node == null || node.NodeType != NodeType.PART) continue;
            if (!node.is_global) return true;
        }
        return false;
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

        // 이벤트는 항상 발행한다(Fusion 전파가 이 이벤트를 탄다).
        //   서버 미등록 노드의 NODE_MOVE 억제는 서버 경계(GraphSyncClient)에서 처리하고,
        //   보류된 위치는 생성 ACK(ApplyServerNodeId) 시점에 한 번 동기화한다.
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

    // 노드 라벨칸(키보드) 제출 처리 — "키보드 직접 생성" 경로.
    //  - 서버 미등록 노드면: WS NODE_CREATE 로 직접 생성. node_id 는 서버가 발급하고, 클라는 로컬 노드 id 를
    //    job_id 로 실어 보낸다. 서버 ACK(job_id+node_id) 도착 시 ApplyServerNodeId 로 로컬 노드를 rekey 한다.
    //  - 이미 서버 등록된 노드면: NODE_TEXT_UPDATE(텍스트 수정, 새 노드 생성 아님).
    //  - LLM 확장이 필요한 음성 발화는 이 경로가 아니라 RequestNodeByUtterance 로.
    // 서버 계약(2026-07-10 API 명세): NODE_CREATE payload = { job_id, sub_graph_id(루트만), node_text, parent_node_id(루트 ""), position[x,y,z] }.
    //   - 루트(부모 없음): OnSubGraphRequested → SubGraphApiClient 가 sub_graph 발급 후 SubmitRootNodeWithSubGraph 로 NODE_CREATE.
    //   - 자식: OnNodeCreated 로 즉시 NODE_CREATE(sub_graph_id="" → 서버가 부모에서 유도).
    //   → 서버-known 표시/‘+’ 활성화는 낙관적으로 하지 않고 ACK(ApplyServerNodeId)에서 한다.
    public void RequestSubmitNodeText(string nodeId, string text)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestSubmitNodeText 실패: 존재하지 않는 node_id ({nodeId})");
            return;
        }

        string t = (text ?? "").Trim();
        if (string.IsNullOrEmpty(t)) return;   // 빈 텍스트는 서버가 GRAPH400 으로 거부 → 생성 안 함.

        node.label     = t;
        node.node_text = t;

        if (_serverKnownNodeIds.Contains(nodeId))
        {
            // 이미 서버에 있는 노드 → 텍스트 수정.
            OnNodeTextUpdated?.Invoke(nodeId, t);
            return;
        }

        // 이미 생성 요청이 진행 중이면(ACK 대기) 중복 발행 금지 — 로컬 텍스트만 갱신된 채 유지한다.
        if (_pendingCreateNodeIds.Contains(nodeId))
        {
            Debug.Log($"[GraphManager] NODE_CREATE 재요청 무시: 이미 생성 진행 중(ACK 대기). node_id={nodeId}");
            return;
        }

        // 첫 제출 → 서버에 직접 생성. 부모는 이 노드의 PROPERTY 부모(루트면 없음).
        string parentId = GetPropertyParent(nodeId);
        if (string.IsNullOrEmpty(parentId))
        {
            // 루트(부모 없음): sub_graph_id 없이 바로 NODE_CREATE 를 보낸다.
            //   서버가 parent_node_id 없음 + PROPERTY 인 경우 서브그래프를 자동 생성한다
            //   (graph_interaction_service.py:354-361 create_sub_graph). 빈 문자열은 서버에서 None 으로 파싱된다.
            //
            // [정정 2026-08-01] 옛 명세의 선행 REST(/api/sub_graph/generate)는 서버에 구현된 적이 없다.
            //   그걸 기다리느라 루트 노드가 서버에 영영 등록되지 않았고, 이후 NODE_TEXT_UPDATE / NODE_MOVE 가
            //   전부 [NODE404] 로 거부됐다(퀘스트 실기 확인 — 주먹 제스처로 만든 루트 PROPERTY).
            //   서버가 해당 REST 를 추가하면 OnSubGraphRequested / SubmitRootNodeWithSubGraph 경로를 되살리면 된다.
            _pendingCreateNodeIds.Add(nodeId);
            OnNodeCreated?.Invoke(nodeId, t, "", "", node.Position);
            return;
        }
        if (!_serverKnownNodeIds.Contains(parentId))
        {
            Debug.LogWarning($"[GraphManager] NODE_CREATE 보류: 부모 노드가 아직 서버 미등록입니다(부모를 먼저 입력하세요). parent={parentId}");
            return;
        }

        // 자식 노드: 로컬 id 를 job_id 로 실어 NODE_CREATE 발행(서버가 node_id 발급). sub_graph_id 는 서버가 부모에서 유도하므로 빈 값.
        // 서버-known 표시/"+"(자식 추가) 활성화는 여기서 하지 않고 ACK(ApplyServerNodeId)에서 처리한다(낙관적 선반영 금지).
        _pendingCreateNodeIds.Add(nodeId);
        OnNodeCreated?.Invoke(nodeId, t, parentId, "", node.Position);
    }

    // SubGraphApiClient 가 /api/sub_graph/generate 성공 후 호출한다. 루트 노드에 서버 sub_graph_id 를 반영하고
    // NODE_CREATE(parent 없음 + sub_graph_id)를 발행한다. ACK 는 ApplyServerNodeId 로 rekey.
    //   rootNodeId : OnSubGraphRequested 로 넘겼던 로컬 루트 노드 id. subGraphId : 서버 발급 sub_graph_id.
    public void SubmitRootNodeWithSubGraph(string rootNodeId, string subGraphId)
    {
        var node = _nodeRegistry.Get(rootNodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] SubmitRootNodeWithSubGraph 실패: 존재하지 않는 node_id ({rootNodeId})");
            return;
        }
        if (string.IsNullOrEmpty(subGraphId))
        {
            Debug.LogWarning("[GraphManager] SubmitRootNodeWithSubGraph 실패: subGraphId 가 비어 있습니다.");
            return;
        }
        if (string.IsNullOrEmpty(node.node_text))
        {
            Debug.LogWarning($"[GraphManager] SubmitRootNodeWithSubGraph 보류: node_text 가 비어 있습니다. (node_id={rootNodeId})");
            return;
        }

        node.sub_graph_id = subGraphId;   // 로컬 자기참조 값 → 서버 sub_graph_id 로 교체
        OnNodeCreated?.Invoke(rootNodeId, node.node_text, "", subGraphId, node.Position);
    }

    // 서버 NODE_CREATE ACK / 발화 노드 생성 응답 수신 시 호출한다. 요청 때 실어 보낸 job_id(로컬 노드 id)로
    // 로컬 임시 노드를 찾아 서버가 발급한 node_id 로 rekey 한다. 이 시점에 비로소 서버-known 이 되어 자식 "+" 가 활성화된다.
    //   jobId         : 요청 때 보낸 로컬 노드 id (현재 이 노드의 node_id).
    //   serverNodeId  : 서버가 발급한 진짜 node_id.
    //   serverNodeText: (선택) 발화 경로에서 서버가 LLM 으로 추출한 node_text. 주면 라벨/텍스트를 갱신한다.
    public void ApplyServerNodeId(string jobId, string serverNodeId, string serverNodeText = null)
    {
        if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(serverNodeId))
        {
            Debug.LogWarning("[GraphManager] ApplyServerNodeId 실패: jobId 또는 serverNodeId 가 비어 있습니다.");
            return;
        }
        if (jobId == serverNodeId)
        {
            _serverKnownNodeIds.Add(serverNodeId);   // 이미 같은 id → 서버-known 표시만 보장
            return;
        }

        var node = _nodeRegistry.Get(jobId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] ApplyServerNodeId 실패: job_id 노드를 찾을 수 없습니다. (job_id={jobId})");
            return;
        }

        // 1) 노드 레지스트리 rekey (NodeData 는 동일 객체라 _graphData.nodes 도 함께 반영됨)
        _nodeRegistry.Unregister(jobId);
        node.node_id = serverNodeId;
        _nodeRegistry.Register(node);

        // 1-2) 발화 경로: 서버가 준 node_text(LLM 키워드)로 라벨/텍스트 갱신(view.Bind 전에 반영해야 라벨에 보임).
        if (!string.IsNullOrEmpty(serverNodeText))
        {
            node.node_text = serverNodeText;
            node.label     = serverNodeText;
        }

        // 2) 이 노드를 참조하는 엣지의 from/to 를 서버 id 로 치환 (EdgeData 인플레이스 → 레지스트리·graphData 동시 반영)
        foreach (var edge in _edgeRegistry.GetEdgesConnectedTo(jobId))
        {
            if (edge.from_node_id == jobId) edge.from_node_id = serverNodeId;
            if (edge.to_node_id   == jobId) edge.to_node_id   = serverNodeId;
        }

        // 서버-known 표시/생성 진행중 해제를 뷰 재바인드 **전에** 한다 —
        // NodeActionPanel.Bind 가 IsServerKnown(nodeId) 로 "+"(자식 추가) 활성화를 결정하므로,
        // 이 순서가 아니면 새로 만든 자식의 "+"가 비활성으로 굳는다.
        _serverKnownNodeIds.Remove(jobId);
        _serverKnownNodeIds.Add(serverNodeId);
        _pendingCreateNodeIds.Remove(jobId);

        // 3) NodeView 맵 키 이동 + 재바인드
        if (_nodeViewMap.TryGetValue(jobId, out NodeView view))
        {
            _nodeViewMap.Remove(jobId);
            _nodeViewMap[serverNodeId] = view;
            if (view != null)
            {
                view.gameObject.name = $"Node_{serverNodeId}";
                view.Bind(node, this);
                if (view.ActionPanel != null) view.ActionPanel.Bind(serverNodeId, this);
            }
        }

        // 4) 루트 자기참조 sub_graph_id 보정 후 백필
        if (node.sub_graph_id == jobId) node.sub_graph_id = serverNodeId;
        BackfillSubGraphIds();

        Debug.Log($"[GraphManager] ApplyServerNodeId: job_id={jobId} → node_id={serverNodeId}");
        OnNodeRekeyed?.Invoke(jobId, serverNodeId, serverNodeText);

        // [2026-08-01] ACK 이전의 이동은 RequestMoveNode 에서 보류됐다(서버 미등록 → NODE404).
        //   이제 서버-known 이 됐으므로 현재 위치를 한 번 동기화한다.
        //   (NODE_CREATE 는 생성 시점 위치로 저장되므로, 그 뒤 옮긴 위치가 서버에 반영되지 않는 문제 보정.)
        OnNodeMoved?.Invoke(serverNodeId, node.Position);
    }

    // 서버 EDGE_CREATE ACK 수신 시 호출한다(GraphSyncClient). 요청 때 실어 보낸 job_id(= 로컬 edge_id)로
    // 로컬 교차 엣지를 찾아 서버가 발급한 edge_id 로 rekey 한다. 이후 EDGE_DELETE 가 서버 edge_id 로 나가 404 를 피한다.
    //   jobId       : 요청 때 보낸 로컬 edge_id (현재 이 엣지의 edge_id).
    //   serverEdgeId: 서버가 발급한 진짜 edge_id.
    public void ApplyServerEdgeId(string jobId, string serverEdgeId)
    {
        if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(serverEdgeId))
        {
            Debug.LogWarning("[GraphManager] ApplyServerEdgeId 실패: jobId 또는 serverEdgeId 가 비어 있습니다.");
            return;
        }
        if (jobId == serverEdgeId) return;

        var edge = _edgeRegistry.Get(jobId);
        if (edge == null)
        {
            Debug.LogWarning($"[GraphManager] ApplyServerEdgeId 실패: job_id 엣지를 찾을 수 없습니다. (job_id={jobId})");
            return;
        }

        // EdgeData 는 동일 객체라 edge_id 를 바꾸면 _graphData.edges 도 함께 반영된다.
        _edgeRegistry.Unregister(jobId);
        edge.edge_id = serverEdgeId;
        _edgeRegistry.Register(edge);

        if (_edgeViewMap.TryGetValue(jobId, out EdgeView view))
        {
            _edgeViewMap.Remove(jobId);
            _edgeViewMap[serverEdgeId] = view;
            if (view != null) view.gameObject.name = $"Edge_{serverEdgeId}";
        }

        Debug.Log($"[GraphManager] ApplyServerEdgeId: job_id={jobId} → edge_id={serverEdgeId}");
        OnEdgeRekeyed?.Invoke(jobId, serverEdgeId);
    }

    // 발화 입력칸(노드 텍스트칸) 제출 처리.
    //  - 입력 텍스트를 로컬 placeholder 노드에 즉시 반영(오프라인 폴백·서버 실패 시 유지).
    //  - 서버 확장 요청은 이벤트로 위임한다(REST는 UtteranceApiClient 담당).
    //  - 서버 모델 정렬: parent 는 "방금 만든 이 노드"가 아니라 이 노드가 매달린 **기존(서버-known) 부모**.
    //    부모가 없으면 루트 발화(parent=null → 서버가 새 서브그래프 생성). 성공 시 이 placeholder 는 제거된다.
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

        // 발화를 붙일 서버 부모. placeholder 의 PROPERTY 부모(= "+" 눌렀던 기존 노드).
        string serverParentId = GetPropertyParent(nodeId);

        // 부모가 있는데 아직 서버에 등록 전이면 서버가 404 → 전송 차단(로컬 텍스트만 유지).
        // (부모를 먼저 발화로 확정하면 서버-known 이 되어 이후 자식 발화가 가능해진다.)
        if (!string.IsNullOrEmpty(serverParentId) && !_serverKnownNodeIds.Contains(serverParentId))
        {
            Debug.LogWarning($"[GraphManager] 발화 전송 보류: 부모 노드가 아직 서버 미등록입니다(부모를 먼저 입력하세요). parent={serverParentId}");
            return;
        }

        OnUtteranceNodeRequested?.Invoke(serverParentId, text, nodeId);
    }

    // (구 MergeServerGraph / RemoveLocalNodeWithView 는 발화 경로가 ApplyServerNodeId rekey 로 전환되며 제거됨 —
    //  전체 그래프 반영은 콜드로드/GRAPH_UPDATED 의 LoadGraph 경로가 담당한다.)

    // 메인 그래프 UI (AddPartPort) 전용 PART 노드 즉시 생성
    public string RequestCreatePartNode(string label, bool isGlobal = false)
        => RequestCreatePartNode(label, isGlobal, null);

    // nodeId 지정 오버로드. 서버(part_node/generate)가 발급한 UUID로 로컬 PART를 생성할 때 사용한다.
    // nodeId 가 null/blank 면 로컬 GUID 를 발급하고 OnLocalPartNodeCreated 를 발행해
    //   PartNodeApiClient 가 서버 등록 + rekey 를 잇도록 한다(GraphManager 는 서버를 모른다).
    // nodeId 를 명시하면 이미 서버가 발급했거나(REST 성공) 오프라인 폴백이므로 이벤트를 발행하지 않는다
    //   — 이중 등록 및 폴백 재귀를 막는다.
    public string RequestCreatePartNode(string label, bool isGlobal, string nodeId)
    {
        bool locallyIssued = string.IsNullOrEmpty(nodeId);

        var node = new NodeData
        {
            node_id   = locallyIssued ? Guid.NewGuid().ToString() : nodeId,
            type      = "PART",
            label     = label,
            position  = new float[] { 0f, 0f, 0f },
            is_global = isGlobal
        };
        if (!AddNode(node)) return null;

        if (locallyIssued)
            OnLocalPartNodeCreated?.Invoke(node.node_id, label, isGlobal, node.Position);

        return node.node_id;
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
    //   (서버 저장 표준은 반대인 PART → PROPERTY/REFERENCE 이며, 그 변환은 WS 경계인
    //    GraphSyncClient.HandleEdgeCreated / GraphSnapshotDto.ToGraphData 가 전담한다.)
    // PROPERTY의 어느 멤버에 드롭하더라도 적용 엣지의 from은 반드시 그 서브그래프 root로 정규화한다.
    // REFERENCE는 자체 node_id를 그대로 사용한다.
    public bool RequestConnectFromPort(string portNodeId, string subgraphNodeId)
    {
        var subgraphNode = _nodeRegistry.Get(subgraphNodeId);
        if (subgraphNode == null)
        {
            Debug.LogWarning($"[GraphManager] RequestConnectFromPort 실패: 서브그래프 노드를 찾을 수 없습니다. node_id={subgraphNodeId}");
            return false;
        }

        string appliedNodeId = subgraphNode.NodeType == NodeType.PROPERTY
            ? GetPropertyRootNodeId(subgraphNodeId)
            : subgraphNodeId;

        if (appliedNodeId != subgraphNodeId)
            Debug.Log($"[GraphManager] 적용 연결 정규화: drop={subgraphNodeId} → root={appliedNodeId}, part={portNodeId}");

        return RequestConnectNodes(appliedNodeId, portNodeId);
    }

    // 자손 PROPERTY + 종속 REFERENCE 를 post-order 로 캐스케이드 삭제한 뒤 Reflow.
    // 서버는 자식 캐스케이드를 스스로 처리하므로(서버팀 확정 2026-07-04), WS NODE_DELETE 는
    //   삭제 root(nodeId) 하나만 발행한다. 로컬은 자손 전체를 즉시 제거한다.
    // emitSync=false 이면 OnNodeDeleted(→ WS NODE_DELETE) 를 발행하지 않는다(로컬 전용 삭제).
    //   PART 노드는 REST(part_node/delete)로 동기화하므로 PartNodeApiClient 가 emitSync:false 로 호출한다.
    //   PROPERTY 등 기존 호출부는 기본값(true) 유지.
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
        }

        // 서버는 root node_id 하나만 받아 자식 노드·엣지까지 캐스케이드 삭제한다(서버팀 확정 2026-07-04).
        // → 로컬은 자손 전체를 지우되, WS NODE_DELETE 는 삭제 root 하나만 발행한다.
        if (emitSync) OnNodeDeleted?.Invoke(nodeId);

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

        if (string.IsNullOrEmpty(trimmed)) return true;

        // 이벤트는 항상 발행한다 — 구독자가 서버(GraphSyncClient)만이 아니라
        //   Fusion 전파(MvpGraphNetworkBridge)와 MVP 시나리오(MvpWaterRocketGraphController)에도 걸려 있다.
        //   서버 미등록 노드의 NODE_TEXT_UPDATE 억제는 서버 경계(GraphSyncClient)에서 처리한다.
        OnNodeTextUpdated?.Invoke(nodeId, trimmed);

        // [2026-08-01] 제스처 생성 경로(MvpSpatialNodeGestureController → RequestCreateRootPropertyNode → 여기)는
        //   서버 등록을 한 번도 거치지 않아 이후 모든 뮤테이션이 [NODE404] 로 죽었다(퀘스트 실기 확인).
        //   미등록이면 텍스트 수정에 앞서 "생성"이 필요하므로 생성 경로로 넘긴다(루트/자식 분기 포함).
        if (!_serverKnownNodeIds.Contains(nodeId))
            RequestSubmitNodeText(nodeId, trimmed);

        return true;
    }

    // ─────────────────────────────────────────────
    // MVP 자식 활성화 (활성 자식만 부모로부터 초록선)
    // ─────────────────────────────────────────────

    // 이 노드가 "활성" 상태인지. (자식 PROPERTY 전용 의미)
    public bool IsNodeActive(string nodeId)
    {
        var node = _nodeRegistry.Get(nodeId);
        return node != null && node.is_active;
    }

    // 자식 노드의 활성/비활성 토글용. 데이터만 바꾼다(선/흐림 갱신은 MVP UI 담당).
    public bool RequestSetNodeActive(string nodeId, bool active)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphManager] RequestSetNodeActive 실패: 존재하지 않는 node_id ({nodeId})");
            return false;
        }
        if (node.is_active == active)
            return true;   // 변화 없음 → 이벤트/네트워크 전파 생략
        node.is_active = active;
        OnNodeActiveChanged?.Invoke(nodeId, active);
        return true;
    }

    // 이 노드의 PROPERTY 부모 id (없으면 null). MVP 활성화 UI가 자식 여부 판별/선 연결에 사용.
    public string GetPropertyParentId(string nodeId) => GetPropertyParent(nodeId);

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
    // 재배치로 실제 위치가 바뀐 "서버 등록 노드"는 서버 영속화를 위해 NODE_MOVE(OnNodeMoved)를 발행한다.
    //   (실시간 동기화는 Photon, 서버는 콜드로드/영속 담당 — 2026-07-04 방향. [[project-reflow-sync]])
    public void ReflowAllSubtrees()
    {
        // 재배치 전 위치 스냅샷 (이동 감지용)
        var before = new Dictionary<string, Vector3>();
        foreach (var n in _nodeRegistry.GetAll())
            before[n.node_id] = n.Position;

        var roots = FindLayoutRoots();

        foreach (var rootId in roots)
        {
            var rootNode = _nodeRegistry.Get(rootId);
            if (rootNode == null) continue;
            // 루트 위치는 건드리지 않고 자식들만 루트 기준으로 재배치
            AssignChildPositions(rootId, rootNode.Position, 0f, 0f);
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

        EmitReflowMoves(before);
    }

    // Reflow로 위치가 바뀐 노드 중 서버 등록 노드에만 NODE_MOVE 를 발행한다.
    // 로컬 전용 노드(아직 서버 미등록)는 제외 → 서버 NODE404 방지.
    private void EmitReflowMoves(Dictionary<string, Vector3> before)
    {
        if (OnNodeMoved == null) return;

        foreach (var node in _nodeRegistry.GetAll())
        {
            if (!_serverKnownNodeIds.Contains(node.node_id)) continue;
            if (!before.TryGetValue(node.node_id, out Vector3 prev)) continue;   // 이번 Reflow 직전엔 없던 노드
            if ((node.Position - prev).sqrMagnitude < ReflowMoveEpsilonSqr) continue;
            OnNodeMoved.Invoke(node.node_id, node.Position);
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

    // 루트를 원점으로 오른쪽(dx)·위(dy) 만큼 떨어진 곳에 배치하고 자식도 재귀 배치 (오른쪽 방향 트리)
    private void AssignPositions(string nodeId, Vector3 rootOrigin, float dx, float dy)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null) return;
        node.SetPosition(rootOrigin + _layoutRight * dx + _layoutUp * dy);
        AssignChildPositions(nodeId, rootOrigin, dx, dy);
    }

    // 이 노드 자체는 움직이지 않고 자식들만 배치한다.
    // dx: 루트에서 오른쪽으로 떨어진 거리(depth 단계), dy: 이 서브트리의 세로 중심.
    // 깊이(작업 평면과의 거리)는 루트 위치가 그대로 갖고 있다 — 오프셋은 평면 안에서만 움직인다.
    private void AssignChildPositions(string nodeId, Vector3 rootOrigin, float dx, float dy)
    {
        var children = GetPropertyChildren(nodeId);
        if (children.Count == 0) return;

        float totalHeight = 0f;
        foreach (var child in children)
            totalHeight += CalculateSubtreeWidth(child);

        float cursor = dy - (totalHeight / 2f) * _layoutVSpacing;
        foreach (var child in children)
        {
            float childHeight = CalculateSubtreeWidth(child);
            float childCenter = cursor + (childHeight / 2f) * _layoutVSpacing;
            AssignPositions(child, rootOrigin, dx + _layoutHSpacing, childCenter);
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

    // PROPERTY 멤버가 속한 트리의 root node_id를 반환한다.
    // PART 적용 및 2D 생성 계약에서 PROPERTY node_id는 leaf가 아니라 항상 이 root를 사용한다.
    public string GetPropertyRootNodeId(string nodeId)
    {
        var node = _nodeRegistry.Get(nodeId);
        if (node == null || node.NodeType != NodeType.PROPERTY) return nodeId;

        var visited = new HashSet<string>();
        string cur = nodeId;
        while (visited.Add(cur))
        {
            string parent = GetPropertyParent(cur);
            if (string.IsNullOrEmpty(parent)) return cur;
            cur = parent;
        }

        Debug.LogWarning($"[GraphManager] PROPERTY root 탐색 중 사이클 감지: node_id={nodeId}");
        return nodeId;
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

    // 서버는 is_global(ALL 표시)을 주지 않는다. 합의(2026-07-04): 루트 PART(parent_node_id 없음)를
    // ALL 로 유도한다. 하위 PART(parent 있음)는 일반 PART. 서버-known PART 에만 적용하고,
    // 로컬 전용 PART(RequestCreatePartNode 가 넘긴 is_global)는 그 값을 유지한다.
    private void BackfillIsGlobal()
    {
        foreach (var node in _nodeRegistry.GetAll())
        {
            if (node == null || node.NodeType != NodeType.PART) continue;
            if (!_serverKnownNodeIds.Contains(node.node_id)) continue;
            node.is_global = string.IsNullOrEmpty(node.parent_node_id);
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
