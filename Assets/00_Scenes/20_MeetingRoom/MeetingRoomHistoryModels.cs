/*
 * 파일명: MeetingRoomHistoryModels.cs
 * 목적: 히스토리(그래프 스냅샷) 조회용 DTO 정의.
 *
 * 백엔드는 이미 그래프 변경 시점마다 graph_snapshots 를 쌓고 있다(버전별 그래프 전체).
 * 이 클라이언트는 "그 스냅샷 목록을 받아 바에 점을 찍고, 스크럽하면 해당 시점 그래프를 그리는" 역할만 한다.
 *
 * 스냅샷 본체(nodes/edges/graph_version)는 기존 GraphData 를 그대로 재사용한다.
 * (백엔드 build_snapshot_data 의 JSON 구조와 GraphData 필드가 그대로 일치하기 때문)
 *
 * 백엔드에 요청할 API 계약 (조회 엔드포인트, 아직 미구현):
 *   GET /api/rooms/{room_id}/snapshots
 *     → { "isSuccess": true, "result": { "snapshots": [ {version, created_at, event_type}, ... ] } }
 *   GET /api/rooms/{room_id}/snapshots/{version}
 *     → { "isSuccess": true, "result": { "graph_version": N, "nodes": [...], "edges": [...] } }
 *
 * ※ JsonUtility 는 최상위가 배열이면 파싱 못 하므로, 목록은 result.snapshots 처럼 객체로 감싸는 형태로 계약.
 */
using System;
using System.Collections.Generic;

// 스냅샷 하나의 메타 정보 (바에 점 찍는 용도, 가볍다)
[Serializable]
public class HistorySnapshotMeta
{
    public int version;         // 스냅샷 버전 (1부터 증가, 클수록 최신)
    public string created_at;   // 생성 시각 (ISO 문자열)
    public string event_type;   // 무슨 변경이었는지 (NODE_CREATED / NODE_DELETED / EDGE_CREATED ...)
}

// GET /snapshots 응답의 result 부분
[Serializable]
public class HistorySnapshotListResult
{
    public List<HistorySnapshotMeta> snapshots = new List<HistorySnapshotMeta>();
}

// GET /snapshots 전체 응답 래퍼
[Serializable]
public class HistorySnapshotListResponse
{
    public bool isSuccess;
    public HistorySnapshotListResult result;
}

// GET /snapshots/{version} 전체 응답 래퍼 (본체는 GraphData 재사용)
[Serializable]
public class HistorySnapshotDetailResponse
{
    public bool isSuccess;
    public GraphData result;
}
