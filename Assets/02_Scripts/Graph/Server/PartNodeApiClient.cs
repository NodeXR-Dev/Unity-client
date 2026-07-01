using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 파트 노드 CRUD 서버 REST 경계.
//   생성 POST   /api/part_node/generate  { room_id, utterance }
//   수정 PATCH  /api/part_node/modify    { room_id, part_node_id, part_node_text }
//   삭제 DELETE /api/part_node/delete     { room_id, part_node_id }
//
// 협의(2026-07-01): PART 노드는 REST로 일원화(WS NODE_* 미사용). GraphManager 는 서버를 모르며,
//   이 클래스가 REST를 호출하고 성공 시에만 GraphManager 로컬 변경(이벤트 미발행 경로)을 유발한다.
// host/room_id 는 GraphSyncClient 가 이미 들고 있으므로 재사용한다(Generate2DController 선례).
// [주의] 서버 미배포면 404 가능(2D generate 선례) → 실패 시 로컬 변경 없이 onDone(false).
public class PartNodeApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    [Header("옵션")]
    [Tooltip("true면 REST 실패 시 로컬만으로 진행(생성은 self-GUID). 서버 미배포 상태의 UI 테스트용. 기본 false(REST 일원화).")]
    [SerializeField] private bool _offlineFallback = false;

    // ─────────────────────────────────────────────
    // 공개 API (UI 포트가 호출)
    // ─────────────────────────────────────────────

    // 파트 생성: utterance 로 서버 생성 요청 → 서버 발급 part_node_id/text 로 로컬 PART 생성.
    public void CreatePart(string utterance, bool isGlobal, Action<bool> onDone)
    {
        if (!EnsureRefs(onDone)) return;
        StartCoroutine(CoCreate(utterance, isGlobal, onDone));
    }

    // 파트 수정: part_node_text 갱신 요청 → 성공 시 로컬 label 갱신.
    public void ModifyPart(string partNodeId, string text, Action<bool> onDone)
    {
        if (!EnsureRefs(onDone)) return;
        StartCoroutine(CoModify(partNodeId, text, onDone));
    }

    // 파트 삭제: 서버 삭제 요청 → 성공 시 로컬 삭제(WS 미발행).
    public void DeletePart(string partNodeId, Action<bool> onDone)
    {
        if (!EnsureRefs(onDone)) return;
        StartCoroutine(CoDelete(partNodeId, onDone));
    }

    // ─────────────────────────────────────────────
    // 코루틴 구현
    // ─────────────────────────────────────────────

    private IEnumerator CoCreate(string utterance, bool isGlobal, Action<bool> onDone)
    {
        string body = JsonUtility.ToJson(new PartNodeGenerateRequest
        {
            room_id   = _syncClient.RoomId,
            utterance = utterance,
        });

        PartNodeResponse res = null;
        yield return Send("part_node/generate", UnityWebRequest.kHttpVerbPOST, body, (ok, r) => res = ok ? r : null);

        if (res?.result != null && !string.IsNullOrEmpty(res.result.part_node_id))
        {
            string label = string.IsNullOrEmpty(res.result.part_node_text) ? utterance : res.result.part_node_text;
            _graphManager.RequestCreatePartNode(label, isGlobal, res.result.part_node_id);
            onDone?.Invoke(true);
            yield break;
        }

        // 실패 → 오프라인 폴백(로컬 self-GUID 생성) 또는 실패 통보
        if (_offlineFallback)
        {
            Debug.LogWarning("[PartNodeApiClient] generate 실패 → offlineFallback: 로컬 PART 생성(self-GUID).");
            _graphManager.RequestCreatePartNode(utterance, isGlobal);
            onDone?.Invoke(true);
        }
        else onDone?.Invoke(false);
    }

    private IEnumerator CoModify(string partNodeId, string text, Action<bool> onDone)
    {
        string body = JsonUtility.ToJson(new PartNodeModifyRequest
        {
            room_id        = _syncClient.RoomId,
            part_node_id   = partNodeId,
            part_node_text = text,
        });

        PartNodeResponse res = null;
        yield return Send("part_node/modify", "PATCH", body, (ok, r) => res = ok ? r : null);

        if (res != null && res.result != null)
        {
            string label = string.IsNullOrEmpty(res.result.part_node_text) ? text : res.result.part_node_text;
            _graphManager.RequestRenamePartNode(partNodeId, label);
            onDone?.Invoke(true);
            yield break;
        }

        if (_offlineFallback)
        {
            Debug.LogWarning("[PartNodeApiClient] modify 실패 → offlineFallback: 로컬 label 갱신.");
            _graphManager.RequestRenamePartNode(partNodeId, text);
            onDone?.Invoke(true);
        }
        else onDone?.Invoke(false);
    }

    private IEnumerator CoDelete(string partNodeId, Action<bool> onDone)
    {
        string body = JsonUtility.ToJson(new PartNodeDeleteRequest
        {
            room_id      = _syncClient.RoomId,
            part_node_id = partNodeId,
        });

        // 삭제 성공 판정은 HTTP 2xx 만으로 한다(응답 봉투 스키마가 minimal 일 수 있음).
        bool ok = false;
        yield return Send("part_node/delete", "DELETE", body, (httpOk, _) => ok = httpOk);

        if (ok || _offlineFallback)
        {
            if (!ok) Debug.LogWarning("[PartNodeApiClient] delete 실패 → offlineFallback: 로컬 삭제.");
            _graphManager.RequestDeleteNode(partNodeId, emitSync: false);
            onDone?.Invoke(true);
        }
        else onDone?.Invoke(false);
    }

    // ─────────────────────────────────────────────
    // 송신 공통
    // ─────────────────────────────────────────────

    // path: /api 이후 경로. method: POST/PATCH/DELETE. body: JSON.
    // onResult(httpOk, parsed): httpOk=HTTP 2xx 여부, parsed=봉투 파싱 결과(실패/미파싱 시 null).
    private IEnumerator Send(
        string path, string method, string body,
        Action<bool, PartNodeResponse> onResult)
    {
        string url = $"http://{_syncClient.Host}/api/{path}";

        using (var req = new UnityWebRequest(url, method))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[PartNodeApiClient] {method} {url} body={body}");
            yield return req.SendWebRequest();

            bool httpOk = req.result == UnityWebRequest.Result.Success;
            if (!httpOk)
            {
                Debug.LogError($"[PartNodeApiClient] {method} {path} 실패: {req.error} (code={req.responseCode})");
                onResult?.Invoke(false, null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[PartNodeApiClient] {method} {path} 응답(code={req.responseCode}): {raw}");

            PartNodeResponse parsed = null;
            try { parsed = JsonUtility.FromJson<PartNodeResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[PartNodeApiClient] 응답 파싱 실패: {e.Message}"); }

            onResult?.Invoke(true, parsed);
        }
    }

    // ─────────────────────────────────────────────
    // 참조 확인 + ContextMenu 테스트
    // ─────────────────────────────────────────────

    private bool EnsureRefs(Action<bool> onDone)
    {
        if (_graphManager == null || _syncClient == null)
        {
            Debug.LogWarning("[PartNodeApiClient] 실패: GraphManager 또는 GraphSyncClient 가 연결되지 않았습니다.");
            onDone?.Invoke(false);
            return false;
        }
        if (string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[PartNodeApiClient] 실패: host 또는 room_id 가 비어 있습니다.");
            onDone?.Invoke(false);
            return false;
        }
        return true;
    }

    [ContextMenu("테스트: 파트 생성")]
    private void TestCreate() => CreatePart("테스트 파트", false, ok => Debug.Log($"[PartNodeApiClient] CreatePart 결과={ok}"));
}
