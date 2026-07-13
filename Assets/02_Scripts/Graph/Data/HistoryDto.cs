using System.Collections.Generic;

// 그래프 히스토리 조회(GET /api/history/{room_id}) 응답 DTO. (API 명세 2026-07-10)
//   result.history[] 의 각 항목은 콜드로드(GET /api/graph)와 동일한 GraphSnapshotDto(graph_version 별 스냅샷).
//   → GraphSnapshotDto(Data/GraphQueryDto.cs)를 그대로 재사용한다.
// 봉투: { isSuccess, code:"HISTORY200", message, result:{ room_id, history:[ {snapshot}, ... ] } }

[System.Serializable]
public class HistoryResult
{
    public string room_id;
    public List<GraphSnapshotDto> history = new List<GraphSnapshotDto>();
}

[System.Serializable]
public class HistoryResponse
{
    public bool isSuccess;
    public string code;      // 예: HISTORY200
    public string message;
    public HistoryResult result;
}
