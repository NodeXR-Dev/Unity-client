using System.Collections.Generic;

// 서버 2D regenerate(부분 재생성) 요청 DTO.
// 협의(2026-07-01): connection 배열로 { part_node_id, node_id } 쌍 전송.
//   node_id = 사용자가 PART에 연결한 서브그래프 기점(맨 하위 leaf). 서버가 거기서 상위 체인을 탐색.
// [주의] 서버 2D generate 엔드포인트는 현재 미구현(주석). 이 DTO/빌더는 계약 대비 Unity 선구현.

[System.Serializable]
public class ConnectionDto
{
    public string part_node_id;  // 적용 대상 PART/ALL node_id (엣지의 to)
    public string node_id;       // 적용 기점 PROPERTY(leaf) 또는 REFERENCE node_id (엣지의 from)
}

[System.Serializable]
public class RegenerateRequestDto
{
    public string room_id;
    public string asset_id;   // 현재 중앙 이미지 ID (없으면 null)
    public List<ConnectionDto> connection = new List<ConnectionDto>();
}
