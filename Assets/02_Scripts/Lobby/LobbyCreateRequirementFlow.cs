using System;
using System.Collections;
using Photon.Voice.Unity;
using UnityEngine;
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

    [Header("Loading")]
    [SerializeField] private float loadingFrameSeconds = 0.18f;

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

    private void Awake()
    {
        ResolveReferences();
        HideError();
        ApplyLoadingFrame(0);
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        CancelRequirementCheck();
    }

    private void Update()
    {
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
            networkManager.CreateCustomSession();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            ShowErrorState();
        }
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
}
