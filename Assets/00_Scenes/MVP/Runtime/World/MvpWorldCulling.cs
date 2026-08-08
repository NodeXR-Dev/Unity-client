using UnityEngine;

/// <summary>
/// XRMeetingWorld 의 자연물에 거리 컬링을 건다.
///
/// 월드는 420x420m 에 렌더러가 1,678개 있는데 Quest 에서 전부 그리면 드로우콜이 감당이 안 된다.
/// 고유 메시가 20개뿐이라 크기별로 컬링 거리를 나누면 대부분을 싸게 잘라낼 수 있다.
///   - WorldGrass  (풀 1,320개): 가까이서만 보이면 되므로 짧게
///   - WorldNature (나무·바위·지형): 실루엣이 남아야 하므로 길게
///
/// Camera.layerCullDistances 는 컬링 단계에서 잘라내므로 드로우콜 자체가 사라진다.
/// 잘리는 경계는 포그로 가린다.
/// </summary>
[DefaultExecutionOrder(-600)]
public class MvpWorldCulling : MonoBehaviour
{
    [Header("컬링 거리")]
    [SerializeField] private float _grassCullDistance = 55f;
    [SerializeField] private float _natureCullDistance = 220f;

    [Header("카메라 far plane (0 이면 그대로 둠)")]
    [SerializeField] private float _farClipPlane = 400f;

    [Header("경계를 가리는 포그")]
    [SerializeField] private bool _applyFog = true;
    [SerializeField] private float _fogStart = 90f;
    [SerializeField] private float _fogEnd = 260f;
    [SerializeField] private Color _fogColor = new Color(0.66f, 0.74f, 0.84f, 1f);

    private const string GrassLayerName = "WorldGrass";
    private const string NatureLayerName = "WorldNature";

    private Camera _applied;
    private float _nextScan;

    private void OnEnable()
    {
        _applied = null;
        _nextScan = 0f;

        if (_applyFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
            RenderSettings.fogColor = _fogColor;
        }
    }

    private void Update()
    {
        // 카메라 리그는 XR 부트스트랩 이후에 살아나므로 잡힐 때까지 주기적으로 재시도한다.
        if (_applied != null && _applied.isActiveAndEnabled)
            return;

        if (Time.unscaledTime < _nextScan)
            return;
        _nextScan = Time.unscaledTime + 0.5f;

        Camera camera = ResolveCamera();
        if (camera == null)
            return;

        Apply(camera);
        _applied = camera;
    }

    private static Camera ResolveCamera()
    {
        Camera camera = Camera.main;
        if (camera != null && camera.isActiveAndEnabled)
            return camera;

        Camera[] all = FindObjectsByType<Camera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (Camera candidate in all)
        {
            if (candidate != null &&
                candidate.isActiveAndEnabled &&
                candidate.targetTexture == null)
                return candidate;
        }
        return null;
    }

    private void Apply(Camera camera)
    {
        int grassLayer = LayerMask.NameToLayer(GrassLayerName);
        int natureLayer = LayerMask.NameToLayer(NatureLayerName);
        if (grassLayer < 0 && natureLayer < 0)
        {
            Debug.LogWarning(
                "[MVP World] WorldGrass / WorldNature 레이어가 없어 " +
                "거리 컬링을 건너뜁니다.");
            return;
        }

        if (_farClipPlane > 0f)
            camera.farClipPlane = _farClipPlane;

        // 0 은 '카메라 far plane 을 그대로 쓴다'는 뜻이므로 자연물 레이어만 값을 채운다.
        float[] distances = new float[32];
        if (grassLayer >= 0)
            distances[grassLayer] = Mathf.Max(1f, _grassCullDistance);
        if (natureLayer >= 0)
            distances[natureLayer] = Mathf.Max(1f, _natureCullDistance);

        // layerCullSpherical 은 빌트인 렌더러 전용이라 URP 에서는 건드리지 않는다.
        camera.layerCullDistances = distances;

        Debug.Log(
            "[MVP World] 거리 컬링 적용 - 풀 " + _grassCullDistance +
            "m / 나무·바위 " + _natureCullDistance +
            "m (카메라: " + camera.name + ").");
    }
}
