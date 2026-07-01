using Fusion;
using UnityEngine;

public class PlayerVRSync : NetworkBehaviour
{
    private Transform _mainCamera;

    [Header("Avatar Targets")]
    public Transform characterHead;
    public Transform characterBody;

    [Header("Body Rotation")]
    public float rotateThreshold = 30f;
    public float rotateSpeed = 5f;

    [Networked] private Vector3 NetworkHeadEuler { get; set; }
    [Networked] private float NetworkBodyYaw { get; set; }
    [Networked] private NetworkBool NetworkPoseReady { get; set; }

    private float _targetYRotation;

    public override void Spawned()
    {
        if (!Object.HasInputAuthority)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("[PlayerVRSync] Main Camera not found.");
            return;
        }

        _mainCamera = mainCamera.transform;
        _targetYRotation = _mainCamera.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (Object.HasInputAuthority)
        {
            UpdateLocalPose();
            return;
        }

        ApplyRemotePose();
    }

    private void UpdateLocalPose()
    {
        if (_mainCamera == null || characterHead == null || characterBody == null)
            return;

        characterHead.rotation = _mainCamera.rotation;

        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(characterBody.eulerAngles.y, _mainCamera.eulerAngles.y));
        if (angleDiff >= rotateThreshold)
            _targetYRotation = _mainCamera.eulerAngles.y;

        characterBody.rotation = Quaternion.Slerp(
            characterBody.rotation,
            Quaternion.Euler(0f, _targetYRotation, 0f),
            Time.deltaTime * rotateSpeed
        );

        if (!Object.HasStateAuthority)
            return;

        NetworkHeadEuler = characterHead.rotation.eulerAngles;
        NetworkBodyYaw = characterBody.rotation.eulerAngles.y;
        NetworkPoseReady = true;
    }

    private void ApplyRemotePose()
    {
        if (!NetworkPoseReady)
            return;

        if (characterHead != null)
            characterHead.rotation = Quaternion.Euler(NetworkHeadEuler);

        if (characterBody != null)
        {
            Quaternion targetRotation = Quaternion.Euler(0f, NetworkBodyYaw, 0f);
            characterBody.rotation = Quaternion.Slerp(
                characterBody.rotation,
                targetRotation,
                Time.deltaTime * rotateSpeed
            );
        }
    }
}
