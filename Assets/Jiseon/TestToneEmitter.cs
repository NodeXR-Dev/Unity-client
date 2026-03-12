using UnityEngine;

[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(VoiceAudioModeHard))]
public class TestToneEmitter : MonoBehaviour
{
    public float frequency = 440f;   // 음 높이
    public float volume = 0.2f;   // 크기
    public float seconds = 2f;     // 루프 길이
    public int sampleRate = 48000;

    void Awake()
    {
        var src = GetComponent<AudioSource>();
        src.playOnAwake = true;
        src.loop = true;
        src.dopplerLevel = 0f;
        src.spatialBlend = 1f;                         // 기본은 3D
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = GetComponent<VoiceAudioModeHard>().proxMinDistance;
        src.maxDistance = GetComponent<VoiceAudioModeHard>().proxMaxDistance;

        int len = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
        var clip = AudioClip.Create("TestTone", len, 1, sampleRate, false);
        var data = new float[len];
        float step = 2f * Mathf.PI * frequency / sampleRate;
        float phase = 0f;
        for (int i = 0; i < len; i++)
        {
            data[i] = Mathf.Sin(phase) * volume;
            phase += step;
            if (phase > Mathf.PI * 2f) phase -= Mathf.PI * 2f;
        }
        clip.SetData(data, 0);
        src.clip = clip;
        src.Play();
    }
}
