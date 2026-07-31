using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 히스토리 타임라인 컨트롤러.
/// 하단 바의 핸들(점)을 좌(과거)~우(현재)로 움직이면 정규화 값(0~1)이 바뀌고,
/// 이를 구독하는 시각화(예: HistoryNodeGraph)가 그 시점의 상태를 보여준다.
///
/// 조작 방법 (아무거나):
///  - World Space Canvas의 UI Slider를 Slider 슬롯에 연결 → VR 레이/마우스로 핸들 드래그
///  - 인스펙터에서 Normalized 값을 직접 드래그 (플레이 중 테스트용)
///  - 외부 코드에서 SetNormalized() 호출
///
/// 0 = 과거(왼쪽 끝), 1 = 현재(오른쪽 끝).
/// </summary>
public class HistoryTimeline : MonoBehaviour
{
    [Header("타임라인 위치 (0 = 과거, 1 = 현재/오른쪽 끝)")]
    [Range(0f, 1f)] public float normalized = 1f;

    [Header("선택: 하단 바 Slider 연결")]
    [Tooltip("World Space Canvas에 만든 Slider. 연결하면 VR 레이/마우스로 핸들을 드래그할 수 있다.")]
    [SerializeField] private Slider slider;

    /// <summary>값이 바뀔 때 호출된다 (인자 = 0~1).</summary>
    public event Action<float> OnChanged;

    private float _last = -1f;

    void Start()
    {
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(normalized);
            slider.onValueChanged.AddListener(OnSliderChanged);
        }
        Emit(force: true);
    }

    void OnDestroy()
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    void Update()
    {
        // normalized가 (슬라이더/인스펙터/코드 어디서든) 바뀌면 구독자에게 통지
        Emit(force: false);
    }

    private void OnSliderChanged(float v) => normalized = v;

    private void Emit(bool force)
    {
        normalized = Mathf.Clamp01(normalized);
        if (!force && Mathf.Approximately(normalized, _last)) return;
        _last = normalized;

        if (slider != null && !Mathf.Approximately(slider.value, normalized))
            slider.SetValueWithoutNotify(normalized);

        OnChanged?.Invoke(normalized);
    }

    /// <summary>외부에서 타임라인 위치를 설정한다 (0~1).</summary>
    public void SetNormalized(float value)
    {
        normalized = Mathf.Clamp01(value);
        Emit(force: false);
    }
}
