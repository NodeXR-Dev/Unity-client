using Fusion;
using UnityEngine;

public class XRPlayerBinder : NetworkBehaviour
{
    private Transform cameraRigTransform;

    // 사용자의 눈(CenterEyeAnchor)과, 아바타에서 그에 해당하는 지점.
    // 루트를 리그 루트에 맞추면 모델 키(약 1.2m)와 실제 사용자 키가 달라
    // 카메라가 캐릭터 머리 위 허공에 뜬다. 눈끼리 맞춰야 한다.
    private Transform headTransform;

    // 루트 기준 눈 위치(로컬). 스폰 때 한 번만 재고 이후 재사용한다.
    private Vector3 eyeLocalOffset;
    private bool hasEyeOffset;

    /// <summary>
    /// 아바타에서 '눈'에 해당하는 지점을 루트 기준 로컬 좌표로 잰다.
    ///
    /// 노드 위치(transform)를 쓰면 안 된다. HMD 메시는 Neck 본에 스킨되어 있어
    /// transform 은 본 원점(루트 기준 y 0.37)에 있고 실제로 그려지는 바이저는
    /// y 0.87~1.11 에 있다. 노드를 기준으로 맞추면 62cm 어긋나 카메라가 목에 온다.
    /// 그래서 렌더러가 실제로 차지하는 영역의 중심을 쓴다.
    /// </summary>
    private static bool TryMeasureEyeOffset(Transform root, out Vector3 localOffset)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.name != "HMD")
                continue;

            localOffset = root.InverseTransformPoint(renderer.bounds.center);
            return true;
        }

        // HMD 모형이 없는 아바타면 디자이너가 심어 둔 눈 노드를 쓴다.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Camera" && t.name != "FaceCam")
                continue;

            localOffset = root.InverseTransformPoint(t.position);
            return true;
        }

        localOffset = Vector3.zero;
        return false;
    }

    public override void Spawned()
    {
        // 내 로컬 플레이어만 카메라 리그에 따라다니게 설정
        if (!Object.HasInputAuthority)
            return;

        // [수정] 사진에 나온 "[BuildingBlock] Camera Rig" 오브젝트를 직접 찾아옵니다.
        GameObject rigObj = GameObject.Find("[BuildingBlock] Camera Rig");

        if (rigObj == null)
        {
            // 만약 이름을 못 찾으면 씬에 있는 모든 리깅 시스템 중 하나를 시도
            Debug.LogWarning("BuildingBlock Camera Rig name not found, trying fallback...");
            rigObj = GameObject.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>()?.gameObject;
        }

        if (rigObj != null)
        {
            cameraRigTransform = rigObj.transform;

            // 눈 위치는 리그 루트가 아니라 CenterEyeAnchor 다.
            OVRCameraRig rig = rigObj.GetComponent<OVRCameraRig>();
            headTransform = rig != null && rig.centerEyeAnchor != null
                ? rig.centerEyeAnchor
                : (Camera.main != null ? Camera.main.transform : null);
            hasEyeOffset = TryMeasureEyeOffset(transform, out eyeLocalOffset);

            // 생성되자마자 착! 달라붙기
            SyncTransform();
        }
        else
        {
            Debug.LogError("카메라 리그를 찾을 수 없습니다! 하이어라키 이름을 확인해주세요.");
        }
    }

    private void LateUpdate()
    {
        // 로컬 플레이어이고 카메라 리그가 있을 때만 위치 동기화
        if (!Object.HasInputAuthority || cameraRigTransform == null)
            return;

        SyncTransform();
    }

    // 위치와 회전을 일치시키는 공통 함수
    private void SyncTransform()
    {
        // 눈 위치를 못 쟀으면 예전처럼 리그 루트에 맞춘다.
        if (headTransform == null || !hasEyeOffset)
        {
            transform.position = cameraRigTransform.position;
            transform.rotation = cameraRigTransform.rotation;
            return;
        }

        // 몸통은 바라보는 방향(수평)만 따라간다.
        // 피치·롤까지 따르면 고개를 숙일 때 아바타가 통째로 기운다.
        Vector3 forward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
        transform.rotation = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : cameraRigTransform.rotation;

        // 아바타의 눈(HMD 바이저)이 사용자의 눈에 오도록 루트를 그만큼 뒤로 물린다.
        // 회전을 먼저 정했으므로 로컬 오프셋을 그 회전으로 돌려 쓴다.
        transform.position =
            headTransform.position - transform.rotation * eyeLocalOffset;
    }
}