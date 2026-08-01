// 서버 파트 노드 CRUD REST API DTO.
// 협의(2026-07-01): PART 노드 생성/수정/삭제는 REST로 일원화(WS NODE_* 미사용).
// 실서버 검증(2026-07-30, app/api/part_node.py + app/schema/graph/part_node_request.py):
//   - POST   /api/part_node/generate  { room_id, text, position:[x,y,z] }      → code PART_NODE200
//   - PATCH  /api/part_node/modify    { room_id, part_node_id, part_node_text } → code PART_NODE201
//   - DELETE /api/part_node/delete    { room_id, part_node_id }                 → code PART_NODE202
// [정정] 옛 명세(2026-07-10)의 발화/키보드 2분기(`generate` = utterance + `generate/keyboard` = text)는
//        서버에 구현되지 않았다. 서버는 `generate` 하나만 두고 `text`만 받는다(LLM 분기 없음).
//        → 요청 DTO 도 PartNodeCreateRequest 하나로 통합.
// 응답 봉투: { isSuccess, code, message, result:{ room_id, part_node_id, part_node_text } }
//   (delete 응답 result 는 { room_id, part_node_id } 만 — part_node_text 는 빈 값으로 무시)
//   [주의] 명세 원문 키 "isSuccess "(뒤 공백)은 오타로 간주하고 isSuccess 로 파싱.
//          성공 판정은 HTTP 2xx + result.part_node_id 유무를 기준으로 하고 isSuccess 는 보조.

// PART 생성 요청. 서버는 발화/키보드를 구분하지 않고 text 를 그대로 받는다.
[System.Serializable]
public class PartNodeCreateRequest
{
    public string room_id;
    public string text;
    public float[] position;   // [x, y, z]
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
