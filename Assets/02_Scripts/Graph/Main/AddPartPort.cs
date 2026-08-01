/*
 * 파일명: AddPartPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-06-03 — 디자인 반영: 클릭 후 Add 스프라이트 → Empty 스프라이트로 교체(inputting 상태).
 * 목적: PartPortContainer 끝에 항상 깔리는 "파트 추가" 슬롯.
 *
 * 상태 (우선순위: inputting > hovered > add):
 *   Add (default) → _emptySprite    : joint_part_Add.png (+가 있는 원).
 *   Hover         → _hoverSprite    : joint_part_Hover.png (테두리 2개).
 *   Inputting     → _inputtingSprite: joint_part_Empty.png (테두리만 있는 원, 이름 입력 중).
 *
 * 클릭 흐름:
 *   1) _addButton 클릭 → BeginInput(): _isInputting=true, InputField 활성화.
 *   2) 사용자가 이름 입력 후 Enter → CommitInput(): 입력 텍스트(키보드)로 서버 REST 생성 요청
 *      (PartNodeApiClient.CreatePartKeyboard → POST /api/part_node/generate). 서버 발급 UUID로 로컬 PART 생성.
 *   3) InputField가 없으면 _defaultLabel로 생성 요청.
 * [주의] 생성은 서버 응답 후 비동기 반영된다(서버가 part_node_id 발급). 로컬 즉시 생성 아님.
 */
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AddPartPort : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [Header("동그라미 스프라이트 (joint_part_*)")]
    [SerializeField] private Image _dotImage;
    [SerializeField] private Sprite _emptySprite;       // joint_part_Add.png   — Add(default) 상태
    [SerializeField] private Sprite _hoverSprite;       // joint_part_Hover.png — Hover 상태
    [SerializeField] private Sprite _inputtingSprite;   // joint_part_Empty.png — 이름 입력 중 상태

    [Header("후순위 (현재 미사용, 필드만 유지)")]
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _loadingSprite;

    [Header("빈 원 포트 버튼")]
    [Tooltip("클릭 시 InputField 활성화 또는 즉시 파트 생성.")]
    [SerializeField] private Button _addButton;

    [Header("이름 입력 (선택)")]
    [Tooltip("연결 시 클릭하면 활성화. Enter(OnSubmit)로 파트 생성.")]
    [SerializeField] private TMP_InputField _inputField;

    [Header("기본 라벨")]
    [Tooltip("InputField 없음 또는 빈 문자열 제출 시 fallback.")]
    [SerializeField] private string _defaultLabel = "새 파트";

    private GraphManager _manager;
    private PartNodeApiClient _apiClient;
    private Action _onChanged;
    private bool _isHovered;
    private bool _isInputting;

    public void Bind(GraphManager manager, PartNodeApiClient apiClient, Action onChanged)
    {
        if (manager == null)
        {
            Debug.LogWarning("[AddPartPort] Bind 실패: manager가 null입니다.");
            return;
        }
        _manager   = manager;
        _apiClient = apiClient;
        _onChanged = onChanged;

        _isInputting = false;
        if (_inputField != null) _inputField.gameObject.SetActive(false);
        ApplySprite();
    }

    private void OnEnable()
    {
        if (_addButton  != null) _addButton.onClick.AddListener(BeginInput);
        if (_inputField != null) _inputField.onSubmit.AddListener(CommitInput);
    }

    private void OnDisable()
    {
        if (_addButton  != null) _addButton.onClick.RemoveListener(BeginInput);
        if (_inputField != null) _inputField.onSubmit.RemoveListener(CommitInput);
    }

    // 빈 원(+) 클릭 → 입력 모드 진입.
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

        _isInputting = true;
        ApplySprite();

        _inputField.text = "";
        _inputField.gameObject.SetActive(true);
        _inputField.Select();
        _inputField.ActivateInputField();

        // VR엔 시스템 키보드가 없다 — 월드 키보드를 이 입력창에 띄운다.
        MvpWorldKeyboard.Open(_inputField);
    }

    // InputField Enter 핸들러.
    private void CommitInput(string text)
    {
        _isInputting = false;
        if (_inputField != null) _inputField.gameObject.SetActive(false);
        ApplySprite();

        string label = string.IsNullOrWhiteSpace(text) ? _defaultLabel : text.Trim();
        InvokeAdd(label);
    }

    // UnityEvent 슬롯에서 매개변수 없이 호출 가능.
    public void InvokeAdd()
    {
        InvokeAdd(_defaultLabel);
    }

    // 파트 생성은 서버 REST(part_node/generate)로 위임한다.
    // 서버가 발급한 part_node_id 로 로컬 PART가 생성되며, 응답 후(비동기) 화면이 갱신된다.
    //
    // [2026-07-30] 서버 경로가 실제로 열린 것을 확인해 서버 우선으로 되돌렸다.
    //   (직전 MVP 우회는 `generate/keyboard` 404 때문에 로컬 우선이었고, 그 탓에 서버 호출이 도달하지 못했다.)
    //   로컬/서버 어느 쪽으로 만들어도 GraphManager.AddNode 를 타므로 Fusion 전파는 동일하다.
    //   차이는 node_id 발급 주체뿐 — 서버 UUID 여야 이후 2D 생성의 part_node_id 로 쓸 수 있다.
    private void InvokeAdd(string label)
    {
        if (_apiClient != null)
        {
            _apiClient.CreatePartKeyboard(label, false, transform.position, ok =>
            {
                if (ok) { _onChanged?.Invoke(); return; }

                // 서버 실패(오프라인/에러) → 로컬 생성으로 폴백해 조작 흐름은 끊지 않는다.
                Debug.LogWarning("[AddPartPort] 서버 PART 생성 실패 → 로컬 생성으로 폴백(self-GUID).");
                CreateLocalPart(label);
            });
            return;
        }

        CreateLocalPart(label);
    }

    // 로컬 PART 생성(self-GUID). 서버가 없거나 실패했을 때만 사용한다.
    private void CreateLocalPart(string label)
    {
        if (_manager == null)
        {
            Debug.LogWarning("[AddPartPort] InvokeAdd 실패: GraphManager/PartNodeApiClient 모두 없음.");
            return;
        }

        string id = _manager.RequestCreatePartNode(label, false);
        if (!string.IsNullOrEmpty(id)) _onChanged?.Invoke();
    }

    // 우선순위: inputting > hovered > add(default)
    private void ApplySprite()
    {
        if (_dotImage == null) return;

        Sprite target;
        if      (_isInputting && _inputtingSprite != null) target = _inputtingSprite;
        else if (_isHovered   && _hoverSprite     != null) target = _hoverSprite;
        else                                               target = _emptySprite;

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
        ApplySprite();
    }
}
