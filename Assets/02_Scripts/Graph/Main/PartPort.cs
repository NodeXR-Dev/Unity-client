/*
 * 파일명: PartPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-06-03 — 디자인 반영: Pressed 토글 + 위 X(삭제) / 아래 인라인 rename.
 * 목적: 메인 그래프 패널 하단의 PART 포트 한 개를 한 PART 노드에 바인딩한다.
 *
 * [AllPort와의 차이]
 *   AllPort  : _isExpanded는 목록 표시/숨김만 제어. 스프라이트는 connected/empty로 복귀.
 *              _isHeldDown(누르는 동안)만 pressedSprite를 표시.
 *   PartPort : _isExpanded(편집 모드)가 true인 동안 pressedSprite를 계속 유지.
 *              _isHeldDown 없음. OnPointerDown/Up 없음.
 *              편집 모드 = 위에 X(삭제) + 아래 rename field가 표시된 상태.
 *
 * 상태 우선순위 (expanded > hovered > connected > empty):
 *   Expanded(Pressed) → _pressedSprite   : 클릭마다 토글. 삭제 X + rename 활성화.
 *   Hovered           → _hoverSprite     : 마우스 올림 (expanded 아닐 때만).
 *   Connected         → _connectedSprite : PROPERTY/REFERENCE가 연결된 상태.
 *   Empty (default)   → _emptySprite     : 연결 없는 상태.
 *
 * Pressed 행동:
 *   - _deleteButton 활성화 → 클릭 시 서버 REST 삭제(PartNodeApiClient.DeletePart → DELETE /api/part_node/delete),
 *     성공 시 로컬 삭제 + _onChanged().
 *   - _labelText 숨김, _renameField 활성화 (현재 라벨로 pre-fill).
 *   - _renameField Enter(OnSubmit) → 서버 REST 수정(PartNodeApiClient.ModifyPart → PATCH /api/part_node/modify),
 *     성공 시 로컬 label 갱신 + _onChanged(). Collapse()는 즉시.
 *   - PartPort 재클릭 → Collapse().
 * [주의] PART 노드는 REST로 서버 동기화한다(WS NODE_* 미사용). 삭제/수정은 서버 응답 후 비동기 반영.
 *
 * Unity Inspector 연결 필요:
 *   - _deleteButton : Pressed 시 위쪽에 표시할 X 버튼 (기본 비활성).
 *   - _renameField  : Pressed 시 아래쪽에 표시할 TMP_InputField (기본 비활성).
 */
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PartPort : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("동그라미 스프라이트 (joint_part_*)")]
    [SerializeField] private Image _dotImage;
    [SerializeField] private Sprite _emptySprite;       // joint_part_Empty.png
    [SerializeField] private Sprite _hoverSprite;       // joint_part_Hover.png
    [SerializeField] private Sprite _connectedSprite;   // joint_part_Connected.png
    [SerializeField] private Sprite _pressedSprite;     // joint_part_Pressed.png

    [Header("후순위 (현재 미사용, 필드만 유지)")]
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _loadingSprite;

    [Header("라벨 (기본 표시)")]
    [SerializeField] private TMP_Text _labelText;

    [Header("Pressed — 삭제 / 이름 변경")]
    [Tooltip("Pressed 시 위쪽에 나타나는 빨간 X 버튼. 기본 비활성.")]
    [SerializeField] private Button _deleteButton;
    [Tooltip("Pressed 시 아래쪽에 나타나는 이름 변경 InputField. 기본 비활성.")]
    [SerializeField] private TMP_InputField _renameField;

    private string _nodeId;
    private GraphManager _manager;
    private PartNodeApiClient _apiClient;
    private Action _onChanged;
    private bool _isConnected;
    private bool _isHovered;
    private bool _isExpanded;

    public string NodeId      => _nodeId;
    public bool   IsConnected => _isConnected;

    public void Bind(NodeData partNode, GraphManager manager, PartNodeApiClient apiClient, Action onChanged, bool isConnected)
    {
        if (partNode == null || manager == null)
        {
            Debug.LogWarning("[PartPort] Bind 실패: partNode 또는 manager가 null입니다.");
            return;
        }
        _nodeId    = partNode.node_id;
        _manager   = manager;
        _apiClient = apiClient;
        _onChanged = onChanged;

        if (_labelText != null)
            _labelText.text = string.IsNullOrEmpty(partNode.label) ? "(이름 없음)" : partNode.label;

        Collapse();
        SetConnected(isConnected);
    }

    public void SetConnected(bool connected)
    {
        _isConnected = connected;
        ApplySprite();
    }

    // 우선순위: expanded > hovered > connected > empty
    // AllPort와 달리 _isExpanded가 true인 동안 pressedSprite를 계속 유지한다.
    private void ApplySprite()
    {
        if (_dotImage == null) return;

        Sprite target;
        if      (_isExpanded  && _pressedSprite   != null)  target = _pressedSprite;
        else if (_isHovered   && _hoverSprite     != null)  target = _hoverSprite;
        else if (_isConnected && _connectedSprite != null)  target = _connectedSprite;
        else                                                target = _emptySprite;

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

    // 클릭마다 Pressed 토글.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isExpanded) Collapse();
        else             Expand();
    }

    private void Expand()
    {
        _isExpanded = true;
        ApplySprite();

        // 삭제 버튼 표시
        if (_deleteButton != null) _deleteButton.gameObject.SetActive(true);

        // 라벨 숨기고 rename 필드 표시
        if (_labelText  != null) _labelText.gameObject.SetActive(false);
        if (_renameField != null)
        {
            string currentLabel = _manager?.GetNode(_nodeId)?.label ?? "";
            _renameField.text = currentLabel;
            _renameField.gameObject.SetActive(true);
            _renameField.ActivateInputField();
            StartCoroutine(MoveCaretToEnd());
        }
    }

    // ActivateInputField() 직후 Unity가 텍스트를 전체 선택하므로, 한 프레임 뒤에 선택 해제.
    private IEnumerator MoveCaretToEnd()
    {
        yield return null;
        if (_renameField != null && _isExpanded)
            _renameField.MoveTextEnd(false);
    }

    private void Collapse()
    {
        _isExpanded = false;
        ApplySprite();

        if (_deleteButton != null) _deleteButton.gameObject.SetActive(false);
        if (_renameField  != null) _renameField.gameObject.SetActive(false);
        if (_labelText    != null) _labelText.gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        if (_deleteButton != null) _deleteButton.onClick.AddListener(InvokeDelete);
        if (_renameField  != null) _renameField.onSubmit.AddListener(CommitRename);
    }

    private void OnDisable()
    {
        if (_deleteButton != null) _deleteButton.onClick.RemoveListener(InvokeDelete);
        if (_renameField  != null) _renameField.onSubmit.RemoveListener(CommitRename);
        Collapse();
    }

    // 삭제는 서버 REST(part_node/delete)로 위임한다. 성공 시(비동기) 로컬 삭제 + 갱신.
    // UnityEvent 슬롯에서도 호출 가능하도록 public.
    public void InvokeDelete()
    {
        if (_apiClient == null || string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning("[PartPort] InvokeDelete 실패: PartNodeApiClient 미연결 또는 nodeId 없음(Bind 확인).");
            return;
        }
        _apiClient.DeletePart(_nodeId, ok =>
        {
            if (ok) _onChanged?.Invoke();
        });
    }

    // 이름 변경은 서버 REST(part_node/modify)로 위임한다. 성공 시(비동기) 로컬 label 갱신 + 표시.
    private void CommitRename(string newLabel)
    {
        if (_apiClient == null || string.IsNullOrEmpty(_nodeId)) { Collapse(); return; }

        _apiClient.ModifyPart(_nodeId, newLabel, ok =>
        {
            if (ok)
            {
                var node = _manager?.GetNode(_nodeId);
                string label = node != null ? node.label : newLabel.Trim();
                if (_labelText != null) _labelText.text = label;
                _onChanged?.Invoke();
            }
        });
        Collapse();
    }
}
