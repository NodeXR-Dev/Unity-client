using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 노드 생성(발화) 서버 REST 경계.
//   노드 "+" → 발화 입력 → GraphManager.OnUtteranceNodeRequested 발행 → 이 클래스가 구독해 POST.
//   POST /api/utterances { room_id, user_id, parent_node_id, parent_node_position, utterance }
//   응답 graph 를 GraphManager.MergeServerGraph 로 upsert 병합(노드 위치는 클라 reflow 규칙).
//
// GraphManager는 서버를 모른다(이벤트만 발행). host/room_id/user_id 는 GraphSyncClient 재사용.
// 입력 텍스트의 노드 로컬 반영은 GraphManager.RequestNodeByUtterance가 이미 수행하므로,
// 서버 실패(미배포 404 등) 시에는 하위 그래프 병합만 생략된다(로컬 텍스트는 유지).
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

    private void HandleUtterance(string parentNodeId, string utterance)
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
        StartCoroutine(CoRequest(parentNodeId, utterance));
    }

    private IEnumerator CoRequest(string parentNodeId, string utterance)
    {
        var parent = _graphManager != null ? _graphManager.GetNode(parentNodeId) : null;
        var parentPos = parent != null ? parent.Position : Vector3.zero;

        string body = JsonUtility.ToJson(new UtteranceRequest
        {
            room_id              = _syncClient.RoomId,
            user_id              = _syncClient.UserId,
            parent_node_id       = parentNodeId,
            parent_node_position = new[] { parentPos.x, parentPos.y, parentPos.z },
            utterance            = utterance,
        });

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
                _graphManager.MergeServerGraph(res.result.graph);
            else
                Debug.LogWarning("[UtteranceApiClient] 응답에 result.graph 가 없습니다(로컬 반영은 유지됨).");
        }
    }

    [ContextMenu("테스트 안내")]
    private void TestHint() =>
        Debug.Log("[UtteranceApiClient] 테스트: 노드 '+' → 발화 입력 → Enter. 또는 GraphManager.RequestNodeByUtterance 호출.");
}
