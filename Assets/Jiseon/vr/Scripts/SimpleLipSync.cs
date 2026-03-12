using UnityEngine;
using Fusion;

public class SimpleLipSync : NetworkBehaviour
{
    public VoiceLevelSource_V2 source;
    public SkinnedMeshRenderer headMesh;
    public int mouthBlendShapeIndex = 0; 
    public float maxMouthOpen = 100f;    

    void Update()
    {
        if (source == null || headMesh == null) return;

        // [수정 포인트]
        // source.level01이 0(침묵)일 때: 100 - 0 = 100 (앙 다문 상태)
        // source.level01이 1(최대 소리)일 때: 100 - 100 = 0 (완전히 벌린 상태)
        float weight = maxMouthOpen - (source.level01 * maxMouthOpen);

        // 계산된 가중치를 적용
        headMesh.SetBlendShapeWeight(mouthBlendShapeIndex, weight);
    }
}