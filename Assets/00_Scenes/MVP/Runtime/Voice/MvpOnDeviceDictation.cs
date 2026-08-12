using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using SherpaOnnx;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// sherpa-onnx 한국어 스트리밍 zipformer 로 돌아가는 온디바이스 받아쓰기.
//  - 서버·인터넷·OS 언어팩 없이 동작한다 (모델: StreamingAssets/SherpaOnnx/ko-zipformer, int8 ≈ 130MB).
//  - 스트리밍이라 말하는 동안 부분 자막(OnPartial)이 나오고, 문장 끝 무음을 감지하면
//    자동으로 확정(OnFinal)한다.
//  - 현재 에디터/Windows 지원. Quest(Android)는 ① arm64 네이티브 라이브러리 반입
//    ② StreamingAssets → persistentDataPath 추출이 추가로 필요하다 (docs/voice-input-plan.md Phase 2.5).
[DisallowMultipleComponent]
public class MvpOnDeviceDictation : MonoBehaviour
{
    private const int SampleRate = 16000;

    private static readonly string[] ModelFiles =
    {
        "encoder-epoch-99-avg-1.int8.onnx",
        "decoder-epoch-99-avg-1.int8.onnx",
        "joiner-epoch-99-avg-1.int8.onnx",
        "tokens.txt"
    };

    // Android 의 StreamingAssets 는 APK 내부라 File API 로 열 수 없다 →
    // 첫 사용 시 persistentDataPath 로 추출한 뒤 그 경로를 쓴다.
    private static string ModelDir =>
#if UNITY_ANDROID && !UNITY_EDITOR
        Path.Combine(Application.persistentDataPath, "SherpaOnnx/ko-zipformer");
#else
        Path.Combine(Application.streamingAssetsPath, "SherpaOnnx/ko-zipformer");
#endif

    // ModelDir 는 Application.persistentDataPath 를 부르는데 이건 메인 스레드 전용이다.
    // 인식기 생성을 워커 스레드로 옮겼으므로(아래 PrepareRoutine 참고) 경로는
    // 메인 스레드에서 미리 확정해 둔다.
    private static string _modelDirCache;

    private static string ModelDirCached =>
        _modelDirCache ?? (_modelDirCache = ModelDir);

    // 인식기(≈수백 MB 모델 로드)는 프로세스에 하나만 — 액션바용/키보드용 컴포넌트가
    // 각자 만들면 메모리가 두 배가 된다(Quest 치명적). 스트림/마이크는 인스턴스별.
    private static OnlineRecognizer _sharedRecognizer;

    // 마이크는 한 번에 한 소비자만 — 새로 듣기 시작하면 기존 리스너를 먼저 정리한다.
    private static MvpOnDeviceDictation _activeListener;

    private OnlineStream _stream;
    private AudioClip _micClip;
    private string _micDevice;   // null = 기본 장치
    private int _micReadPos;
    private bool _listening;
    private string _lastPartial = "";

    // ── 인식 스레드 ───────────────────────────────────────────
    // Decode() 한 번이 PC 에서도 20ms 대(측정치)라 메인 스레드에서 돌리면 VR 프레임
    // 예산(72Hz = 13.9ms)을 통째로 넘겨 주기적으로 끊긴다. 그래서:
    //   메인 스레드 = 마이크 링버퍼 읽기 + 큐 적재 + 이벤트 발화 (Unity API 제약)
    //   워커 스레드 = AcceptWaveform / Decode / GetResult (네이티브 추론)
    // _stream 은 워커가 소유한다 — 시작 시 메인이 만들어 넘기고, 정리(Dispose)까지
    // 워커가 책임진다. 두 스레드가 같은 스트림을 동시에 만지는 지점은 없다.
    private readonly ConcurrentQueue<float[]> _pending =
        new ConcurrentQueue<float[]>();
    private Thread _worker;
    private volatile bool _workerRunning;
    private volatile bool _flushRequested;   // 종료 신호: 남은 것 마저 처리하고 빠져나와라
    private volatile bool _workerFinished;   // 마무리 디코딩까지 끝났다
    private volatile string _workerPartial = "";
    private volatile string _workerFinal;
    private volatile bool _workerEndpoint;   // 문장 끝 무음 감지

    // 듣기를 멈춘 뒤 워커의 마무리를 기다리는 중인지. 메인 스레드는 여기서
    // 블록하지 않고 Update 에서 폴링한다(Join 은 Quest 에서 모래시계를 부른다).
    private const float FlushTimeoutSeconds = 2f;
    private bool _awaitingFinal;
    private float _flushStartedAt;

