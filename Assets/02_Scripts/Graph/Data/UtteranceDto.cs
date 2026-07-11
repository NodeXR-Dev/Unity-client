// 서버 노드 생성(발화) REST API DTO.
// API 명세(2026-07-10): 노드 "+" → 발화 입력 → POST /api/node/generate/utterance.
//   서버가 utterance 저장 + LLM 키워드 추출 + Semantic Memory 갱신 후 **생성된 노드 1개**를 응답.
//   req: { room_id, user_id, node_type, parent_node_id, utterance, position:[x,y,z] }
//   res: { isSuccess, code:"NODE200", message, result:{ node_id, node_text } }
// [변경] 구 /api/utterances(응답=전체 그래프, MergeServerGraph)에서 → 응답이 { node_id, node_text } 단건으로 바뀜.
//        클라는 로컬 placeholder 를 이 node_id 로 rekey 하고 node_text(LLM 키워드)로 라벨을 갱신한다(ApplyServerNodeId).
// [루트/자식 분리] parent_node_id 가 서버에서 UUID|None 타입이면 빈 문자열이 422 로 거부될 수 있어,
//        루트(부모 없음)는 parent_node_id 필드를 아예 갖지 않는 별도 DTO 로 보낸다(구 구현과 동일한 안전 패턴).

[System.Serializable]
public class UtteranceRequest
{
    public string room_id;
    public string user_id;
    public string node_type;
    public string parent_node_id;
    public string utterance;
    public float[] position;   // [x, y, z]
}

// 루트(부모 없음) 발화용 — parent_node_id 필드 없음(서버가 새 서브그래프에 루트 생성).
[System.Serializable]
public class UtteranceRootRequest
{
    public string room_id;
    public string user_id;
    public string node_type;
    public string utterance;
    public float[] position;   // [x, y, z]
}

// 응답 result: 생성된 노드 단건(node_id, node_text).
[System.Serializable]
public class UtteranceResult
{
    public string node_id;
    public string node_text;   // 서버가 LLM 으로 추출한 텍스트(라벨로 채택)
}

[System.Serializable]
public class UtteranceResponse
{
    public bool isSuccess;
    public string code;       // 예: NODE200
    public string message;
    public UtteranceResult result;
}
