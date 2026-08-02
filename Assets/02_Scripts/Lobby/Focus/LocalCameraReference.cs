using UnityEngine;

public class LocalCameraReference : MonoBehaviour
{
    [SerializeField] private Camera explicitCamera;
    [SerializeField] private string centerEyeObjectName = "CenterEyeAnchor";
    [SerializeField] private bool allowCameraMainFallback = true;

    private Camera cachedCamera;

    public Camera Camera
    {
        get
        {
            ResolveCamera();
            return cachedCamera;
        }
    }

    public Transform CameraTransform
    {
        get
        {
            Camera resolvedCamera = Camera;
            return resolvedCamera != null ? resolvedCamera.transform : null;
        }
    }

    public bool TryGetPose(out Pose pose)
    {
        Transform cameraTransform = CameraTransform;
        if (cameraTransform == null)
        {
            pose = default;
            return false;
        }

        pose = new Pose(cameraTransform.position, cameraTransform.rotation);
        return true;
    }

    public Camera ResolveCamera()
    {
        if (explicitCamera != null)
        {
            cachedCamera = explicitCamera;
            return cachedCamera;
        }

        if (cachedCamera != null)
        {
            return cachedCamera;
        }

        if (!string.IsNullOrWhiteSpace(centerEyeObjectName))
        {
            GameObject centerEye = GameObject.Find(centerEyeObjectName);
            if (centerEye != null && centerEye.TryGetComponent(out Camera centerEyeCamera))
            {
                cachedCamera = centerEyeCamera;
                return cachedCamera;
            }
        }

        if (allowCameraMainFallback && Camera.main != null)
        {
            cachedCamera = Camera.main;
            return cachedCamera;
        }

#if UNITY_2023_1_OR_NEWER
        cachedCamera = FindFirstObjectByType<Camera>();
#else
        cachedCamera = FindObjectOfType<Camera>();
#endif
        return cachedCamera;
    }

    public void ClearCache()
    {
        cachedCamera = null;
    }
}
