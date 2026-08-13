using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// 2D 생성/변경 흐름 컨트롤러. (서버 aa81878 "job_id 추가" 반영, 2026-08-03)
//   RequestGenerateFeature(): POST /api/2d/generate/feature { room_id, user_id, job_id } — 요구사항(feature) 기반 기본 스케치.
//   RequestGenerateGraph():   POST /api/2d/generate/graph   { room_id, user_id, job_id, connections } — 그래프(PART↔속성) 기반 스케치.
//   RequestColorChange():     POST /api/2d/color_change (multipart) { room_id, user_id, job_id, asset_id, file, metadata } — 색상 변경.
//   결과 이미지는 HTTP 응답이 아니라 WS 2D_GENERATED{img_url} 로 온다.
//     GraphSyncClient.OnImage2DResult 를 구독해 img_url 텍스처를 중앙 RawImage 에 표시한다.
//     (OnImage2DGenerated 는 개발자1 MvpGenerated2DSync 용 호환 이벤트다.)
//
// [job_id] 요청마다 새로 발급해 보내고(_pendingJobId), 결과 수신 시 대조한다. 서버 스키마에서 필수라 비우면 422.
//   서버가 broadcast_to_room 을 삭제하고 요청자에게만 보내도록 바뀌었으므로(aa81878),
//   같은 클라가 요청을 연달아 보냈을 때 늦게 도착한 이전 결과가 최신 화면을 덮는 것을 막는 것이 목적이다.
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

    [Header("로비에서 만든 스케치 복원")]
    [Tooltip("회의실 진입 후 이 시간까지 서버에 그림이 생기길 기다린다")]
    [SerializeField] private float _restoreTimeoutSeconds = 90f;
    [Tooltip("복원 조회 주기")]
    [SerializeField] private float _restorePollSeconds = 3f;

    private Texture2D _currentTexture;   // 이전 다운로드 텍스처(교체 시 파기용)
    private Coroutine _timeoutCoroutine;
    private bool _isGenerating;
    private string _pendingJobId;        // 진행 중인 요청의 job_id. 결과 수신 시 대조용(비어 있으면 대기 중인 요청 없음).
    private string _currentAssetId;      // 현재 중앙에 표시 중인 2D 이미지의 서버 asset_id

    // 요청 접수부터 이미지 표시(또는 실패/타임아웃)까지 true.
    // 호출부(MvpClassroomFlow)가 "언제까지 기다려야 하는지" 판단하는 데 쓴다.
    public bool IsGenerating => _isGenerating;

    // 현재 표시 중인 2D 이미지의 서버 asset_id. 3D 생성의 source_asset_id 로 쓴다.
    // 비어 있으면 아직 서버 이미지를 받은 적이 없다는 뜻이고, 그 상태에서는 3D 를 만들 수 없다.
    public string CurrentAssetId => _currentAssetId;

    // ─────────────────────────────────────────────
    // 생명주기 / 구독
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        ResolveReferences();
        if (_syncClient != null)
            _syncClient.OnImage2DResult += HandleImageGenerated;

        // [2026-08-01] _generateButton 은 지금까지 interactable 제어에만 쓰였고 클릭 리스너가 없었다.
        //   그 결과 2D 생성 진입점이 MvpClassroomFlow 의 버튼 하나뿐이었다.
        //   (중복 호출은 _isGenerating 가드가 막는다. 비어 있으면 아무 일도 없다.)
        if (_generateButton != null)
        {
            _generateButton.onClick.RemoveListener(RequestGenerateGraphAll);
            _generateButton.onClick.AddListener(RequestGenerateGraphAll);
        }


        SetStatus("부품과 속성을 연결한 뒤 생성하세요.");
        SetButtonInteractable(true);
    }

    private void OnDisable()
    {
        if (_syncClient != null)
            _syncClient.OnImage2DResult -= HandleImageGenerated;
        if (_generateButton != null)
            _generateButton.onClick.RemoveListener(RequestGenerateGraphAll);
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
        string jobId = NewJobId();
        string body = JsonUtility.ToJson(new Generate2DFeatureRequest
        {
            room_id = _syncClient.RoomId,
            user_id = _syncClient.UserId,
            job_id  = jobId,
        });
        BeginGeneration("2D 생성 요청을 보내는 중...", jobId);
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
        string jobId = NewJobId();
        var req = new Generate2DGraphRequest
        {
            room_id     = _syncClient.RoomId,
            user_id     = _syncClient.UserId,
            job_id      = jobId,
            connections = connections,
        };
        BeginGeneration("2D 생성 요청을 보내는 중...", jobId);
        StartCoroutine(PostJson("2d/generate/graph", JsonUtility.ToJson(req)));
    }

    // 색상 변경(multipart). imageData/fileName/mime 는 호출부(스케치 보드 등)가 제공.
    // width/height 를 0 으로 두면 imageData 를 디코드해 크기를 구한다. 서버 metadata 는 width/height > 0 을 요구한다.
    public void RequestColorChange(string assetId, byte[] imageData, string fileName, string mime,
                                   int width = 0, int height = 0)
    {
        if (_isGenerating) return;
        if (!EnsureConn("RequestColorChange")) return;
        if (imageData == null || imageData.Length == 0)
        {
            Debug.LogWarning("[Generate2DController] RequestColorChange 실패: 이미지 데이터가 비어 있습니다.");
            SetStatus("변경할 이미지가 없습니다.");
            return;
        }
        if (!ResolveImageSize(imageData, ref width, ref height))
        {
            Debug.LogWarning("[Generate2DController] RequestColorChange 실패: 이미지 크기를 확인할 수 없습니다.");
            SetStatus("이미지 크기를 확인할 수 없습니다.");
            return;
        }
        string jobId = NewJobId();
        BeginGeneration("색상 변경 요청을 보내는 중...", jobId);
        StartCoroutine(PostColorChange(assetId, imageData, fileName, mime, width, height, jobId));
    }

    // width/height 가 지정돼 있으면 그대로 쓰고, 아니면 imageData 를 디코드해 채운다.
    // 서버 ColorChangeMetadataRequest 가 width/height 를 gt=0 으로 검증하므로 0 이면 422 가 된다.
    private bool ResolveImageSize(byte[] imageData, ref int width, ref int height)
    {
        if (width > 0 && height > 0) return true;

        var probe = new Texture2D(2, 2);
        try
        {
            if (!probe.LoadImage(imageData)) return false;
            width  = probe.width;
            height = probe.height;
            return width > 0 && height > 0;
        }
        finally { Destroy(probe); }
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
        string url = $"{ServerAddress.Http(_syncClient.Host)}/api/{path}";
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

    private IEnumerator PostColorChange(string assetId, byte[] imageData, string fileName, string mime,
                                        int width, int height, string jobId)
    {
        string mimeType = string.IsNullOrEmpty(mime) ? "image/png" : mime;
        // metadata 는 JSON 문자열 폼 필드다(서버가 json.loads 후 ColorChangeMetadataRequest 로 검증).
        string metadata = JsonUtility.ToJson(new ColorChangeMetadata
        {
            mime_type = mimeType,
            width     = width,
            height    = height,
        });

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("room_id",  _syncClient.RoomId),
            new MultipartFormDataSection("user_id",  _syncClient.UserId),
            new MultipartFormDataSection("job_id",   jobId),
            new MultipartFormDataSection("asset_id", assetId ?? ""),
            new MultipartFormDataSection("metadata", metadata),
            new MultipartFormFileSection("file", imageData,
                string.IsNullOrEmpty(fileName) ? "sketch.png" : fileName,
                mimeType),
        };
        string url = $"{ServerAddress.Http(_syncClient.Host)}/api/2d/color_change";

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

    private void HandleImageGenerated(GraphSyncClient.Image2DResult result)
    {
        string imgUrl = result.ImgUrl;
        string jobId  = result.JobId;

        if (string.IsNullOrEmpty(imgUrl))
        {
            Debug.LogWarning("[Generate2DController] 2D_GENERATED img_url 이 비어 있습니다.");
            FinishGeneration("생성 결과 주소가 비어 있습니다.");
            return;
        }

        // "생성 버튼을 누른 그때의 결과만 보여준다" 규칙.
        //   1) 대기 중인 요청이 없으면 무시한다. 이전 요청의 결과가 뒤늦게 도착해 최신 화면을 덮는 것을 막는다.
        //      (서버가 요청자에게만 보내므로, 내가 요청하지 않은 결과는 화면에 올릴 이유가 없다.)
        //   2) 대기 중인데 job_id 가 다르면 무시한다. 연속 요청 시 이전 것이 늦게 온 경우다.
        //      서버가 job_id 를 안 실어 보내면("") 판정할 수 없으므로 그대로 받는다(구버전 호환).
        if (string.IsNullOrEmpty(_pendingJobId))
        {
            Debug.Log($"[Generate2DController] 2D_GENERATED 무시(대기 중인 요청 없음): 수신 job_id={jobId}");
            return;
        }
        if (!string.IsNullOrEmpty(jobId) &&
            !string.Equals(_pendingJobId, jobId, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Generate2DController] 2D_GENERATED 무시(job_id 불일치): 수신={jobId} 대기중={_pendingJobId}");
            return;
        }

        // 수락한 job 은 즉시 소비한다. 서버가 같은 결과를 두 번 보내도 중복 다운로드하지 않는다.
        _pendingJobId = null;

        // 3D 생성이 이 asset_id 를 source 로 쓴다. 화면에 뜬 이미지와 짝을 맞추기 위해 여기서 갱신한다.
        _currentAssetId = result.AssetId;

        _isGenerating = true;
        SetButtonInteractable(false);
        SetStatus("생성된 스케치를 불러오는 중...");
        StopGenerationTimeout();
        StartCoroutine(DownloadAndShow(imgUrl, "2D 스케치 생성 완료"));
    }

    // ─────────────────────────────────────────────
    // 진입 시 기존 스케치 복원 (로비 → 회의실)
    // ─────────────────────────────────────────────

    // 로비에서 요구사항을 제출하면 서버가 POST /api/features/generate 를 받는 즉시 초기 2D 스케치
    // 생성을 백그라운드로 시작한다. 완성되면 WS 2D_GENERATED 를 "요청한 사용자에게만" 보내는데
    // (서버 image_2d_generation_task_service 의 send_to_user), 로비 → 회의실은 씬을 갈아타면서
    // WS 가 끊기므로 그 푸시를 놓친다. 서버 로그에는 2d_generation_ws_skipped 만 남는다.
    //
    // 그래서 회의실에 들어올 때 이미 만들어진 이미지를 GET /api/history/{room_id} 로 직접 가져온다.
    // (서버는 손댈 필요가 없다 — 결과가 이미 core_2d_image 로 조회된다.)
    //
    // 생성 중이거나 이미 서버 이미지가 화면에 올라와 있으면 아무 것도 하지 않는다.
    // 사용자가 방금 만든 그림을 로비 시절 그림으로 덮지 않기 위해서다.
    public void RestoreExistingSketch()
    {
        if (_isGenerating || !string.IsNullOrEmpty(_currentAssetId))
            return;

        ResolveReferences();
        if (_syncClient == null ||
            string.IsNullOrEmpty(_syncClient.Host) ||
            string.IsNullOrEmpty(_syncClient.RoomId))
        {
            Debug.Log(
                "[Generate2DController] 기존 스케치 복원 건너뜀: host/room_id 가 아직 없습니다.");
            return;
        }

        StartCoroutine(CoRestoreExistingSketch());
    }

    private IEnumerator CoRestoreExistingSketch()
    {
        string url =
            $"{ServerAddress.Http(_syncClient.Host)}/api/history/" +
            UnityWebRequest.EscapeURL(_syncClient.RoomId);

        // 회의실 진입은 로비의 요구사항 제출 직후(실측 1초)라, 그 시점엔 서버가 아직 그림을
        // 만드는 중이다. 한 번만 조회하면 항상 빈손이므로 생길 때까지 주기적으로 다시 본다.
        CoreImageDto latest = null;
        int latestVersion = int.MinValue;
        float deadline = Time.unscaledTime + _restoreTimeoutSeconds;

        while (latest == null && Time.unscaledTime < deadline)
        {
            // 사용자가 직접 생성을 시작했으면 그쪽이 우선이다. 폴링을 접는다.
            if (_isGenerating || !string.IsNullOrEmpty(_currentAssetId))
                yield break;

            string raw = null;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    raw = req.downloadHandler.text;
                }
                else
                {
                    // 아직 방이 준비되지 않았거나 서버가 늦은 경우다. 다음 주기에 다시 본다.
                    Debug.LogWarning(
                        $"[Generate2DController] 기존 스케치 조회 실패: {req.error} (code={req.responseCode})");
                }
            }

            if (!string.IsNullOrEmpty(raw))
            {
                HistoryResponse res = null;
                try { res = JsonUtility.FromJson<HistoryResponse>(raw); }
                catch (System.Exception e)
                {
                    Debug.LogWarning(
                        $"[Generate2DController] 기존 스케치 응답 파싱 실패: {e.Message}");
                }

                // 여러 버전이 올 수 있으므로 이미지가 붙어 있는 것 중 가장 최신(graph_version 최대)을 고른다.
                List<GraphSnapshotDto> history = res?.result?.history;
                if (history != null)
                {
                    foreach (GraphSnapshotDto snapshot in history)
                    {
                        if (snapshot?.core_2d_image == null ||
                            string.IsNullOrEmpty(snapshot.core_2d_image.image_url))
                            continue;
                        if (snapshot.graph_version < latestVersion)
                            continue;
                        latestVersion = snapshot.graph_version;
                        latest = snapshot.core_2d_image;
                    }
                }
            }

            if (latest == null)
                yield return new WaitForSecondsRealtime(_restorePollSeconds);
        }

        if (latest == null)
        {
            Debug.Log(
                $"[Generate2DController] 복원할 기존 스케치가 없습니다 " +
                $"({_restoreTimeoutSeconds}초 안에 생성되지 않음).");
            yield break;
        }

        // 조회하는 사이에 사용자가 직접 생성을 시작했으면 그쪽이 우선이다.
        if (_isGenerating || !string.IsNullOrEmpty(_currentAssetId))
            yield break;

        // 3D 생성이 source_asset_id 로 쓰므로 복원한 이미지의 asset_id 도 채워 둔다.
        _currentAssetId = latest.asset_id;
        _isGenerating   = true;
        SetButtonInteractable(false);
        SetStatus("이전에 만든 스케치를 불러오는 중...");
        Debug.Log(
            $"[Generate2DController] 기존 스케치 복원: graph_version={latestVersion} " +
            $"asset_id={latest.asset_id}");

        yield return DownloadAndShow(latest.image_url, "로비에서 만든 스케치예요.");
    }

    private IEnumerator DownloadAndShow(string imgUrl, string doneStatus)
    {
        // 상대 경로면 host 를 붙여 절대 URL 로 만든다.
        string url = imgUrl.StartsWith("http")
            ? imgUrl
            : $"{ServerAddress.Http(_syncClient?.Host)}{(imgUrl.StartsWith("/") ? "" : "/")}{imgUrl}";

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
            HideSketchPlaceholder();
            Debug.Log(
                $"[Generate2DController] 2D 이미지 표시 완료: {texture.width}x{texture.height} " +
                $"→ {_centerImage.name} (canvas={(_centerImage.canvas != null ? _centerImage.canvas.name : "없음")}, " +
                $"활성={_centerImage.gameObject.activeInHierarchy})");
            FinishGeneration(doneStatus);
        }
    }

    // 보드에 깔려 있는 "아직 생성된 그림이 없어요" 안내를 지운다.
    //
    // 지금까지는 MvpClassroomFlow 가 자기 생성 흐름에서만 이 문구를 숨겼다(HideSketchPlaceholder).
    // 그래서 서버에서 받아 온 이미지를 올려도 안내가 그림 위에 그대로 남아, 그림이 없는 것처럼 보였다.
    // 이미지를 올리는 지점이 여기 하나이므로 여기서 함께 처리한다.
    private void HideSketchPlaceholder()
    {
        if (_centerImage == null) return;

        Transform root = _centerImage.canvas != null
            ? _centerImage.canvas.transform
            : _centerImage.transform.root;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == "SketchPlaceholder")
            {
                t.gameObject.SetActive(false);
                return;
            }
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

    // 요청마다 새 job_id 를 발급한다. 서버 스키마가 UUID 를 요구하므로 Guid 형식(하이픈 포함)을 그대로 쓴다.
    private static string NewJobId() => System.Guid.NewGuid().ToString();

    private void BeginGeneration(string status, string jobId)
    {
        _isGenerating = true;
        _pendingJobId = jobId;
        SetButtonInteractable(false);
        SetStatus(status);
        RestartGenerationTimeout();
    }

    private void FinishGeneration(string status)
    {
        StopGenerationTimeout();
        _isGenerating = false;
        _pendingJobId = null;
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
