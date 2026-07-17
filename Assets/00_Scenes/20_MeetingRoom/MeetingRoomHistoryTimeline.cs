/*
 * 파일명: MeetingRoomHistoryTimeline.cs
 * 목적: 하단 바(슬라이더)의 위치를 0~1 값으로 들고 있다가, 바뀌면 OnChanged 이벤트로 알린다.
 *
 * 0 = 과거(왼쪽 끝), 1 = 현재(오른쪽 끝).
 * 조작:
 *  - World Space Canvas 의 UI Slider 를 slider 슬롯에 연결 → VR 레이/마우스로 드래그
 *  - 인스펙터에서 normalized 직접 드래그 (테스트용)
 *  - 코드에서 SetNormalized() 호출
 *
 * MeetingRoomHistoryController 가 이 이벤트를 구독해 해당 시점 스냅샷을 그린다.
 */
using System;
using UnityEngine;
using UnityEngine.UI;

public class MeetingRoomHistoryTimeline : MonoBehaviour
{
    [Header("타임라인 위치 (0 = 과거, 1 = 현재)")]
    [Range(0f, 1f)] public float normalized = 1f;

    [Header("선택: 하단 바 Slider 연결")]
    [SerializeField] private Slider slider;

    /// <summary>값이 바뀔 때 호출 (인자 = 0~1).</summary>
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
        // slider / 인스펙터 / 코드 어디서 바뀌든 감지
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

    /// <summary>외부에서 타임라인 위치 설정 (0~1).</summary>
    public void SetNormalized(float value)
    {
        normalized = Mathf.Clamp01(value);
        Emit(force: false);
    }
}
