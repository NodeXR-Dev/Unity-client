/*
 * 파일명: AddPartPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-05-27 — UX v2c: 4상태 스프라이트(empty/hover/pressed) + 인라인 InputField로 이름 입력 지원.
 * 목적: PartPortContainer 끝에 항상 깔리는 "빈 원 포트(Empty Part Port)" 슬롯.
 *       + 버튼이 아니라 "다음 PART를 만들기 위한 빈 슬롯"이다.
 * 핵심 내용:
 * - Bind(manager, onChanged)로 GraphManager와 갱신 콜백을 받는다.
 * - 3상태 스프라이트(empty / hover / pressed) — AddPartPort는 항상 empty(미연결)이므로 filled 슬롯은 없다.
 *   우선순위 pressed > hovered > empty. (AllPort/PartPort와 동일 패턴, filled 단계만 빠짐)
 * - disabled / loading 슬롯은 필드만 유지 (후순위).
 * - 클릭 흐름:
 *     1) _inputField가 연결되어 있으면 → InputField 활성화 + 포커스. 사용자가 텍스트 입력 후 Enter(OnSubmit)
 *        → 입력값(비어 있으면 _defaultLabel)으로 RequestCreatePartNode 호출 → onChanged.
 *     2) _inputField가 비어 있으면(fallback) → 즉시 _defaultLabel로 RequestCreatePartNode 호출.
 * - Refresh에 의해 자기 자신은 채워진 PartPort로 교체되고, 그 우측에 새 빈 원 포트가 다시 생성된다.
 * - 빈 원 스프라이트는 _dotImage가 연결되어 있으면 본 컴포넌트가 토글, 없으면 Image의 Source Image 그대로.
 * - 디자이너 AddButton(IconButton.cs)은 그대로 두고, Variant 단계에서 같은 GameObject에
 *   Unity Button을 추가한 뒤 본 컴포넌트의 _addButton 슬롯에 연결한다.
 *   (필드명 _addButton은 기존 prefab 호환을 위해 유지하되, 의미상은 "빈 원 포트 버튼"이다.)
 */
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AddPartPort : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("동그라미 표시 (선택)")]
    [SerializeField] private Image _dotImage;
    [SerializeField] private Sprite _emptySprite;
    [SerializeField] private Sprite _hoverSprite;
    [SerializeField] private Sprite _pressedSprite;

    [Header("후순위 (현재 미사용, 필드만 유지)")]
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _loadingSprite;

    [Header("빈 원 포트 버튼")]
    [Tooltip("빈 원 GameObject에 추가한 Unity Button. 클릭 시 InputField가 있으면 입력 모드, 없으면 _defaultLabel 즉시 생성. 필드명은 기존 prefab 호환을 위해 _addButton 유지.")]
    [SerializeField] private Button _addButton;

    [Header("이름 입력 (선택)")]
    [Tooltip("연결되어 있으면 빈 원 클릭 시 활성화되어 입력을 받는다. Enter(OnSubmit) 시 RequestCreatePartNode 호출.")]
    [SerializeField] private TMP_InputField _inputField;

    [Header("기본 라벨")]
    [Tooltip("InputField가 없거나 사용자가 빈 문자열을 제출한 경우의 fallback 라벨.")]
    [SerializeField] private string _defaultLabel = "새 파트";

    private GraphManager _manager;
    private Action _onChanged;
    private bool _isHovered;
    private bool _isPressed;

    public void Bind(GraphManager manager, Action onChanged)
    {
        if (manager == null)
        {
            Debug.LogWarning("[AddPartPort] Bind 실패: manager가 null입니다.");
            return;
        }
        _manager = manager;
        _onChanged = onChanged;

        // 초기 상태: InputField는 숨겨두고 빈 원만 표시.
        if (_inputField != null) _inputField.gameObject.SetActive(false);
        ApplySprite();
    }

    private void OnEnable()
    {
        if (_addButton != null)  _addButton.onClick.AddListener(BeginInput);
        if (_inputField != null) _inputField.onSubmit.AddListener(CommitInput);
    }

    private void OnDisable()
    {
        if (_addButton != null)  _addButton.onClick.RemoveListener(BeginInput);
        if (_inputField != null) _inputField.onSubmit.RemoveListener(CommitInput);
    }

    // 빈 원 클릭 진입점. InputField 있으면 입력 모드 진입, 없으면 default label로 즉시 생성.
    private void BeginInput()
    {
        if (_manager == null)
        {
            Debug.LogWarning("[AddPartPort] BeginInput 실패: Bind()가 먼저 호출되어야 합니다.");
            return;
        }

        if (_inputField == null)
        {
            InvokeAdd(_defaultLabel);
            return;
        }

        _inputField.text = "";
        _inputField.gameObject.SetActive(true);
        _inputField.Select();
        _inputField.ActivateInputField();
    }

    // InputField OnSubmit (Enter) 핸들러.
    private void CommitInput(string text)
    {
        if (_inputField != null) _inputField.gameObject.SetActive(false);

        string label = string.IsNullOrWhiteSpace(text) ? _defaultLabel : text.Trim();
        InvokeAdd(label);
    }

    // UnityEvent 슬롯에서 매개변수 없이 호출 가능. _defaultLabel 사용.
    public void InvokeAdd()
    {
        InvokeAdd(_defaultLabel);
    }

    private void InvokeAdd(string label)
    {
        if (_manager == null)
        {
            Debug.LogWarning("[AddPartPort] InvokeAdd 실패: Bind()가 먼저 호출되어야 합니다.");
            return;
        }
        Debug.Log($"[AddPartPort] RequestCreatePartNode 호출: label='{label}', isGlobal=false");
        string newId = _manager.RequestCreatePartNode(label, false);
        if (!string.IsNullOrEmpty(newId)) _onChanged?.Invoke();
    }

    // 우선순위: pressed > hovered > empty. AddPartPort는 filled 단계가 없다.
    private void ApplySprite()
    {
        if (_dotImage == null) return;

        Sprite target;
        if      (_isPressed && _pressedSprite != null) target = _pressedSprite;
        else if (_isHovered && _hoverSprite   != null) target = _hoverSprite;
        else                                           target = _emptySprite;

        if (target != null) _dotImage.sprite = target;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        ApplySprite();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        _isPressed = false;
        ApplySprite();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        ApplySprite();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        ApplySprite();
    }
}
