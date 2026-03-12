using UnityEngine;

public enum VoicePlayMode { Proximity3D, Global2D, Off }

[RequireComponent(typeof(AudioSource))]
public class VoiceAudioModeHard : MonoBehaviour
{
    public VoicePlayMode mode = VoicePlayMode.Proximity3D;
    public float proxMinDistance = 1f;
    public float proxMaxDistance = 25f;

    AudioSource src;

    void Awake()
    {
        src = GetComponent<AudioSource>();
        src.dopplerLevel = 0f;
        src.reverbZoneMix = 0f;
        Apply(mode);
    }

    public void SetMode(VoicePlayMode newMode)
    {
        if (mode == newMode) return;
        mode = newMode;
        Apply(mode);
    }

    void Apply(VoicePlayMode m)
    {
        switch (m)
        {
            case VoicePlayMode.Proximity3D:
                src.mute = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = proxMinDistance;
                src.maxDistance = proxMaxDistance;
                break;
            case VoicePlayMode.Global2D:
                src.mute = false;
                src.spatialBlend = 0f;
                break;
            case VoicePlayMode.Off:
                src.mute = true;
                break;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!src) src = GetComponent<AudioSource>();
        if (src) Apply(mode);
    }
#endif
}
