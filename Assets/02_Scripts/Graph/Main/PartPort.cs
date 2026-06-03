/*
 * 파일명: PartPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-05-27 — UX v2b: AllPort와 동일한 4상태 스프라이트 + connected 토글 도입.
 * 목적: 메인 그래프 패널 하단의 PART 포트 한 개를 한 PART 노드(NodeData.is_global == false)에 바인딩한다.
 *       동그라미 + 라벨로 구성되며, 다른 서브그래프(PROPERTY → PART)와 연결되면 "채워진 원"으로 바뀐다.
 * 핵심 내용:
 * - Bind(partNode, manager, onChanged, isConnected) 한 번에 데이터/매니저/콜백/연결 상태를 받는다.
 *   부분 갱신이 필요하면 SetConnected(bool)만 따로 호출해도 된다.
 * - 4상태 스프라이트(empty / hover / pressed / filled) 우선순위:
 *     pressed > hovered > (connected ? filled : empty).
 *   hover/pressed는 연결 상태와 무관하게 항상 적용된다 (AllPort, 디자이너 IconButton.cs 패턴과 동일).
 * - disabled / loading 스프라이트는 필드만 열어두고 본 작업에서는 사용하지 않는다 (후순위).
 * - Delete 버튼은 본 메인 그래프 UI에서 사용하지 않는다. _deleteButton 슬롯이 비어 있으면
 *   PartPort는 표시 전용으로 동작하며, OnEnable/OnDisable의 리스너 등록도 건너뛴다.
 * - 클릭 후 PROPERTY 서브그래프 진입 동작은 본 작업 범위 외.
 */
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PartPort : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("동그라미 표시")]
    [SerializeField] private Image _dotImage;
    [SerializeField] private Sprite _emptySprite;
    [SerializeField] private Sprite _hoverSprite;
    [SerializeField] private Sprite _pressedSprite;
    [SerializeField] private Sprite _filledSprite;

    [Header("후순위 (현재 미사용, 필드만 유지)")]
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _loadingSprite;

    [Header("라벨")]
    [SerializeField] private TMP_Text _labelText;

    [Header("Delete (선택, 본 메인 그래프 UI에선 미사용)")]
    [SerializeField] private Button _deleteButton;

    private string _nodeId;
    private GraphManager _manager;
    private Action _onChanged;
    private bool _isConnected;
    private bool _isHovered;
    private bool _isPressed;

    public string NodeId      => _nodeId;
    public bool   IsConnected => _isConnected;

    public void Bind(NodeData partNode, GraphManager manager, Action onChanged, bool isConnected)
    {
        if (partNode == null || manager == null)
        {
            Debug.LogWarning("[PartPort] Bind 실패: partNode 또는 manager가 null입니다.");
            return;
        }
        _nodeId = partNode.node_id;
        _manager = manager;
        _onChanged = onChanged;

        if (_labelText != null)
            _labelText.text = string.IsNullOrEmpty(partNode.label) ? "(이름 없음)" : partNode.label;

        SetConnected(isConnected);
    }

    public void SetConnected(bool connected)
    {
        _isConnected = connected;
        ApplySprite();
    }

    // 우선순위: pressed > hovered > (connected ? filled : empty). 슬롯이 null이면 해당 단계 건너뜀.
    private void ApplySprite()
    {
        if (_dotImage == null) return;

        Sprite target;
        if      (_isPressed && _pressedSprite != null) target = _pressedSprite;
        else if (_isHovered && _hoverSprite   != null) target = _hoverSprite;
        else                                           target = _isConnected ? _filledSprite : _emptySprite;

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

    private void OnEnable()
    {
        if (_deleteButton != null) _deleteButton.onClick.AddListener(InvokeDelete);
    }

    private void OnDisable()
    {
        if (_deleteButton != null) _deleteButton.onClick.RemoveListener(InvokeDelete);
    }

    // UnityEvent 슬롯에서도 호출 가능하도록 public.
    public void InvokeDelete()
    {
        if (_manager == null || string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning("[PartPort] InvokeDelete 실패: Bind()가 먼저 호출되어야 합니다.");
            return;
        }
        Debug.Log($"[PartPort] RequestDeleteNode 호출: nodeId='{_nodeId}'");
        bool ok = _manager.RequestDeleteNode(_nodeId);
        if (ok) _onChanged?.Invoke();
    }
}
