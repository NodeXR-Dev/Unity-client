using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PresenterCameraPoseSync : NetworkBehaviour
{
    [Header("Local Camera")]
    [SerializeField] private LocalCameraReference localCameraReference;
    [SerializeField] private Camera explicitLocalCamera;
    [SerializeField] private string centerEyeObjectName = "CenterEyeAnchor";

    [Header("Network Publish")]
    [SerializeField, Min(1f)] private float sendRate = 30f;
    [SerializeField, Min(0f)] private float positionThreshold = 0.01f;
    [SerializeField, Min(0f)] private float rotationThreshold = 0.5f;
    [SerializeField, Range(1f, 179f)] private float fallbackFieldOfView = 60f;

    [Networked] public Vector3 SharedCameraPosition { get; private set; }
    [Networked] public Quaternion SharedCameraRotation { get; private set; }
    [Networked] public float SharedFieldOfView { get; private set; }
    [Networked] public NetworkBool PoseReady { get; private set; }
    [Networked] public int PoseRevision { get; private set; }
    [Networked] public NetworkBool IsSharingView { get; private set; }

    private Transform cachedCameraTransform;
    private float lastSendTime;
    private Vector3 lastSentPosition;
    private Quaternion lastSentRotation = Quaternion.identity;
    private bool hasSentPose;

    private float SendInterval => 1f / Mathf.Max(1f, sendRate);
    public bool IsLocalSharing => Object != null && Object.HasInputAuthority && IsSharingView;

    private void LateUpdate()
    {
        if (!Object.HasInputAuthority || !ShouldPublishPose())
        {
            return;
        }

        if (Time.time - lastSendTime < SendInterval)
        {
            return;
        }

        if (!TryCaptureLocalCamera(out Vector3 position, out Quaternion rotation, out float fieldOfView))
        {
            return;
        }

        if (hasSentPose &&
            Vector3.Distance(lastSentPosition, position) < positionThreshold &&
            Quaternion.Angle(lastSentRotation, rotation) < rotationThreshold)
        {
            return;
        }

        lastSendTime = Time.time;
        lastSentPosition = position;
        lastSentRotation = rotation;
        hasSentPose = true;

        if (HasStateAuthority)
        {
            ApplyPoseAsAuthority(position, rotation, fieldOfView);
        }
        else
        {
            RPC_SetPresenterPose(position, rotation, fieldOfView);
        }
    }

    public void RequestStartSharing()
    {
        RequestSetSharing(true);
    }

    public void RequestStopSharing()
    {
        RequestSetSharing(false);
    }

    public void RequestToggleSharing()
    {
        RequestSetSharing(!IsSharingView);
    }

    public void RequestSetSharing(bool isSharing)
    {
        if (Object == null || !Object.HasInputAuthority)
        {
            return;
        }

        ResetPublishCache();

        if (HasStateAuthority)
        {
            ApplySharingAsAuthority(isSharing);
        }
        else
        {
            RPC_SetSharing(isSharing);
        }
    }

    public bool TryGetSharedCameraPose(out Vector3 position, out Quaternion rotation, out float fieldOfView)
    {
        if (!PoseReady)
        {
            position = default;
            rotation = Quaternion.identity;
            fieldOfView = fallbackFieldOfView;
            return false;
        }

        position = SharedCameraPosition;
        rotation = SharedCameraRotation;
        fieldOfView = SharedFieldOfView > 1f ? SharedFieldOfView : fallbackFieldOfView;
        return true;
    }

    private bool ShouldPublishPose()
    {
        if (IsSharingView)
        {
            return true;
        }

        PresenterViewSession session = PresenterViewSession.Instance;
        if (session == null || !session.IsPresenterViewActive || session.Runner == null)
        {
            return false;
        }

        return session.Presenter == session.Runner.LocalPlayer;
    }

    private bool TryCaptureLocalCamera(out Vector3 position, out Quaternion rotation, out float fieldOfView)
    {
        Transform cameraTransform = ResolveLocalCameraTransform(out Camera resolvedCamera);
        if (cameraTransform == null)
        {
            position = default;
            rotation = Quaternion.identity;
            fieldOfView = fallbackFieldOfView;
            return false;
        }

        position = cameraTransform.position;
        rotation = cameraTransform.rotation;
        fieldOfView = resolvedCamera != null ? resolvedCamera.fieldOfView : fallbackFieldOfView;
        return true;
    }

    private Transform ResolveLocalCameraTransform(out Camera resolvedCamera)
    {
        if (localCameraReference != null && localCameraReference.CameraTransform != null)
        {
            resolvedCamera = localCameraReference.Camera;
            cachedCameraTransform = localCameraReference.CameraTransform;
            return cachedCameraTransform;
        }

        if (explicitLocalCamera != null)
        {
            resolvedCamera = explicitLocalCamera;
            cachedCameraTransform = explicitLocalCamera.transform;
            return cachedCameraTransform;
        }

        if (cachedCameraTransform != null)
        {
            resolvedCamera = cachedCameraTransform.GetComponent<Camera>();
            return cachedCameraTransform;
        }

        if (!string.IsNullOrWhiteSpace(centerEyeObjectName))
        {
            GameObject centerEye = GameObject.Find(centerEyeObjectName);
            if (centerEye != null)
            {
                cachedCameraTransform = centerEye.transform;
                resolvedCamera = centerEye.GetComponent<Camera>();
                return cachedCameraTransform;
            }
        }

        if (Camera.main != null)
        {
            resolvedCamera = Camera.main;
            cachedCameraTransform = Camera.main.transform;
            return cachedCameraTransform;
        }

        resolvedCamera = null;
        return null;
    }

    private void ApplyPoseAsAuthority(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        SharedCameraPosition = position;
        SharedCameraRotation = rotation;
        SharedFieldOfView = fieldOfView;
        PoseReady = true;
        PoseRevision++;
    }

    private void ApplySharingAsAuthority(bool isSharing)
    {
        IsSharingView = isSharing;
        ResetPublishCache();

        if (!isSharing)
        {
            PoseReady = false;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetPresenterPose(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        ApplyPoseAsAuthority(position, rotation, fieldOfView);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetSharing(bool isSharing)
    {
        ApplySharingAsAuthority(isSharing);
    }

    private void ResetPublishCache()
    {
        hasSentPose = false;
        lastSendTime = -SendInterval;
    }
}
