using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && META_VSDK_PLATFORM_INTEGRATION
using Oculus.Voice.Dictation;
#endif

// MVP 전용 음성 요구사항 입력.
// 인식된 문장은 GraphManager.Request* 경로로 PROPERTY root를 만든 뒤 발화로 제출한다.
public class MvpVoiceRequirementController : MonoBehaviour
{

    [SerializeField] private MvpWorkspaceLayout _workspaceLayout;
    [SerializeField] private GraphManager _graphManager;

    public event Action<string, Color> OnStatusChanged;
    public bool IsListening { get; private set; }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private DictationRecognizer _windowsDictation;
    private bool _receivedFinalText;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && META_VSDK_PLATFORM_INTEGRATION
    private AppDictationExperience _metaDictation;
#endif

    public void Configure(GraphManager graphManager)
    {
        _graphManager = graphManager;
    }

    public void ToggleListening()
    {
        if (IsListening)
        {
            StopListening();
            Report("음성 입력을 멈췄어요.", MvpStudentUiFactory.Amber);
            return;
        }

        // 1순위: sherpa-onnx 온디바이스 한국어 인식 (서버·OS 언어팩 불필요).
        if (MvpOnDeviceDictation.IsSupported())
        {
            BeginOnDeviceDictation();
            return;
        }

        BeginPlatformDictation();
    }

    // 온디바이스가 없거나 실패했을 때의 플랫폼 내장 경로.
    private void BeginPlatformDictation()
    {
#if UNITY_ANDROID && !UNITY_EDITOR && META_VSDK_PLATFORM_INTEGRATION
        BeginMetaDictation();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        BeginWindowsDictation();
#else
        Report(
            "이 기기의 음성 인식 설정이 아직 필요해요. 노드 입력칸을 사용해 주세요.",
            MvpStudentUiFactory.Amber);
#endif
    }

    // ─────────────────────────────────────────────
    // 온디바이스(sherpa-onnx) 경로
    // ─────────────────────────────────────────────
    private MvpOnDeviceDictation _onDevice;

    private bool _preparingOnDevice;

    // 버튼 UI(플로우)가 상태 점·라벨을 그리는 데 쓴다.
    public bool IsPreparingVoice => _preparingOnDevice;
    public MvpOnDeviceDictation Dictation => _onDevice;

    private void BeginOnDeviceDictation()
    {
        if (_preparingOnDevice)
            return;

        if (_onDevice == null)
        {
            _onDevice = GetComponent<MvpOnDeviceDictation>();
            if (_onDevice == null)
                _onDevice = gameObject.AddComponent<MvpOnDeviceDictation>();
            _onDevice.OnPartial += HandleOnDevicePartial;
            _onDevice.OnFinal += HandleOnDeviceFinal;
        }

        if (_onDevice.IsPrepared)
        {
            StartOnDeviceListening();
            return;
        }

        // 최초 1회: (Quest) 마이크 권한 + 모델 추출 + 인식기 초기화.
        _preparingOnDevice = true;
        Report("음성 인식 준비 중…", MvpStudentUiFactory.HoloCyan);
        _onDevice.Prepare(
            status => Report(status, MvpStudentUiFactory.HoloCyan),
            (ok, failReason) =>
            {
                _preparingOnDevice = false;
                if (ok)
                {
                    StartOnDeviceListening();
                    return;
                }

                Report(
                    failReason ?? "음성 인식을 준비하지 못했어요.",
                    MvpStudentUiFactory.Amber);
                // 온디바이스 불가 → 플랫폼 내장 경로로 폴백(있으면).
                if (!MvpOnDeviceDictation.IsSupported())
                    BeginPlatformDictation();
            });
    }

    private void StartOnDeviceListening()
    {
        if (_onDevice.StartListening())
        {
            IsListening = true;
            Report("듣고 있어요… 한 문장으로 말해 주세요.", MvpStudentUiFactory.HoloCyan);
        }
        else
        {
            Report(
                "마이크를 시작하지 못했어요. 마이크 연결을 확인해 주세요.",
                MvpStudentUiFactory.Coral);
        }
    }

    private void HandleOnDevicePartial(string text)
    {
        Report(
            MvpStudentWorkspaceGuide.LiveMarker + " 듣는 중 · " + text,
            MvpStudentUiFactory.HoloCyan);
    }

    private void HandleOnDeviceFinal(string text)
    {
        IsListening = false;
        SubmitTranscription(text);
    }

