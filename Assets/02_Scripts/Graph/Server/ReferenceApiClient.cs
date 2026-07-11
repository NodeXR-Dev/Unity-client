using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 레퍼런스(REFERENCE 노드) 서버 REST 경계. (API 명세 2026-07-10)
//   RequestKeyword(nodeId): POST /api/references/keyword { room_id, node_id } → reference_keyword(부모 체인 반영).
//     R 버튼(NodeActionPanel) 컨텍스트에서 검색 키워드 추천에 사용.
//   GenerateReference(nodeId, imageBytes, …): POST /api/references/generate (multipart) → reference_url.
//     이미지 바이트는 호출부(파일 선택/스케치 보드 — 개발자2 영역)가 제공한다. 서버가 저장·노드화하면
//     REFERENCE 노드는 그래프 동기화(GRAPH_UPDATED / GET /api/graph)로 반영된다(여기서 로컬 노드 생성 안 함).
//
// host/room_id 는 GraphSyncClient 재사용. 서버 미배포면 404 → 실패 콜백.
public class ReferenceApiClient : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphSyncClient _syncClient;  // host/room_id 재사용
    // (REFERENCE 노드는 서버 그래프 동기화로 반영되므로 GraphManager 참조 불필요.)

    // ─────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────

    // 레퍼런스 키워드 추천. onDone(keyword) — 실패 시 null.
    public void RequestKeyword(string nodeId, Action<string> onDone)
    {
        if (!EnsureRefs()) { onDone?.Invoke(null); return; }
        if (string.IsNullOrEmpty(nodeId)) { Debug.LogWarning("[ReferenceApiClient] RequestKeyword 실패: nodeId 비어 있음."); onDone?.Invoke(null); return; }
        StartCoroutine(CoKeyword(nodeId, onDone));
    }

    // 레퍼런스 연결(이미지 업로드). imageData/fileName/mime/width/height 는 호출부가 제공.
    // onDone(success, reference_url) — 실패 시 (false, null).
    public void GenerateReference(string nodeId, byte[] imageData, string fileName, string mime, int width, int height, Action<bool, string> onDone)
    {
        if (!EnsureRefs()) { onDone?.Invoke(false, null); return; }
        if (string.IsNullOrEmpty(nodeId) || imageData == null || imageData.Length == 0)
        {
            Debug.LogWarning("[ReferenceApiClient] GenerateReference 실패: nodeId 또는 이미지 데이터가 비어 있음.");
            onDone?.Invoke(false, null);
            return;
        }
        StartCoroutine(CoGenerate(nodeId, imageData, fileName, mime, width, height, onDone));
    }

    // ─────────────────────────────────────────────
    // 코루틴
    // ─────────────────────────────────────────────

    private IEnumerator CoKeyword(string nodeId, Action<string> onDone)
    {
        string body = JsonUtility.ToJson(new ReferenceKeywordRequest
        {
            room_id = _syncClient.RoomId,
            node_id = nodeId,
        });
        string url = $"http://{_syncClient.Host}/api/references/keyword";

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[ReferenceApiClient] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ReferenceApiClient] references/keyword 실패: {req.error} (code={req.responseCode})");
                onDone?.Invoke(null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[ReferenceApiClient] keyword 응답(code={req.responseCode}): {raw}");

            ReferenceKeywordResponse res = null;
            try { res = JsonUtility.FromJson<ReferenceKeywordResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[ReferenceApiClient] keyword 파싱 실패: {e.Message}"); }

            onDone?.Invoke(res?.result?.reference_keyword);
        }
    }

    private IEnumerator CoGenerate(string nodeId, byte[] imageData, string fileName, string mime, int width, int height, Action<bool, string> onDone)
    {
        string metadataJson = JsonUtility.ToJson(new ReferenceMetadata
        {
            mime_type = string.IsNullOrEmpty(mime) ? "image/png" : mime,
            width     = width,
            height    = height,
        });

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("room_id",  _syncClient.RoomId),
            new MultipartFormDataSection("node_id",  nodeId),
            new MultipartFormDataSection("metadata", metadataJson),
            new MultipartFormFileSection("file", imageData,
                string.IsNullOrEmpty(fileName) ? "reference.png" : fileName,
                string.IsNullOrEmpty(mime) ? "image/png" : mime),
        };

        string url = $"http://{_syncClient.Host}/api/references/generate";

        using (var req = UnityWebRequest.Post(url, form))
        {
            Debug.Log($"[ReferenceApiClient] POST(multipart) {url} node_id={nodeId} bytes={imageData.Length}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ReferenceApiClient] references/generate 실패: {req.error} (code={req.responseCode})");
                onDone?.Invoke(false, null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            Debug.Log($"[ReferenceApiClient] generate 응답(code={req.responseCode}): {raw}");

            ReferenceGenerateResponse res = null;
            try { res = JsonUtility.FromJson<ReferenceGenerateResponse>(raw); }
            catch (Exception e) { Debug.LogWarning($"[ReferenceApiClient] generate 파싱 실패: {e.Message}"); }

            string refUrl = res?.result?.reference_url;
            // REFERENCE 노드 자체는 서버 그래프 동기화(GRAPH_UPDATED / GET /api/graph)로 반영된다.
            onDone?.Invoke(!string.IsNullOrEmpty(refUrl), refUrl);
        }
    }

    // ─────────────────────────────────────────────
    // 참조 확인
    // ─────────────────────────────────────────────

    private bool EnsureRefs()
    {
        if (_syncClient == null || string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[ReferenceApiClient] 실패: GraphSyncClient(host/room_id)가 비어 있습니다.");
            return false;
        }
        return true;
    }
}
