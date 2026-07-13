using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// 그래프 콜드로드 서버 REST 경계.
//   GET /api/graph?room_id={room_id} → 서버 그래프 스냅샷(sub_graphs 중첩) 수신 →
//   GraphSnapshotDto.ToGraphData() 로 평탄화 → GraphManager.LoadGraph + RenderGraph.
//
// 지금까지 서버엔 그래프 조회 경로가 없어 SeedGraphLoader(하드코딩)로 대체했는데, API 명세(2026-07-10)에
// GET /api/graph 가 추가되어 이 클래스가 실서버 로더가 된다. 단 서버 배포 전(현재 404)에는 실패 시
// 기존 그래프를 그대로 두므로(seed 등), SeedGraphLoader 와 공존 가능하다.
//
// GraphManager 는 서버를 모른다. host/room_id 는 GraphSyncClient 재사용.
// [배선] _loadOnStart 는 기본 false — 서버 배포 후 true 로 켜고 SeedGraphLoader 는 비활성화한다.
//        배포 전에는 ContextMenu "Load Graph From Server" 로 수동 트리거해 파서/왕복만 검증.
public class GraphLoadApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    [Header("옵션")]
    [Tooltip("Start 에서 서버 그래프를 자동 로드. 서버 /api/graph 배포 후 켠다(그 전엔 404).")]
    [SerializeField] private bool _loadOnStart = false;

    private void Start()
    {
        if (_loadOnStart) LoadGraphFromServer();
    }

    [ContextMenu("Load Graph From Server")]
    public void LoadGraphFromServer()
    {
        if (_graphManager == null) _graphManager = FindObjectOfType<GraphManager>();
        if (_graphManager == null)
        {
            Debug.LogWarning("[GraphLoadApiClient] 실패: GraphManager 미연결.");
            return;
        }
        if (_syncClient == null || string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[GraphLoadApiClient] 실패: GraphSyncClient(host/room_id)가 비어 있습니다.");
            return;
        }
        StartCoroutine(CoLoad());
    }

    private IEnumerator CoLoad()
    {
        string url = $"http://{_syncClient.Host}/api/graph?room_id={UnityWebRequest.EscapeURL(_syncClient.RoomId)}";

        using (var req = UnityWebRequest.Get(url))
        {
            Debug.Log($"[GraphLoadApiClient] GET {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 서버 미배포(404 등)/네트워크 실패 → 기존 그래프 유지(seed 등 덮지 않음).
                Debug.LogWarning($"[GraphLoadApiClient] /api/graph 실패(기존 그래프 유지): {req.error} (code={req.responseCode})");
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[GraphLoadApiClient] 응답(code={req.responseCode}): {raw}");

            GraphQueryResponse res = null;
            try { res = JsonUtility.FromJson<GraphQueryResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[GraphLoadApiClient] 응답 파싱 실패: {e.Message}"); }

            if (res?.result == null)
            {
                Debug.LogWarning("[GraphLoadApiClient] 응답에 result 가 없습니다(기존 그래프 유지).");
                yield break;
            }

            GraphData graph = res.result.ToGraphData();
            _graphManager.LoadGraph(graph);
            _graphManager.RenderGraph();

            string imgUrl = res.result.core_2d_image?.image_url;
            Debug.Log($"[GraphLoadApiClient] 콜드로드 완료: nodes={graph.nodes.Count}, edges={graph.edges.Count}, " +
                      $"graph_version={graph.graph_version}. core_2d_image={(string.IsNullOrEmpty(imgUrl) ? "없음" : imgUrl)}");
            // TODO: core_2d_image 를 중앙 이미지(Generate2DController)에 연결 — 별도 세션.
        }
    }
}
