// 서버 2D generate(기본 생성) 요청 DTO.
// 협의(2026-07-01): 기본 2D 생성은 이 단계에서 노드/connection/RegenerateConnectionBuilder 를
//   반영하지 않는다. (노드 기반 부분 재생성은 /api/2d/regenerate + RegenerateRequestDto 사용.)
// 결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 통보된다.
//
// 서버 현재 스키마(2026-07-01): Generate2DRequest = { room_id } 뿐.
//   발화(utterance)는 기능 정의 단계에서 입력되고 서버가 room_id로 그 문맥을 알고 있는 구조로 추정.
//   → 지금은 room_id 만 보낸다. 서버 스키마 확정(협의) 후 utterance 필드 추가 예정.
// TODO(서버 협의 후): public string utterance;  // 사용자 발화 텍스트

[System.Serializable]
public class Generate2DRequestDto
{
    public string room_id;
}
