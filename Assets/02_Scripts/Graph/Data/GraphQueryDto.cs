using System.Collections.Generic;

// GET /api/graph (그래프 콜드로드) 응답 DTO + flat GraphData 매퍼.
// 서버 응답은 sub_graphs 로 중첩되어 있고(각 서브그래프가 nodes/edges 를 가짐), 클라 GraphData 는
// flat(nodes/edges) 이므로 ToGraphData() 로 평탄화한다. sub_graph_id 는 서브그래프 레벨 값이므로
// 각 노드에 그대로 채워 넣는다(GraphManager.LoadGraph 의 BackfillSubGraphIds 가 덮어쓰지 않음).
//
// 봉투: { isSuccess, code:"GRAPH200", message, result: {...} }
// GRAPH_UPDATED WS payload({ graph: {...} })도 GraphSnapshotDto 구조가 동일하므로 재사용 대상이다.
// (코드베이스 컨벤션에 맞춰 전역 네임스페이스로 둔다.)

[System.Serializable]
public class GraphQueryResponse
{
    public bool isSuccess;
    public string code;
    public string message;
    public GraphSnapshotDto result;
}

// GET /api/graph 의 result, GRAPH_UPDATED 의 payload.graph 가 공유하는 그래프 스냅샷 형태.
[System.Serializable]
public class GraphSnapshotDto
{
    public string room_id;
    public int graph_version;
    public CoreImageDto core_2d_image;
    public List<SubGraphDto> sub_graphs = new List<SubGraphDto>();

    // 중첩 sub_graphs → flat GraphData 로 평탄화. 각 노드에 소속 sub_graph_id 를 채운다.
    // 현재 서버의 PART → PROPERTY/REFERENCE 적용 엣지는 로컬 표준 PROPERTY/REFERENCE → PART로 뒤집는다.
    public GraphData ToGraphData()
    {
        var graph = new GraphData
        {
            room_id       = room_id,
            graph_version = graph_version,
            nodes         = new List<NodeData>(),
            edges         = new List<EdgeData>(),
        };

        if (sub_graphs == null) return graph;

        var typeByNodeId = new Dictionary<string, NodeType>();

        // 교차 엣지가 다른 sub_graph의 노드를 가리킬 수 있으므로 노드를 먼저 전부 수집한다.
        foreach (var sg in sub_graphs)
        {
            if (sg?.nodes == null) continue;

            foreach (var n in sg.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.node_id)) continue;
                var node = new NodeData
                {
                    node_id            = n.node_id,
                    type               = n.type,
                    node_text          = n.node_text,
                    label              = n.node_text,
                    position           = n.position,
                    parent_node_id     = n.parent_node_id,
                    sub_graph_id       = sg.sub_graph_id,
                    used_in_generation = n.used_in_generation,
                    data               = n.data,
                };
                graph.nodes.Add(node);
                typeByNodeId[node.node_id] = node.NodeType;
            }
        }

        foreach (var sg in sub_graphs)
        {
            if (sg?.edges == null) continue;

            foreach (var e in sg.edges)
            {
                if (e == null || string.IsNullOrEmpty(e.edge_id)) continue;

                string localFromNodeId = e.from_node_id;
                string localToNodeId   = e.to_node_id;

                if (typeByNodeId.TryGetValue(e.from_node_id, out NodeType fromType) &&
                    typeByNodeId.TryGetValue(e.to_node_id, out NodeType toType) &&
                    fromType == NodeType.PART &&
                    (toType == NodeType.PROPERTY || toType == NodeType.REFERENCE))
                {
                    localFromNodeId = e.to_node_id;
                    localToNodeId   = e.from_node_id;
                }

                graph.edges.Add(new EdgeData
                {
                    edge_id            = e.edge_id,
                    from_node_id       = localFromNodeId,
                    to_node_id         = localToNodeId,
                    used_in_generation = e.used_in_generation,
                });
            }
        }

        return graph;
    }
}

[System.Serializable]
public class CoreImageDto
{
    public string asset_id;
    public string image_url;
    public string mime_type;
    public int width;
    public int height;
}

[System.Serializable]
public class SubGraphDto
{
    public string sub_graph_id;
    public string root_node_id;
    public List<GraphNodeDto> nodes = new List<GraphNodeDto>();
    public List<GraphEdgeDto> edges = new List<GraphEdgeDto>();
}

// 서버 그래프 스냅샷의 노드. (NODE_CREATE 요청 DTO 와 다른, 조회 전용 형태.)
[System.Serializable]
public class GraphNodeDto
{
    public string node_id;
    public string type;           // "PART" | "PROPERTY" | "REFERENCE"
    public string node_text;      // REFERENCE 는 null 가능
    public float[] position;      // [x, y, z]
    public string parent_node_id; // 루트는 null
    public bool used_in_generation;
    public NodeAssetData data;    // REFERENCE 자산 정보, 그 외 {}
}

[System.Serializable]
public class GraphEdgeDto
{
    public string edge_id;
    public string from_node_id;
    public string to_node_id;
    public string label;          // 관계는 from/to 타입으로 해석 → 저장만(EdgeData 엔 미보존)
    public bool used_in_generation;
}
