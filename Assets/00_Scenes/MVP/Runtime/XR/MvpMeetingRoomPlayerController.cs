using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// MVP 회의실에서 로컬 XR 사용자를 의자에 배치하고
/// 왼쪽 스틱 이동·오른쪽 스틱 스냅 회전을 보장한다.
/// 네트워크 아바타 생성 코드는 건드리지 않고 CameraRig만 이동한다.
/// </summary>
[DefaultExecutionOrder(-750)]
public class MvpMeetingRoomPlayerController : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] private float _moveSpeed = 1.35f;
    [SerializeField] private float _inputDeadZone = 0.18f;
    [SerializeField] private float _snapTurnThreshold = 0.72f;
    [SerializeField] private float _snapTurnAngle = 30f;

    [Header("회의실 자리")]
    [SerializeField] private bool _respawnAtSeatOnStart = true;
    [SerializeField] private float _seatedEyeHeight = 0.62f;
    [SerializeField] private int _seatStabilizeFrames = 12;

    private OVRCameraRig _cameraRig;
    private Camera _headCamera;
    private Coroutine _respawnRoutine;
    private bool _turnReady = true;
    private bool _reportedMoveInput;


    private IEnumerator Start()
    {
        for (int frame = 0; frame < 180; frame++)
        {
            ResolveRig();
            if (_cameraRig != null &&
                _cameraRig.gameObject.activeInHierarchy &&
                ResolveHeadCamera() != null)
            {
                yield return null;
                if (_respawnAtSeatOnStart)
                    RespawnAtAssignedSeat();
                yield break;
            }
            yield return null;
        }
    }

    private void Update()
    {
        ResolveRig();
        if (_cameraRig == null ||
            !_cameraRig.gameObject.activeInHierarchy ||
            ResolveHeadCamera() == null)
            return;

        Vector2 moveAxis = ReadAxis(XRNode.LeftHand, true);
        Vector2 turnAxis = ReadAxis(XRNode.RightHand, false);

        if (moveAxis.sqrMagnitude >=
            _inputDeadZone * _inputDeadZone)
        {
            MoveRig(Vector2.ClampMagnitude(moveAxis, 1f));
            if (!_reportedMoveInput)
            {
                _reportedMoveInput = true;
                Debug.Log(
                    "[MVP XR] 왼쪽 조이스틱 이동 입력을 감지했습니다.");
            }
        }

        if (_turnReady &&
            Mathf.Abs(turnAxis.x) >= _snapTurnThreshold)
        {
            SnapTurn(Mathf.Sign(turnAxis.x) * _snapTurnAngle);
            _turnReady = false;
        }
        else if (Mathf.Abs(turnAxis.x) <= _inputDeadZone)
        {
            _turnReady = true;
        }
    }

    [ContextMenu("Respawn At Assigned Meeting Seat")]
    public void RespawnAtAssignedSeat()
    {
        if (!Application.isPlaying)
        {
            PlaceAtAssignedSeat(out _);
            return;
        }

        if (_respawnRoutine != null)
            StopCoroutine(_respawnRoutine);
        _respawnRoutine =
            StartCoroutine(StabilizeAssignedSeat());
    }

    private IEnumerator StabilizeAssignedSeat()
    {
        int seatNumber = 0;
        int frames = Mathf.Max(2, _seatStabilizeFrames);
        for (int frame = 0; frame < frames; frame++)
        {
            yield return new WaitForEndOfFrame();
            PlaceAtAssignedSeat(out seatNumber);
        }

        MvpXrCanvasAnchor[] anchors =
            FindObjectsByType<MvpXrCanvasAnchor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (MvpXrCanvasAnchor anchor in anchors)
            anchor.Recenter();

        MvpTableSettingsDock dock =
            FindFirstObjectByType<MvpTableSettingsDock>();
        dock?.RefreshRoomPlacement();

        _respawnRoutine = null;
        if (seatNumber > 0)
        {
            Debug.Log(
                "[MVP XR] 회의실 의자 " + seatNumber +
                "번에 로컬 사용자를 안정적으로 배치했습니다.");
        }
    }

    private bool PlaceAtAssignedSeat(out int seatNumber)
    {
        seatNumber = 0;
        ResolveRig();
        Camera head = ResolveHeadCamera();
        if (_cameraRig == null || head == null)
            return false;

        if (!TryResolveRoom(
                out Transform table,
                out Bounds tableBounds,
                out List<Transform> chairs))
            return false;

        int seatIndex = ResolveParticipantIndex() % chairs.Count;
        if (seatIndex < 0)
            seatIndex += chairs.Count;
        seatNumber = seatIndex + 1;

        Transform chair = chairs[seatIndex];
        TryGetRendererBounds(chair, out Bounds chairBounds);

        Vector3 tableCenter = tableBounds.center;
        Vector3 chairCenter = chairBounds.size.sqrMagnitude > 0f
            ? chairBounds.center
            : chair.position;
        Vector3 inward = Vector3.ProjectOnPlane(
            tableCenter - chairCenter,
            Vector3.up);
        if (inward.sqrMagnitude < 0.001f)
            inward = Vector3.forward;
        inward.Normalize();

        Vector3 targetEye = chairCenter + inward * 0.08f;
        targetEye.y =
            (chairBounds.size.sqrMagnitude > 0f
                ? chairBounds.max.y
                : chair.position.y + 0.5f) +
            _seatedEyeHeight;

        Vector3 currentForward = Vector3.ProjectOnPlane(
            head.transform.forward,
            Vector3.up);
        if (currentForward.sqrMagnitude < 0.001f)
            currentForward = _cameraRig.transform.forward;
        currentForward.Normalize();

        float yaw = Vector3.SignedAngle(
            currentForward,
            inward,
            Vector3.up);
        _cameraRig.transform.RotateAround(
            head.transform.position,
            Vector3.up,
            yaw);
        _cameraRig.transform.position +=
            targetEye - head.transform.position;
        return true;
    }

    private void MoveRig(Vector2 axis)
    {
        Transform head = _headCamera.transform;
        Vector3 forward = Vector3.ProjectOnPlane(
            head.forward,
            Vector3.up);
        Vector3 right = Vector3.ProjectOnPlane(
            head.right,
            Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = _cameraRig.transform.forward;
        if (right.sqrMagnitude < 0.001f)
            right = _cameraRig.transform.right;

        Vector3 delta =
            (forward.normalized * axis.y +
             right.normalized * axis.x) *
            (_moveSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        _cameraRig.transform.position += delta;
    }

    private void SnapTurn(float degrees)
    {
        _cameraRig.transform.RotateAround(
            _headCamera.transform.position,
            Vector3.up,
            degrees);
    }

    private Vector2 ReadAxis(XRNode node, bool left)
    {
        InputDevice device =
            InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid &&
            device.TryGetFeatureValue(
                CommonUsages.primary2DAxis,
                out Vector2 axis) &&
            axis.sqrMagnitude > 0.0001f)
            return axis;

        OVRInput.Axis2D ovrAxis = left
            ? OVRInput.Axis2D.PrimaryThumbstick
            : OVRInput.Axis2D.SecondaryThumbstick;
        OVRInput.Controller controller = left
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;
        return OVRInput.Get(ovrAxis, controller);
    }

    private void ResolveRig()
    {
        if (_cameraRig != null)
            return;

        OVRCameraRig[] rigs =
            Resources.FindObjectsOfTypeAll<OVRCameraRig>();
        foreach (OVRCameraRig candidate in rigs)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid())
            {
                _cameraRig = candidate;
                break;
            }
        }
    }

    private Camera ResolveHeadCamera()
    {
        if (_headCamera != null &&
            _headCamera.isActiveAndEnabled)
            return _headCamera;

        if (_cameraRig != null &&
            _cameraRig.centerEyeAnchor != null)
            _headCamera =
                _cameraRig.centerEyeAnchor.GetComponent<Camera>();

        return _headCamera;
    }

    private bool TryResolveRoom(
        out Transform table,
        out Bounds tableBounds,
        out List<Transform> chairs)
    {
        table = null;
        tableBounds = default;
        chairs = new List<Transform>();

        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                candidate.gameObject.scene != gameObject.scene ||
                !candidate.gameObject.activeInHierarchy)
                continue;

            if (candidate.name == "Table_01")
                table = candidate;
            else if (candidate.name.StartsWith(
                         "Chair_02",
                         StringComparison.OrdinalIgnoreCase))
                chairs.Add(candidate);
        }

        if (table == null ||
            chairs.Count == 0 ||
            !TryGetRendererBounds(table, out tableBounds))
            return false;

        Vector3 center = tableBounds.center;
        chairs.Sort((left, right) =>
        {
            float leftAngle = Mathf.Atan2(
                left.position.z - center.z,
                left.position.x - center.x);
            float rightAngle = Mathf.Atan2(
                right.position.z - center.z,
                right.position.x - center.x);
            return leftAngle.CompareTo(rightAngle);
        });
        return true;
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

    private static int ResolveParticipantIndex()
    {
        MonoBehaviour[] behaviours =
            UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null ||
                behaviour.GetType().FullName != "Fusion.NetworkRunner")
                continue;

            try
            {
                PropertyInfo localPlayerProperty =
                    behaviour.GetType().GetProperty("LocalPlayer");
                object playerRef =
                    localPlayerProperty?.GetValue(behaviour);
                if (playerRef == null)
                    continue;

                PropertyInfo rawProperty =
                    playerRef.GetType().GetProperty("RawEncoded") ??
                    playerRef.GetType().GetProperty("PlayerId");
                if (rawProperty != null)
                    return Convert.ToInt32(
                        rawProperty.GetValue(playerRef));
            }
            catch
            {
                // 네트워크 세션이 아직 준비되지 않았으면 첫 번째 자리를 사용한다.
            }
        }

        return 0;
    }
}
