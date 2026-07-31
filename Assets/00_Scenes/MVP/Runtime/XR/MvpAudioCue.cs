using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// MVP 공용 오디오/햅틱 피드백 서비스.
// 프로젝트에 UI 사운드 애셋이 없어서 짧은 큐를 '런타임 절차적 합성'(사인/글라이드/엔벨로프)으로 만든다.
// 핸드트래킹(맨손)엔 컨트롤러 진동이 불가하므로 오디오가 사실상 햅틱 대체다.
// 컨트롤러가 연결돼 있으면 오디오에 더해 OVRInput 진동을 얹는다(연결 안 됐으면 무동작).
[DisallowMultipleComponent]
public class MvpAudioCue : MonoBehaviour
{
    public enum Cue
    {
        Connect, Disconnect, NodeSpawn, NodeDelete,
        KeyClick, GestureTick, Commit, Grab, Release, Error, Success
    }

    public static MvpAudioCue Instance { get; private set; }

    [SerializeField, Range(0f, 1f)] private float _masterVolume = 0.5f;
    [SerializeField] private int _voices = 4;
    [SerializeField] private bool _enableAudio = true;
    [SerializeField] private bool _enableHaptics = true;

    private const int SampleRate = 44100;
    private AudioSource[] _sources;
    private int _next;
    private readonly Dictionary<Cue, AudioClip> _clips = new Dictionary<Cue, AudioClip>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        int n = Mathf.Max(1, _voices);
        _sources = new AudioSource[n];
        for (int i = 0; i < n; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;      // 2D UI 사운드
            s.reverbZoneMix = 0f;
            s.dopplerLevel = 0f;
            _sources[i] = s;
        }

        BuildClips();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---- public API ----

    public void Play(Cue cue, float volume = 1f)
    {
        if (!_enableAudio || _sources == null) return;
        if (!_clips.TryGetValue(cue, out AudioClip clip) || clip == null) return;

        AudioSource s = _sources[_next];
        _next = (_next + 1) % _sources.Length;
        s.PlayOneShot(clip, Mathf.Clamp01(volume) * _masterVolume);
    }

    public void PlayHaptic(Cue cue, float amplitude = 0.5f, float duration = 0.06f, float volume = 1f)
    {
        Play(cue, volume);
        Pulse(amplitude, duration);
    }

    // 컨트롤러 진동(연결됐을 때만 효과). 핸드트래킹이면 무동작.
    public void Pulse(float amplitude, float duration)
    {
        if (!_enableHaptics || !isActiveAndEnabled) return;
        StartCoroutine(PulseRoutine(Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration)));
    }

    private IEnumerator PulseRoutine(float amplitude, float duration)
    {
        SetVibration(0.5f, amplitude);
        yield return new WaitForSecondsRealtime(duration);
        SetVibration(0f, 0f);
    }

    private static void SetVibration(float frequency, float amplitude)
    {
        try
        {
            OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
        }
        catch
        {
            // OVR 미초기화/미지원 환경 — 무시(오디오만).
        }
    }

    // ---- 절차적 합성 ----

    private void BuildClips()
    {
        // (f0, f1) 글라이드, dur, vol, 2nd harmonic, buzz(사각파 근사)
        _clips[Cue.Connect]    = Tone("cue_connect", 460f, 720f, 0.13f, 0.6f, 0.25f, false);
        _clips[Cue.Disconnect] = Tone("cue_disc",    560f, 320f, 0.13f, 0.55f, 0.2f, false);
        _clips[Cue.NodeSpawn]  = Tone("cue_spawn",   520f, 560f, 0.10f, 0.55f, 0.2f, false);
        _clips[Cue.NodeDelete] = Tone("cue_del",     260f, 170f, 0.14f, 0.6f, 0.0f, false);
        _clips[Cue.KeyClick]   = Tone("cue_key",     880f, 880f, 0.028f, 0.35f, 0.0f, false);
        _clips[Cue.GestureTick]= Tone("cue_tick",    680f, 720f, 0.030f, 0.3f, 0.0f, false);
        _clips[Cue.Grab]       = Tone("cue_grab",    300f, 340f, 0.06f, 0.5f, 0.15f, false);
        _clips[Cue.Release]    = Tone("cue_rel",     360f, 300f, 0.06f, 0.5f, 0.15f, false);
        _clips[Cue.Error]      = Tone("cue_err",     180f, 150f, 0.16f, 0.55f, 0.0f, true);
        _clips[Cue.Commit]     = Chord("cue_commit", new float[] { 523f, 659f }, 0.16f, 0.5f);
        _clips[Cue.Success]    = Arp("cue_success",  new float[] { 523f, 659f, 784f }, 0.28f, 0.5f);
    }

    private static AudioClip Tone(
        string name, float f0, float f1, float dur, float vol, float harmonic2, bool buzz)
    {
        int n = Mathf.Max(1, (int)(SampleRate * dur));
        float[] data = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float freq = Mathf.Lerp(f0, f1, t);
            phase += 2.0 * Mathf.PI * freq / SampleRate;
            float s = Mathf.Sin((float)phase);
            if (harmonic2 > 0f)
                s += harmonic2 * Mathf.Sin((float)phase * 2f);
            if (buzz)
                s = Mathf.Sign(s) * 0.6f;
            data[i] = s * Envelope(t) * vol * 0.9f;
        }
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip Chord(string name, float[] freqs, float dur, float vol)
    {
        int n = Mathf.Max(1, (int)(SampleRate * dur));
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float s = 0f;
            for (int k = 0; k < freqs.Length; k++)
                s += Mathf.Sin(2f * Mathf.PI * freqs[k] * (i / (float)SampleRate));
            data[i] = (s / freqs.Length) * Envelope(t) * vol * 0.9f;
        }
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 상승 아르페지오(세그먼트별 음).
    private static AudioClip Arp(string name, float[] freqs, float dur, float vol)
    {
        int n = Mathf.Max(1, (int)(SampleRate * dur));
        float[] data = new float[n];
        int seg = Mathf.Max(1, n / freqs.Length);
        for (int i = 0; i < n; i++)
        {
            int k = Mathf.Min(freqs.Length - 1, i / seg);
            float localT = (i % seg) / (float)seg;
            float s = Mathf.Sin(2f * Mathf.PI * freqs[k] * (i / (float)SampleRate));
            float env = Mathf.Min(1f, localT / 0.06f) * Mathf.Exp(-3.5f * localT);
            data[i] = s * env * vol * 0.85f;
        }
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 빠른 어택 + 지수 감쇠.
    private static float Envelope(float t)
    {
        float attack = Mathf.Min(1f, t / 0.02f);
        float decay = Mathf.Exp(-4.5f * t);
        return attack * decay;
    }
}
