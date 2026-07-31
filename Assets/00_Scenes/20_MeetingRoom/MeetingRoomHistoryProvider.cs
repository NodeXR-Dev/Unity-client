/*
 * 파일명: MeetingRoomHistoryProvider.cs
 * 목적: 히스토리 스냅샷 데이터 공급원. 인터페이스 + 두 구현(Mock / API).
 *
 * - Mock: 백엔드 조회 API가 아직 없어도 "지금 당장" 동작. 버전이 올라갈수록 그래프가 자라나는 가짜 스냅샷 생성.
 * - Api : 실제 UnityWebRequest 호출. 백엔드에 GET /snapshots 조회 엔드포인트가 생기면 이걸로 스위치.
 *
 * 두 구현 모두 코루틴(IEnumerator)로 결과를 콜백에 넘긴다. 실행은 MonoBehaviour(컨트롤러)가 StartCoroutine 으로 돌린다.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public interface IMeetingRoomHistoryProvider
{
    // 스냅샷 목록(버전/시각/이벤트)을 가져온다.
    IEnumerator FetchList(string roomId, Action<List<HistorySnapshotMeta>> onDone, Action<string> onError);

    // 특정 버전의 그래프 전체를 가져온다.
    IEnumerator FetchSnapshot(string roomId, int version, Action<GraphData> onDone, Action<string> onError);
}


// ─────────────────────────────────────────────────────────────
// Mock: 백엔드 없이 로컬에서 동작. 버전이 올라갈수록 노드/엣지가 늘어난다.
// ─────────────────────────────────────────────────────────────
public class MockMeetingRoomHistoryProvider : IMeetingRoomHistoryProvider
{
    private readonly int _versionCount;

    public MockMeetingRoomHistoryProvider(int versionCount = 6)
    {
        _versionCount = Mathf.Max(1, versionCount);
    }

    public IEnumerator FetchList(string roomId, Action<List<HistorySnapshotMeta>> onDone, Action<string> onError)
    {
        var list = new List<HistorySnapshotMeta>();
        string[] events = { "NODE_CREATED", "NODE_CREATED", "EDGE_CREATED", "NODE_CREATED", "EDGE_CREATED", "NODE_DELETED" };
        for (int v = 1; v <= _versionCount; v++)
        {
            list.Add(new HistorySnapshotMeta
            {
                version = v,
                created_at = $"2026-07-01T10:{v:00}:00Z",
                event_type = events[(v - 1) % events.Length],
            });
        }
        onDone?.Invoke(list);
        yield break;
    }

    public IEnumerator FetchSnapshot(string roomId, int version, Action<GraphData> onDone, Action<string> onError)
    {
        onDone?.Invoke(BuildMockGraph(version));
        yield break;
    }

    // 버전 v 시점의 그래프: 중심 PART(ALL)에서 시작해 버전이 오를수록 PROPERTY 노드/엣지가 붙는다.
    private GraphData BuildMockGraph(int version)
    {
        var graph = new GraphData { room_id = "mock_room", graph_version = version };

        // v1: 루트 PART 노드 하나
        graph.nodes.Add(new NodeData { node_id = "part_all", type = "PART", label = "ALL", position = new float[] { 0f, 1.5f, 0f }, is_global = true });

        // 버전마다 하나씩 붙는 속성 노드 정의 (텍스트, 상대 위치, 부모)
        (string id, string text, float x, float y, string parent)[] steps =
        {
            ("prop_metal",  "Metal",      -0.8f, 2.2f, "part_all"),   // v2
            ("prop_matte",  "Matte",      -1.5f, 2.9f, "prop_metal"), // v3 (엣지도 함께)
            ("prop_future", "Futuristic",  0.8f, 2.2f, "part_all"),   // v4
            ("prop_plant",  "Plant",       1.5f, 2.9f, "prop_future"),// v5 (엣지)
        };

        int added = Mathf.Clamp(version - 1, 0, steps.Length);
        for (int i = 0; i < added; i++)
        {
            var s = steps[i];
            graph.nodes.Add(new NodeData { node_id = s.id, type = "PROPERTY", label = s.text, position = new float[] { s.x, s.y, 0f }, property_category = "mock" });
            graph.edges.Add(new EdgeData { edge_id = $"e_{s.id}", from_node_id = s.parent, to_node_id = s.id });
        }

        // 마지막 버전에서는 노드 하나 삭제된 상태를 흉내 (식물 제거)
        if (version >= 6)
        {
            graph.nodes.RemoveAll(n => n.node_id == "prop_plant");
            graph.edges.RemoveAll(e => e.to_node_id == "prop_plant" || e.from_node_id == "prop_plant");
        }

        return graph;
    }
}


// ─────────────────────────────────────────────────────────────
// Api: 실제 백엔드 조회. 조회 엔드포인트가 생기면 컨트롤러에서 이걸로 교체.
// ─────────────────────────────────────────────────────────────
public class ApiMeetingRoomHistoryProvider : IMeetingRoomHistoryProvider
{
    private readonly string _baseUrl;

    public ApiMeetingRoomHistoryProvider(string baseUrl = "http://localhost:8000")
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public IEnumerator FetchList(string roomId, Action<List<HistorySnapshotMeta>> onDone, Action<string> onError)
    {
        string url = $"{_baseUrl}/api/rooms/{roomId}/snapshots";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"[History] 목록 요청 실패: {req.error} ({url})");
                yield break;
            }

            HistorySnapshotListResponse res = null;
            try { res = JsonUtility.FromJson<HistorySnapshotListResponse>(req.downloadHandler.text); }
            catch (Exception e) { onError?.Invoke($"[History] 목록 파싱 실패: {e.Message}"); yield break; }

            var snapshots = res?.result?.snapshots ?? new List<HistorySnapshotMeta>();
            onDone?.Invoke(snapshots);
        }
    }

    public IEnumerator FetchSnapshot(string roomId, int version, Action<GraphData> onDone, Action<string> onError)
    {
        string url = $"{_baseUrl}/api/rooms/{roomId}/snapshots/{version}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"[History] 스냅샷 요청 실패: {req.error} ({url})");
                yield break;
            }

            HistorySnapshotDetailResponse res = null;
            try { res = JsonUtility.FromJson<HistorySnapshotDetailResponse>(req.downloadHandler.text); }
            catch (Exception e) { onError?.Invoke($"[History] 스냅샷 파싱 실패: {e.Message}"); yield break; }

            if (res?.result == null) { onError?.Invoke("[History] 스냅샷 응답에 result가 없습니다."); yield break; }
            onDone?.Invoke(res.result);
        }
    }
}
