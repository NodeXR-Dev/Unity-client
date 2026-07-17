using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Quest에서는 Meta XR CameraRig를, 에디터 단독 실행에서는
/// 기존 MVP 카메라를 선택해 두 검증 경로를 함께 유지한다.
/// </summary>
[DefaultExecutionOrder(-1100)]
public class MvpXrCameraModeController : MonoBehaviour
{
    [SerializeField] private GameObject _metaCameraRig;
    [SerializeField] private Camera _desktopCamera;
    [SerializeField] private int _xrProbeFrames = 30;

    private bool _useXr;
    private float _nextInputModuleCheck;

    private IEnumerator Start()
    {
        ResolveReferences();

        int frames = Mathf.Max(1, _xrProbeFrames);
        for (int index = 0; index < frames; index++)
        {
            if (IsXrDisplayRunning())
            {
                ApplyMode(true);
                yield break;
            }
            yield return null;
        }

        ApplyMode(false);
    }

    public void Configure(
        GameObject metaCameraRig,
        Camera desktopCamera)
    {
        _metaCameraRig = metaCameraRig;
        _desktopCamera = desktopCamera;
    }

    private void ResolveReferences()
    {
        if (_metaCameraRig == null)
        {
            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
                _metaCameraRig = rig.gameObject;
        }

        if (_desktopCamera == null)
        {
            Camera[] cameras =
                Resources.FindObjectsOfTypeAll<Camera>();
            foreach (Camera candidate in cameras)
            {
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid())
                    continue;
                if (candidate.name == "Main Camera")
                {
                    _desktopCamera = candidate;
                    break;
                }
            }
        }
    }

    private static bool IsXrDisplayRunning()
    {
        List<XRDisplaySubsystem> displays =
            new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);

        bool displayRunning = false;
        foreach (XRDisplaySubsystem display in displays)
        {
            if (display != null && display.running)
            {
                displayRunning = true;
                break;
            }
        }
        if (!displayRunning)
            return false;

#if !UNITY_EDITOR
        // Quest 단말에서는 디스플레이 서브시스템이 실행 중이면 XR 모드다.
        // 앱 시작 직후 Head 입력 장치 등록이 한두 프레임 늦어져도
        // 데스크톱 카메라로 잘못 전환하지 않는다.
        return true;
#else
        InputDevice head =
            InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid)
            return false;

        if (head.TryGetFeatureValue(
                CommonUsages.isTracked,
                out bool isTracked))
            return isTracked;

        return (head.characteristics &
                InputDeviceCharacteristics.HeadMounted) != 0;
#endif
    }

    private void ApplyMode(bool useXr)
    {
        _useXr = useXr;
        if (_metaCameraRig != null)
            _metaCameraRig.SetActive(useXr);

        if (_desktopCamera != null)
        {
            _desktopCamera.gameObject.SetActive(!useXr);
            _desktopCamera.enabled = !useXr;

            AudioListener listener =
                _desktopCamera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = !useXr;
        }

        ApplyInputModuleMode(useXr);
        Debug.Log(
            useXr
                ? "[MVP XR] Meta XR CameraRig 모드"
                : "[MVP XR] 헤드셋 없음 — 데스크톱 카메라 모드");
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextInputModuleCheck)
            return;

        _nextInputModuleCheck = Time.unscaledTime + 0.25f;
        // Link 실행 후 헤드셋을 늦게 착용해도 XR 입력으로 자동 전환한다.
        // 한 번 XR로 들어간 뒤에는 일시적인 추적 손실로 모드를 내리지 않는다.
        if (!_useXr && IsXrDisplayRunning())
        {
            ApplyMode(true);
            return;
        }

        ApplyInputModuleMode(_useXr);
    }

    private static void ApplyInputModuleMode(bool useXr)
    {
        EventSystem eventSystem =
            EventSystem.current ??
            FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
            return;

        BaseInputModule[] modules =
            eventSystem.GetComponents<BaseInputModule>();
        foreach (BaseInputModule module in modules)
        {
            if (module == null)
                continue;

            string typeName =
                module.GetType().FullName ?? string.Empty;
            bool isPointable =
                typeName ==
                    "Oculus.Interaction.PointableCanvasModule";
            bool isDesktop =
                typeName ==
                    "UnityEngine.InputSystem.UI.InputSystemUIInputModule" ||
                typeName ==
                    "UnityEngine.EventSystems.StandaloneInputModule";

            if (isPointable)
                module.enabled = useXr;
            else if (isDesktop)
                module.enabled = !useXr;
        }
    }


    private void Awake()
    {
        ResolveReferences();
#if UNITY_EDITOR
        // 에디터는 OpenXR 로더가 떠 있어도 실제 HMD가 추적되지 않으면
        // 데스크톱 카메라로 시작해야 첫 화면 포인터 좌표가 정상이다.
        ApplyMode(false);
#endif
    }
}