    // 마이크 링버퍼 길이(초). 30초는 16kHz 기준 480,000 샘플이라 여는 데만도 부담이다.
    private const int MicBufferSeconds = 10;

    private static OnlineRecognizer Recognizer => _sharedRecognizer;

    public event Action<string> OnPartial;   // 듣는 중 부분 자막
    public event Action<string> OnFinal;     // 확정 문장

    public bool IsListening => _listening;

    // 최근 마이크 입력 세기(0~1, 부드럽게 감쇠) — 듣는 중 인디케이터가 목소리에 반응하는 데 쓴다.
    public float Level { get; private set; }

    // 이 기기에서 온디바이스 인식을 시도할 수 있는지.
    //  - 에디터/Windows: 모델 파일 존재 여부로 즉시 판정.
    //  - Android(Quest): 추출 전이라 미리 알 수 없으므로 시도 대상(true)으로 두고,
    //    Prepare 실패 시 호출부가 폴백한다(빌드에 모델 미포함 등).
    public static bool IsSupported()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        return !_unavailable &&
               File.Exists(Path.Combine(ModelDirCached, "tokens.txt"));
#elif UNITY_ANDROID
        return !_unavailable;
#else
        return false;
#endif
    }

    // Prepare 가 복구 불가 사유(모델 미포함 등)로 실패하면 true — 이후 재시도하지 않는다.
    private static bool _unavailable;

    public bool IsPrepared => _sharedRecognizer != null;

    // 권한 요청 → (Android) 모델 추출 → 인식기 초기화까지 비동기로 준비한다.
    //   onStatus: 진행 상황 문구(안내 칩용), onDone(성공 여부, 실패 사유)
    public void Prepare(Action<string> onStatus, Action<bool, string> onDone)
    {
        if (IsPrepared)
        {
            onDone?.Invoke(true, null);
            return;
        }
        StartCoroutine(PrepareRoutine(onStatus, onDone));
    }

    private IEnumerator PrepareRoutine(
        Action<string> onStatus, Action<bool, string> onDone)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 1) 마이크 권한
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            onStatus?.Invoke("마이크 사용을 허용해 주세요.");
            Permission.RequestUserPermission(Permission.Microphone);
            float wait = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) &&
                   wait < 30f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                onDone?.Invoke(false, "마이크 권한이 없어 음성 입력을 쓸 수 없어요.");
                yield break;
            }
        }

        // 2) 모델 추출 (APK → persistentDataPath, 최초 1회 ≈130MB)
        Directory.CreateDirectory(ModelDir);
        foreach (string file in ModelFiles)
        {
            string dst = Path.Combine(ModelDirCached, file);
            if (File.Exists(dst) && new FileInfo(dst).Length > 0)
                continue;

            onStatus?.Invoke("음성 인식 준비 중… (" + file + ")");
            string src = Path.Combine(
                Application.streamingAssetsPath, "SherpaOnnx/ko-zipformer/" + file);
            using (UnityWebRequest request = UnityWebRequest.Get(src))
            {
                request.downloadHandler =
                    new DownloadHandlerFile(dst) { removeFileOnAbort = true };
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _unavailable = true;   // 빌드에 모델 미포함 → 이후 재시도 안 함
                    onDone?.Invoke(false,
                        "음성 모델이 이 빌드에 없어요. 키보드로 입력해 주세요.");
                    yield break;
                }
            }
        }
