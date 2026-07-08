using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 노드 생성(발화) 서버 REST 경계.
//   노드 "+" → 빈 placeholder 생성 → 발화 입력 → GraphManager.OnUtteranceNodeRequested 발행 → 이 클래스가 POST.
//   parent_node_id 는 "방금 만든 placeholder"가 아니라 그 **기존(서버-known) 부모**다(서버가 존재를 요구).
//   부모가 없으면(루트) parent_* 필드 없는 UtteranceRootRequest 로 보낸다(서버가 새 서브그래프 생성).
//   POST /api/utterances → 응답 graph 를 MergeServerGraph 로 병합(서버 UUID 채택, 위치는 클라 reflow) +
//   성공 시 로컬 placeholder 제거(서버 노드가 그 자리를 대신함).
//
// GraphManager는 서버를 모른다(이벤트만 발행). host/room_id/user_id 는 GraphSyncClient 재사용.
// 서버 실패(미배포 404 등) 시에는 병합·placeholder 제거를 생략한다(입력 텍스트가 반영된 로컬 노드는 유지).
public class UtteranceApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id/user_id 재사용

    private bool _subscribed;

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_graphManager == null || _subscribed) return;
        _graphManager.OnUtteranceNodeRequested += HandleUtterance;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graphManager == null || !_subscribed) return;
        _graphManager.OnUtteranceNodeRequested -= HandleUtterance;
        _subscribed = false;
    }

    // ─────────────────────────────────────────────
    // 요청 처리
    // ─────────────────────────────────────────────

    // serverParentId 가 비어 있으면 루트 발화(서버가 새 서브그래프 생성). placeholderNodeId 는
    // 사용자가 입력한 로컬 임시 노드 → 성공 병합 후 제거한다.
    private void HandleUtterance(string serverParentId, string utterance, string placeholderNodeId)
    {
        if (_syncClient == null)
        {
            Debug.LogWarning("[UtteranceApiClient] 실패: GraphSyncClient 미연결.");
            return;
        }
        if (string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[UtteranceApiClient] 실패: host 또는 room_id 가 비어 있습니다.");
            return;
        }
        StartCoroutine(CoRequest(serverParentId, utterance, placeholderNodeId));
    }

    private IEnumerator CoRequest(string serverParentId, string utterance, string placeholderNodeId)
    {
        // 서버 parent_node_id 는 "이미 서버에 존재하는" 노드여야 한다. 방금 만든 로컬 placeholder 가
        // 아니라 그 부모다. 부모가 없으면(루트) parent_* 필드 없는 요청으로 보낸다.
        string body;
        if (string.IsNullOrEmpty(serverParentId))
        {
            body = JsonUtility.ToJson(new UtteranceRootRequest
            {
                room_id   = _syncClient.RoomId,
                user_id   = _syncClient.UserId,
                utterance = utterance,
            });
        }
        else
        {
            var parent = _graphManager != null ? _graphManager.GetNode(serverParentId) : null;
            var parentPos = parent != null ? parent.Position : Vector3.zero;

            body = JsonUtility.ToJson(new UtteranceRequest
            {
                room_id              = _syncClient.RoomId,
                user_id              = _syncClient.UserId,
                parent_node_id       = serverParentId,
                parent_node_position = new[] { parentPos.x, parentPos.y, parentPos.z },
                utterance            = utterance,
            });
        }

        string url = $"http://{_syncClient.Host}/api/utterances";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[UtteranceApiClient] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 입력 텍스트는 이미 GraphManager.RequestNodeByUtterance에서 노드에 로컬 반영됨.
                // 서버 확장(하위 그래프)만 실패한 것이므로 추가 노드 생성 없이 로그만.
                Debug.LogWarning($"[UtteranceApiClient] utterances 실패(로컬 반영은 유지됨): {req.error} (code={req.responseCode})");
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[UtteranceApiClient] 응답(code={req.responseCode}): {raw}");

            UtteranceResponse res = null;
            try { res = JsonUtility.FromJson<UtteranceResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[UtteranceApiClient] 응답 파싱 실패: {e.Message}"); }

            if (res?.result?.graph != null)
                // 서버 노드 병합 + 로컬 placeholder 제거(서버 노드가 그 자리를 대신함).
                _graphManager.MergeServerGraph(res.result.graph, placeholderNodeId);
            else
                Debug.LogWarning("[UtteranceApiClient] 응답에 result.graph 가 없습니다(로컬 반영은 유지됨).");
        }
    }

    [ContextMenu("테스트 안내")]
    private void TestHint() =>
        Debug.Log("[UtteranceApiClient] 테스트: 노드 '+' → 발화 입력 → Enter. 또는 GraphManager.RequestNodeByUtterance 호출.");
}
