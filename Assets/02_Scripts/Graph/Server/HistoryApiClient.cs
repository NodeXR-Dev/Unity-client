using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// 그래프 히스토리 조회 서버 REST 경계. (API 명세 2026-07-10)
//   GET /api/history/{room_id} → result.history[] (graph_version 별 GraphSnapshotDto 배열).
//   각 스냅샷은 콜드로드와 동일 구조이므로 GraphSnapshotDto.ToGraphData()로 평탄화 후 LoadGraph+RenderGraph 로
//   과거 버전을 그대로 되살릴 수 있다(히스토리 스크러버 UI 는 별도/개발자2, 여기선 조회+로드 API 제공).
//
// host/room_id 는 GraphSyncClient 재사용. 서버 미배포면 404 → 실패 콜백.
public class HistoryApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    // 최근 조회 결과(스크러버 UI 가 인덱스로 LoadSnapshot 하도록 보관).
    private List<GraphSnapshotDto> _history;
    public IReadOnlyList<GraphSnapshotDto> History => _history;

    // 히스토리 조회. onDone(list) — 실패 시 null.
    public void GetHistory(Action<List<GraphSnapshotDto>> onDone)
    {
        if (_syncClient == null || string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[HistoryApiClient] 실패: GraphSyncClient(host/room_id)가 비어 있습니다.");
            onDone?.Invoke(null);
            return;
        }
        StartCoroutine(CoGetHistory(onDone));
    }

    // 조회된 스냅샷 하나를 현재 그래프로 로드(과거 버전 복원 표시).
    public bool LoadSnapshot(GraphSnapshotDto snapshot)
    {
        if (_graphManager == null || snapshot == null)
        {
            Debug.LogWarning("[HistoryApiClient] LoadSnapshot 실패: GraphManager 또는 snapshot 이 null.");
            return false;
        }
        _graphManager.LoadGraph(snapshot.ToGraphData());
        _graphManager.RenderGraph();
        Debug.Log($"[HistoryApiClient] 스냅샷 로드: graph_version={snapshot.graph_version}");
        return true;
    }

    // 인덱스로 로드(_history 기준). 0 = 응답 배열 첫 항목.
    public bool LoadSnapshotAt(int index)
    {
        if (_history == null || index < 0 || index >= _history.Count)
        {
            Debug.LogWarning($"[HistoryApiClient] LoadSnapshotAt 실패: 범위 밖 index={index} (count={_history?.Count ?? 0}).");
            return false;
        }
        return LoadSnapshot(_history[index]);
    }

    private IEnumerator CoGetHistory(Action<List<GraphSnapshotDto>> onDone)
    {
        string url = $"{ServerAddress.Http(_syncClient.Host)}/api/history/{UnityWebRequest.EscapeURL(_syncClient.RoomId)}";

        using (var req = UnityWebRequest.Get(url))
        {
            Debug.Log($"[HistoryApiClient] GET {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[HistoryApiClient] /api/history 실패: {req.error} (code={req.responseCode})");
                onDone?.Invoke(null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[HistoryApiClient] 응답(code={req.responseCode}): {raw}");

            HistoryResponse res = null;
            try { res = JsonUtility.FromJson<HistoryResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[HistoryApiClient] 응답 파싱 실패: {e.Message}"); }

            _history = res?.result?.history;
            Debug.Log($"[HistoryApiClient] 히스토리 {_history?.Count ?? 0}건 조회.");
            onDone?.Invoke(_history);
        }
    }

    [ContextMenu("Test: 히스토리 조회")]
    private void TestGetHistory() => GetHistory(list => Debug.Log($"[HistoryApiClient] 조회 결과 {list?.Count ?? 0}건"));
}
