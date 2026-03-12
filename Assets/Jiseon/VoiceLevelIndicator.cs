using UnityEngine;
using UnityEngine.UI;

public class VoiceLevelIndicator : MonoBehaviour
{
    [Header("이미지 3개 (낮음 -> 중간 -> 높음 순서)")]
    public Image bar1;
    public Image bar2;
    public Image bar3;

    [Header("마이크 상태 아이콘")]
    public GameObject micOnIcon;
    public GameObject micOffIcon;

    [Header("음성 소스 (V2로 변경됨)")]
    public VoiceLevelSource_V2 source; // 👈 V2 타입으로 변경!

    [Header("임계값 (0~1)")]
    public float th1 = 0.05f; // 좀 더 민감하게 반응하도록 기본값 살짝 하향
    public float th2 = 0.30f; 
    public float th3 = 0.60f; 

    // 인스펙터에서 편하게 잡으려고 추가
    void Reset() { source = FindFirstObjectByType<VoiceLevelSource_V2>(); }

    void Update()
    {
        if (!source) 
        {
            source = FindFirstObjectByType<VoiceLevelSource_V2>();
            return;
        }

        float v = source.level01;

        // 1. 음향 크기에 따른 구간별 단독 표시
        // [1단계]: th1 이상이고 th2 미만일 때만 
        bool isBar1 = (v >= th1 && v < th2);
        // [2단계]: th2 이상이고 th3 미만일 때만
        bool isBar2 = (v >= th2 && v < th3);
        // [3단계]: th3 이상일 때
        bool isBar3 = (v >= th3);

        SetActive(bar1, isBar1);
        SetActive(bar2, isBar2);
        SetActive(bar3, isBar3);

        // 2. 마이크 아이콘은 소리가 나기만 하면(th1 이상) 켜짐
        bool isSpeaking = v >= th1;
        if (micOnIcon != null) micOnIcon.SetActive(isSpeaking);
        if (micOffIcon != null) micOffIcon.SetActive(!isSpeaking);
    }

    void SetActive(Graphic g, bool on)
{
    if (!g) return;
    
    var c = g.color; 
    c.a = on ? 1f : 0f; 
    g.color = c;
    
    g.enabled = on; 
}
}