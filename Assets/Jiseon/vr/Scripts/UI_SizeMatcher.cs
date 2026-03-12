using UnityEngine;

public class UI_SizeSync : MonoBehaviour
{
    public RectTransform targetRect; // NameTag_Root(배경)
    
    [Header("기준 설정")]
    public float baseTargetWidth;    // 인스펙터에서 직접 입력 (예: 295)
    
    private RectTransform myRect;
    private float initialMyWidth;

    void Awake()
    {
        myRect = GetComponent<RectTransform>();
        // 각 Bar가 에디터에서 배치된 당시의 초기 Width를 기억합니다.
        initialMyWidth = myRect.rect.width;
    }

    void LateUpdate()
    {
        if (targetRect == null || baseTargetWidth <= 0) return;

        // 1. 현재 배경 너비에서 내가 입력한 기준 너비를 뺍니다. (변화량 계산)
        float deltaWidth = targetRect.rect.width - baseTargetWidth;

        // 2. 내 원래 너비에 변화량만큼만 더해줍니다.
        // 9-Slice가 적용된 Sliced 타입이라면 비율 깨짐 없이 가로만 늘어납니다.
        myRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, initialMyWidth + deltaWidth);
    }
}