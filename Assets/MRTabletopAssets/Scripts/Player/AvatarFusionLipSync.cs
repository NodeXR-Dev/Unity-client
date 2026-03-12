using UnityEngine;
using Fusion; // Fusion 네임스페이스 필수

public class AvatarFusionLipSync : NetworkBehaviour
{
    [Header("연결 설정")]
    [SerializeField] private VoiceLevelSource m_Source; // 기존 VoiceLevelSource 연결
    [SerializeField] private SkinnedMeshRenderer m_HeadRend;
    [SerializeField] private int m_MouthBlendIndex = 0; // Mouth_Open은 0번

    [Header("입모양 커브")]
    [SerializeField] private AnimationCurve m_OutputCurve = AnimationCurve.Linear(0, 0, 1, 1);

    // [Networked]를 붙여야 다른 사람 화면으로 이 수치가 전달됩니다.
    [Networked]
    private float NetworkedLevel { get; set; }

    // 네트워크 데이터 갱신 (내 캐릭터인 경우에만 실행)
    public override void FixedUpdateNetwork()
    {
        if (Object.HasInputAuthority && m_Source != null)
        {
            // 내 화면에서 계산된 level01 수치를 네트워크 변수에 동기화
            NetworkedLevel = m_Source.level01;
        }
    }

    // 실제 화면 렌더링 (내 화면 + 남의 화면 모두 실행)
    public override void Render()
    {
        if (m_HeadRend == null) return;

        // 서버에서 동기화되어 내려온 NetworkedLevel 값을 사용
        float voiceLevel = NetworkedLevel;
        float appliedValue = m_OutputCurve.Evaluate(voiceLevel);

        // 0일 때 Open, 100일 때 Closed 특성 반영
        float weight = 100f - (appliedValue * 100f);
        
        m_HeadRend.SetBlendShapeWeight(m_MouthBlendIndex, weight);
    }
}