    private void SubmitTranscription(string transcription)
    {
        string text = (transcription ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            Report("말을 듣지 못했어요. 다시 시도해 주세요.", MvpStudentUiFactory.Coral);
            return;
        }

        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
        if (_graphManager == null)
        {
            Report("그래프를 찾지 못했어요.", MvpStudentUiFactory.Coral);
            return;
        }

        string nodeId = _graphManager.RequestCreateRootPropertyNode();
        if (string.IsNullOrEmpty(nodeId))
        {
            Report("아이디어 노드를 만들지 못했어요.", MvpStudentUiFactory.Coral);
            return;
        }

        if (_workspaceLayout == null)
            _workspaceLayout =
                FindFirstObjectByType<MvpWorkspaceLayout>();
        if (_workspaceLayout != null)
            _graphManager.RequestMoveNode(
                nodeId,
                _workspaceLayout.GetSuggestedRootPosition());

        _graphManager.RequestNodeByUtterance(nodeId, text);
        Report("“" + text + "” 아이디어를 만들었어요.", MvpStudentUiFactory.Mint);
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private void BeginWindowsDictation()
    {
        try
        {
            DisposeWindowsDictation();
            _receivedFinalText = false;
            _windowsDictation = new DictationRecognizer();
            _windowsDictation.DictationHypothesis += HandleWindowsHypothesis;
            _windowsDictation.DictationResult += HandleWindowsResult;
            _windowsDictation.DictationComplete += HandleWindowsComplete;
            _windowsDictation.DictationError += HandleWindowsError;
            _windowsDictation.Start();
            IsListening = true;
            Report("듣고 있어요… 한 문장으로 말해 주세요.", MvpStudentUiFactory.HoloCyan);
        }
        catch (Exception exception)
        {
            IsListening = false;
            DisposeWindowsDictation();
            Debug.LogWarning("[MVP Voice] Windows 음성 인식 시작 실패: " + exception.Message);
            Report(
                "Windows 음성 인식을 시작하지 못했어요. 마이크와 음성 언어를 확인해 주세요.",
                MvpStudentUiFactory.Coral);
        }
    }

    private void HandleWindowsHypothesis(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            Report("듣는 중 · " + text, MvpStudentUiFactory.HoloCyan);
    }

    private void HandleWindowsResult(string text, ConfidenceLevel confidence)
    {
        _receivedFinalText = true;
        StopListening();
        SubmitTranscription(text);
    }

    private void HandleWindowsComplete(DictationCompletionCause cause)
    {
        IsListening = false;
        if (!_receivedFinalText && cause != DictationCompletionCause.Complete)
            Report("음성 입력이 끝났어요. 다시 눌러 말해 주세요.", MvpStudentUiFactory.Amber);
    }

    private void HandleWindowsError(string error, int hresult)
    {
        IsListening = false;
        Debug.LogWarning("[MVP Voice] Windows 음성 인식 오류: " + error + " (" + hresult + ")");
        Report("음성 인식 오류가 났어요. 마이크 설정을 확인해 주세요.", MvpStudentUiFactory.Coral);
        DisposeWindowsDictation();
    }

    private void DisposeWindowsDictation()
    {
        if (_windowsDictation == null)
            return;

        if (_windowsDictation.Status == SpeechSystemStatus.Running)
            _windowsDictation.Stop();
        _windowsDictation.DictationHypothesis -= HandleWindowsHypothesis;
        _windowsDictation.DictationResult -= HandleWindowsResult;
        _windowsDictation.DictationComplete -= HandleWindowsComplete;
        _windowsDictation.DictationError -= HandleWindowsError;
        _windowsDictation.Dispose();
        _windowsDictation = null;
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && META_VSDK_PLATFORM_INTEGRATION
    private void BeginMetaDictation()
    {
        if (_metaDictation == null)
        {
            _metaDictation = GetComponent<AppDictationExperience>();
            if (_metaDictation == null)
                _metaDictation = gameObject.AddComponent<AppDictationExperience>();

            _metaDictation.UsePlatformIntegrations = true;
            _metaDictation.DoNotFallbackToWit = true;
            _metaDictation.DictationEvents.OnPartialTranscription.AddListener(HandleMetaPartial);
            _metaDictation.DictationEvents.OnFullTranscription.AddListener(HandleMetaFull);
            _metaDictation.DictationEvents.OnError.AddListener(HandleMetaError);
        }

        IsListening = true;
        Report("듣고 있어요… 한 문장으로 말해 주세요.", MvpStudentUiFactory.HoloCyan);
        _metaDictation.Activate();
    }

    private void HandleMetaPartial(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            Report("듣는 중 · " + text, MvpStudentUiFactory.HoloCyan);
    }

    private void HandleMetaFull(string text)
    {
        StopListening();
        SubmitTranscription(text);
    }

    private void HandleMetaError(string error, string message)
    {
        IsListening = false;
        Debug.LogWarning("[MVP Voice] Meta 음성 인식 오류: " + error + " / " + message);
        Report(
            "Quest 음성 인식을 사용할 수 없어요. Meta Voice 설정을 확인해 주세요.",
            MvpStudentUiFactory.Coral);
    }
#endif

    private void StopListening()
    {
        // 온디바이스 인식 중이면 지금까지 들은 문장을 확정한다(OnFinal → 노드 생성).
        if (_onDevice != null && _onDevice.IsListening)
            _onDevice.StopListening();
#if UNITY_ANDROID && !UNITY_EDITOR && META_VSDK_PLATFORM_INTEGRATION
        if (_metaDictation != null)
            _metaDictation.Deactivate();
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DisposeWindowsDictation();
#endif
        IsListening = false;
    }

    private void OnDisable()
    {
        StopListening();
    }

    private void Report(string message, Color color)
    {
        OnStatusChanged?.Invoke(message, color);
    }
}
