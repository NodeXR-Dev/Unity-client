using System;
using System.Collections;
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
    [Tooltip("발화를 서버로 올려 에이전트 분석을 받는다. 비우면 런타임에 찾는다.")]
    [SerializeField] private GraphSyncClient _syncClient;

    [Tooltip("한 문장이 끝나도 계속 듣는다. 마이크 버튼을 다시 누를 때까지 이어진다. " +
             "끄면 예전처럼 한 번 누를 때 한 문장만 받는다.")]
    [SerializeField] private bool _continuousListening = true;

    // 사용자가 켜 둔 상태인가. 문장이 끝나 IsListening 이 false 가 되어도 이 값은 유지되며,
    // 마이크 버튼을 다시 눌러 끄거나 오류가 나야 false 가 된다.
    private bool _keepListening;

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
            _keepListening = false;   // 사용자가 직접 끈 것 — 연속 모드도 함께 끝낸다
            StopListening();
            Report("음성 입력을 멈췄어요.", MvpStudentUiFactory.Amber);
            return;
        }

        // 여기서부터는 사용자가 켠 것이다. 연속 모드면 한 문장이 끝나도 계속 듣는다.
        _keepListening = _continuousListening;

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

    private bool StartOnDeviceListening()
    {
        if (_onDevice.StartListening())
        {
            IsListening = true;
            // 연속 모드가 실제로 이어지는지는 화면만 봐서는 알 수 없다.
            // 듣기 시작/문장 확정을 남겨 두면 로그만으로 흐름을 따라갈 수 있다.
            Debug.Log(
                "[MVP Voice] 듣기 시작(연속=" + _keepListening + ")");
            Report(
                _keepListening
                    ? "듣고 있어요… 계속 말해도 돼요."
                    : "듣고 있어요… 한 문장으로 말해 주세요.",
                MvpStudentUiFactory.HoloCyan);
            return true;
        }

        Report(
            "마이크를 시작하지 못했어요. 마이크 연결을 확인해 주세요.",
            MvpStudentUiFactory.Coral);
        return false;
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
        Debug.Log("[MVP Voice] 문장 확정: " + text);
        SubmitTranscription(text);

        // 연속 모드: 한 문장을 넘긴 뒤 곧바로 다시 듣는다.
        // 마이크 버튼을 매 문장마다 누르지 않아도 대화하듯 이어 말할 수 있다.
        if (_keepListening)
            StartCoroutine(RestartListeningNextFrame());
    }

    // 인식기가 앞 문장을 정리할 틈을 준 뒤 다시 시작한다.
    //
    // 한 번만 시도하면 안 된다. 직전 워커 스레드가 스트림을 아직 붙잡고 있으면
    // StartListening 이 false 를 돌려주는데, 그대로 포기하면 연속 모드가 조용히 끝나
    // "왜 새 발화를 못 알아듣지?" 가 된다. 될 때까지 잠깐 동안 다시 시도한다.
    private IEnumerator RestartListeningNextFrame()
    {
        const float retryWindowSeconds = 3f;
        const float retryIntervalSeconds = 0.15f;

        yield return null;

        float deadline = Time.unscaledTime + retryWindowSeconds;
        int attempts = 0;

        while (_keepListening && !IsListening && Time.unscaledTime < deadline)
        {
            attempts++;
            if (_onDevice != null && StartOnDeviceListening())
            {
                if (attempts > 1)
                    Debug.Log(
                        $"[MVP Voice] 연속 듣기 재시작 성공(시도 {attempts}회)");
                yield break;
            }

            yield return new WaitForSecondsRealtime(retryIntervalSeconds);
        }

        if (_keepListening && !IsListening)
        {
            _keepListening = false;   // 더 시도해도 안 되면 접는다(무한 반복 방지)
            Debug.LogWarning(
                $"[MVP Voice] 연속 듣기 재시작 실패({attempts}회 시도) — 마이크 버튼을 다시 눌러 주세요.");
            Report(
                "음성 입력이 멈췄어요. 마이크를 다시 눌러 주세요.",
                MvpStudentUiFactory.Amber);
        }
    }

    // 시연용 대본 발화를 실제 인식 결과와 같은 경로로 흘려보낸다.
    //   createNode=false 면 서버 에이전트 트리거만 하고 노드는 만들지 않는다.
    //   에이전트(제약 위반·근거 복기)는 발화 텍스트만 필요하고, 시연에서는 노드를
    //   대본대로 배치하고 싶지 발화마다 하나씩 생기는 건 방해가 된다.
    public void SubmitText(string text, bool createNode = true) =>
        SubmitTranscription(text, createNode);

    private void SubmitTranscription(string transcription, bool createNode = true)
    {
        string text = (transcription ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            Report("말을 듣지 못했어요. 다시 시도해 주세요.", MvpStudentUiFactory.Coral);
            return;
        }

        // 인식된 발화는 노드 생성과 별개로 서버에 올린다.
        //
        // 서버가 이걸 받아 토픽 분류·제약 위반 감지·결정 근거 복기를 돌리고 AGENT_GUIDE 로 답한다.
        // "우리가 모터를 쓰지 않기로 한 이유가 뭐였지?" 처럼 노드를 만들지 않는 질문도 트리거이므로,
        // 노드 생성 경로에 얹지 않고 여기서 먼저 보낸다(아래에서 실패해도 에이전트는 동작해야 한다).
        if (_syncClient == null)
            _syncClient = FindFirstObjectByType<GraphSyncClient>(
                FindObjectsInactive.Include);
        if (_syncClient != null)
            _syncClient.SendUtterance(text);

        if (!createNode)
        {
            Report("“" + text + "”", MvpStudentUiFactory.Mint);
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

        if (_keepListening)
            StartCoroutine(RestartMetaNextFrame());
    }

    private IEnumerator RestartMetaNextFrame()
    {
        yield return null;

        if (!_keepListening || IsListening)
            yield break;

        BeginPlatformDictation();
    }

    private void HandleMetaError(string error, string message)
    {
        IsListening = false;
        // 오류가 났는데 계속 되살리면 실패를 무한 반복한다.
        _keepListening = false;
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
        _keepListening = false;   // 꺼진 뒤 코루틴이 되살리지 않게
        StopListening();
    }

    private void Report(string message, Color color)
    {
        OnStatusChanged?.Invoke(message, color);
    }
}
