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

    [Header("의자가 없을 때 (열린 공간) 서는 자리")]
    [Tooltip("서는 자리의 중심. 파빌리온처럼 가구 없는 공간에서 쓴다.")]
    [SerializeField] private Vector3 _openFloorCenter = new Vector3(0f, -2.1f, 0f);
    [Tooltip("바라보는 방향(수평). 참가자 전원이 같은 쪽을 본다.")]
    [SerializeField] private Vector3 _openFloorFacing = Vector3.forward;
    [SerializeField] private float _openFloorSpacing = 1.2f;
    [SerializeField] private int _openFloorSlots = 4;
    [SerializeField] private float _standingEyeHeight = 1.55f;

    /// <summary>
    /// 회의실 바닥 높이(월드 Y). 열린 공간 배치의 기준점이다.
    /// 3D 결과물을 바닥 기준으로 놓을 때 쓴다.
    /// </summary>
    public float FloorY => _openFloorCenter.y;

    /// <summary>선 자세의 눈높이. 바닥을 역산할 때 쓴다.</summary>
    public float StandingEyeHeight => _standingEyeHeight;

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

        // 헤드 트래킹은 씬이 뜨고 한참 뒤에야 붙는다. 정해진 프레임 수만 돌고
        // 끝내면 머리 위치가 아직 0 인 상태로 자리를 잡고 마무리되고, 트래킹이
        // 붙는 순간 눈높이가 통째로 어긋난다(사용자만 아래로 뚝 떨어진 것처럼
        // 보이고, 그 사이에 붙은 작업판만 위에 남는다).
        //
        // 그래서 프레임을 세지 않고, 리그 안에서의 머리 위치(트래킹이 직접
        // 움직이는 값)가 실제로 멎을 때까지 배치를 반복한다.
        const float SettleTolerance = 0.015f;
        const int RequiredStableTicks = 6;
        const float MaxWaitSeconds = 5f;

        int minFrames = Mathf.Max(2, _seatStabilizeFrames);
        float deadline = Time.unscaledTime + MaxWaitSeconds;
        bool hasSample = false;
        Vector3 lastLocalEye = Vector3.zero;
        int stableTicks = 0;
        int frame = 0;

        while (true)
        {
            yield return new WaitForEndOfFrame();
            PlaceAtAssignedSeat(out seatNumber);
            frame++;

            Camera head = ResolveHeadCamera();
            Vector3 localEye = head != null
                ? head.transform.localPosition
                : Vector3.zero;

            if (hasSample &&
                (localEye - lastLocalEye).sqrMagnitude <=
                    SettleTolerance * SettleTolerance)
                stableTicks++;
            else
                stableTicks = 0;

            hasSample = true;
            lastLocalEye = localEye;

            if (frame >= minFrames && stableTicks >= RequiredStableTicks)
                break;
            if (Time.unscaledTime >= deadline)
                break;
        }

        MvpXrCanvasAnchor[] anchors =
            FindObjectsByType<MvpXrCanvasAnchor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (MvpXrCanvasAnchor anchor in anchors)
            anchor.Recenter();

        // 설계 보드는 이 컨트롤러가 자리를 잡기 전에 Start 에서 이미 놓인다.
        // 그대로 두면 "떨어지기 전" 눈높이에 박혀 보드만 위에 남으므로,
        // 자리가 확정된 지금 다시 놓는다.
        MvpWorkspaceLayout workspace =
            FindFirstObjectByType<MvpWorkspaceLayout>();
        workspace?.RecenterWorkspacePose();

        MvpTableSettingsDock dock =
            FindFirstObjectByType<MvpTableSettingsDock>();
        dock?.RefreshRoomPlacement();

        _respawnRoutine = null;
        if (seatNumber > 0)
        {
            Debug.Log(
                "[MVP XR] 회의실 " + seatNumber +
                "번 자리에 로컬 사용자를 안정적으로 배치했습니다.");
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
            return PlaceAtOpenFloorSpot(out seatNumber);

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

    /// <summary>
    /// 책상·의자가 없는 열린 공간(파빌리온)용 배치.
    /// 참가자를 좌우로 나란히 세우고 전원이 같은 방향을 보게 한다.
    /// 서로 마주 보게 하면 각자 앞에 뜨는 작업판이 상대 시야를 가린다.
    /// </summary>
    private bool PlaceAtOpenFloorSpot(out int seatNumber)
    {
        seatNumber = 0;
        Camera head = ResolveHeadCamera();
        if (_cameraRig == null || head == null)
            return false;

        int slots = Mathf.Max(1, _openFloorSlots);
        int index = ResolveParticipantIndex() % slots;
        if (index < 0)
            index += slots;
        seatNumber = index + 1;

        Vector3 facing = Vector3.ProjectOnPlane(
            _openFloorFacing,
            Vector3.up);
        if (facing.sqrMagnitude < 0.001f)
            facing = Vector3.forward;
        facing.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;

        float offset =
            (index - (slots - 1) * 0.5f) * _openFloorSpacing;
        // 머리가 눈높이에 오도록 맞춘다.
        //
        // 한때 리그 루트를 바닥에 직접 놓아 봤다(아바타가 리그 루트를 따라가 어긋난다고
        // 봤기 때문). 그런데 그러면 에디터 보정을 위한
        //     head.y - rig.y > 0.2f
        // 판정이 프레임마다 뒤집힌다. 트래킹이 붙기 전에는 머리 높이가 0 이라
        // 1.55m 올려 놓고, 붙는 순간 바닥으로 내려놓는다. 그 사이에 작업판이 배치되면
        // 판만 위에 남고 사용자는 아래로 뚝 떨어진 것처럼 보인다.
        //
        // 카메라와 아바타의 어긋남은 XRPlayerBinder 가 아바타의 눈(HMD 바이저)을
        // CenterEyeAnchor 에 맞추는 것으로 이미 해결했다. 여기서는 흔들리지 않는
        // 머리 기준 배치로 되돌린다.
        Vector3 targetEye =
            _openFloorCenter +
            right * offset +
            Vector3.up * _standingEyeHeight;

        Vector3 currentForward = Vector3.ProjectOnPlane(
            head.transform.forward,
            Vector3.up);
        if (currentForward.sqrMagnitude < 0.001f)
            currentForward = _cameraRig.transform.forward;
        currentForward.Normalize();

        float yaw = Vector3.SignedAngle(
            currentForward,
            facing,
            Vector3.up);
        _cameraRig.transform.RotateAround(
            head.transform.position,
            Vector3.up,
            yaw);

        // 트래킹이 아직 안 붙은 프레임에는 머리 위치가 튀어 보정이 누적 발산할 수
        // 있다(로비에서 y=-743 까지 내려간 적이 있다). 비정상적으로 큰 보정이면
        // 오프셋을 믿지 않고 리그를 목표에 직접 둔다.
        Vector3 delta = targetEye - head.transform.position;
        const float MaxCorrection = 50f;
        if (delta.sqrMagnitude > MaxCorrection * MaxCorrection)
        {
            _cameraRig.transform.position = targetEye;
            return true;
        }

        _cameraRig.transform.position += delta;
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
