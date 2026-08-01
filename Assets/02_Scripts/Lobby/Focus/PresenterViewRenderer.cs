using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class PresenterViewRenderer : MonoBehaviour
{
    private enum OutputMode
    {
        DirectCameraView = 0,
        RenderTexturePanel = 1
    }

    [Header("Session")]
    [SerializeField] private PresenterViewSession session;
    [SerializeField] private bool autoFindSession = true;
    [SerializeField] private bool autoRejoinOnNewShare = true;
    [SerializeField] private bool showOwnSharePreview = false;
    [SerializeField] private bool usePlayerShareFallback = true;

    [Header("Output")]
    [SerializeField] private OutputMode outputMode = OutputMode.DirectCameraView;
    [SerializeField] private Camera renderCamera;
    [SerializeField] private bool createRuntimeCamera = true;
    [SerializeField] private float directCameraDepth = 1000f;
    [SerializeField] private RawImage outputRawImage;
    [SerializeField] private Renderer outputRenderer;
    [SerializeField] private string outputTextureProperty = "_MainTex";
    [SerializeField] private RenderTexture outputTexture;
    [SerializeField] private Vector2Int runtimeTextureSize = new Vector2Int(1280, 720);
    [SerializeField] private bool hideOutputWhenInactive = true;

    [Header("Camera Motion")]
    [SerializeField] private bool smoothCameraMotion = true;
    [SerializeField, Min(0f)] private float positionLerpSpeed = 18f;
    [SerializeField, Min(0f)] private float rotationLerpSpeed = 18f;

    [Header("Personal Mode Exit")]
    [SerializeField] private bool exitToPersonalModeOnLocalViewManipulation = true;
    [SerializeField] private bool exitToPersonalModeOnInput = true;
    [SerializeField] private LocalCameraReference localCameraReference;
    [SerializeField, Min(0f)] private float exitInputGraceSeconds = 1f;
    [SerializeField, Min(0f)] private float exitPositionThreshold = 0.35f;
    [SerializeField, Min(0f)] private float exitRotationThreshold = 25f;

    private RenderTexture runtimeTexture;
    private bool ownsRuntimeTexture;
    private MaterialPropertyBlock materialPropertyBlock;
    private PresenterCameraPoseSync cachedPresenterPoseSync;
    private PresenterViewState lastSessionState;
    private bool hasLastSessionState;
    private bool localPersonalMode;
    private bool hasRenderCameraPose;
    private PresenterCameraPoseSync activePresenterPoseSync;
    private PresenterCameraPoseSync personalModePresenterPoseSync;
    private NetworkObject personalModePresenterObject;
    private PresenterViewState personalModeSessionState;
    private bool hasPersonalModeSessionState;
    private Pose localViewBaseline;
    private bool hasLocalViewBaseline;
    private float localViewStartTime;

    public bool IsLocalPersonalMode => localPersonalMode;
    public bool IsShowingPresenterView { get; private set; }

    private void Awake()
    {
        ResolveSession();
        EnsureRenderCamera();
        EnsureOutputTexture();
        ApplyOutputTexture();
        SetOutputVisible(false);
    }

    private void OnEnable()
    {
        ResolveSession();

        if (session != null)
        {
            session.StateChanged += HandleSessionChanged;
            HandleSessionChanged(session.GetState());
        }
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.StateChanged -= HandleSessionChanged;
        }
    }

    private void OnDestroy()
    {
        if (ownsRuntimeTexture && runtimeTexture != null)
        {
            runtimeTexture.Release();
            Destroy(runtimeTexture);
        }
    }

    private void Update()
    {
        if (session == null && autoFindSession)
        {
            ResolveSession();

            if (session != null)
            {
                session.StateChanged += HandleSessionChanged;
                HandleSessionChanged(session.GetState());
            }
        }

        PresenterCameraPoseSync presenterPoseSync = ResolveActivePresenterPoseSync();
        if (presenterPoseSync == null)
        {
            ClearPresenterViewState();
            return;
        }

        if (localPersonalMode)
        {
            if (ShouldAutoRejoinPresenterView(presenterPoseSync))
            {
                localPersonalMode = false;
                personalModePresenterPoseSync = null;
                personalModePresenterObject = null;
                hasPersonalModeSessionState = false;
            }
            else
            {
                SetOutputVisible(false);
                return;
            }
        }

        if (activePresenterPoseSync != presenterPoseSync)
        {
            activePresenterPoseSync = presenterPoseSync;
            hasRenderCameraPose = false;
            ResetLocalViewBaseline();
        }

        if (ShouldExitToPersonalModeFromInput())
        {
            ExitToPersonalMode();
            return;
        }

        if (!presenterPoseSync.TryGetSharedCameraPose(out Vector3 position, out Quaternion rotation, out float fieldOfView))
        {
            SetOutputVisible(false);
            return;
        }

        EnsureRenderCamera();
        EnsureOutputTexture();
        ApplyOutputTexture();
        ApplyDirectCameraSettings();
        UpdateRenderCamera(position, rotation, fieldOfView);
        SetOutputVisible(true);
        DetectLocalViewManipulation();
    }

    public void ExitToPersonalMode()
    {
        localPersonalMode = true;
        personalModePresenterPoseSync = activePresenterPoseSync;
        personalModePresenterObject = activePresenterPoseSync != null ? activePresenterPoseSync.Object : null;
        personalModeSessionState = session != null ? session.GetState() : default;
        hasPersonalModeSessionState = session != null;
        cachedPresenterPoseSync = null;
        SetOutputVisible(false);
    }

    public void RejoinPresenterView()
    {
        localPersonalMode = false;
        personalModePresenterPoseSync = null;
        personalModePresenterObject = null;
        hasPersonalModeSessionState = false;
        ResetLocalViewBaseline();
    }

    public void TogglePersonalMode()
    {
        if (localPersonalMode)
        {
            RejoinPresenterView();
        }
        else
        {
            ExitToPersonalMode();
        }
    }

    private PresenterCameraPoseSync ResolveActivePresenterPoseSync()
    {
        if (session != null)
        {
            PresenterViewState state = session.GetState();
            if (HasSessionStateChanged(state))
            {
                HandleSessionChanged(state);
            }

            if (ShouldShowPresenterView(state))
            {
                PresenterCameraPoseSync sessionPresenter = ResolvePresenterPoseSync(state.Presenter);
                if (sessionPresenter != null)
                {
                    return sessionPresenter;
                }
            }
        }

        if (!usePlayerShareFallback)
        {
            return null;
        }

        return ResolvePlayerShareFallback();
    }

    private PresenterCameraPoseSync ResolvePlayerShareFallback()
    {
        if (cachedPresenterPoseSync != null && IsFallbackShareCandidate(cachedPresenterPoseSync))
        {
            return cachedPresenterPoseSync;
        }

#if UNITY_2023_1_OR_NEWER
        PresenterCameraPoseSync[] poseSyncs = FindObjectsByType<PresenterCameraPoseSync>(FindObjectsSortMode.None);
#else
        PresenterCameraPoseSync[] poseSyncs = FindObjectsOfType<PresenterCameraPoseSync>();
#endif

        foreach (PresenterCameraPoseSync poseSync in poseSyncs)
        {
            if (IsFallbackShareCandidate(poseSync))
            {
                cachedPresenterPoseSync = poseSync;
                return cachedPresenterPoseSync;
            }
        }

        return null;
    }

    private bool IsFallbackShareCandidate(PresenterCameraPoseSync poseSync)
    {
        if (poseSync == null || poseSync.Object == null || !poseSync.IsSharingView)
        {
            return false;
        }

        return showOwnSharePreview || !poseSync.Object.HasInputAuthority;
    }

    private void HandleSessionChanged(PresenterViewState state)
    {
        bool isNewRevision = !hasLastSessionState || state.Revision != lastSessionState.Revision;

        lastSessionState = state;
        hasLastSessionState = true;
        cachedPresenterPoseSync = null;

        if (!state.IsPresenterViewActive)
        {
            ClearPresenterViewState();
            return;
        }

        if (isNewRevision && autoRejoinOnNewShare)
        {
            localPersonalMode = false;
        }

        ResetLocalViewBaseline();
    }

    private bool ShouldShowPresenterView(PresenterViewState state)
    {
        if (!state.IsPresenterViewActive || localPersonalMode)
        {
            return false;
        }

        PlayerRef localPlayer = GetLocalPlayer();
        if (!showOwnSharePreview && localPlayer != PlayerRef.None && localPlayer == state.Presenter)
        {
            return false;
        }

        return true;
    }

    private PresenterCameraPoseSync ResolvePresenterPoseSync(PlayerRef presenter)
    {
        if (presenter == PlayerRef.None)
        {
            return null;
        }

        if (cachedPresenterPoseSync != null && IsPoseSyncForPresenter(cachedPresenterPoseSync, presenter))
        {
            return cachedPresenterPoseSync;
        }

        NetworkRunner runner = session != null && session.Runner != null
            ? session.Runner
            : NetworkManager.runnerInsatance;

        if (runner != null)
        {
            NetworkObject presenterObject = runner.GetPlayerObject(presenter);
            if (presenterObject != null)
            {
                cachedPresenterPoseSync = presenterObject.GetComponentInChildren<PresenterCameraPoseSync>();
                if (cachedPresenterPoseSync != null)
                {
                    return cachedPresenterPoseSync;
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        PresenterCameraPoseSync[] poseSyncs = FindObjectsByType<PresenterCameraPoseSync>(FindObjectsSortMode.None);
#else
        PresenterCameraPoseSync[] poseSyncs = FindObjectsOfType<PresenterCameraPoseSync>();
#endif

        foreach (PresenterCameraPoseSync poseSync in poseSyncs)
        {
            if (IsPoseSyncForPresenter(poseSync, presenter))
            {
                cachedPresenterPoseSync = poseSync;
                return cachedPresenterPoseSync;
            }
        }

        return null;
    }

    private bool IsPoseSyncForPresenter(PresenterCameraPoseSync poseSync, PlayerRef presenter)
    {
        return poseSync != null &&
               poseSync.Object != null &&
               (poseSync.Object.InputAuthority == presenter || poseSync.Object.StateAuthority == presenter);
    }

    private void UpdateRenderCamera(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        if (renderCamera == null)
        {
            return;
        }

        if (!smoothCameraMotion || !hasRenderCameraPose)
        {
            renderCamera.transform.SetPositionAndRotation(position, rotation);
            hasRenderCameraPose = true;
        }
        else
        {
            float positionT = positionLerpSpeed <= 0f ? 1f : 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime);
            float rotationT = rotationLerpSpeed <= 0f ? 1f : 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
            renderCamera.transform.position = Vector3.Lerp(renderCamera.transform.position, position, positionT);
            renderCamera.transform.rotation = Quaternion.Slerp(renderCamera.transform.rotation, rotation, rotationT);
        }

        renderCamera.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
    }

    private void DetectLocalViewManipulation()
    {
        if (!exitToPersonalModeOnLocalViewManipulation || localPersonalMode)
        {
            return;
        }

        if (ShouldExitToPersonalModeFromInput())
        {
            ExitToPersonalMode();
            return;
        }

        if (Time.time - localViewStartTime < exitInputGraceSeconds)
        {
            return;
        }

        if (!TryGetLocalViewPose(out Pose currentPose))
        {
            return;
        }

        if (!hasLocalViewBaseline)
        {
            localViewBaseline = currentPose;
            hasLocalViewBaseline = true;
            return;
        }

        float positionDelta = Vector3.Distance(localViewBaseline.position, currentPose.position);
        float rotationDelta = Quaternion.Angle(localViewBaseline.rotation, currentPose.rotation);

        if (positionDelta >= exitPositionThreshold || rotationDelta >= exitRotationThreshold)
        {
            ExitToPersonalMode();
        }
    }

    private bool HasLocalViewInput()
    {
        return Input.GetKey(KeyCode.W) ||
               Input.GetKey(KeyCode.A) ||
               Input.GetKey(KeyCode.S) ||
               Input.GetKey(KeyCode.D) ||
               Input.GetKey(KeyCode.Q) ||
               Input.GetKey(KeyCode.E) ||
               Input.GetMouseButton(1);
    }

    private bool ShouldExitToPersonalModeFromInput()
    {
        return exitToPersonalModeOnLocalViewManipulation &&
               exitToPersonalModeOnInput &&
               !localPersonalMode &&
               HasLocalViewInput();
    }

    private bool ShouldAutoRejoinPresenterView(PresenterCameraPoseSync presenterPoseSync)
    {
        if (!autoRejoinOnNewShare || presenterPoseSync == null)
        {
            return false;
        }

        if (session != null)
        {
            PresenterViewState currentState = session.GetState();
            if (!currentState.IsPresenterViewActive)
            {
                return false;
            }

            if (!hasPersonalModeSessionState)
            {
                return true;
            }

            return currentState.Revision != personalModeSessionState.Revision ||
                   currentState.Presenter != personalModeSessionState.Presenter;
        }

        return presenterPoseSync != personalModePresenterPoseSync &&
               presenterPoseSync.Object != personalModePresenterObject;
    }

    private bool TryGetLocalViewPose(out Pose pose)
    {
        if (localCameraReference != null && localCameraReference.TryGetPose(out pose))
        {
            return true;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            pose = new Pose(mainCamera.transform.position, mainCamera.transform.rotation);
            return true;
        }

        pose = default;
        return false;
    }

    private void ResetLocalViewBaseline()
    {
        localViewStartTime = Time.time;
        hasLocalViewBaseline = TryGetLocalViewPose(out localViewBaseline);
    }

    private void ClearPresenterViewState()
    {
        activePresenterPoseSync = null;
        personalModePresenterPoseSync = null;
        cachedPresenterPoseSync = null;
        localPersonalMode = false;
        hasRenderCameraPose = false;
        hasLocalViewBaseline = false;
        hasPersonalModeSessionState = false;
        SetOutputVisible(false);
    }

    private bool HasSessionStateChanged(PresenterViewState state)
    {
        return !hasLastSessionState ||
               state.Mode != lastSessionState.Mode ||
               state.Presenter != lastSessionState.Presenter ||
               state.Revision != lastSessionState.Revision;
    }

    private PlayerRef GetLocalPlayer()
    {
        if (session != null && session.Runner != null)
        {
            return session.Runner.LocalPlayer;
        }

        NetworkRunner runner = NetworkManager.runnerInsatance;
        return runner != null ? runner.LocalPlayer : PlayerRef.None;
    }

    private void ResolveSession()
    {
        if (session != null)
        {
            return;
        }

        session = PresenterViewSession.Instance;

        if (session == null && autoFindSession)
        {
#if UNITY_2023_1_OR_NEWER
            session = FindFirstObjectByType<PresenterViewSession>();
#else
            session = FindObjectOfType<PresenterViewSession>();
#endif
        }
    }

    private void EnsureRenderCamera()
    {
        if (renderCamera != null || !createRuntimeCamera)
        {
            return;
        }

        GameObject cameraObject = new GameObject("Presenter View Camera");
        cameraObject.hideFlags = HideFlags.DontSave;
        renderCamera = cameraObject.AddComponent<Camera>();
        renderCamera.enabled = false;
        ApplyDirectCameraSettings();
    }

    private void EnsureOutputTexture()
    {
        if (outputMode == OutputMode.DirectCameraView)
        {
            return;
        }

        if (outputTexture != null)
        {
            return;
        }

        if (runtimeTexture != null)
        {
            outputTexture = runtimeTexture;
            return;
        }

        int width = Mathf.Max(16, runtimeTextureSize.x);
        int height = Mathf.Max(16, runtimeTextureSize.y);
        runtimeTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "PresenterViewRuntimeTexture"
        };
        runtimeTexture.Create();

        outputTexture = runtimeTexture;
        ownsRuntimeTexture = true;
    }

    private void ApplyOutputTexture()
    {
        if (outputMode == OutputMode.DirectCameraView)
        {
            if (renderCamera != null)
            {
                renderCamera.targetTexture = null;
            }

            return;
        }

        if (outputTexture == null)
        {
            return;
        }

        if (renderCamera != null && renderCamera.targetTexture != outputTexture)
        {
            renderCamera.targetTexture = outputTexture;
        }

        if (outputRawImage != null && outputRawImage.texture != outputTexture)
        {
            outputRawImage.texture = outputTexture;
        }

        if (outputRenderer != null)
        {
            materialPropertyBlock ??= new MaterialPropertyBlock();
            outputRenderer.GetPropertyBlock(materialPropertyBlock);
            materialPropertyBlock.SetTexture(outputTextureProperty, outputTexture);
            outputRenderer.SetPropertyBlock(materialPropertyBlock);
        }
    }

    private void ApplyDirectCameraSettings()
    {
        if (renderCamera == null || outputMode != OutputMode.DirectCameraView)
        {
            return;
        }

        renderCamera.depth = directCameraDepth;
        renderCamera.targetTexture = null;
        renderCamera.clearFlags = CameraClearFlags.Skybox;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
        {
            renderCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        }
    }

    private void SetOutputVisible(bool visible)
    {
        IsShowingPresenterView = visible;

        if (renderCamera != null)
        {
            if (visible)
            {
                if (!renderCamera.gameObject.activeSelf)
                {
                    renderCamera.gameObject.SetActive(true);
                }

                renderCamera.depth = directCameraDepth;
                renderCamera.enabled = true;
            }
            else
            {
                renderCamera.enabled = false;
                renderCamera.targetTexture = null;
                renderCamera.depth = -1000f;
            }
        }

        if (outputMode == OutputMode.DirectCameraView)
        {
            if (outputRawImage != null && outputRawImage.gameObject.activeSelf)
            {
                outputRawImage.gameObject.SetActive(false);
            }

            if (outputRenderer != null && outputRenderer.enabled)
            {
                outputRenderer.enabled = false;
            }

            return;
        }

        if (!hideOutputWhenInactive && !visible)
        {
            return;
        }

        if (outputRawImage != null && outputRawImage.gameObject.activeSelf != visible)
        {
            outputRawImage.gameObject.SetActive(visible);
        }

        if (outputRenderer != null && outputRenderer.enabled != visible)
        {
            outputRenderer.enabled = visible;
        }
    }
}
