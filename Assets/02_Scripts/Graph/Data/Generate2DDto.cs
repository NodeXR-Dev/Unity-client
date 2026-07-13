using System.Collections.Generic;

// 2D 스케치 생성 요청 DTO. (API 명세 2026-07-10)
//   요구사항(feature) 기반  POST /api/2d/generate/feature  { room_id, user_id }
//   그래프(graph) 기반      POST /api/2d/generate/graph    { room_id, user_id, connections:[{part_node_id, node_id}] }
//   색상 변경               POST /api/2d/color_change (multipart) { room_id, file, asset_id }  ← JSON DTO 없이 form 필드
//   결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 통보된다.

[System.Serializable]
public class Generate2DFeatureRequest
{
    public string room_id;
    public string user_id;
}

[System.Serializable]
public class Generate2DGraphRequest
{
    public string room_id;
    public string user_id;
    public List<ConnectionDto> connections = new List<ConnectionDto>();
}
