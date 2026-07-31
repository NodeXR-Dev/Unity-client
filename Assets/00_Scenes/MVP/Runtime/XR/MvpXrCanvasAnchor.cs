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

    private Camera _camera;
    private bool _anchored;
    private int _readyFrame;
    private float _nextResolveTime;


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
        if (_anchored ||
            Time.frameCount < _readyFrame ||
            Time.unscaledTime < _nextResolveTime)
            return;

        _nextResolveTime = Time.unscaledTime + 0.25f;
        TryPlaceInMeetingRoom();
    }

    private void RequestRoomPlacement()
    {
        _anchored = false;
        _readyFrame = Time.frameCount + Mathf.Max(1, _settleFrames);
        _nextResolveTime = 0f;
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

