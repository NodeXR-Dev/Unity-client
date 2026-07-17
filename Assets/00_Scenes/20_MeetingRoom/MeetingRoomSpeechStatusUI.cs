using System;
using System.Collections;
using System.Reflection;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MeetingRoomSpeechStatusUI : MonoBehaviour
{
    private const string LoadingKey = "meetingroom-stt";
    private const string AgentMissingAlertKey = "meetingroom-stt-agent-missing";
    private const string ProviderMissingAlertKey = "meetingroom-stt-provider-missing";
    private const string ApiKeyMissingAlertKey = "meetingroom-stt-api-key-missing";

    private enum SpeechUiState
    {
        Idle,
        Listening,
        Speaking,
        Processing,
        Result,
        Error
    }

    [SerializeField] private SpeechToTextAgent speechToTextAgent;
    [SerializeField] private bool autoFindAgent = true;
    [SerializeField] private bool autoStartListening = true;
    [SerializeField] private bool restartAfterResult = true;
    [SerializeField] private float resultVisibleSeconds = 3f;
    [SerializeField] private float startRetrySeconds = 1f;
    [SerializeField] private Vector2Int canvasSize = new Vector2Int(620, 190);
    [SerializeField] private float widthInMeters = 1.05f;
    [SerializeField] private float distanceFromCamera = 1.9f;
    [SerializeField] private float horizontalOffset = -0.55f;
    [SerializeField] private float verticalOffset = 0.74f;
    [SerializeField] private Vector3 fallbackWorldPosition = new Vector3(5.35f, 1.55f, 1.9f);
    [SerializeField] private Vector3 fallbackWorldEulerAngles = Vector3.zero;

    private FieldInfo listeningField;
    private FieldInfo captureField;
    private FieldInfo speechTaskField;
    private FieldInfo providerAssetField;
    private FieldInfo envelopeField;

    private Canvas canvas;
    private Image panelImage;
    private Image statusDot;
    private Image levelFill;
    private Text statusText;
    private Text detailText;
    private Text transcriptText;

    private SpeechToTextAgent subscribedAgent;
    private SpeechUiState currentState = SpeechUiState.Idle;
    private bool heardSpeech;
    private bool lastListening;
    private bool waitingForTranscript;
    private float nextStartAttemptAt;
    private float resultShownAt = -1f;
    private string lastTranscript = "";

    private void Awake()
    {
        CacheAgentReflection();
        CreateStatusCanvas();
        SetState(SpeechUiState.Idle, "대기 중", "음성 인식을 준비하고 있습니다.", "");
    }

    private void OnEnable()
    {
        ResolveAgent();
        SubscribeToAgent();
    }

    private void OnDisable()
    {
        UnsubscribeFromAgent();
        MeetingRoomCommonUI.HideLoading(LoadingKey);
    }

    private void Update()
    {
        ResolveAgent();
        SubscribeToAgent();
        UpdateCanvasPose();

        if (speechToTextAgent == null)
        {
            SetState(SpeechUiState.Error, "STT 없음", "Speech To Text 오브젝트를 찾지 못했습니다.", lastTranscript);
            MeetingRoomCommonUI.AlertOnce(AgentMissingAlertKey, "STT 없음", "Speech To Text 오브젝트를 찾지 못했습니다.");
            return;
        }

        if (!IsSpeechTaskReady())
        {
            SetState(SpeechUiState.Error, "STT 설정 필요", "Provider Asset을 연결해야 음성 인식이 시작됩니다.", lastTranscript);
            MeetingRoomCommonUI.AlertOnce(
                ProviderMissingAlertKey,
                "STT 설정 필요",
                "SpeechToTextAgent의 Provider Asset을 ElevenLabs 또는 OpenAI Provider Profile로 연결해야 합니다."
            );
            return;
        }

        if (!IsProviderApiKeyConfigured())
        {
            SetState(SpeechUiState.Error, "API 키 필요", "Provider Asset에 STT API 키를 입력해야 합니다.", lastTranscript);
            MeetingRoomCommonUI.AlertOnce(
                ApiKeyMissingAlertKey,
                "API 키 필요",
                "SpeechToText_ElevenLabs_ProviderProfile.asset에서 Override Api Key를 켜고 API Key를 입력하세요."
            );
            return;
        }

        ResetSetupAlerts();

        var listening = IsAgentListening();
        var captureCount = GetCaptureCount();

        if (listening)
        {
            waitingForTranscript = false;
            if (captureCount > 0)
            {
                heardSpeech = true;
                SetState(SpeechUiState.Speaking, "발화 중", "말을 감지하고 있습니다.", lastTranscript);
            }
            else
            {
                SetState(SpeechUiState.Listening, "듣는 중", "말을 시작하면 자동으로 인식합니다.", lastTranscript);
            }
        }
        else if (lastListening && heardSpeech)
        {
            waitingForTranscript = true;
            SetState(SpeechUiState.Processing, "처리 중", "음성을 텍스트로 변환하고 있습니다.", lastTranscript);
        }
        else if (waitingForTranscript)
        {
            SetState(SpeechUiState.Processing, "처리 중", "음성을 텍스트로 변환하고 있습니다.", lastTranscript);
        }
        else if (currentState != SpeechUiState.Result)
        {
            SetState(SpeechUiState.Idle, "대기 중", "음성 인식을 준비하고 있습니다.", lastTranscript);
        }

        if (autoStartListening)
        {
            TryStartOrRestartListening(listening);
        }

        UpdateLevelVisual();
        lastListening = listening;
    }

    public void ShowListening()
    {
        waitingForTranscript = false;
        heardSpeech = false;
        SetState(SpeechUiState.Listening, "듣는 중", "말을 시작하면 자동으로 인식합니다.", lastTranscript);
    }

    public void ShowSpeaking()
    {
        heardSpeech = true;
        SetState(SpeechUiState.Speaking, "발화 중", "말을 감지하고 있습니다.", lastTranscript);
    }

    public void ShowProcessing()
    {
        waitingForTranscript = true;
        SetState(SpeechUiState.Processing, "처리 중", "음성을 텍스트로 변환하고 있습니다.", lastTranscript);
    }

    public void ShowResult(string transcript)
    {
        OnTranscriptReceived(transcript);
    }

    public void ShowError(string message)
    {
        SetState(SpeechUiState.Error, "오류", message, lastTranscript);
    }

    private void ResolveAgent()
    {
        if (speechToTextAgent != null || !autoFindAgent)
        {
            return;
        }

        speechToTextAgent = FindFirstObjectByType<SpeechToTextAgent>();
    }

    private void SubscribeToAgent()
    {
        if (speechToTextAgent == null || subscribedAgent == speechToTextAgent)
        {
            return;
        }

        UnsubscribeFromAgent();
        subscribedAgent = speechToTextAgent;
        subscribedAgent.onTranscript.AddListener(OnTranscriptReceived);
    }

    private void UnsubscribeFromAgent()
    {
        if (subscribedAgent == null)
        {
            return;
        }

        subscribedAgent.onTranscript.RemoveListener(OnTranscriptReceived);
        subscribedAgent = null;
    }

    private void TryStartOrRestartListening(bool listening)
    {
        if (listening || waitingForTranscript || Time.time < nextStartAttemptAt)
        {
            return;
        }

        if (currentState == SpeechUiState.Result && restartAfterResult)
        {
            if (Time.time - resultShownAt < resultVisibleSeconds)
            {
                return;
            }
        }
        else if (currentState == SpeechUiState.Result)
        {
            return;
        }

        heardSpeech = false;
        waitingForTranscript = false;
        nextStartAttemptAt = Time.time + Mathf.Max(0.1f, startRetrySeconds);
        speechToTextAgent.StartListening();
        SetState(SpeechUiState.Listening, "듣는 중", "말을 시작하면 자동으로 인식합니다.", lastTranscript);
    }

    private void OnTranscriptReceived(string transcript)
    {
        heardSpeech = false;
        waitingForTranscript = false;
        lastTranscript = string.IsNullOrWhiteSpace(transcript) ? "(인식된 텍스트 없음)" : transcript.Trim();
        resultShownAt = Time.time;
        SetState(SpeechUiState.Result, "결과", "음성 인식이 완료되었습니다.", lastTranscript);
    }

    private void CacheAgentReflection()
    {
        var agentType = typeof(SpeechToTextAgent);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        listeningField = agentType.GetField("_listening", flags);
        captureField = agentType.GetField("_capture", flags);
        speechTaskField = agentType.GetField("_stt", flags);
        providerAssetField = agentType.GetField("providerAsset", flags);
        envelopeField = agentType.GetField("_env", flags);
    }

    private bool IsSpeechTaskReady()
    {
        if (speechTaskField == null || speechToTextAgent == null)
        {
            return true;
        }

        return speechTaskField.GetValue(speechToTextAgent) != null;
    }

    private bool IsProviderApiKeyConfigured()
    {
        if (providerAssetField == null || speechToTextAgent == null)
        {
            return true;
        }

        var provider = providerAssetField.GetValue(speechToTextAgent);
        if (provider == null)
        {
            return false;
        }

        var apiKeyField = provider.GetType().GetField("apiKey", BindingFlags.Instance | BindingFlags.NonPublic);
        return apiKeyField == null
            || apiKeyField.GetValue(provider) is string apiKey && !string.IsNullOrWhiteSpace(apiKey);
    }

    private bool IsAgentListening()
    {
        return listeningField != null
            && speechToTextAgent != null
            && listeningField.GetValue(speechToTextAgent) is bool value
            && value;
    }

    private int GetCaptureCount()
    {
        if (captureField == null || speechToTextAgent == null)
        {
            return 0;
        }

        return captureField.GetValue(speechToTextAgent) is ICollection capture ? capture.Count : 0;
    }

    private float GetEnvelope()
    {
        if (envelopeField == null || speechToTextAgent == null)
        {
            return 0f;
        }

        return envelopeField.GetValue(speechToTextAgent) is float value ? value : 0f;
    }

    private void CreateStatusCanvas()
    {
        var canvasObject = new GameObject(
            "MeetingRoom Speech Status Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(canvasSize.x, canvasSize.y);

        var metersPerPixel = widthInMeters / Mathf.Max(1, canvasSize.x);
        canvasObject.transform.localScale = new Vector3(metersPerPixel, metersPerPixel, metersPerPixel);
        UpdateCanvasPose();

        var panel = CreateImage("Panel", canvasRect, new Color(0.05f, 0.06f, 0.075f, 0.92f));
        panelImage = panel.GetComponent<Image>();
        Stretch(panel.rectTransform, 0f, 0f, 0f, 0f);

        statusDot = CreateImage("Status Dot", panel.rectTransform, Color.gray).GetComponent<Image>();
        SetRect(statusDot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -48f), new Vector2(48f, -24f));

        statusText = CreateText("Status Text", panel.rectTransform, 26, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(0.65f, 1f), new Vector2(62f, -58f), new Vector2(-12f, -18f));

        detailText = CreateText("Detail Text", panel.rectTransform, 17, FontStyle.Normal, TextAnchor.MiddleLeft);
        detailText.color = new Color(0.74f, 0.78f, 0.84f, 1f);
        SetRect(detailText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(62f, -91f), new Vector2(-24f, -58f));

        var levelTrack = CreateImage("Level Track", panel.rectTransform, new Color(0.16f, 0.18f, 0.22f, 1f));
        SetRect(levelTrack.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -118f), new Vector2(-24f, -106f));

        levelFill = CreateImage("Level Fill", levelTrack.rectTransform, new Color(0.2f, 0.75f, 0.48f, 1f)).GetComponent<Image>();
        levelFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        levelFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        levelFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        levelFill.rectTransform.offsetMin = Vector2.zero;
        levelFill.rectTransform.offsetMax = new Vector2(0f, 0f);

        transcriptText = CreateText("Transcript Text", panel.rectTransform, 18, FontStyle.Normal, TextAnchor.UpperLeft);
        transcriptText.color = new Color(0.94f, 0.95f, 0.97f, 1f);
        SetRect(transcriptText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 18f), new Vector2(-24f, 64f));
    }

    private void UpdateCanvasPose()
    {
        if (canvas == null)
        {
            return;
        }

        var canvasTransform = canvas.transform;
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var cameraTransform = mainCamera.transform;
            var targetPosition = cameraTransform.position
                + cameraTransform.forward * distanceFromCamera
                + cameraTransform.right * horizontalOffset
                + Vector3.up * verticalOffset;

            canvasTransform.position = targetPosition;
            canvasTransform.rotation = Quaternion.LookRotation(targetPosition - cameraTransform.position, Vector3.up);
            return;
        }

        canvasTransform.position = fallbackWorldPosition;
        canvasTransform.rotation = Quaternion.Euler(fallbackWorldEulerAngles);
    }

    private void SetState(SpeechUiState state, string title, string detail, string transcript)
    {
        var previousState = currentState;
        currentState = state;

        if (state == SpeechUiState.Processing)
        {
            MeetingRoomCommonUI.ShowLoading("음성 처리 중...", LoadingKey);
        }
        else if (previousState == SpeechUiState.Processing)
        {
            MeetingRoomCommonUI.HideLoading(LoadingKey);
        }

        if (statusText != null) statusText.text = title;
        if (detailText != null) detailText.text = detail;
        if (transcriptText != null) transcriptText.text = transcript;
        if (statusDot != null) statusDot.color = GetStateColor(state);
        if (panelImage != null) panelImage.color = state == SpeechUiState.Error
            ? new Color(0.13f, 0.05f, 0.055f, 0.94f)
            : new Color(0.05f, 0.06f, 0.075f, 0.92f);
    }

    private void UpdateLevelVisual()
    {
        if (levelFill == null)
        {
            return;
        }

        var target = currentState switch
        {
            SpeechUiState.Listening => 0.12f + Mathf.PingPong(Time.time * 0.18f, 0.08f),
            SpeechUiState.Speaking => Mathf.Clamp01(GetEnvelope() * 24f),
            SpeechUiState.Processing => 0.35f + Mathf.PingPong(Time.time * 0.9f, 0.4f),
            SpeechUiState.Result => 1f,
            SpeechUiState.Error => 1f,
            _ => 0.05f
        };

        var width = Mathf.Lerp(24f, canvasSize.x - 48f, target);
        levelFill.rectTransform.offsetMax = new Vector2(width, 0f);
        levelFill.color = GetStateColor(currentState);
    }

    private static Color GetStateColor(SpeechUiState state)
    {
        return state switch
        {
            SpeechUiState.Listening => new Color(0.28f, 0.58f, 1f, 1f),
            SpeechUiState.Speaking => new Color(0.22f, 0.82f, 0.48f, 1f),
            SpeechUiState.Processing => new Color(1f, 0.72f, 0.22f, 1f),
            SpeechUiState.Result => new Color(0.52f, 0.86f, 1f, 1f),
            SpeechUiState.Error => new Color(1f, 0.3f, 0.28f, 1f),
            _ => new Color(0.55f, 0.58f, 0.64f, 1f)
        };
    }

    private static void ResetSetupAlerts()
    {
        MeetingRoomCommonUI.ResetAlertOnce(AgentMissingAlertKey);
        MeetingRoomCommonUI.ResetAlertOnce(ProviderMissingAlertKey);
        MeetingRoomCommonUI.ResetAlertOnce(ApiKeyMissingAlertKey);
    }

    private Text CreateText(string objectName, Transform parent, int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        var text = textObject.GetComponent<Text>();
        text.font = GetUIFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.color = Color.white;
        return text;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        var image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Font GetUIFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static void Stretch(RectTransform rectTransform, float left, float bottom, float right, float top)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(left, bottom);
        rectTransform.offsetMax = new Vector2(-right, -top);
    }

    private static void SetRect(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
    }
}
