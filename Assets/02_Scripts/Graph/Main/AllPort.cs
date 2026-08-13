/*
 * 파일명: AllPort.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 수정일: 2026-06-03 — 디자인 반영: Pressed 토글 + 연결 엣지 목록(X 버튼) 표시.
 * 목적: 메인 그래프 패널 위쪽의 ALL 포트를 표시한다.
 *
 * 상태 우선순위 (expanded > hovered > connected/empty):
 *   Expanded(Pressed) → _pressedSprite   : 클릭마다 토글. 연결 엣지 목록 표시.
 *   Hovered           → _hoverSprite     : 마우스 올림.
 *   Connected         → _connectedSprite : PROPERTY/REFERENCE가 연결된 상태.
 *   Empty (default)   → _emptySprite     : 연결 없는 초기 상태.
 *
 * Pressed 행동:
 *   - _connectedListContainer를 활성화, 연결 엣지별 X 버튼 생성.
 *   - X 클릭 → RequestDeleteEdge(edgeId): 노드는 유지, 해당 엣지만 삭제.
 *   - X 클릭 후 _onChanged?.Invoke() + Collapse().
 *   - AllPort 재클릭 → Collapse().
 *
 * Unity Inspector 연결 필요:
 *   - _connectedNodeButtonPrefab : Button + 자식 TMP_Text(노드 라벨) 구조 프리팹.
 *   - _connectedListContainer    : Pressed 시 버튼을 담을 부모 Transform (기본 비활성).
 */
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AllPort : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler
{
    [Header("동그라미 스프라이트 (joint_all_*)")]
    [SerializeField] private Image _dotImage;
    [SerializeField] private Sprite _emptySprite;       // joint_all_Empty-3.png
    [SerializeField] private Sprite _hoverSprite;       // joint_all_Hover.png
    [SerializeField] private Sprite _connectedSprite;   // joint_all_Connected.png
    [SerializeField] private Sprite _pressedSprite;     // joint_all_Pressed.png

    [Header("Pressed — 연결 엣지 목록")]
    [Tooltip("Pressed 상태에서 활성화할 컨테이너. 자식이 동적으로 채워짐.")]
    [SerializeField] private Transform _connectedListContainer;
    [Tooltip("엣지 1개당 생성할 버튼 프리팹. 자식 TMP_Text에 연결 노드 라벨이 설정됨.")]
    [SerializeField] private Button _connectedNodeButtonPrefab;

    [Header("후순위 (현재 미사용, 필드만 유지)")]
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _loadingSprite;

    [Header("라벨 (선택, 본 UX에선 미사용)")]
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private string _defaultLabel = "";

    private string _nodeId;
    private GraphManager _manager;
    private Action _onChanged;
    private bool _isConnected;
    private bool _isHovered;
    private bool _isHeldDown;  // 누르는 동안만 true — 스프라이트 sparkle 표시용
    private bool _isExpanded;  // 클릭 후 토글 상태 — 연결 목록 표시

    public string NodeId      => _nodeId;
    public bool   IsConnected => _isConnected;

    // MainSketchView.Refresh()에서 호출.
    public void Bind(NodeData allNode, GraphManager manager, Action onChanged, bool isConnected)
    {
        _manager   = manager;
        _onChanged = onChanged;

        if (allNode == null)
        {
            _nodeId = null;
            if (_labelText != null) _labelText.text = _defaultLabel;
            Collapse();
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

    // 우선순위: heldDown > hovered > connected > empty
    // _isExpanded는 목록 표시/숨김 전용이며 스프라이트 조건에 포함하지 않는다.
    private void ApplySprite()
    {
        if (_dotImage == null) return;

        Sprite target;
        if      (_isHeldDown  && _pressedSprite   != null)  target = _pressedSprite;
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
        _isHovered  = false;
        _isHeldDown = false;
        ApplySprite();
    }

    // 누르는 동안만 sparkle 스프라이트 표시.
    public void OnPointerDown(PointerEventData eventData)
    {
        _isHeldDown = true;
        ApplySprite();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isHeldDown = false;
        ApplySprite();
    }

    // 클릭마다 Pressed 토글 — 스프라이트와 무관, 목록 표시/숨김만 제어.
    // 단, 속성 노드가 연결 대기(무장) 중이면 ALL 에 적용하는 연결을 먼저 처리한다.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (TryCompletePendingLink()) return;

        if (_isExpanded) Collapse();
        else             Expand();
    }

    // 무장된 속성 노드를 ALL(루트 PART)에 연결한다. PartPort 와 동일한 규약.
    private bool TryCompletePendingLink()
    {
        if (!GraphLinkSelection.IsArmed) return false;

        string ideaNodeId = GraphLinkSelection.ArmedNodeId;
        GraphLinkSelection.Clear();

        if (_manager == null || string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning("[AllPort] 연결 실패: manager 또는 node_id 가 비어 있습니다.");
            return true;
        }

        if (_manager.RequestConnectFromPort(_nodeId, ideaNodeId))
            Debug.Log($"[AllPort] 연결 성공: {ideaNodeId} → ALL {_nodeId}");
        else
            Debug.LogWarning($"[AllPort] 연결 실패: {ideaNodeId} → ALL {_nodeId}");

        return true;
    }

    private void Expand()
    {
        _isExpanded = true;
        ApplySprite();
        PopulateConnectedList();
        if (_connectedListContainer != null) _connectedListContainer.gameObject.SetActive(true);
    }

    private void Collapse()
    {
        _isExpanded = false;
        ApplySprite();
        if (_connectedListContainer != null) _connectedListContainer.gameObject.SetActive(false);
        ClearConnectedList();
    }

    // incoming 엣지(PROPERTY/REFERENCE → ALL) 기준으로 X 버튼 목록을 생성한다.
    // X 클릭 시 해당 edge_id만 삭제 (연결된 노드 자체는 유지).
    private void PopulateConnectedList()
    {
        ClearConnectedList();
        if (_connectedListContainer == null || _connectedNodeButtonPrefab == null) return;
        if (_manager == null || string.IsNullOrEmpty(_nodeId)) return;

        List<EdgeData> incoming = _manager.GetEdgesIncomingToNode(_nodeId);
        foreach (EdgeData edge in incoming)
        {
            NodeData node = _manager.GetNode(edge.from_node_id);
            if (node == null) continue;

            string capturedEdgeId = edge.edge_id;
            Button entry = Instantiate(_connectedNodeButtonPrefab, _connectedListContainer);

            TMP_Text labelText = ResolveLabel(entry.transform);
            if (labelText != null) labelText.text = node.DisplayText;

            // PartPort 처럼 원 + X + 이름을 한 칸에 담는 구조를 쓰면 X 는 자식 버튼이다.
            // 예전 구조(루트 자체가 X)도 그대로 동작하도록 둘 다 받는다.
            Button btn = ResolveDeleteButton(entry);
            btn.onClick.AddListener(() =>
            {
                bool ok = _manager.RequestDeleteEdge(capturedEdgeId);
                if (ok)
                {
                    _onChanged?.Invoke();
                    Collapse();
                }
            });
        }
    }

    // 한 칸(ConnectedNodeButton) 안에서 실제로 눌러야 할 X 버튼을 고른다.
    //   새 구조: 칸 안에 "DeleteButton" 자식이 있다(PartPort 와 같은 이름).
    //   옛 구조: 칸 루트가 곧 X 버튼이다.
    private static Button ResolveDeleteButton(Button entry)
    {
        Transform child = entry.transform.Find("DeleteButton");
        Button button = child != null ? child.GetComponent<Button>() : null;
        return button != null ? button : entry;
    }

    // 이름표도 같은 규약. "LabelText" 가 있으면 그것, 없으면 칸 안의 첫 TMP_Text.
    private static TMP_Text ResolveLabel(Transform entry)
    {
        Transform child = entry.Find("LabelText");
        TMP_Text text = child != null ? child.GetComponent<TMP_Text>() : null;
        return text != null ? text : entry.GetComponentInChildren<TMP_Text>(true);
    }

    private void ClearConnectedList()
    {
        if (_connectedListContainer == null) return;
        for (int i = _connectedListContainer.childCount - 1; i >= 0; i--)
            Destroy(_connectedListContainer.GetChild(i).gameObject);
    }

    private void OnDisable()
    {
        if (_isExpanded) Collapse();
    }
}