#endif

        // 3) 인식기 초기화
        //
        // 130MB 모델을 올리는 작업이라 메인 스레드에서 하면 Quest 에서 몇 초간
        // 화면이 멈추고 시스템 모래시계가 뜬다. 워커 스레드로 넘기고 기다린다.
        // 경로(Application.persistentDataPath)는 메인 스레드 전용이라 먼저 확정한다.
        onStatus?.Invoke("음성 인식 준비 중…");
        _ = ModelDirCached;
        yield return null;   // 상태 문구가 먼저 그려지도록 한 프레임 양보

        bool finished = false;
        bool ok = false;
        var initThread = new Thread(() =>
        {
            ok = EnsureRecognizer();
            finished = true;
        })
        {
            IsBackground = true,
            Name = "MvpSttInit"
        };
        initThread.Start();

        while (!finished)
            yield return null;

        onDone?.Invoke(ok, ok ? null : "음성 인식기를 초기화하지 못했어요.");
    }

    public bool StartListening()
    {
        if (_listening)
            return true;
        if (!IsSupported() || !EnsureRecognizer())
            return false;

        // 앞 세션의 마무리 결과가 아직 안 나왔으면 먼저 거둬들인다.
        // (안 그러면 새 워커가 참조를 덮어써 그 문장이 사라진다)
        if (_awaitingFinal)
            PumpFinalResult();

        // 다른 소비자(예: 키보드 ↔ 액션바)가 듣는 중이면 그쪽을 먼저 확정·정리한다.
        if (_activeListener != null && _activeListener != this)
            _activeListener.StopListening();

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[MVP STT] 사용할 수 있는 마이크가 없습니다.");
            return false;
        }

        // 마이크는 발화마다 껐다 켜지 않는다.
        // Microphone.Start / End 는 OS 오디오 경로를 타서 메인 스레드를 수백 ms 잡는다.
        // Quest 에서 인식기를 누를 때와 끝날 때 모래시계가 뜨던 원인이 여기였다.
        // 한 번 열어 두고 읽는 위치만 옮기며, 실제 정지는 ReleaseMicrophone 에서 한다.
        if (_micClip == null || !Microphone.IsRecording(_micDevice))
        {
            _micClip = Microphone.Start(_micDevice, true, MicBufferSeconds, SampleRate);
            if (_micClip == null)
            {
                Debug.LogWarning("[MVP STT] 마이크를 시작하지 못했습니다.");
                return false;
            }
        }

        // 이번 발화는 '지금'부터 듣는다. 앞선 구간이 섞이지 않게 읽기 위치를 현재로.
        _micReadPos = Mathf.Max(0, Microphone.GetPosition(_micDevice));

        _stream = Recognizer.CreateStream();
        _lastPartial = "";

        while (_pending.TryDequeue(out _)) { }   // 이전 세션 잔여 제거
        _workerPartial = "";
        _workerFinal = null;
        _workerEndpoint = false;
        _workerFinished = false;
        _flushRequested = false;
        _workerRunning = true;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "MvpSttDecode"
        };
        _worker.Start();

        _listening = true;
        _activeListener = this;
        return true;
    }

    // 워커 스레드 본체. 큐에 쌓인 마이크 청크를 받아 디코딩하고 결과만 필드로 넘긴다.
    // 종료(_flushRequested) 시 남은 청크 + 무음 패딩까지 처리한 뒤 스트림을 정리한다.
    private void WorkerLoop()
    {
        OnlineStream stream = _stream;
        try
        {
            while (_workerRunning && !_flushRequested)
            {
                bool fed = false;
                while (_pending.TryDequeue(out float[] chunk))
                {
                    stream.AcceptWaveform(SampleRate, chunk);
                    fed = true;
                }

                if (fed)
                {
                    while (Recognizer.IsReady(stream))
                        Recognizer.Decode(stream);

                    _workerPartial =
                        (Recognizer.GetResult(stream).Text ?? "").Trim();
                    if (Recognizer.IsEndpoint(stream))
                        _workerEndpoint = true;
                }

                Thread.Sleep(fed ? 1 : 5);
            }

            // 마무리: 남은 청크 → 무음 패딩(스트리밍 모델은 마지막 어절이 잘리기 쉽다) → 끝까지 디코드
            while (_pending.TryDequeue(out float[] tail))
                stream.AcceptWaveform(SampleRate, tail);
            stream.AcceptWaveform(
                SampleRate, new float[(int)(SampleRate * 0.66f)]);
            stream.InputFinished();
            while (Recognizer.IsReady(stream))
                Recognizer.Decode(stream);

            _workerFinal = (Recognizer.GetResult(stream).Text ?? "").Trim();
            _workerFinished = true;
        }
        catch (Exception exception)
        {
            // 스레드에서 터지면 조용히 죽으므로 반드시 남긴다. 부분 결과는 살려서 넘긴다.
            Debug.LogWarning("[MVP STT] 인식 스레드 오류: " + exception.Message);
            _workerFinal = _workerPartial ?? "";
            _workerFinished = true;
        }
        finally
        {
            _workerRunning = false;
            stream?.Dispose();
        }
    }

    // 수동 종료(버튼 재탭)와 자동 종료(문장 끝 무음) 공용. 최종 텍스트를 반환하고 OnFinal 을 발화한다.
    public string StopListening()
    {
        if (!_listening)
            return "";
        _listening = false;
        if (_activeListener == this)
            _activeListener = null;

        DrainMic();   // 남은 마이크 샘플을 큐에 밀어 넣는다
        Level = 0f;

        // 마이크는 여기서 끄지 않는다(Microphone.End 가 메인 스레드를 잡는다).
        // 다음 발화를 바로 시작할 수 있고, 실제 정지는 ReleaseMicrophone 이 한다.

        // 마무리 디코딩(무음 패딩 포함)은 워커가 이어서 한다.
        //
        // 예전에는 여기서 Join(1500) 으로 기다렸는데, 그러면 말이 끝날 때마다
        // 메인 스레드가 최대 1.5초 멈춘다. Quest 에서 그때마다 모래시계가 떴다.
        // 신호만 주고 즉시 빠져나오고, 결과는 Update 가 받아 OnFinal 로 넘긴다.
        if (_worker != null)
        {
            _flushRequested = true;
            _awaitingFinal = true;
            _flushStartedAt = Time.unscaledTime;
        }

        return _workerPartial ?? "";
    }

    /// <summary>
    /// 마이크를 실제로 놓는다. 발화 사이에는 열어 두므로(재시작 비용 회피),
    /// 음성 UI 를 닫을 때 이걸 불러 준다. 안 부르면 마이크가 계속 켜져 있다.
    /// </summary>
    public void ReleaseMicrophone()
    {
        if (_listening)
            StopListening();

        if (_micClip != null || Microphone.IsRecording(_micDevice))
            Microphone.End(_micDevice);

        _micClip = null;
        _micReadPos = 0;
        Level = 0f;
    }

    // 워커가 마무리 디코딩을 끝냈는지 확인하고, 끝났으면 확정 문장을 넘긴다.
    private void PumpFinalResult()
    {
        if (_worker == null)
        {
            _awaitingFinal = false;
            return;
        }

        bool timedOut = Time.unscaledTime - _flushStartedAt > FlushTimeoutSeconds;
        if (!_workerFinished && !timedOut)
            return;

        if (timedOut && !_workerFinished)
            Debug.LogWarning(
                "[MVP STT] 인식 스레드 마무리가 늦어 부분 결과를 사용합니다.");

        string text = _workerFinished
            ? (_workerFinal ?? "")
            : (_workerPartial ?? "");

        _worker = null;
        // 스트림 정리는 워커의 finally 가 책임진다(아직 쓰는 중일 수 있다).
        _stream = null;
        _awaitingFinal = false;

        if (text.Length > 0)
            OnFinal?.Invoke(text);
    }

    private void Update()
    {
        // 듣기를 멈춘 뒤 워커의 마무리 결과를 받아 가는 단계.
        if (_awaitingFinal)
            PumpFinalResult();

        if (!_listening)
            return;

        DrainMic();   // 마이크 읽기만 메인 스레드. 디코딩은 워커가 한다.

        string partial = _workerPartial;
        if (!string.IsNullOrEmpty(partial) && partial != _lastPartial)
        {
            _lastPartial = partial;
            OnPartial?.Invoke(partial);
        }

        // 말이 끝나고 무음이 이어지면 자동 확정한다.
        if (_workerEndpoint && !string.IsNullOrEmpty(partial))
            StopListening();
    }

    // 마이크 링버퍼에서 새로 쌓인 샘플만 인식기로 보낸다(랩어라운드 처리 포함).
    private void DrainMic()
    {
        if (_micClip == null || !_workerRunning)
            return;

        int writePos = Microphone.GetPosition(_micDevice);
        if (writePos == _micReadPos || writePos < 0)
            return;

        int total = _micClip.samples;
        if (writePos > _micReadPos)
        {
            FeedRange(_micReadPos, writePos - _micReadPos);
        }
        else
        {
            FeedRange(_micReadPos, total - _micReadPos);   // 버퍼 끝까지
            if (writePos > 0)
                FeedRange(0, writePos);                    // 앞부분
        }
        _micReadPos = writePos;
    }

    private void FeedRange(int offset, int count)
    {
        if (count <= 0)
            return;
        float[] buffer = new float[count];
        _micClip.GetData(buffer, offset);
        _pending.Enqueue(buffer);   // 소유권을 워커로 넘긴다(이후 메인은 건드리지 않는다)

        // RMS 기반 입력 레벨 (상승은 즉시, 하강은 부드럽게).
        float sum = 0f;
        for (int i = 0; i < count; i++)
            sum += buffer[i] * buffer[i];
        float rms = Mathf.Clamp01(
            Mathf.Sqrt(sum / count) * 6f);
        Level = rms > Level
            ? rms
            : Mathf.Lerp(Level, rms, 8f * Time.unscaledDeltaTime);
    }

    private bool EnsureRecognizer()
    {
        if (_sharedRecognizer != null)
            return true;

        try
        {
            var config = new OnlineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder =
                Path.Combine(ModelDirCached, "encoder-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Transducer.Decoder =
                Path.Combine(ModelDirCached, "decoder-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Transducer.Joiner =
                Path.Combine(ModelDirCached, "joiner-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Tokens =
                Path.Combine(ModelDirCached, "tokens.txt");
            // Quest 는 앱에 주는 코어가 적어 디코드 스레드를 2개 쓰면 렌더 스레드와
            // 다퉈 말하는 동안 프레임이 끊긴다. 기기에서는 1개로 둔다.
#if UNITY_ANDROID && !UNITY_EDITOR
            config.ModelConfig.NumThreads = 1;
#else
            config.ModelConfig.NumThreads = 2;
#endif
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";
            config.EnableEndpoint = 1;
            config.Rule1MinTrailingSilence = 2.4f;   // 발화 전 무음
            config.Rule2MinTrailingSilence = 1.0f;   // 발화 후 이 시간 무음이면 문장 확정
            config.Rule3MinUtteranceLength = 20f;

            _sharedRecognizer = new OnlineRecognizer(config);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[MVP STT] sherpa-onnx 인식기 초기화 실패: " + exception.Message);
            _sharedRecognizer = null;
            return false;
        }
    }

    private void OnDisable()
    {
        // 키보드가 닫히거나 컴포넌트가 꺼지면 마이크를 놓는다.
        // (발화 사이에는 열어 두므로 여기서 정리해야 계속 켜져 있지 않다)
        ReleaseMicrophone();
    }

    private void OnDestroy()
    {
        if (_micClip != null || Microphone.IsRecording(_micDevice))
        {
            Microphone.End(_micDevice);
            _micClip = null;
        }
        _listening = false;
        if (_activeListener == this)
            _activeListener = null;

        // 워커를 세우고 짧게만 기다린다(에디터 정지·씬 전환에서 멈춰 보이면 안 된다).
        // 스트림 Dispose 는 워커의 finally 가 한다.
        if (_worker != null)
        {
            _flushRequested = true;
            _workerRunning = false;
            _worker.Join(300);
            _worker = null;
        }
        _stream = null;
        // 공유 인식기는 해제하지 않는다 — 다른 소비자가 계속 쓴다(프로세스 종료 시 정리).
    }

    // ─────────────────────────────────────────────
    // 검증용: 16-bit PCM WAV 파일을 통째로 인식한다 (마이크 없이 품질 확인).
    // ─────────────────────────────────────────────
    public string DecodeWavForTest(string wavPath)
    {
        if (!EnsureRecognizer())
            return null;

        byte[] bytes = File.ReadAllBytes(wavPath);
        int channels = BitConverter.ToInt16(bytes, 22);
        int sampleRate = BitConverter.ToInt32(bytes, 24);
        int bitsPerSample = BitConverter.ToInt16(bytes, 34);
        if (bitsPerSample != 16)
        {
            Debug.LogWarning("[MVP STT] 16-bit PCM WAV 만 지원합니다: " + wavPath);
            return null;
        }

        // 'data' 청크를 찾는다 (fmt 확장 등 가변 헤더 대응).
        int dataOffset = -1, dataSize = 0;
        for (int i = 12; i + 8 <= bytes.Length;)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            int chunkSize = BitConverter.ToInt32(bytes, i + 4);
            if (chunkId == "data") { dataOffset = i + 8; dataSize = chunkSize; break; }
            i += 8 + chunkSize + (chunkSize & 1);
        }
        if (dataOffset < 0)
            return null;

        int sampleCount = dataSize / 2 / channels;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            samples[i] = BitConverter.ToInt16(
                bytes, dataOffset + i * 2 * channels) / 32768f;   // 다채널이면 첫 채널만

        using (OnlineStream stream = Recognizer.CreateStream())
        {
            stream.AcceptWaveform(sampleRate, samples);
            // 무음 패딩 — 마지막 어절 잘림 방지.
            stream.AcceptWaveform(
                sampleRate, new float[(int)(sampleRate * 0.66f)]);
            stream.InputFinished();
            while (Recognizer.IsReady(stream))
                Recognizer.Decode(stream);
            return (Recognizer.GetResult(stream).Text ?? "").Trim();
        }
    }
}
