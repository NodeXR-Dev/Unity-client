/*
 * 파일명: AllPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-05-27 — UX v2: 라벨 미사용, 4상태 스프라이트(empty/hover/pressed/filled), Pointer 핸들링
 * 목적: 메인 그래프 패널 위쪽의 ALL 포트를 표시한다.
 *       ALL 노드(NodeData.is_global == true)에 PROPERTY 서브그래프가 하나라도 연결되어 있으면
 *       "채워진 동그라미", 연결이 없으면 "빈 동그라미" 스프라이트로 표시한다.
 * 핵심 내용:
 * - 상태 결정은 외부(MainSketchView)가 주입한다. AllPort 자체는 GraphData를 알지 않는다.
 * - Bind(allNode, isConnected) 한 번에 node_id / 라벨 / 동그라미 상태를 갱신한다.
 *   부분 갱신이 필요하면 SetConnected(bool)만 따로 호출해도 된다.
 * - allNode가 null이면 ALL이 아직 GraphData에 없는 상태로 간주하고 빈 동그라미를 표시한다.
 * - 4상태 스프라이트(empty / hover / pressed / filled) 우선순위:
 *     pressed > hovered > (connected ? filled : empty).
 *   hover/pressed는 연결 상태와 무관하게 항상 적용된다 (디자이너 IconButton.cs 패턴과 동일).
 * - disabled / loading 스프라이트는 필드만 열어두고 본 작업에서는 사용하지 않는다 (후순위).
 * - 라벨(_labelText)은 본 UX에서 사용하지 않는다. 슬롯이 비어 있으면 표시 코드를 건너뛴다.
 * - 클릭 후 PROPERTY 서브그래프 진입 동작은 본 작업 범위 외.
 */
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AllPort : MonoBehaviour,
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

    [Header("라벨 (선택, 본 UX에선 미사용)")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private string _defaultLabel = "";

    private string _nodeId;
    private bool _isConnected;
    private bool _isHovered;
    private bool _isPressed;

    public string NodeId      => _nodeId;
    public bool   IsConnected => _isConnected;

    public void Bind(NodeData allNode, bool isConnected)
    {
        if (allNode == null)
        {
            _nodeId = null;
            if (_labelText != null) _labelText.text = _defaultLabel;
            SetConnected(false);
            return;
        }
        _nodeId = allNode.node_id;
        if (_labelText != null)
            _labelText.text = string.IsNullOrEmpty(allNode.label) ? _defaultLabel : allNode.label;
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
}
