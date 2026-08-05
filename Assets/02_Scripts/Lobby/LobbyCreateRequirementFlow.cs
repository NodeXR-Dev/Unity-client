using System;
using System.Collections;
using System.Text;
using TMPro;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyCreateRequirementFlow : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject[] createSessionPanelsToClose;
    [SerializeField] private GameObject requirementsPanel;
    [SerializeField] private GameObject makeImagePanel;
    [SerializeField] private GameObject errorMessageObject;

    [Header("Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private Image loadingImage;
    [SerializeField] private Sprite[] loadingFrames;

    [Header("Requirement Check")]
    [SerializeField] private bool requireSpeechBeforeCreation = true;
    [SerializeField] private Recorder voiceRecorder;
    [SerializeField] private bool autoFindVoiceRecorder = true;
    [SerializeField] private float requiredSpeechSeconds = 2f;
    [SerializeField] private float speechTimeoutSeconds = 6f;
    [SerializeField] private float recorderSpeechThreshold = 0.002f;
    [SerializeField] private bool resetSpeechProgressOnSilence = true;

    [Header("Microphone Fallback")]
    [SerializeField] private bool useMicrophoneFallback = true;
    [SerializeField] private float microphoneSpeechThreshold = 0.01f;
    [SerializeField] private int microphoneSampleRate = 16000;
    [SerializeField] private int microphoneBufferSeconds = 1;
    [SerializeField] private int microphoneSampleWindow = 1024;
    [SerializeField] private float microphoneWarmupTimeoutSeconds = 2f;

    [Header("Room Creation")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private bool autoFindNetworkManager = true;
    [SerializeField] private float creationTimeoutSeconds = 30f;

    [Header("Feature API")]
    [SerializeField] private bool sendFeatureTextToServer = true;
    [SerializeField] private string apiHost = "localhost:8000";
    [SerializeField] private int featureRequestTimeoutSeconds = 60;
    [SerializeField] private TMP_InputField featureTextInput;
    [SerializeField, TextArea] private string testFeatureText;

    [Header("Server Info")]
    [SerializeField] private bool autoFindServerInfoObjects = true;
    [SerializeField] private GameObject serverInfoRoot;
    [SerializeField] private GameObject serverConnectedObject;
    [SerializeField] private GameObject serverConnectingObject;
    [SerializeField] private GameObject serverDisconnectedObject;
    [SerializeField] private float serverInfoRefreshSeconds = 0.25f;

    [Header("Loading")]
    [SerializeField] private float loadingFrameSeconds = 0.18f;

    private enum ServerInfoState
    {
        Unknown,
        Connecting,
        Connected,
        Disconnected
    }

    private bool waitingForRoomCreation;
    private string sourceSceneName;
    private float creationStartedAt;
    private float nextLoadingFrameAt;
    private int loadingFrameIndex;
    private Coroutine requirementRoutine;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    private float[] microphoneSamples;
    private bool usingMicrophoneFallback;
    private string pendingFeatureText;
    private ServerInfoState currentServerInfoState = ServerInfoState.Unknown;
    private float nextServerInfoRefreshAt;

    private void Awake()
    {
        ResolveReferences();
        HideError();
        ApplyLoadingFrame(0);
        RefreshServerInfoObjects(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        RefreshServerInfoObjects(true);
    }

    private void OnDisable()
    {
        CancelRequirementCheck();
    }

    private void Update()
    {
        RefreshServerInfoObjects(false);

        if (makeImagePanel != null && makeImagePanel.activeInHierarchy)
            TickLoadingAnimation();

        if (!waitingForRoomCreation)
            return;

        bool stillInSourceScene =
            SceneManager.GetActiveScene().name == sourceSceneName;
        if (stillInSourceScene &&
            Time.unscaledTime - creationStartedAt >= creationTimeoutSeconds)
        {
            ShowErrorState();
        }
    }

    public void OpenRequirementsPanel()
    {
        CancelRequirementCheck();
        waitingForRoomCreation = false;
        SetStartButtonInteractable(true);
        HideError();

        SetCreateSessionPanelsActive(false);

        if (makeImagePanel != null)
            makeImagePanel.SetActive(false);
        if (requirementsPanel != null)
            requirementsPanel.SetActive(true);

        RefreshServerInfoObjects(true);
    }

    public void StartCreationFromRequirements()
    {
        ResolveReferences();
        HideError();

        if (requirementRoutine != null)
            return;

        SetStartButtonInteractable(false);

        if (requirementsPanel != null)
            requirementsPanel.SetActive(true);
        if (makeImagePanel != null)
            makeImagePanel.SetActive(false);

        requirementRoutine = StartCoroutine(StartCreationAfterRequirements());
    }

    public void SetRequirementFeatureText(string featureText)
    {
        pendingFeatureText = string.IsNullOrWhiteSpace(featureText)
            ? string.Empty
            : featureText.Trim();
    }

    public void ClearRequirementFeatureText()
    {
        pendingFeatureText = string.Empty;
    }

    public void StartRequirementSpeechToText()
    {
        // STT 담당자가 여기에서 마이크 시작 -> 음성 텍스트 변환 ->
        // SetRequirementFeatureText(finalText) 호출까지 연결하면 됩니다.
    }

    public void ShowErrorState()
    {
        CancelRequirementCheck();
        waitingForRoomCreation = false;
        SetStartButtonInteractable(true);

        if (makeImagePanel != null)
            makeImagePanel.SetActive(false);
        if (requirementsPanel != null)
            requirementsPanel.SetActive(true);
        if (errorMessageObject != null)
            errorMessageObject.SetActive(true);
    }

    private IEnumerator StartCreationAfterRequirements()
    {
        bool requirementsPassed = true;

        if (requireSpeechBeforeCreation)
        {
            requirementsPassed = false;
            yield return WaitForSpeechRequirement(
                passed => requirementsPassed = passed);
        }

        requirementRoutine = null;
        StopMicrophoneFallback();

        if (!requirementsPassed)
        {
            ShowErrorState();
            yield break;
        }

        ResolveReferences();
        if (networkManager == null)
        {
            ShowErrorState();
            yield break;
        }

        BeginRoomCreation();
    }

    private void BeginRoomCreation()
    {
        if (requirementsPanel != null)
            requirementsPanel.SetActive(false);
        if (makeImagePanel != null)
            makeImagePanel.SetActive(true);

        ResetLoadingAnimation();
        waitingForRoomCreation = true;
        sourceSceneName = SceneManager.GetActiveScene().name;
        creationStartedAt = Time.unscaledTime;

        try
        {
            if (sendFeatureTextToServer)
            {
                networkManager.CreateCustomSessionWithBeforeStart(
                    SendFeatureGenerateBeforeSessionStart,
                    ShowErrorState);
            }
            else
            {
                networkManager.CreateCustomSession();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            ShowErrorState();
        }
    }

    private IEnumerator SendFeatureGenerateBeforeSessionStart(
        string roomId,
        string userId,
        Action<bool> setCanStartSession)
    {
        string featureText = GetFeatureText();
        if (string.IsNullOrWhiteSpace(featureText))
        {
            Debug.LogWarning(
                "[LobbyCreateRequirementFlow] Feature text is empty.");
            setCanStartSession?.Invoke(false);
            yield break;
        }

        bool success = false;
        yield return SendFeatureGenerate(
            roomId,
            userId,
            featureText,
            ok => success = ok);

        setCanStartSession?.Invoke(success);
    }

    private IEnumerator SendFeatureGenerate(
        string roomId,
        string userId,
        string featureText,
        Action<bool> onComplete)
    {
        string url = BuildApiUrl("features/generate");
        string body = JsonUtility.ToJson(new FeatureGenerateRequestBody
        {
            room_id = roomId,
            user_id = userId,
            job_id = Guid.NewGuid().ToString(),
            feature_text = featureText,
        });

        using (UnityWebRequest request =
               new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.timeout = Mathf.Max(1, featureRequestTimeoutSeconds);
            request.uploadHandler =
                new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[LobbyCreateRequirementFlow] POST {url} body={body}");
            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                Debug.LogError(
                    "[LobbyCreateRequirementFlow] Feature generate failed: " +
                    $"{request.error} (code={request.responseCode})\n" +
                    request.downloadHandler.text);
                onComplete?.Invoke(false);
                yield break;
            }

            Debug.Log(
                "[LobbyCreateRequirementFlow] Feature generate accepted: " +
                request.downloadHandler.text);
            onComplete?.Invoke(true);
        }
    }

    private string GetFeatureText()
    {
        if (!string.IsNullOrWhiteSpace(pendingFeatureText))
            return pendingFeatureText.Trim();

        if (featureTextInput != null &&
            !string.IsNullOrWhiteSpace(featureTextInput.text))
        {
            return featureTextInput.text.Trim();
        }

        return string.IsNullOrWhiteSpace(testFeatureText)
            ? string.Empty
            : testFeatureText.Trim();
    }

    private string BuildApiUrl(string path)
    {
        string host = string.IsNullOrWhiteSpace(apiHost)
            ? "localhost:8000"
            : apiHost.Trim();
        host = host.TrimEnd('/');

        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{host}/api/{path}";
        }

        return $"http://{host}/api/{path}";
    }

    private IEnumerator WaitForSpeechRequirement(Action<bool> setPassed)
    {
        setPassed(false);
        ResolveReferences();

        bool canReadRecorder = CanReadRecorderLevel();
        if (!canReadRecorder)
        {
            if (!useMicrophoneFallback || !StartMicrophoneFallback())
                yield break;

            float warmupStartedAt = Time.unscaledTime;
            while (Microphone.GetPosition(microphoneDevice) <= 0)
            {
                if (Time.unscaledTime - warmupStartedAt >=
                    microphoneWarmupTimeoutSeconds)
                {
                    yield break;
                }

                yield return null;
            }
        }

        float checkStartedAt = Time.unscaledTime;
        float lastFrameAt = Time.unscaledTime;
        float spokenSeconds = 0f;

        while (Time.unscaledTime - checkStartedAt < speechTimeoutSeconds)
        {
            float now = Time.unscaledTime;
            float deltaTime = Mathf.Max(0f, now - lastFrameAt);
            lastFrameAt = now;

            if (IsSpeechDetected())
            {
                spokenSeconds += deltaTime;
                if (spokenSeconds >= requiredSpeechSeconds)
                {
                    setPassed(true);
                    yield break;
                }
            }
            else if (resetSpeechProgressOnSilence)
            {
                spokenSeconds = 0f;
            }

            yield return null;
        }
    }

    private void ResolveReferences()
    {
        if (autoFindNetworkManager && networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();

        if (autoFindVoiceRecorder && voiceRecorder == null)
            voiceRecorder = FindFirstObjectByType<Recorder>();

        ResolveServerInfoObjects();
    }

    private void ResolveServerInfoObjects()
    {
        if (!autoFindServerInfoObjects)
            return;

        if (serverInfoRoot == null)
        {
            Transform root = FindTransformInLoadedScenes("server_info");
            if (root != null)
                serverInfoRoot = root.gameObject;
        }

        if (serverInfoRoot == null)
            return;

        if (serverConnectedObject == null)
            serverConnectedObject = FindDirectChildByName(serverInfoRoot, "green", "connected");
        if (serverConnectingObject == null)
            serverConnectingObject = FindDirectChildByName(serverInfoRoot, "yellow", "connecting");
        if (serverDisconnectedObject == null)
            serverDisconnectedObject = FindDirectChildByName(serverInfoRoot, "red", "disconnected", "failed");

        if (serverConnectedObject == null && serverInfoRoot.transform.childCount > 0)
            serverConnectedObject = serverInfoRoot.transform.GetChild(0).gameObject;
        if (serverConnectingObject == null && serverInfoRoot.transform.childCount > 1)
            serverConnectingObject = serverInfoRoot.transform.GetChild(1).gameObject;
        if (serverDisconnectedObject == null && serverInfoRoot.transform.childCount > 2)
            serverDisconnectedObject = serverInfoRoot.transform.GetChild(2).gameObject;
    }

    private void RefreshServerInfoObjects(bool force)
    {
        if (force)
            ResolveServerInfoObjects();

        if (!force && Time.unscaledTime < nextServerInfoRefreshAt)
            return;

        nextServerInfoRefreshAt =
            Time.unscaledTime + Mathf.Max(0.05f, serverInfoRefreshSeconds);

        ServerInfoState state = GetServerInfoState();
        if (!force && state == currentServerInfoState)
            return;

        currentServerInfoState = state;
        SetOnlyServerInfoObjectActive(state);
    }

    private ServerInfoState GetServerInfoState()
    {
        if (networkManager != null && networkManager.networkStatusText != null)
        {
            string status = networkManager.networkStatusText.text;
            if (!string.IsNullOrWhiteSpace(status))
            {
                status = status.ToLowerInvariant();

                if (status.Contains("failed") ||
                    status.Contains("disconnected") ||
                    status.Contains("shutdown"))
                {
                    return ServerInfoState.Disconnected;
                }

                if (status.Contains("reconnecting") ||
                    status.Contains("connecting"))
                {
                    return ServerInfoState.Connecting;
                }

                if (status.Contains("connected"))
                    return ServerInfoState.Connected;
            }
        }

        var runner = NetworkManager.runnerInsatance;
        if (runner == null)
            return ServerInfoState.Connecting;

        if (runner.LobbyInfo.IsValid || runner.IsCloudReady)
            return ServerInfoState.Connected;

        return ServerInfoState.Connecting;
    }

    private void SetOnlyServerInfoObjectActive(ServerInfoState state)
    {
        SetActiveIfNeeded(
            serverConnectedObject,
            state == ServerInfoState.Connected);
        SetActiveIfNeeded(
            serverConnectingObject,
            state == ServerInfoState.Connecting ||
            state == ServerInfoState.Unknown);
        SetActiveIfNeeded(
            serverDisconnectedObject,
            state == ServerInfoState.Disconnected);
    }

    private static void SetActiveIfNeeded(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static GameObject FindDirectChildByName(
        GameObject root,
        params string[] names)
    {
        if (root == null)
            return null;

        Transform rootTransform = root.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
        {
            Transform child = rootTransform.GetChild(i);
            string childName = child.name.ToLowerInvariant();

            for (int j = 0; j < names.Length; j++)
            {
                if (childName.Contains(names[j]))
                    return child.gameObject;
            }
        }

        return null;
    }

    private static Transform FindTransformInLoadedScenes(string targetName)
    {
        for (int sceneIndex = 0;
             sceneIndex < SceneManager.sceneCount;
             sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform result =
                    FindTransformRecursive(roots[i].transform, targetName);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static Transform FindTransformRecursive(
        Transform root,
        string targetName)
    {
        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result =
                FindTransformRecursive(root.GetChild(i), targetName);
            if (result != null)
                return result;
        }

        return null;
    }

    private void SetCreateSessionPanelsActive(bool active)
    {
        if (createSessionPanelsToClose == null)
            return;

        for (int i = 0; i < createSessionPanelsToClose.Length; i++)
        {
            if (createSessionPanelsToClose[i] != null)
                createSessionPanelsToClose[i].SetActive(active);
        }
    }

    private void HideError()
    {
        if (errorMessageObject != null)
            errorMessageObject.SetActive(false);
    }

    private void SetStartButtonInteractable(bool interactable)
    {
        if (startButton != null)
            startButton.interactable = interactable;
    }

    private void CancelRequirementCheck()
    {
        if (requirementRoutine != null)
        {
            StopCoroutine(requirementRoutine);
            requirementRoutine = null;
        }

        StopMicrophoneFallback();
    }

    private bool CanReadRecorderLevel()
    {
        return voiceRecorder != null && voiceRecorder.LevelMeter != null;
    }

    private bool IsSpeechDetected()
    {
        if (CanReadRecorderLevel())
            return voiceRecorder.LevelMeter.CurrentAvgAmp >= recorderSpeechThreshold;

        return ReadMicrophoneLevel() >= microphoneSpeechThreshold;
    }

    private bool StartMicrophoneFallback()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
            return false;

        microphoneDevice = Microphone.devices[0];
        int bufferSeconds = Mathf.Max(1, microphoneBufferSeconds);
        int sampleRate = Mathf.Max(8000, microphoneSampleRate);

        try
        {
            microphoneClip = Microphone.Start(
                microphoneDevice,
                true,
                bufferSeconds,
                sampleRate);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            microphoneClip = null;
        }

        usingMicrophoneFallback = microphoneClip != null;
        return usingMicrophoneFallback;
    }

    private void StopMicrophoneFallback()
    {
        if (!usingMicrophoneFallback)
            return;

        Microphone.End(microphoneDevice);
        microphoneClip = null;
        microphoneDevice = null;
        usingMicrophoneFallback = false;
    }

    private float ReadMicrophoneLevel()
    {
        if (microphoneClip == null)
            return 0f;

        int micPosition = Microphone.GetPosition(microphoneDevice);
        if (micPosition <= 0)
            return 0f;

        int sampleCount = Mathf.Min(
            Mathf.Max(1, microphoneSampleWindow),
            microphoneClip.samples);
        if (microphoneSamples == null || microphoneSamples.Length != sampleCount)
            microphoneSamples = new float[sampleCount];

        int startPosition = Mathf.Max(0, micPosition - sampleCount);
        microphoneClip.GetData(microphoneSamples, startPosition);

        double sum = 0d;
        for (int i = 0; i < sampleCount; i++)
            sum += microphoneSamples[i] * microphoneSamples[i];

        return Mathf.Sqrt((float)(sum / sampleCount));
    }

    private void ResetLoadingAnimation()
    {
        loadingFrameIndex = 0;
        nextLoadingFrameAt = 0f;
        ApplyLoadingFrame(loadingFrameIndex);
    }

    private void TickLoadingAnimation()
    {
        if (loadingImage == null ||
            loadingFrames == null ||
            loadingFrames.Length == 0)
        {
            return;
        }

        float interval = Mathf.Max(0.02f, loadingFrameSeconds);
        if (Time.unscaledTime < nextLoadingFrameAt)
            return;

        loadingFrameIndex = (loadingFrameIndex + 1) % loadingFrames.Length;
        ApplyLoadingFrame(loadingFrameIndex);
        nextLoadingFrameAt = Time.unscaledTime + interval;
    }

    private void ApplyLoadingFrame(int index)
    {
        if (loadingImage == null ||
            loadingFrames == null ||
            loadingFrames.Length == 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(index, 0, loadingFrames.Length - 1);
        Sprite frame = loadingFrames[safeIndex];
        if (frame != null)
            loadingImage.sprite = frame;
    }

    [Serializable]
    private class FeatureGenerateRequestBody
    {
        public string room_id;
        public string user_id;
        public string job_id;
        public string feature_text;
    }
}
