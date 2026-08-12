using Fusion;
using UnityEngine;

public class XRPlayerBinder : NetworkBehaviour
{
    private Transform cameraRigTransform;

    // 사용자의 눈(HMD)과 아바타의 머리.
    // 루트를 리그 루트에 맞추면 모델 키(약 1.2m)와 실제 사용자 키가 달라
    // 카메라가 캐릭터 머리 위 허공에 뜬다. 머리끼리 맞춰야 한다.
    private Transform headTransform;
    private Transform avatarHead;

    private static Transform FindAvatarHead(Transform root)
    {
        // 아바타가 쓰고 있는 HMD 모형이 곧 눈 위치다.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "HMD")
                return t;
        }

        // 없으면 머리 노드로 대신한다(Avatar_Head 아래 중복 노드는 제외).
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Head" && (t.parent == null || t.parent.name != "Avatar_Head"))
                return t;
        }

        return null;
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
            avatarHead = FindAvatarHead(transform);

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
        // 머리를 못 찾았으면 예전처럼 리그 루트에 맞춘다.
        if (headTransform == null || avatarHead == null)
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

        // 아바타 머리(HMD 모형)가 사용자의 눈에 오도록 루트를 그만큼 뒤로 물린다.
        // 회전을 먼저 정한 뒤에 재야 오프셋이 같이 돌아간다.
        Vector3 headOffset = avatarHead.position - transform.position;
        transform.position = headTransform.position - headOffset;
    }
}