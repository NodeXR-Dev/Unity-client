// 서버 노드 생성(발화) REST API DTO.
//
// 실제 서버(2026-08-08 확인): POST /api/utterances
//   req: { room_id, user_id, parent_node_id?, parent_node_position?, utterance }
//   res: { isSuccess, code:"BTUTT200", message,
//          result: { room_id, graph_version, core_2d_image?, sub_graphs:[ { sub_graph_id, root_node_id, nodes[], edges[] } ] } }
//
// 이전 구현은 명세 초안(2026-07-10)의 POST /api/node/generate/utterance +
// 단건 응답 { node_id, node_text } 를 기준으로 선구현돼 있었는데 서버에 그 경로가 없어
// 항상 404 였다. 그러면 속성 노드가 서버에 등록되지 않아 서버 그래프 스냅샷에서 누락되고,
// 2D 생성이 "Input Snapshot에서 생성 Connection의 Node를 찾을 수 없습니다"로 실패한다.
//
// node_type 은 서버가 받지 않는다(발화 내용으로 서버가 판단). position 은
// parent_node_position(부모 노드 위치)으로 이름과 의미가 다르다.

[System.Serializable]
public class UtteranceRequest
{
    public string room_id;
    public string user_id;
    public string parent_node_id;
    public string utterance;
    public float[] parent_node_position;   // 부모 노드 위치 [x, y, z]
}

// 루트(부모 없음) 발화용 — parent_node_id 필드 자체를 보내지 않는다.
// 서버 타입이 UUID|None 이라 빈 문자열은 422 로 거부된다.
[System.Serializable]
public class UtteranceRootRequest
{
    public string room_id;
    public string user_id;
    public string utterance;
}

[System.Serializable]
public class UtteranceNode
{
    public string node_id;
    public string type;         // PART / PROPERTY / REFERENCE
    public string node_text;
    public float[] position;
    public string parent_node_id;
    public bool used_in_generation;
}

[System.Serializable]
public class UtteranceEdge
{
    public string edge_id;
    public string from_node_id;
    public string to_node_id;
    public string label;
    public bool used_in_generation;
}

// 서버는 발화 하나로 노드 여러 개(서브그래프)를 만든다.
// 클라는 그 중 root_node_id 를 로컬 placeholder 에 매핑한다.
// PROPERTY→PART 연결 시 보내는 node_id 가 서브그래프의 root 이므로
// (CLAUDE.md 규약) root 만 일치하면 서버가 하위 체인을 탐색해 문맥을 만든다.
[System.Serializable]
public class UtteranceSubGraph
{
    public string sub_graph_id;
    public string root_node_id;
    public UtteranceNode[] nodes;
    public UtteranceEdge[] edges;
}

[System.Serializable]
public class UtteranceResult
{
    public string room_id;
    public int graph_version;
    public UtteranceSubGraph[] sub_graphs;
}

[System.Serializable]
public class UtteranceResponse
{
    public bool isSuccess;
    public string code;       // BTUTT200
    public string message;
    public UtteranceResult result;
}
