// 서버 노드 생성(발화) REST API DTO.
// 회의 확정(2026-07-02): 노드 "+" → 발화 입력 → POST /api/utterances.
//   서버가 utterance 저장 + LLM 키워드 추출 + Semantic Memory 갱신 후 추천 그래프를 응답.
//   req: { room_id, user_id, parent_node_id, parent_node_position:[x,y,z], utterance }
//   res: { isSuccess, code:"GRAPH200", message, result:{ room_id, graph:{ graph_version, nodes[], edges[] } } }
// [주의] 응답 노드의 position 은 신뢰하지 않고 클라 reflow 규칙으로 재배치한다.
//        명세 키 "isSuccess "(뒤 공백)은 오타로 간주. 성공 판정은 HTTP 2xx + result.graph 유무.

[System.Serializable]
public class UtteranceRequest
{
    public string room_id;
    public string user_id;
    public string parent_node_id;
    public float[] parent_node_position;   // [x, y, z]
    public string utterance;
}

[System.Serializable]
public class UtteranceResult
{
    public string room_id;
    public GraphData graph;   // graph_version / nodes / edges (GraphData 재사용)
}

[System.Serializable]
public class UtteranceResponse
{
    public bool isSuccess;
    public string code;       // 예: GRAPH200
    public string message;
    public UtteranceResult result;
}
