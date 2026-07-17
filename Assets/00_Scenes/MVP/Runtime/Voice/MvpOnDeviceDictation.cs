using System;
using System.Collections;
using System.IO;
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
               File.Exists(Path.Combine(ModelDir, "tokens.txt"));
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
            string dst = Path.Combine(ModelDir, file);
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

        // 3) 인식기 초기화 (Quest 에서 수 초 걸릴 수 있음)
        onStatus?.Invoke("음성 인식 준비 중…");
        yield return null;   // 상태 문구가 먼저 그려지도록 한 프레임 양보
        bool ok = EnsureRecognizer();
        onDone?.Invoke(ok, ok ? null : "음성 인식기를 초기화하지 못했어요.");
    }

    public bool StartListening()
    {
        if (_listening)
            return true;
        if (!IsSupported() || !EnsureRecognizer())
            return false;

        // 다른 소비자(예: 키보드 ↔ 액션바)가 듣는 중이면 그쪽을 먼저 확정·정리한다.
        if (_activeListener != null && _activeListener != this)
            _activeListener.StopListening();

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[MVP STT] 사용할 수 있는 마이크가 없습니다.");
            return false;
        }

        _micClip = Microphone.Start(_micDevice, true, 30, SampleRate);
        if (_micClip == null)
        {
            Debug.LogWarning("[MVP STT] 마이크를 시작하지 못했습니다.");
            return false;
        }

        _stream = Recognizer.CreateStream();
        _micReadPos = 0;
        _lastPartial = "";
        _listening = true;
        _activeListener = this;
        return true;
    }

    // 수동 종료(버튼 재탭)와 자동 종료(문장 끝 무음) 공용. 최종 텍스트를 반환하고 OnFinal 을 발화한다.
    public string StopListening()
    {
        if (!_listening)
            return "";
        _listening = false;
        if (_activeListener == this)
            _activeListener = null;

        DrainMic();
        Microphone.End(_micDevice);
        _micClip = null;
        Level = 0f;

        string text = "";
        if (_stream != null)
        {
            // 스트리밍 모델은 마지막 어절이 잘리기 쉬우므로 무음 패딩으로 끝까지 디코드한다.
            _stream.AcceptWaveform(
                SampleRate, new float[(int)(SampleRate * 0.66f)]);
            _stream.InputFinished();
            while (Recognizer.IsReady(_stream))
                Recognizer.Decode(_stream);
            text = (Recognizer.GetResult(_stream).Text ?? "").Trim();
            _stream.Dispose();
            _stream = null;
        }

        if (text.Length > 0)
            OnFinal?.Invoke(text);
        return text;
    }

    private void Update()
    {
        if (!_listening || _stream == null)
            return;

        DrainMic();
        while (Recognizer.IsReady(_stream))
            Recognizer.Decode(_stream);

        string partial = (Recognizer.GetResult(_stream).Text ?? "").Trim();
        if (partial.Length > 0 && partial != _lastPartial)
        {
            _lastPartial = partial;
            OnPartial?.Invoke(partial);
        }

        // 말이 끝나고 무음이 이어지면 자동 확정한다.
        if (Recognizer.IsEndpoint(_stream) && partial.Length > 0)
            StopListening();
    }

    // 마이크 링버퍼에서 새로 쌓인 샘플만 인식기로 보낸다(랩어라운드 처리 포함).
    private void DrainMic()
    {
        if (_micClip == null || _stream == null)
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
        _stream.AcceptWaveform(SampleRate, buffer);

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
                Path.Combine(ModelDir, "encoder-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Transducer.Decoder =
                Path.Combine(ModelDir, "decoder-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Transducer.Joiner =
                Path.Combine(ModelDir, "joiner-epoch-99-avg-1.int8.onnx");
            config.ModelConfig.Tokens =
                Path.Combine(ModelDir, "tokens.txt");
            config.ModelConfig.NumThreads = 2;
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

    private void OnDestroy()
    {
        if (_listening)
        {
            Microphone.End(_micDevice);
            _listening = false;
        }
        _stream?.Dispose();
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
