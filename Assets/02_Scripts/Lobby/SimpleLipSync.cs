using Fusion;
using UnityEngine;

public class SimpleLipSync : NetworkBehaviour
{
    public VoiceLevelSource_V2 source;
    public SkinnedMeshRenderer headMesh;
    public int mouthBlendShapeIndex = 0;
    public float maxMouthOpen = 100f;

    [Header("Voice Sync")]
    [Range(0f, 1f)] public float speakingThreshold = 0.08f;
    public float remoteSmoothSpeed = 12f;

    [Networked] public float NetworkVoiceLevel { get; private set; }
    [Networked] public NetworkBool IsSpeaking { get; private set; }

    public float CurrentVoiceLevel { get; private set; }

    public override void Spawned()
    {
        if (source != null)
            source.enabled = Object.HasInputAuthority;
    }

    private void Update()
    {
        if (headMesh == null)
            return;

        float targetLevel = GetTargetVoiceLevel();
        CurrentVoiceLevel = Mathf.Lerp(CurrentVoiceLevel, targetLevel, Time.deltaTime * remoteSmoothSpeed);

        float weight = maxMouthOpen - (CurrentVoiceLevel * maxMouthOpen);
        headMesh.SetBlendShapeWeight(mouthBlendShapeIndex, weight);
    }

    private float GetTargetVoiceLevel()
    {
        if (Object.HasInputAuthority)
        {
            float localLevel = source != null ? Mathf.Clamp01(source.level01) : 0f;

            if (Object.HasStateAuthority)
            {
                NetworkVoiceLevel = localLevel;
                IsSpeaking = localLevel >= speakingThreshold;
            }

            return localLevel;
        }

        return Mathf.Clamp01(NetworkVoiceLevel);
    }
}
