using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 노드 생성(발화) 서버 REST 경계.
//   노드 "+" → 빈 placeholder 생성 → 발화 입력 → GraphManager.OnUtteranceNodeRequested 발행 → 이 클래스가 POST.
//   parent_node_id 는 "방금 만든 placeholder"가 아니라 그 **기존(서버-known) 부모**다(서버가 존재를 요구).
//   부모가 없으면(루트) parent_node_id 필드 없는 UtteranceRootRequest 로 보낸다(서버가 새 서브그래프 생성).
//   POST /api/node/generate/utterance → 응답 { node_id, node_text } 단건 →
//   로컬 placeholder 를 그 node_id 로 rekey 하고 node_text(LLM 키워드)로 라벨 갱신(GraphManager.ApplyServerNodeId).
//   (구 /api/utterances 전체그래프 병합 방식에서 명세 2026-07-10 기준으로 개편.)
//
// GraphManager는 서버를 모른다(이벤트만 발행). host/room_id/user_id 는 GraphSyncClient 재사용.
// 서버 실패(미배포 404 등) 시에는 rekey 를 생략한다(입력 텍스트가 반영된 로컬 placeholder 는 유지).
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
        // 요청 값은 새로 만드는 노드(placeholder) 기준: node_type/position 은 placeholder 에서 읽는다.
        // parent_node_id 는 "이미 서버에 존재하는" 부모다(placeholder 자신이 아님). 부모가 없으면(루트) parent 필드 없는 요청.
        var placeholder = _graphManager != null ? _graphManager.GetNode(placeholderNodeId) : null;
        string nodeType = !string.IsNullOrEmpty(placeholder?.type) ? placeholder.type : "PROPERTY";
        Vector3 pos     = placeholder != null ? placeholder.Position : Vector3.zero;

        string body;
        if (string.IsNullOrEmpty(serverParentId))
        {
            body = JsonUtility.ToJson(new UtteranceRootRequest
            {
                room_id   = _syncClient.RoomId,
                user_id   = _syncClient.UserId,
                node_type = nodeType,
                utterance = utterance,
                position  = new[] { pos.x, pos.y, pos.z },
            });
        }
        else
        {
            body = JsonUtility.ToJson(new UtteranceRequest
            {
                room_id        = _syncClient.RoomId,
                user_id        = _syncClient.UserId,
                node_type      = nodeType,
                parent_node_id = serverParentId,
                utterance      = utterance,
                position       = new[] { pos.x, pos.y, pos.z },
            });
        }

        string url = $"{ServerAddress.Http(_syncClient.Host)}/api/node/generate/utterance";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[UtteranceApiClient] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 입력 텍스트는 이미 GraphManager.RequestNodeByUtterance에서 placeholder 에 로컬 반영됨.
                // 서버 생성만 실패한 것이므로 rekey 없이 로그만(placeholder 는 로컬 유지).
                Debug.LogWarning($"[UtteranceApiClient] node/generate/utterance 실패(로컬 placeholder 유지): {req.error} (code={req.responseCode})");
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[UtteranceApiClient] 응답(code={req.responseCode}): {raw}");

            UtteranceResponse res = null;
            try { res = JsonUtility.FromJson<UtteranceResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[UtteranceApiClient] 응답 파싱 실패: {e.Message}"); }

            string serverNodeId = res?.result?.node_id;
            if (!string.IsNullOrEmpty(serverNodeId))
                // 로컬 placeholder 를 서버 node_id 로 rekey + node_text(LLM 키워드)로 라벨 갱신.
                _graphManager.ApplyServerNodeId(placeholderNodeId, serverNodeId, res.result.node_text);
            else
                Debug.LogWarning("[UtteranceApiClient] 응답에 result.node_id 가 없습니다(로컬 placeholder 유지).");
        }
    }

    [ContextMenu("테스트 안내")]
    private void TestHint() =>
        Debug.Log("[UtteranceApiClient] 테스트: 노드 '+' → 발화 입력 → Enter. 또는 GraphManager.RequestNodeByUtterance 호출.");
}
