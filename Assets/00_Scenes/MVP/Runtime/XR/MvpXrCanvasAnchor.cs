using System;
using UnityEngine;

/// <summary>
/// 안내·추천 패널을 각 사용자의 회의실 자리 앞에 한 번 배치한다.
/// 머리를 움직여도 따라오지 않으며, 중앙 설계판과 그래프 노드는
/// 기존처럼 회의실 공용 공간에 고정된다.
/// </summary>
public class MvpXrCanvasAnchor : MonoBehaviour
{
    [SerializeField] private float _distance = 1.1f;
    [SerializeField] private float _verticalOffset;
    [SerializeField] private float _worldScale = 0.001f;
    [SerializeField] private int _settleFrames = 4;

    // 눈 위치가 이만큼 안 움직인 폴링이 연속으로 나와야 "트래킹이 붙었다"로 본다.
    private const float SettleTolerance = 0.04f;

    // 고정한 뒤라도 이 시간 안에 눈이 크게 움직이면 다시 붙인다.
    // 트래킹이 늦게 잡히거나 회의실 자리 배치가 리그를 옮기는 경우를 흡수한다.
    // 트래킹이 늦게 붙을 때 눈이 뛰는 폭은 1.5m 안팎이라 몸을 기울이는 정도와
    // 뚜렷이 구분된다. 문턱이 낮으면 살짝 움직였을 뿐인데 패널이 끌려온다.
    private const float ReanchorWindowSeconds = 6f;
    private const float ReanchorThreshold = 0.5f;

    private Camera _camera;
    private bool _anchored;
    private int _readyFrame;
    private float _nextResolveTime;

    private bool _hasSample;
    private Vector3 _lastSample;
    private Vector3 _anchoredEye;
    private float _reanchorDeadline;


    public void Configure(
        float distance,
        float verticalOffset,
        float worldScale)
    {
        _distance = Mathf.Clamp(distance, 0.65f, 1.5f);
        _verticalOffset = verticalOffset;
        _worldScale = Mathf.Max(0.0001f, worldScale);
        transform.localScale = Vector3.one * _worldScale;
        RequestRoomPlacement();
    }

    public void Recenter()
    {
        RequestRoomPlacement();
        TryPlaceInMeetingRoom();
    }

    private void OnEnable()
    {
        RequestRoomPlacement();
    }

    private void LateUpdate()
    {
        if (Time.frameCount < _readyFrame ||
            Time.unscaledTime < _nextResolveTime)
            return;

        _nextResolveTime = Time.unscaledTime + 0.25f;

        Camera view = ResolveViewCamera();
        if (view == null)
            return;
        Vector3 eye = view.transform.position;

        if (_anchored)
        {
            // 판을 붙인 뒤 눈높이가 내려앉는 일이 있었다.
            // 헤드 트래킹은 씬이 뜨고 한참 뒤에 붙고, 회의실 자리 배치도
            // 리그를 통째로 옮긴다. 그 전에 판을 고정해 버리면 사용자만
            // 아래로 내려가고 판은 위에 남는다.
            //
            // 그래서 들어온 직후 잠깐은 큰 이동을 따라가고, 그 시간이 지나면
            // 다시는 따라가지 않는다(고개를 돌려도 안 따라오는 성질은 유지).
            if (Time.unscaledTime < _reanchorDeadline &&
                (eye - _anchoredEye).sqrMagnitude >
                    ReanchorThreshold * ReanchorThreshold)
                TryPlaceInMeetingRoom();
            return;
        }

        // 트래킹이 붙기 전 눈 위치는 원점 근처에서 튄다. 그 값으로 판을 놓으면
        // 엉뚱한 높이에 박히므로, 두 번 연속 같은 자리일 때만 고정한다.
        if (!_hasSample ||
            (eye - _lastSample).sqrMagnitude >
                SettleTolerance * SettleTolerance)
        {
            _hasSample = true;
            _lastSample = eye;
            return;
        }

        TryPlaceInMeetingRoom();
    }

    private void RequestRoomPlacement()
    {
        _anchored = false;
        _hasSample = false;
        _readyFrame = Time.frameCount + Mathf.Max(1, _settleFrames);
        _nextResolveTime = 0f;
        _reanchorDeadline = Time.unscaledTime + ReanchorWindowSeconds;
    }

    private bool TryPlaceInMeetingRoom()
    {
        _camera = ResolveViewCamera();
        if (_camera == null)
            return false;

        if (TryResolveTable(out Bounds tableBounds))
        {
            Vector3 towardTable = Vector3.ProjectOnPlane(
                tableBounds.center - _camera.transform.position,
                Vector3.up);
            if (towardTable.sqrMagnitude < 0.001f)
            {
                Transform chair = FindNearestChair(
                    _camera.transform.position);
                if (chair != null)
                {
                    towardTable = Vector3.ProjectOnPlane(
                        tableBounds.center - chair.position,
                        Vector3.up);
                }
            }

            if (towardTable.sqrMagnitude < 0.001f)
                towardTable = _camera.transform.forward;
            towardTable.Normalize();

            Vector3 targetPosition =
                _camera.transform.position +
                towardTable * _distance +
                Vector3.up * _verticalOffset;
            targetPosition.y = Mathf.Max(
                targetPosition.y,
                tableBounds.max.y + 0.46f);

            transform.SetPositionAndRotation(
                targetPosition,
                Quaternion.LookRotation(towardTable, Vector3.up));
            transform.localScale = Vector3.one * _worldScale;
            _anchored = true;
            _anchoredEye = _camera.transform.position;

            Debug.Log(
                "[MVP XR] 안내 UI를 헤드가 아닌 회의실 자리 앞 공간에 고정했습니다.");
            return true;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            _camera.transform.forward,
            Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = _camera.transform.forward;
        forward.Normalize();

        transform.SetPositionAndRotation(
            _camera.transform.position +
            forward * _distance +
            Vector3.up * _verticalOffset,
            Quaternion.LookRotation(forward, Vector3.up));
        transform.localScale = Vector3.one * _worldScale;
        _anchored = true;
        _anchoredEye = _camera.transform.position;
        return true;
    }

    private bool TryResolveTable(out Bounds tableBounds)
    {
        tableBounds = default;
        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                candidate.gameObject.scene != gameObject.scene ||
                candidate.name != "Table_01")
                continue;

            return TryGetRendererBounds(candidate, out tableBounds);
        }
        return false;
    }

    private Transform FindNearestChair(Vector3 position)
    {
        Transform best = null;
        float bestDistance = float.PositiveInfinity;
        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();

        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                candidate.gameObject.scene != gameObject.scene ||
                !candidate.name.StartsWith(
                    "Chair_02",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            Vector3 delta = Vector3.ProjectOnPlane(
                candidate.position - position,
                Vector3.up);
            float distance = delta.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    private static bool TryGetRendererBounds(
        Transform root,
        out Bounds bounds)
    {
        bounds = new Bounds(root.position, Vector3.zero);
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private static Camera ResolveViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        Camera[] cameras =
            Resources.FindObjectsOfTypeAll<Camera>();
        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.isActiveAndEnabled &&
                candidate.CompareTag("MainCamera"))
                return candidate;
        }
        return null;
    }
}

