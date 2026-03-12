// VoiceLevelSource.cs
using UnityEngine;
using Photon.Voice.Unity;
using Photon.Voice.Fusion;

public enum VoiceLevelMode { FromSpeaker, FromRecorder }

public class VoiceLevelSource : MonoBehaviour
{
    [Header("�ҽ� ����")]
    public VoiceLevelMode mode = VoiceLevelMode.FromSpeaker;
    public AudioSource speakerAudio;   // ����: Speaker�� ���� ������Ʈ�� AudioSource
    public Recorder recorder;          // ����: NetRoot�� Recorder (����θ� �ڵ� Ž��)

    [Header("���� �Ķ����")]
    public int sampleWindow = 256;     // ���� ����
    public float smoothTime = 0.06f;   // ������Ȱ

    [Range(0, 1)] public float level01; // 0~1 ����ȭ�� ����(�б� ����)

    float _vel;
    float[] _buf;
    bool _triedResolve;

    void Awake()
    {
        if (_buf == null || _buf.Length != sampleWindow)
            _buf = new float[sampleWindow];

        if (mode == VoiceLevelMode.FromSpeaker && !speakerAudio)
            speakerAudio = GetComponent<AudioSource>();

        if (mode == VoiceLevelMode.FromRecorder && !recorder)
            TryResolveRecorder();  // �� ���⼭ �ڵ� ����
    }

    void Update()
    {
        // �� �ε� Ÿ�̹� ������ Awake���� �� ������� 1ȸ �� �õ�
        if (mode == VoiceLevelMode.FromRecorder && !recorder && !_triedResolve)
            TryResolveRecorder();

        float rms = 0f;

        if (mode == VoiceLevelMode.FromSpeaker && speakerAudio)
        {
            speakerAudio.GetOutputData(_buf, 0);
            rms = RMS(_buf);
        }
        else if (mode == VoiceLevelMode.FromRecorder && recorder && recorder.AudioClip)
        {
            var clip = recorder.AudioClip;
            int micPos = Microphone.GetPosition(null);
            if (micPos > 0)
            {
                int start = Mathf.Max(0, micPos - sampleWindow);
                int count = Mathf.Min(sampleWindow, clip.samples - start);
                clip.GetData(_buf, start);
                rms = RMS(_buf, count);
            }
        }

        // 0~1 ����ȭ
        const float openThreshold = 0.02f;
        const float fullLevel = 0.12f;
        float t = Mathf.Clamp01(Mathf.InverseLerp(openThreshold, fullLevel, rms));

        level01 = Mathf.SmoothDamp(level01, t, ref _vel, smoothTime);
    }

    void TryResolveRecorder()
    {
        _triedResolve = true;

        // 1) FusionVoiceClient�� PrimaryRecorder �켱
        var fvc = FindFirstObjectByType<FusionVoiceClient>();
        if (fvc && fvc.PrimaryRecorder)
        {
            recorder = fvc.PrimaryRecorder;
            return;
        }

        // 2) ������Ʈ �� �ƹ� Recorder (DontDestroyOnLoad ����)
        recorder = FindFirstObjectByType<Recorder>();
    }

    float RMS(float[] arr, int count = -1)
    {
        if (count < 0) count = arr.Length;
        double sum = 0;
        for (int i = 0; i < count; i++) sum += arr[i] * arr[i];
        return Mathf.Sqrt((float)(sum / Mathf.Max(1, count)));
    }
}
