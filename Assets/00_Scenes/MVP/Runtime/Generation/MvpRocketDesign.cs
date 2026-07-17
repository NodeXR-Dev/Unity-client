using System.Collections.Generic;

// 노드 그래프에서 뽑아낸 "이 팀이 설계한 물로켓"의 요약.
// 2D 스케치 생성기와 3D 스테이지가 공유하는 순수 데이터(로직 없음).
// 값은 모두 0~1 또는 배수(multiplier)로 정규화해, 그리는 쪽이 자유롭게 스케일한다.
public class MvpRocketDesign
{
    public bool hasBody;
    public bool hasFins;
    public bool hasNose;

    // 요구사항 키워드로 해석된 형태 특성.
    public float bodyLength = 1f;   // 0.75(짧고 통통) ~ 1.35(길고 슬림)
    public float bodySlim = 1f;     // 0.8(두껍게) ~ 1.15(얇게)
    public float finSpan = 1f;      // 0.6(작은 날개) ~ 1.5(넓은 날개)
    public int finCount = 3;        // 그려질 날개 수(2~4)
    public bool pointedNose;        // true=뾰족(멀리/빠르게), false=둥근(안전)
    public float waterFill = 0.42f; // 0.2 ~ 0.72 (몸통 안 물 높이 비율)

    public int accentIndex;         // 색 변형(히스토리 순번 등)
    public int partCount;
    public int requirementCount;
    public int connectionCount;

    // 각 핵심 부품에 연결된 요구사항 라벨(배지/캡션용, 최대 몇 개만).
    public readonly List<string> bodyReqs = new List<string>();
    public readonly List<string> finReqs = new List<string>();
    public readonly List<string> noseReqs = new List<string>();


    // 색 팔레트: accentIndex 로 순환.
    public static readonly UnityEngine.Color32[] AccentPalette =
    {
        new UnityEngine.Color32(90, 96, 234, 255),   // 인디고
        new UnityEngine.Color32(49, 191, 208, 255),  // 시안
        new UnityEngine.Color32(243, 123, 114, 255), // 코랄
        new UnityEngine.Color32(120, 190, 90, 255),  // 그린
    };

    public UnityEngine.Color32 Accent =>
        AccentPalette[
            ((accentIndex % AccentPalette.Length) + AccentPalette.Length)
            % AccentPalette.Length];
}
