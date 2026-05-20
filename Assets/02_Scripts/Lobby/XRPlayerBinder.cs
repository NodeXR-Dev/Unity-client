using Fusion;
using UnityEngine;

public class XRPlayerBinder : NetworkBehaviour
{
    private Transform cameraRigTransform;

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
        transform.position = cameraRigTransform.position;
        transform.rotation = cameraRigTransform.rotation;
    }
}