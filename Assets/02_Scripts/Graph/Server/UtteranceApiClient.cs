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
        // parent_node_id 는 "이미 서버에 존재하는" 부모다(placeholder 자신이 아님).
        // parent_node_position 도 부모 노드의 위치다 — 서버가 새 서브그래프를 그 근처에 배치한다.
        // 부모가 없으면(루트) parent 필드 없는 요청을 보낸다.
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
            Vector3 parentPos = parent != null ? parent.Position : Vector3.zero;
            body = JsonUtility.ToJson(new UtteranceRequest
            {
                room_id              = _syncClient.RoomId,
                user_id              = _syncClient.UserId,
                parent_node_id       = serverParentId,
                utterance            = utterance,
                parent_node_position = new[] { parentPos.x, parentPos.y, parentPos.z },
            });
        }

        string url = $"{ServerAddress.Http(_syncClient.Host)}/api/utterances";
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
                Debug.LogWarning($"[UtteranceApiClient] utterances 실패(로컬 placeholder 유지): {req.error} (code={req.responseCode})\n{req.downloadHandler.text}");
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[UtteranceApiClient] 응답(code={req.responseCode}): {raw}");

            UtteranceResponse res = null;
            try { res = JsonUtility.FromJson<UtteranceResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[UtteranceApiClient] 응답 파싱 실패: {e.Message}"); }

            // 서버는 발화 하나로 서브그래프(노드 여러 개)를 만든다.
            // 로컬 placeholder 에 매핑할 것은 그 서브그래프의 root 다 —
            // PROPERTY→PART 연결에 쓰는 node_id 가 root 여야 서버가 하위 체인을 찾을 수 있다.
            UtteranceSubGraph subGraph = FindFirstSubGraph(res);
            string rootNodeId = subGraph?.root_node_id;
            if (string.IsNullOrEmpty(rootNodeId))
            {
                Debug.LogWarning("[UtteranceApiClient] 응답에 sub_graphs[].root_node_id 가 없습니다(로컬 placeholder 유지).");
                yield break;
            }

            string rootText = FindNodeText(subGraph, rootNodeId);
            _graphManager.ApplyServerNodeId(
                placeholderNodeId,
                rootNodeId,
                string.IsNullOrEmpty(rootText) ? utterance : rootText);

            int nodeCount = subGraph.nodes != null ? subGraph.nodes.Length : 0;
            Debug.Log(
                $"[UtteranceApiClient] 서브그래프 등록 완료: root={rootNodeId} " +
                $"(서버가 만든 노드 {nodeCount}개). 하위 노드는 서버에만 있고 로컬엔 root 만 매핑된다.");
        }
    }

    // 응답에서 실제 노드가 담긴 첫 서브그래프를 고른다.
    private static UtteranceSubGraph FindFirstSubGraph(UtteranceResponse res)
    {
        if (res?.result?.sub_graphs == null)
            return null;

        foreach (UtteranceSubGraph candidate in res.result.sub_graphs)
        {
            if (candidate != null && !string.IsNullOrEmpty(candidate.root_node_id))
                return candidate;
        }
        return null;
    }

    private static string FindNodeText(UtteranceSubGraph subGraph, string nodeId)
    {
        if (subGraph?.nodes == null)
            return null;

        foreach (UtteranceNode node in subGraph.nodes)
        {
            if (node != null && node.node_id == nodeId)
                return node.node_text;
        }
        return null;
    }

    [ContextMenu("테스트 안내")]
    private void TestHint() =>
        Debug.Log("[UtteranceApiClient] 테스트: 노드 '+' → 발화 입력 → Enter. 또는 GraphManager.RequestNodeByUtterance 호출.");
}
