using UnityEngine;
using Fusion;

public class PlayerVRSync : NetworkBehaviour
{
    private Transform _mainCamera;

    [Header("연결할 뼈대들")]
    public Transform characterHead;
    public Transform characterBody;

    [Header("설정값")]
    public float rotateThreshold = 30f;
    public float rotateSpeed = 5f;

    private float _targetYRotation;

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            _mainCamera = Camera.main.transform;
            _targetYRotation = _mainCamera.eulerAngles.y;
        }
    }

    void LateUpdate()
    {
        if (!Object.HasInputAuthority || _mainCamera == null) return;

        // 1. 머리 회전 (VR HMD 회전값 그대로 적용)
        // 만약 카메라를 머리 뼈의 자식으로 넣으셨다면, 머리가 돌 때 카메라도 같이 돕니다.
        characterHead.rotation = _mainCamera.rotation;

        // 2. 몸통 회전 로직 (임계값 넘었을 때만 스르륵 회전)
        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(characterBody.eulerAngles.y, _mainCamera.eulerAngles.y));
        if (angleDiff >= rotateThreshold)
        {
            _targetYRotation = _mainCamera.eulerAngles.y;
        }

        characterBody.rotation = Quaternion.Slerp(
            characterBody.rotation, 
            Quaternion.Euler(0, _targetYRotation, 0), 
            Time.deltaTime * rotateSpeed
        );
    }
}