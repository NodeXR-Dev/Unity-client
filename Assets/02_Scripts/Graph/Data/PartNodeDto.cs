// 서버 파트 노드 CRUD REST API DTO.
// 협의(2026-07-01): PART 노드 생성/수정/삭제는 REST로 일원화(WS NODE_* 미사용).
//   - POST   /api/part_node/generate  { room_id, utterance }
//   - PATCH  /api/part_node/modify    { room_id, part_node_id, part_node_text }
//   - DELETE /api/part_node/delete    { room_id, part_node_id }
// 응답 봉투: { isSuccess, code, message, result:{ room_id, part_node_id, part_node_text } }
//   [주의] 명세 원문 키 "isSuccess "(뒤 공백)은 오타로 간주하고 isSuccess 로 파싱.
//          성공 판정은 HTTP 2xx + result.part_node_id 유무를 기준으로 하고 isSuccess 는 보조.

[System.Serializable]
public class PartNodeGenerateRequest
{
    public string room_id;
    public string utterance;
}

[System.Serializable]
public class PartNodeModifyRequest
{
    public string room_id;
    public string part_node_id;
    public string part_node_text;
}

[System.Serializable]
public class PartNodeDeleteRequest
{
    public string room_id;
    public string part_node_id;
}

[System.Serializable]
public class PartNodeResult
{
    public string room_id;
    public string part_node_id;
    public string part_node_text;
}

[System.Serializable]
public class PartNodeResponse
{
    public bool isSuccess;
    public string code;      // 예: PART_NODE200 / PART_NODE201
    public string message;
    public PartNodeResult result;
}
