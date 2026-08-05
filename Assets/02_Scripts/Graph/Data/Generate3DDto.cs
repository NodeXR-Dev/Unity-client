// 3D 모델 생성 요청 DTO. (서버 03199d0 "3D 생성" + aa81878 "job_id 추가" 기준, 2026-08-04)
//   POST /api/3d/generate  { room_id, user_id, job_id, asset_id }
//   결과는 HTTP 응답이 아니라 WS 3D_GENERATED{asset_id, mime_type, model_url} 로 통보된다.
//
// [asset_id] 변환할 원본 2D 이미지의 서버 asset_id 다. 서버가 이 asset 을 MinIO 에서 받아
//   Meshy 에 넘겨 GLB 를 만든다. 서버 스키마상 Optional 이지만 실제로는 필수다 —
//   None 이면 _get_valid_source_asset() 이 실패한다.
//
// [비용 주의] 이 엔드포인트는 호출 1회당 Meshy API 과금이 발생한다.
//   서버에는 중복 방지가 없다(같은 asset_id 로 몇 번을 부르든 매번 새로 생성).
//   따라서 중복 요청 차단은 현재 전적으로 클라 책임이다. Generate3DController 참고.

[System.Serializable]
public class Generate3DRequest
{
    public string room_id;
    public string user_id;
    public string job_id;
    public string asset_id;   // 원본 2D 이미지의 asset_id
}
