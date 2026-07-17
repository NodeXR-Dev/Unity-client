using UnityEngine;
using UnityEngine.EventSystems;

// MVP 월드 UI의 버튼/포트에 가벼운 호버·누름 피드백을 제공한다.
// 실제 클릭 동작은 기존 Button, PartPort, AllPort가 그대로 담당한다.
public class MvpPressFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private float _hoverScale = 1.055f;
    [SerializeField] private float _pressedScale = 0.94f;

    [SerializeField] private float _hoverLift = 14f;
    [SerializeField] private float _pressedLift = 5f;
    [SerializeField] private float _responseSpeed = 22f;

    private RectTransform _rect;
    private Vector3 _baseScale;

    private float _baseDepth;
    private float _targetDepth;
    private float _targetMultiplier = 1f;

    // 기존 씬에 직렬화된 값과 런타임 생성 포트가 같은 학생용 반응을 쓰게 한다.
    public void ApplyStudentProfile()
    {
        _hoverScale = 1.06f;
        _pressedScale = 0.94f;
        _responseSpeed = 22f;
        _hoverLift = 14f;
        _pressedLift = 5f;
    }

    private void OnEnable()
    {
        _rect = transform as RectTransform;
        _baseScale = transform.localScale;
        _baseDepth = transform.localPosition.z;
        if (_baseScale == Vector3.zero)
            _baseScale = Vector3.one;
        _targetMultiplier = 1f;
        _targetDepth = 0f;
    }

    private void Update()
    {
        float blend =
            1f - Mathf.Exp(
                -_responseSpeed * Time.unscaledDeltaTime);

        Vector3 targetScale =
            _baseScale * _targetMultiplier;
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            blend);

        // z-lift(깊이 이동)는 제거한다: 포크 버튼에선 버튼이 손가락 쪽으로 떠오르며
        // hover↔press 가 반복 토글돼 '앞뒤 진동'을 만든다(이슈: +버튼). 스케일 반응만 유지.
    }

    private void OnDisable()
    {
        transform.localScale =
            _baseScale == Vector3.zero
                ? Vector3.one
                : _baseScale;

        Vector3 position = transform.localPosition;
        position.z = _baseDepth;
        transform.localPosition = position;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _targetMultiplier = _hoverScale;
        _targetDepth = _hoverLift;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _targetMultiplier = 1f;
        _targetDepth = 0f;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _targetMultiplier = _pressedScale;
        _targetDepth = _pressedLift;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_rect == null)
        {
            _targetMultiplier = 1f;
            _targetDepth = 0f;
            return;
        }

        bool inside =
            RectTransformUtility.RectangleContainsScreenPoint(
                _rect,
                eventData.position,
                eventData.pressEventCamera);
        _targetMultiplier =
            inside ? _hoverScale : 1f;
        _targetDepth =
            inside ? _hoverLift : 0f;
    }
}
