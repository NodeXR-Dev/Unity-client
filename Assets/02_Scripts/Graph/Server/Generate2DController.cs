using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// 기본 2D 생성 흐름 컨트롤러.
//   1) RequestGenerate(): POST /api/2d/generate 로 { room_id } 전송 (발화 기반 기본 생성 트리거).
//      - 이 단계에서는 노드/connection/RegenerateConnectionBuilder 를 반영하지 않는다.
//      - 노드 기반 부분 재생성은 별도(/api/2d/regenerate, GraphManager.BuildRegenerateRequestJson).
//   2) 결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 온다.
//      GraphSyncClient.OnImage2DGenerated 를 구독해 img_url 텍스처를 중앙 RawImage 에 표시한다.
//
// host/room_id 는 GraphSyncClient 가 이미 들고 있으므로 재사용한다.
// [주의] 서버 /api/2d/generate 는 현재 미구현(주석). 이 컨트롤러는 계약 대비 선구현.
public class Generate2DController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphSyncClient _syncClient;
    [Tooltip("생성된 2D 이미지를 표시할 중앙 RawImage")]
    [SerializeField] private RawImage _centerImage;

    private Texture2D _currentTexture;   // 이전 다운로드 텍스처(교체 시 파기용)

    // ─────────────────────────────────────────────
    // 생명주기 / 구독
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        if (_syncClient != null)
            _syncClient.OnImage2DGenerated += HandleImageGenerated;
    }

    private void OnDisable()
    {
        if (_syncClient != null)
            _syncClient.OnImage2DGenerated -= HandleImageGenerated;
    }

    // ─────────────────────────────────────────────
    // 생성 요청 (발화 기반 기본 2D 생성)
    // ─────────────────────────────────────────────

    // 개발자 2(Voice/Interaction)가 발화 후 이 메서드를 호출한다.
    // TODO(서버 협의 후): 발화 텍스트를 인자로 받아 Generate2DRequestDto.utterance 로 전송.
    [ContextMenu("2D Generate 요청")]
    public void RequestGenerate()
    {
        if (_syncClient == null)
        {
            Debug.LogWarning("[Generate2DController] RequestGenerate 실패: GraphSyncClient 가 연결되지 않았습니다.");
            return;
        }
        if (string.IsNullOrEmpty(_syncClient.Host) || string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.LogWarning("[Generate2DController] RequestGenerate 실패: host 또는 room_id 가 비어 있습니다.");
            return;
        }
        StartCoroutine(PostGenerate());
    }

    private IEnumerator PostGenerate()
    {
        string url  = $"http://{_syncClient.Host}/api/2d/generate";
        string body = JsonUtility.ToJson(new Generate2DRequestDto { room_id = _syncClient.RoomId });

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[Generate2DController] 2D generate 요청 → {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate2DController] 2D generate 실패: {req.error} (code={req.responseCode})");
                yield break;
            }
            // 결과 이미지는 WS 2D_GENERATED 로 별도 통보됨. 여기서는 접수 로그만.
            Debug.Log($"[Generate2DController] 2D generate 접수됨(code={req.responseCode}). 결과는 WS 2D_GENERATED 대기.");
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
            return;
        }
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
            Debug.Log($"[Generate2DController] 2D 이미지 다운로드 → {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate2DController] 2D 이미지 다운로드 실패: {req.error} (code={req.responseCode})");
                yield break;
            }

            var texture = DownloadHandlerTexture.GetContent(req);
            if (_centerImage != null)
            {
                _centerImage.texture = texture;
                _centerImage.enabled = true;
            }
            else
            {
                Debug.LogWarning("[Generate2DController] centerImage(RawImage) 가 연결되지 않아 표시를 건너뜁니다.");
            }

            // 이전 텍스처 정리(메모리 누수 방지)
            if (_currentTexture != null && _currentTexture != texture)
                Destroy(_currentTexture);
            _currentTexture = texture;
        }
    }

    private void OnDestroy()
    {
        if (_currentTexture != null)
            Destroy(_currentTexture);
    }
}
