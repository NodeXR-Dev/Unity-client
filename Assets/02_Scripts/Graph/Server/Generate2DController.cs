using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// 2D 생성/변경 흐름 컨트롤러. (API 명세 2026-07-10)
//   RequestGenerateFeature(): POST /api/2d/generate/feature { room_id, user_id } — 요구사항(feature) 기반 기본 스케치.
//   RequestGenerateGraph():   POST /api/2d/generate/graph   { room_id, user_id, connections } — 그래프(PART↔속성) 기반 스케치.
//   RequestColorChange():     POST /api/2d/color_change (multipart) { room_id, file, asset_id } — 색상 변경.
//   결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 온다.
//     GraphSyncClient.OnImage2DGenerated 를 구독해 img_url 텍스처를 중앙 RawImage 에 표시한다.
//
// host/room_id/user_id 는 GraphSyncClient 재사용. connections 는 GraphManager 의 적용 엣지에서 빌드.
// [주의] 서버 2D 엔드포인트 배포 전엔 실패 가능. 이 컨트롤러는 계약 대비 선구현.
public class Generate2DController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphSyncClient _syncClient;
    [Tooltip("그래프 기반 스케치의 connections 빌드용")]
    [SerializeField] private GraphManager _graphManager;
    [Tooltip("생성된 2D 이미지를 표시할 중앙 RawImage")]
    [SerializeField] private RawImage _centerImage;
    [SerializeField] private Button _generateButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private float _generationTimeoutSeconds = 120f;

    private Texture2D _currentTexture;   // 이전 다운로드 텍스처(교체 시 파기용)
    private Coroutine _timeoutCoroutine;
    private bool _isGenerating;

    // ─────────────────────────────────────────────
    // 생명주기 / 구독
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        ResolveReferences();
        if (_syncClient != null)
            _syncClient.OnImage2DGenerated += HandleImageGenerated;
        SetStatus("부품과 속성을 연결한 뒤 생성하세요.");
        SetButtonInteractable(true);
    }

    private void OnDisable()
    {
        if (_syncClient != null)
            _syncClient.OnImage2DGenerated -= HandleImageGenerated;
        StopGenerationTimeout();
        _isGenerating = false;
    }

    // ─────────────────────────────────────────────
    // 생성/변경 요청 (결과는 WS 2D_GENERATED 로 별도 통보)
    // ─────────────────────────────────────────────

    // (호환) 구 진입점 이름 유지 — dev2 Inspector(버튼 OnClick)/코드가 RequestGenerate 로 연결해둔 경우 대비.
    // 명세상 기본 생성은 feature 기반이므로 그쪽으로 위임한다.
    [ContextMenu("2D Generate (호환 → feature)")]
    public void RequestGenerate() => RequestGenerateFeature();

    // 요구사항(feature) 기반 기본 스케치. 개발자 2가 기능 정의 후 호출.
    [ContextMenu("2D Generate (feature)")]
    public void RequestGenerateFeature()
    {
        if (_isGenerating) return;
        if (!EnsureConn("RequestGenerateFeature")) return;
        string body = JsonUtility.ToJson(new Generate2DFeatureRequest
        {
            room_id = _syncClient.RoomId,
            user_id = _syncClient.UserId,
        });
        BeginGeneration("2D 생성 요청을 보내는 중...");
        StartCoroutine(PostJson("2d/generate/feature", body));
    }

    [ContextMenu("2D Generate (graph)")]
    public void RequestGenerateGraphAll()
        => RequestGenerateGraph();

    // 그래프(PART↔속성 연결) 기반 스케치. 선택 PART 만 부분 생성하려면 selectedPartNodeIds 지정.
    public void RequestGenerateGraph(ICollection<string> selectedPartNodeIds = null)
    {
        if (_isGenerating) return;
        if (!EnsureConn("RequestGenerateGraph")) return;
        if (_graphManager == null)
        {
            Debug.LogWarning("[Generate2DController] RequestGenerateGraph 실패: GraphManager 미연결.");
            SetStatus("그래프 연결을 확인해 주세요.");
            return;
        }
        List<ConnectionDto> connections =
            _graphManager.BuildGraphConnections(selectedPartNodeIds);
        if (connections == null || connections.Count == 0)
        {
            Debug.LogWarning("[Generate2DController] RequestGenerateGraph 중단: 적용된 속성-부품 연결이 없습니다.");
            SetStatus("속성을 부품에 연결한 뒤 생성하세요.");
            return;
        }
        var req = new Generate2DGraphRequest
        {
            room_id     = _syncClient.RoomId,
            user_id     = _syncClient.UserId,
            connections = connections,
        };
        BeginGeneration("2D 생성 요청을 보내는 중...");
        StartCoroutine(PostJson("2d/generate/graph", JsonUtility.ToJson(req)));
    }

    // 색상 변경(multipart). imageData/fileName/mime 는 호출부(스케치 보드 등)가 제공.
    public void RequestColorChange(string assetId, byte[] imageData, string fileName, string mime)
    {
        if (_isGenerating) return;
        if (!EnsureConn("RequestColorChange")) return;
        if (imageData == null || imageData.Length == 0)
        {
            Debug.LogWarning("[Generate2DController] RequestColorChange 실패: 이미지 데이터가 비어 있습니다.");
            SetStatus("변경할 이미지가 없습니다.");
            return;
        }
        BeginGeneration("색상 변경 요청을 보내는 중...");
        StartCoroutine(PostColorChange(assetId, imageData, fileName, mime));
    }

    private bool EnsureConn(string from)
    {
        ResolveReferences();
        if (_syncClient == null || string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning($"[Generate2DController] {from} 실패: GraphSyncClient(host/room_id)가 비어 있습니다.");
            SetStatus("백엔드 주소와 방 정보를 확인해 주세요.");
            return false;
        }
        if (string.IsNullOrEmpty(_syncClient.UserId))
        {
            Debug.LogWarning($"[Generate2DController] {from} 실패: user_id가 비어 있습니다.");
            SetStatus("사용자 정보가 없어 생성할 수 없습니다.");
            return false;
        }
        if (!_syncClient.IsConnected)
        {
            Debug.LogWarning($"[Generate2DController] {from} 실패: WebSocket이 연결되지 않았습니다.");
            SetStatus("백엔드 연결이 필요합니다.");
            return false;
        }
        return true;
    }

    // JSON POST 공통(2xx만 확인, 결과 이미지는 WS 2D_GENERATED).
    private IEnumerator PostJson(string path, string body)
    {
        string url = $"http://{_syncClient.Host}/api/{path}";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.timeout = 20;
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[Generate2DController] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate2DController] {path} 실패: {req.error} (code={req.responseCode})");
                FinishGeneration("2D 생성 요청에 실패했습니다.");
                yield break;
            }
            Debug.Log($"[Generate2DController] {path} 접수됨(code={req.responseCode}). 결과는 WS 2D_GENERATED 대기.");
            SetStatus("AI가 스케치를 생성하고 있습니다...");
            RestartGenerationTimeout();
        }
    }

    private IEnumerator PostColorChange(string assetId, byte[] imageData, string fileName, string mime)
    {
        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("room_id",  _syncClient.RoomId),
            new MultipartFormDataSection("asset_id", assetId ?? ""),
            new MultipartFormFileSection("file", imageData,
                string.IsNullOrEmpty(fileName) ? "sketch.png" : fileName,
                string.IsNullOrEmpty(mime) ? "image/png" : mime),
        };
        string url = $"http://{_syncClient.Host}/api/2d/color_change";

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 20;
            Debug.Log($"[Generate2DController] POST(multipart) {url} asset_id={assetId} bytes={imageData.Length}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate2DController] 2d/color_change 실패: {req.error} (code={req.responseCode})");
                FinishGeneration("색상 변경 요청에 실패했습니다.");
                yield break;
            }
            Debug.Log($"[Generate2DController] 2d/color_change 접수됨(code={req.responseCode}). 결과는 WS 2D_GENERATED 대기.");
            SetStatus("AI가 스케치를 변경하고 있습니다...");
            RestartGenerationTimeout();
        }
    }

    // ─────────────────────────────────────────────
    // 결과 수신 (WS 2D_GENERATED → 중앙 이미지)
    // ─────────────────────────────────────────────

    private void HandleImageGenerated(string imgUrl)
    {
        if (string.IsNullOrEmpty(imgUrl))
        {
            Debug.LogWarning("[Generate2DController] 2D_GENERATED img_url 이 비어 있습니다.");
            FinishGeneration("생성 결과 주소가 비어 있습니다.");
            return;
        }
        _isGenerating = true;
        SetButtonInteractable(false);
        SetStatus("생성된 스케치를 불러오는 중...");
        StopGenerationTimeout();
        StartCoroutine(DownloadAndShow(imgUrl));
    }

    private IEnumerator DownloadAndShow(string imgUrl)
    {
        // 상대 경로면 host 를 붙여 절대 URL 로 만든다.
        string url = imgUrl.StartsWith("http")
            ? imgUrl
            : $"http://{_syncClient?.Host}{(imgUrl.StartsWith("/") ? "" : "/")}{imgUrl}";

        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            req.timeout = 30;
            Debug.Log($"[Generate2DController] 2D 이미지 다운로드 → {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate2DController] 2D 이미지 다운로드 실패: {req.error} (code={req.responseCode})");
                FinishGeneration("스케치 이미지를 불러오지 못했습니다.");
                yield break;
            }

            var texture = DownloadHandlerTexture.GetContent(req);
            if (_centerImage != null)
            {
                _centerImage.texture = texture;
                _centerImage.enabled = true;
                Color visibleColor = _centerImage.color;
                visibleColor.a = 1f;
                _centerImage.color = visibleColor;
            }
            else
            {
                Debug.LogWarning("[Generate2DController] centerImage(RawImage) 가 연결되지 않아 표시를 건너뜁니다.");
                Destroy(texture);
                FinishGeneration("중앙 스케치 화면 연결을 확인해 주세요.");
                yield break;
            }

            // 이전 텍스처 정리(메모리 누수 방지)
            if (_currentTexture != null && _currentTexture != texture)
                Destroy(_currentTexture);
            _currentTexture = texture;
            FinishGeneration("2D 스케치 생성 완료");
        }
    }

    private void ResolveReferences()
    {
        if (_syncClient == null)
            _syncClient = GetComponent<GraphSyncClient>();
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
        if (_centerImage == null)
        {
            MainSketchView sketchView = FindFirstObjectByType<MainSketchView>();
            if (sketchView != null)
                _centerImage = sketchView.GetComponentInChildren<RawImage>(true);
        }
    }

    private void BeginGeneration(string status)
    {
        _isGenerating = true;
        SetButtonInteractable(false);
        SetStatus(status);
        RestartGenerationTimeout();
    }

    private void FinishGeneration(string status)
    {
        StopGenerationTimeout();
        _isGenerating = false;
        SetButtonInteractable(true);
        SetStatus(status);
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (_generateButton != null)
            _generateButton.interactable = interactable;
    }

    private void SetStatus(string status)
    {
        if (_statusText != null)
            _statusText.text = status;
    }

    private void RestartGenerationTimeout()
    {
        StopGenerationTimeout();
        if (isActiveAndEnabled)
            _timeoutCoroutine = StartCoroutine(WaitForGenerationTimeout());
    }

    private void StopGenerationTimeout()
    {
        if (_timeoutCoroutine == null) return;
        StopCoroutine(_timeoutCoroutine);
        _timeoutCoroutine = null;
    }

    private IEnumerator WaitForGenerationTimeout()
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(10f, _generationTimeoutSeconds));
        _timeoutCoroutine = null;
        _isGenerating = false;
        SetButtonInteractable(true);
        SetStatus("생성 시간이 초과되었습니다. 백엔드 상태를 확인해 주세요.");
    }

    private void OnDestroy()
    {
        StopGenerationTimeout();
        if (_currentTexture != null)
            Destroy(_currentTexture);
    }
}
