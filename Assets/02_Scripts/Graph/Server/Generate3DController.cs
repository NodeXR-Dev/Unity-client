using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// 3D 생성 흐름 컨트롤러. (2026-08-04 신규)
//   RequestGenerate3D(): POST /api/3d/generate { room_id, user_id, job_id, asset_id }
//   결과는 WS 3D_GENERATED{asset_id, mime_type, model_url} 로 오고, model_url 은 GLB 다.
//   GLB 는 glTFast(com.atteneder.gltfast 6.17.0)로 런타임 로드해 _modelParent 아래에 붙인다.
//
// [비용 방어 — 이 컨트롤러의 핵심 책임]
//   3D 생성은 호출 1회당 Meshy API 과금이 발생하고, 서버에는 중복 방지가 전혀 없다.
//   같은 2D 이미지로 몇 번을 요청하든 서버는 매번 새로 만들고 매번 과금한다.
//   그래서 아래 네 가지를 클라에서 막는다.
//     1) 진행 중 재요청 차단(_isGenerating) — VR 에서 버튼이 두 번 눌리는 사고 방지
//     2) 결과 재사용(_modelUrlBySourceAsset) — 같은 2D 로 이미 만든 3D 가 있으면 API 를 안 부르고
//        캐시된 GLB 를 다시 띄운다. "아까 만든 3D 다시 보기" 가 가장 흔한 재요청 패턴이다.
//     3) source asset 없으면 요청 자체를 안 보냄 — 2D 결과가 없으면 서버가 어차피 거부한다.
//     4) 명시적 호출만 진입점 — 씬 전환·상태 변화에서 자동으로 부르지 않는다.
//   캐시는 프로세스 메모리라 앱을 껐다 켜면 사라지고, 다른 참가자의 요청도 막지 못한다.
//   근본 해결(서버 source_asset_id 중복 조회)은 서버팀 몫이다. TODO.md 참고.
public class Generate3DController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphSyncClient _syncClient;
    [Tooltip("변환할 원본 2D 이미지의 asset_id 를 가져올 컨트롤러")]
    [SerializeField] private Generate2DController _generate2DController;
    [Tooltip("생성된 GLB 를 붙일 부모. 비우면 이 오브젝트 아래에 붙는다.")]
    [SerializeField] private Transform _modelParent;
    [SerializeField] private Button _generateButton;
    [SerializeField] private TMP_Text _statusText;
    [Tooltip("서버 3D 생성 대기 상한(초). 서버 Meshy 폴링 상한이 900초라 그보다 넉넉히 잡는다.")]
    [SerializeField] private float _generationTimeoutSeconds = 600f;

    [Header("테스트 (비용 절약)")]
    [Tooltip("값이 있으면 서버에 요청하지 않고 이 GLB 를 바로 불러온다. " +
             "위치·크기·셰이더를 확인할 때 Meshy 과금 없이 반복 테스트하기 위한 용도. " +
             "실제 생성을 하려면 반드시 비워야 한다.")]
    [SerializeField] private string _debugModelUrl = "";

    [Header("배치")]
    [Tooltip("모델 최장변을 이 크기(m)로 맞춘다. Meshy GLB 는 스케일이 제각각이라 정규화가 필요하다.")]
    [SerializeField] private float _targetSize = 0.45f;
    [Tooltip("_modelParent 가 비었을 때: 카메라 앞 거리(m)")]
    [SerializeField] private float _placementDistance = 1.0f;
    [Tooltip("_modelParent 가 비었을 때: 눈높이 대비 상하 오프셋(m). 음수면 아래.")]
    [SerializeField] private float _placementHeightOffset = -0.25f;
    [Tooltip("모델 Y축 회전 보정(도). GLB 의 정면 축이 배치 기준과 달라 반대를 볼 때 180 으로 돌린다.")]
    [SerializeField] private float _modelYawOffset = 180f;

    private bool _isGenerating;
    private string _pendingJobId;
    private string _pendingSourceAssetId;
    private GameObject _currentModel;
    private Coroutine _timeoutCoroutine;

    // 표시 중인 모델의 GltfImport. Dispose() 는 인스턴스가 쓰는 텍스처·머티리얼까지 파괴하므로
    // 모델을 화면에 두는 동안에는 살려둬야 하고, 교체·파기 시점에 함께 해제한다.
    private GltfImport _currentGltf;

    // 원본 2D asset_id → 생성된 GLB model_url. 같은 원본 재요청 시 API 호출을 건너뛴다.
    private readonly Dictionary<string, string> _modelUrlBySourceAsset =
        new Dictionary<string, string>();

    public bool IsGenerating => _isGenerating;

    // 생성된 서버 3D 모델이 씬에 있는지. 호출부(MvpClassroomFlow)가 "재생성" 대신 "토글"을
    // 선택하는 판단에 쓴다 — 이미 만든 모델을 다시 만들면 그대로 Meshy 과금이다.
    public bool HasModel => _currentModel != null;

    public bool IsModelVisible => _currentModel != null && _currentModel.activeSelf;

    // 테스트 URL 이 설정돼 있으면 서버·2D 결과 없이도 3D 를 띄울 수 있다.
    // 호출부가 "2D 를 먼저 만들라"는 안내로 막지 않도록 알려준다.
    public bool HasDebugModelUrl => !string.IsNullOrEmpty(_debugModelUrl);

    // 모델을 파기하지 않고 보이기/숨기기만 한다(회의 중 비교용).
    public void SetModelVisible(bool visible)
    {
        if (_currentModel != null)
            _currentModel.SetActive(visible);
    }

    // 모델이 놓일 자리를 외부에서 지정한다(MvpClassroomFlow 가 보드 기준 스테이지를 만들어 넘긴다).
    // XR 에서는 Camera.main 좌표가 실제 시점과 달라 카메라 기준 배치가 시야 밖으로 나간다.
    // 실측: pos=(0.08, -1.46, -1.54) — 발밑보다 아래, 뒤쪽.
    public void SetModelParent(Transform parent)
    {
        _modelParent = parent;
    }

    // ─────────────────────────────────────────────
    // 생명주기 / 구독
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        ResolveReferences();
        if (_syncClient != null)
            _syncClient.OnModel3DGenerated += HandleModelGenerated;
        if (_generateButton != null)
        {
            _generateButton.onClick.RemoveListener(RequestGenerate3D);
            _generateButton.onClick.AddListener(RequestGenerate3D);
        }
        SetStatus("2D 그림을 만든 뒤 3D로 바꿀 수 있어요.");
    }

    private void OnDisable()
    {
        if (_syncClient != null)
            _syncClient.OnModel3DGenerated -= HandleModelGenerated;
        if (_generateButton != null)
            _generateButton.onClick.RemoveListener(RequestGenerate3D);
        StopGenerationTimeout();
        _isGenerating = false;
        _pendingJobId = null;
    }

    private void ResolveReferences()
    {
        if (_syncClient == null)
            _syncClient = GetComponent<GraphSyncClient>();
        if (_generate2DController == null)
            _generate2DController = GetComponent<Generate2DController>();
        if (_generate2DController == null)
            _generate2DController = FindFirstObjectByType<Generate2DController>();
        // _modelParent 는 채우지 않는다. 비어 있는 상태가 "카메라 앞에 배치" 를 뜻하며
        // FitAndPlace() 가 그 분기를 탄다. 여기서 transform 으로 채우면 컨트롤러가 붙은
        // 오브젝트 자리(= 화면 밖이거나 머리 위)에 그대로 뜬다.
    }

    // ─────────────────────────────────────────────
    // 요청
    // ─────────────────────────────────────────────

    [ContextMenu("3D Generate")]
    public void RequestGenerate3D()
    {
        if (_isGenerating)
        {
            Debug.Log("[Generate3DController] 이미 생성 중이라 무시합니다(중복 과금 방지).");
            return;
        }
        ResolveReferences();

        // [테스트] _debugModelUrl 이 있으면 서버를 거치지 않고 그 GLB 를 바로 불러온다.
        // Meshy 는 호출 1회당 과금이라, 위치·크기·셰이더를 확인하는 반복 테스트에는
        // 이미 만들어둔 결과물을 재사용한다. 실제 생성 시에는 이 필드를 비워야 한다.
        if (!string.IsNullOrEmpty(_debugModelUrl))
        {
            Debug.Log($"[Generate3DController] 테스트 모드 — 서버 요청 없이 불러옵니다: {_debugModelUrl}");
            SetStatus("저장된 3D를 불러오는 중...");
            _isGenerating = true;
            SetButtonInteractable(false);
            StartCoroutine(LoadAndShow(_debugModelUrl));
            return;
        }

        if (_syncClient == null ||
            string.IsNullOrEmpty(_syncClient.Host) ||
            string.IsNullOrEmpty(_syncClient.RoomId) ||
            string.IsNullOrEmpty(_syncClient.UserId))
        {
            Debug.LogWarning("[Generate3DController] 실패: host/room_id/user_id 가 비어 있습니다.");
            SetStatus("백엔드 연결 정보를 확인해 주세요.");
            return;
        }

        string sourceAssetId = _generate2DController != null
            ? _generate2DController.CurrentAssetId
            : null;
        if (string.IsNullOrEmpty(sourceAssetId))
        {
            // 서버도 거부하지만, 여기서 막아야 불필요한 왕복이 없다.
            Debug.LogWarning("[Generate3DController] 실패: 원본 2D asset_id 가 없습니다. 2D 를 먼저 생성해야 합니다.");
            SetStatus("먼저 2D 그림을 만들어 주세요.");
            return;
        }

        // 캐시 적중이면 API 를 부르지 않는다 — 여기서 걸리는 게 비용 방어의 핵심이다.
        if (_modelUrlBySourceAsset.TryGetValue(sourceAssetId, out string cachedUrl) &&
            !string.IsNullOrEmpty(cachedUrl))
        {
            Debug.Log($"[Generate3DController] 캐시 적중 — 서버 요청 없이 재사용합니다. asset_id={sourceAssetId}");
            SetStatus("이전에 만든 3D를 다시 불러오는 중...");
            _isGenerating = true;
            SetButtonInteractable(false);
            StartCoroutine(LoadAndShow(cachedUrl));
            return;
        }

        string jobId = System.Guid.NewGuid().ToString();
        var req = new Generate3DRequest
        {
            room_id  = _syncClient.RoomId,
            user_id  = _syncClient.UserId,
            job_id   = jobId,
            asset_id = sourceAssetId,
        };

        _isGenerating = true;
        _pendingJobId = jobId;
        _pendingSourceAssetId = sourceAssetId;
        SetButtonInteractable(false);
        SetStatus("3D 모델을 만들고 있어요. 시간이 걸릴 수 있어요...");
        RestartGenerationTimeout();

        StartCoroutine(PostJson("3d/generate", JsonUtility.ToJson(req)));
    }

    private IEnumerator PostJson(string path, string body)
    {
        string url = $"http://{_syncClient.Host}/api/{path}";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.timeout = 20;
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[Generate3DController] POST {url} body={body}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate3DController] {path} 실패: {req.error} (code={req.responseCode}) body={req.downloadHandler?.text}");
                FinishGeneration("3D 만들기 요청에 실패했습니다.");
                yield break;
            }
            Debug.Log($"[Generate3DController] {path} 접수됨(code={req.responseCode}). 결과는 WS 3D_GENERATED 대기.");
        }
    }

    // ─────────────────────────────────────────────
    // 결과 수신
    // ─────────────────────────────────────────────

    private void HandleModelGenerated(GraphSyncClient.Model3DResult result)
    {
        if (string.IsNullOrEmpty(result.ModelUrl))
        {
            Debug.LogWarning("[Generate3DController] 3D_GENERATED model_url 이 비어 있습니다.");
            FinishGeneration("3D 결과 주소가 비어 있습니다.");
            return;
        }

        // 2D 와 같은 규칙 — 내가 요청한 건에 대한 결과만 반영한다.
        if (string.IsNullOrEmpty(_pendingJobId))
        {
            Debug.Log($"[Generate3DController] 3D_GENERATED 무시(대기 중인 요청 없음): job_id={result.JobId}");
            return;
        }
        if (!string.IsNullOrEmpty(result.JobId) &&
            !string.Equals(_pendingJobId, result.JobId, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Generate3DController] 3D_GENERATED 무시(job_id 불일치): 수신={result.JobId} 대기중={_pendingJobId}");
            return;
        }

        _pendingJobId = null;
        StopGenerationTimeout();

        // 원본 2D → GLB 매핑을 남겨 다음 요청 때 API 를 건너뛴다.
        if (!string.IsNullOrEmpty(_pendingSourceAssetId))
            _modelUrlBySourceAsset[_pendingSourceAssetId] = result.ModelUrl;

        SetStatus("3D 모델을 불러오는 중...");
        StartCoroutine(LoadAndShow(result.ModelUrl));
    }

    // GLB 다운로드 → glTFast 로 파싱 → 씬에 붙인다.
    private IEnumerator LoadAndShow(string modelUrl)
    {
        string url = modelUrl.StartsWith("http")
            ? modelUrl
            : $"http://{_syncClient?.Host}{(modelUrl.StartsWith("/") ? "" : "/")}{modelUrl}";

        byte[] data = null;
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 120;
            Debug.Log($"[Generate3DController] GLB 다운로드 → {url}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Generate3DController] GLB 다운로드 실패: {req.error} (code={req.responseCode})");
                FinishGeneration("3D 모델을 불러오지 못했습니다.");
                yield break;
            }
            data = req.downloadHandler.data;
        }

        if (data == null || data.Length == 0)
        {
            FinishGeneration("3D 모델 데이터가 비어 있습니다.");
            yield break;
        }

        // glTFast 는 Task 기반이라 코루틴에서 완료를 폴링한다(async void 를 만들지 않기 위해).
        var gltf = new GltfImport();
        Task<bool> loadTask = gltf.Load(data);
        while (!loadTask.IsCompleted)
            yield return null;

        if (loadTask.IsFaulted || !loadTask.Result)
        {
            Debug.LogError($"[Generate3DController] GLB 파싱 실패: {loadTask.Exception?.GetBaseException().Message}");
            gltf.Dispose();
            FinishGeneration("3D 모델을 읽지 못했습니다.");
            yield break;
        }

        var modelRoot = new GameObject("Generated3DModel");
        modelRoot.transform.SetParent(_modelParent != null ? _modelParent : transform, false);

        Task<bool> instantiateTask =
            gltf.InstantiateMainSceneAsync(modelRoot.transform);
        while (!instantiateTask.IsCompleted)
            yield return null;

        if (instantiateTask.IsFaulted || !instantiateTask.Result)
        {
            Debug.LogError($"[Generate3DController] GLB 배치 실패: {instantiateTask.Exception?.GetBaseException().Message}");
            Destroy(modelRoot);
            gltf.Dispose();
            FinishGeneration("3D 모델을 배치하지 못했습니다.");
            yield break;
        }

        // 크기 정규화 + 배치. Meshy 가 내려주는 GLB 의 단위·크기는 보장되지 않아
        // 그대로 두면 머리 위에 거대하게 뜨거나 발밑에 먼지처럼 박힌다.
        FitAndPlace(modelRoot);

        // 새 모델이 준비된 뒤에야 이전 것을 치운다. 순서를 바꾸면 실패 시 화면이 비어버린다.
        DisposeCurrentModel();
        _currentModel = modelRoot;
        _currentGltf  = gltf;

        Debug.Log("[Generate3DController] 3D 모델 표시 완료.");
        FinishGeneration("3D 모델이 만들어졌어요.");
    }

    // ─────────────────────────────────────────────
    // 상태 / 타임아웃
    // ─────────────────────────────────────────────

    // ─────────────────────────────────────────────
    // 크기 정규화 / 배치
    // ─────────────────────────────────────────────

    // Meshy GLB 는 스케일이 제각각이라(단위·원점 보장 없음) 그대로 붙이면 위치·크기가 어긋난다.
    //   1) 렌더러 바운즈의 최장변을 _targetSize 로 맞춘다
    //   2) 모델 중심을 기준점에 오도록 이동한다(원점이 모델 밖에 있는 경우 대비)
    // _modelParent 가 지정돼 있으면 그 자리를, 없으면 카메라 앞을 기준점으로 쓴다.
    private void FitAndPlace(GameObject root)
    {
        if (root == null) return;

        // --- 기준점 결정 ---
        Vector3 anchorPos;
        Quaternion anchorRot;
        if (_modelParent != null)
        {
            anchorPos = _modelParent.position;
            anchorRot = _modelParent.rotation;
        }
        else
        {
            // 카메라 앞 바닥 쪽. 머리 위에 뜨지 않도록 아래로 내린다.
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[Generate3DController] Camera.main 이 없어 기본 위치에 배치합니다.");
                anchorPos = transform.position;
                anchorRot = transform.rotation;
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    cam.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;

                anchorPos = cam.transform.position
                            + forward * _placementDistance
                            + Vector3.up * _placementHeightOffset;
                anchorRot = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        // GLB 자체의 정면 축이 배치 기준과 다를 수 있다(Meshy 출력은 대개 반대를 본다).
        // 기준 회전에 Y축 보정을 더해 사용자를 향하게 맞춘다.
        root.transform.SetPositionAndRotation(
            anchorPos,
            anchorRot * Quaternion.Euler(0f, _modelYawOffset, 0f));

        // --- 크기 정규화 ---
        if (!TryGetRendererBounds(root, out Bounds bounds))
        {
            Debug.LogWarning("[Generate3DController] 렌더러를 찾지 못해 크기 정규화를 건너뜁니다.");
            return;
        }

        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest > 0.0001f && _targetSize > 0f)
        {
            float k = _targetSize / longest;
            root.transform.localScale *= k;
        }

        // --- 정렬 (스케일 후 바운즈가 바뀌므로 다시 계산) ---
        // x/z 는 기준점 중앙, y 는 모델 바닥이 기준점에 닿도록 올린다.
        // 중심을 기준점에 맞추면 모델 절반이 원판 아래로 파묻힌다.
        if (TryGetRendererBounds(root, out bounds))
        {
            Vector3 delta = anchorPos - bounds.center;
            delta.y = anchorPos.y - bounds.min.y;
            root.transform.position += delta;
        }

        Debug.Log($"[Generate3DController] 배치 완료 — scale={root.transform.localScale.x:F3} " +
                  $"size={bounds.size} pos={root.transform.position} " +
                  $"parent={(_modelParent != null ? _modelParent.name : "(카메라 기준)")}");
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // 표시 중인 모델과 그 리소스를 함께 해제한다.
    // GltfImport.Dispose() 가 텍스처·머티리얼을 파괴하므로 GameObject 파기와 짝을 맞춰야 한다.
    private void DisposeCurrentModel()
    {
        if (_currentModel != null)
        {
            Destroy(_currentModel);
            _currentModel = null;
        }
        if (_currentGltf != null)
        {
            _currentGltf.Dispose();
            _currentGltf = null;
        }
    }

    private void OnDestroy()
    {
        DisposeCurrentModel();
    }

    private void FinishGeneration(string status)
    {
        StopGenerationTimeout();
        _isGenerating = false;
        _pendingJobId = null;
        SetButtonInteractable(true);
        SetStatus(status);
    }

    private void RestartGenerationTimeout()
    {
        StopGenerationTimeout();
        _timeoutCoroutine = StartCoroutine(GenerationTimeout());
    }

    private void StopGenerationTimeout()
    {
        if (_timeoutCoroutine != null)
        {
            StopCoroutine(_timeoutCoroutine);
            _timeoutCoroutine = null;
        }
    }

    private IEnumerator GenerationTimeout()
    {
        yield return new WaitForSecondsRealtime(_generationTimeoutSeconds);
        _timeoutCoroutine = null;
        if (_isGenerating)
        {
            Debug.LogWarning("[Generate3DController] 3D 생성 대기 시간이 초과됐습니다.");
            FinishGeneration("3D 만들기가 오래 걸려요. 잠시 후 다시 시도해 주세요.");
        }
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
}
