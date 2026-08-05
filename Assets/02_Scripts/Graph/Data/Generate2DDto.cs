using System.Collections.Generic;

// 2D 스케치 생성 요청 DTO. (서버 aa81878 "job_id 추가" 반영, 2026-08-03)
//   요구사항(feature) 기반  POST /api/2d/generate/feature  { room_id, user_id, job_id }
//   그래프(graph) 기반      POST /api/2d/generate/graph    { room_id, user_id, job_id, connections:[{part_node_id, node_id}] }
//   색상 변경               POST /api/2d/color_change (multipart) { room_id, user_id, job_id, asset_id, file, metadata }  ← JSON DTO 없이 form 필드
//   결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 통보된다.
//
// [job_id] 클라가 요청마다 발급하는 UUID. 서버 스키마에서 필수(UUID)이므로 비우면 422 로 거부된다.
//   서버는 같은 값을 HTTP 응답 result.job_id 와 WS 이벤트 봉투 최상위 job_id 로 되돌려준다.
//   NODE_CREATE/EDGE_CREATE 가 쓰던 job_id(로컬 id 를 실어 보내 rekey) 와 목적이 다르다.
//   여기서는 rekey 가 아니라 "이 결과가 내 요청의 것인가" 를 가리는 데만 쓴다.
//   결과가 요청자에게만 전달되므로(서버가 broadcast_to_room 삭제) 요청을 연달아 보냈을 때
//   늦게 온 이전 결과로 화면이 덮이는 것을 막는 용도다.

[System.Serializable]
public class Generate2DFeatureRequest
{
    public string room_id;
    public string user_id;
    public string job_id;
}

[System.Serializable]
public class Generate2DGraphRequest
{
    public string room_id;
    public string user_id;
    public string job_id;
    public List<ConnectionDto> connections = new List<ConnectionDto>();
}

// color_change 의 metadata 폼 필드(JSON 문자열로 직렬화해 보낸다).
// 서버 ColorChangeMetadataRequest 는 width/height 를 gt=0 으로 검증하므로 0 이면 422 다.
[System.Serializable]
public class ColorChangeMetadata
{
    public string mime_type;
    public int width;
    public int height;
}
