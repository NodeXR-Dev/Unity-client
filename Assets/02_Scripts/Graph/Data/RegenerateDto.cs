// 2D 그래프 스케치 요청의 connection 항목 DTO.
// 명세(2026-07-10): POST /api/2d/generate/graph 의 connections=[{ part_node_id, node_id }].
//   node_id = 사용자가 PART에 연결한 서브그래프 기점(맨 하위 leaf). 서버가 거기서 상위 체인을 탐색.
//   빌드는 RegenerateConnectionBuilder(적용 엣지 to=PART, from=PROPERTY/REFERENCE)가 담당.
// (구 /api/2d/regenerate + asset_id 계약은 명세에서 /api/2d/generate/graph 로 대체되어 RegenerateRequestDto 제거.)

[System.Serializable]
public class ConnectionDto
{
    public string part_node_id;  // 적용 대상 PART/ALL node_id (엣지의 to)
    public string node_id;       // 적용 기점 PROPERTY(leaf) 또는 REFERENCE node_id (엣지의 from)
}
