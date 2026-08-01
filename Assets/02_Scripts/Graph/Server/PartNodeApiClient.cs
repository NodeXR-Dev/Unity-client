using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 파트 노드 CRUD 서버 REST 경계.
//   생성 POST   /api/part_node/generate  { room_id, text, position:[x,y,z] }
//   수정 PATCH  /api/part_node/modify    { room_id, part_node_id, part_node_text }
//   삭제 DELETE /api/part_node/delete    { room_id, part_node_id }
//
// 협의(2026-07-01): PART 노드는 REST로 일원화(WS NODE_* 미사용). GraphManager 는 서버를 모르며,
//   이 클래스가 REST를 호출하고 성공 시에만 GraphManager 로컬 변경(이벤트 미발행 경로)을 유발한다.
// host/room_id 는 GraphSyncClient 가 이미 들고 있으므로 재사용한다(Generate2DController 선례).
//
// [정정 2026-07-30] 실서버 검증 결과 3개 경로 모두 존재하며 필드명도 일치한다(더 이상 404 아님).
//   단 옛 명세의 `generate/keyboard` 는 서버에 없어 CreatePartKeyboard 도 `generate` 로 보낸다.
//   서버가 발화/키보드를 구분하지 않으므로 CreatePart(발화)와 CreatePartKeyboard(키보드)는
//   같은 요청으로 수렴한다. 공개 API 두 개는 호출부 의도를 남기기 위해 유지.
public class PartNodeApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private GraphSyncClient _syncClient;   // host/room_id 재사용

    [Header("옵션")]
    [Tooltip("true면 REST 실패 시 로컬만으로 진행(생성은 self-GUID). 서버 미배포 상태의 UI 테스트용. 기본 false(REST 일원화).")]
    [SerializeField] private bool _offlineFallback = false;

    private bool _subscribed;

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_graphManager == null || _subscribed) return;
        _graphManager.OnLocalPartNodeCreated += HandleLocalPartNodeCreated;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graphManager == null || !_subscribed) return;
        _graphManager.OnLocalPartNodeCreated -= HandleLocalPartNodeCreated;
        _subscribed = false;
    }

    // 로컬 GUID 로 만들어진 PART 를 서버에 등록하고 서버 UUID 로 rekey 한다.
    //   RequestCreatePartNode(2-인자) 호출부는 서버를 모르므로 여기서 일괄 보정한다.
    //   실패 시 로컬 노드는 그대로 두되, 서버-known 이 아니므로 이후 WS 뮤테이션은 [NODE404] 가 된다.
    private void HandleLocalPartNodeCreated(
        string localNodeId, string label, bool isGlobal, Vector3 position)
    {
        if (_graphManager == null || _syncClient == null) return;
        if (string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId)) return;

        StartCoroutine(CoRegisterLocalPart(localNodeId, label, position));
    }

    // 서버가 발급한 id 로 만든 PART 를 "서버-known" 으로 표시한다.
    //   [2026-08-01] 이 표시가 없으면 GraphSyncClient 가 이후 EDGE_CREATE / NODE_TEXT_UPDATE / NODE_MOVE 를
    //   "양 끝 노드가 서버-known 이 아니다"로 판단해 전부 스킵한다(퀘스트 실기 확인 — 속성↔파트 연결이
    //   서버에 안 올라가 2D 생성의 connections 가 비었다).
    //   ApplyServerNodeId 는 jobId == serverNodeId 인 경우 rekey 없이 서버-known 표시만 수행한다.
    private void MarkServerKnown(string serverNodeId, string serverNodeText)
    {
        if (_graphManager == null || string.IsNullOrEmpty(serverNodeId)) return;
        _graphManager.ApplyServerNodeId(serverNodeId, serverNodeId, serverNodeText);
    }

    private IEnumerator CoRegisterLocalPart(string localNodeId, string label, Vector3 position)
    {
        string body = JsonUtility.ToJson(new PartNodeCreateRequest
        {
            room_id  = _syncClient.RoomId,
            text     = label,
            position = new[] { position.x, position.y, position.z },
        });

        PartNodeResponse res = null;
        yield return Send("part_node/generate", UnityWebRequest.kHttpVerbPOST, body, (ok, r) => res = ok ? r : null);

        if (res?.result != null && !string.IsNullOrEmpty(res.result.part_node_id))
        {
            _graphManager.ApplyServerNodeId(localNodeId, res.result.part_node_id, res.result.part_node_text);
            Debug.Log($"[PartNodeApiClient] 로컬 PART 서버 등록 완료: {localNodeId} → {res.result.part_node_id}");
        }
        else
        {
            Debug.LogWarning($"[PartNodeApiClient] 로컬 PART 서버 등록 실패(로컬 노드 유지): node_id={localNodeId}");
        }
    }

    // ─────────────────────────────────────────────
    // 공개 API (UI 포트가 호출)
    // ─────────────────────────────────────────────

    // 파트 생성: utterance 로 서버 생성 요청 → 서버 발급 part_node_id/text 로 로컬 PART 생성.
    // position 은 서버 명세 필수 필드. 위치 정보가 없으면 Vector3.zero.
    public void CreatePart(string utterance, bool isGlobal, Action<bool> onDone)
        => CreatePart(utterance, isGlobal, Vector3.zero, onDone);

    public void CreatePart(string utterance, bool isGlobal, Vector3 position, Action<bool> onDone)
    {
        if (!EnsureRefs(onDone)) return;
        StartCoroutine(CoCreate(utterance, isGlobal, position, onDone));
    }

    // 파트 생성(키보드): 입력 text 를 그대로 서버 생성 요청(LLM 없음) → 서버 발급 id/text 로 로컬 PART 생성.
    public void CreatePartKeyboard(string text, bool isGlobal, Action<bool> onDone)
        => CreatePartKeyboard(text, isGlobal, Vector3.zero, onDone);

    public void CreatePartKeyboard(string text, bool isGlobal, Vector3 position, Action<bool> onDone)
    {
        if (!EnsureRefs(onDone)) return;
        StartCoroutine(CoCreateKeyboard(text, isGlobal, position, onDone));
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

    private IEnumerator CoCreate(string utterance, bool isGlobal, Vector3 position, Action<bool> onDone)
    {
        string body = JsonUtility.ToJson(new PartNodeCreateRequest
        {
            room_id  = _syncClient.RoomId,
            text     = utterance,
            position = new[] { position.x, position.y, position.z },
        });

        PartNodeResponse res = null;
        yield return Send("part_node/generate", UnityWebRequest.kHttpVerbPOST, body, (ok, r) => res = ok ? r : null);

        if (res?.result != null && !string.IsNullOrEmpty(res.result.part_node_id))
        {
            string label = string.IsNullOrEmpty(res.result.part_node_text) ? utterance : res.result.part_node_text;
            _graphManager.RequestCreatePartNode(label, isGlobal, res.result.part_node_id);
            MarkServerKnown(res.result.part_node_id, res.result.part_node_text);
            onDone?.Invoke(true);
            yield break;
        }

        // 실패 → 오프라인 폴백(로컬 self-GUID 생성) 또는 실패 통보
        if (_offlineFallback)
        {
            Debug.LogWarning("[PartNodeApiClient] generate 실패 → offlineFallback: 로컬 PART 생성(self-GUID).");
            // id 를 명시해 OnLocalPartNodeCreated 재발행을 막는다(재귀 방지).
            _graphManager.RequestCreatePartNode(utterance, isGlobal, Guid.NewGuid().ToString());
            onDone?.Invoke(true);
        }
        else onDone?.Invoke(false);
    }

    private IEnumerator CoCreateKeyboard(string text, bool isGlobal, Vector3 position, Action<bool> onDone)
    {
        string body = JsonUtility.ToJson(new PartNodeCreateRequest
        {
            room_id  = _syncClient.RoomId,
            text     = text,
            position = new[] { position.x, position.y, position.z },
        });

        PartNodeResponse res = null;
        yield return Send("part_node/generate", UnityWebRequest.kHttpVerbPOST, body, (ok, r) => res = ok ? r : null);

        if (res?.result != null && !string.IsNullOrEmpty(res.result.part_node_id))
        {
            string label = string.IsNullOrEmpty(res.result.part_node_text) ? text : res.result.part_node_text;
            _graphManager.RequestCreatePartNode(label, isGlobal, res.result.part_node_id);
            MarkServerKnown(res.result.part_node_id, res.result.part_node_text);
            onDone?.Invoke(true);
            yield break;
        }

        if (_offlineFallback)
        {
            Debug.LogWarning("[PartNodeApiClient] generate(키보드) 실패 → offlineFallback: 로컬 PART 생성(self-GUID).");
            // id 를 명시해 OnLocalPartNodeCreated 재발행을 막는다(재귀 방지).
            _graphManager.RequestCreatePartNode(text, isGlobal, Guid.NewGuid().ToString());
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
