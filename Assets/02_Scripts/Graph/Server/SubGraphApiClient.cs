using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 서브그래프 생성 서버 REST 경계.
//   키보드 "+" 로 만든 로컬 루트 노드에 텍스트를 처음 제출하면 GraphManager 가 OnSubGraphRequested 를 발행한다.
//   이 클래스가 POST /api/sub_graph/generate 로 서버 sub_graph_id 를 발급받은 뒤,
//   GraphManager.SubmitRootNodeWithSubGraph(rootNodeId, subGraphId) 로 넘겨 WS NODE_CREATE(루트) 를 잇는다.
//   (루트 NODE_CREATE 는 parent_node_id="" + sub_graph_id 로 나가며, ACK 로 로컬 노드가 서버 node_id 로 rekey 된다.)
//
// GraphManager 는 서버를 모른다(이벤트만 발행). host/room_id 는 GraphSyncClient 재사용.
// 서버 실패(미배포 404 등) 시에는 NODE_CREATE 를 잇지 않는다(입력 텍스트가 반영된 로컬 루트 노드는 유지).
public class SubGraphApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    private bool _subscribed;

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_graphManager == null || _subscribed) return;
        _graphManager.OnSubGraphRequested += HandleSubGraphRequested;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graphManager == null || !_subscribed) return;
        _graphManager.OnSubGraphRequested -= HandleSubGraphRequested;
        _subscribed = false;
    }

    // ─────────────────────────────────────────────
    // 요청 처리
    // ─────────────────────────────────────────────

    private void HandleSubGraphRequested(string rootNodeId)
    {
        if (string.IsNullOrEmpty(rootNodeId))
        {
            Debug.LogWarning("[SubGraphApiClient] 실패: rootNodeId 가 비어 있습니다.");
            return;
        }
        if (_syncClient == null)
        {
            Debug.LogWarning("[SubGraphApiClient] 실패: GraphSyncClient 미연결.");
            return;
        }
        if (string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[SubGraphApiClient] 실패: host 또는 room_id 가 비어 있습니다.");
            return;
        }
        StartCoroutine(CoGenerate(rootNodeId));
    }

    private IEnumerator CoGenerate(string rootNodeId)
    {
        string body = JsonUtility.ToJson(new SubGraphGenerateRequest { room_id = _syncClient.RoomId });
        string url  = $"{ServerAddress.Http(_syncClient.Host)}/api/sub_graph/generate";

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[SubGraphApiClient] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 서버 실패 → 루트 노드는 로컬 유지(서버-known 아님, "+" 비활성). NODE_CREATE 는 잇지 않는다.
                Debug.LogWarning($"[SubGraphApiClient] sub_graph/generate 실패(로컬 루트 유지): {req.error} (code={req.responseCode})");
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[SubGraphApiClient] 응답(code={req.responseCode}): {raw}");

            SubGraphResponse res = null;
            try { res = JsonUtility.FromJson<SubGraphResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[SubGraphApiClient] 응답 파싱 실패: {e.Message}"); }

            string subGraphId = res?.result?.sub_graph_id;
            if (string.IsNullOrEmpty(subGraphId))
            {
                Debug.LogWarning("[SubGraphApiClient] 응답에 result.sub_graph_id 가 없습니다(로컬 루트 유지).");
                yield break;
            }

            // 서버 sub_graph_id 확보 → 루트 NODE_CREATE 발행(GraphManager).
            _graphManager.SubmitRootNodeWithSubGraph(rootNodeId, subGraphId);
        }
    }

    // ─────────────────────────────────────────────
    // JsonUtility DTO
    // ─────────────────────────────────────────────

    [Serializable] private class SubGraphGenerateRequest { public string room_id; }
    [Serializable] private class SubGraphResponse { public bool isSuccess; public string code; public string message; public SubGraphResult result; }
    [Serializable] private class SubGraphResult   { public string room_id; public string sub_graph_id; }
}
