using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 공유 Graph/UI 원본을 바꾸지 않고 MVP 체험 모드의 로컬 노드 + 버튼을 보완한다.
public class MvpLocalChildAddHandle : MonoBehaviour, IPointerClickHandler
{
    private Button _button;
    private NodeActionPanel _panel;
    private GraphManager _graphManager;
    private GraphSyncClient _syncClient;

    private float _nextRefreshTime;
    private string _nodeId;
    private NodeView _node;
    private float _nextAddTime;

    public void Configure(
        Button button,
        NodeActionPanel panel,
        GraphManager graphManager,
        string nodeId,
        NodeView node)
    {
        _button = button;
        _panel = panel;
        _graphManager = graphManager;
        _nodeId = nodeId;
        _node = node;

        EnsureMvpRoute();
        RefreshInteractable();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextRefreshTime)
        return;

        _nextRefreshTime = Time.unscaledTime + 0.2f;
        EnsureMvpRoute();
        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        if (_button == null || _graphManager == null ||
        string.IsNullOrEmpty(_nodeId))
        return;

        // MVP는 서버 ACK 전에도 GraphManager의 로컬 경로로 자식을 만든다.
        _button.interactable = true;
    }

    public void InvokeAdd()
    {
        Debug.Log("[MVP+] InvokeAdd node=" + _nodeId + " t=" +
        Time.unscaledTime.ToString("F2") + " next=" + _nextAddTime.ToString("F2") +
        " gm=" + (_graphManager != null));
        if (_graphManager == null || string.IsNullOrEmpty(_nodeId) ||
        Time.unscaledTime < _nextAddTime)
        return;

        // 디바운스를 먼저 소비해 같은 클릭이 Button.onClick / OnPointerClick 양쪽으로
        // 이중 디스패치돼도 생성이 두 번 일어나지 않게 한다.
        _nextAddTime = Time.unscaledTime + 0.35f;

        // '+'를 직접 포크/클릭한 것 자체가 의도이므로 접촉 게이트로 막지 않는다.
        // (노드 본체 콜라이더를 실제 크기로 줄인 뒤, 버튼이 콜라이더 밖(중심에서 0.28m)이라
        //  게이트가 '+'를 계속 막던 문제 — 이슈 4)
        string newId = _graphManager.RequestCreatePropertyNode(_nodeId);
        if (string.IsNullOrEmpty(newId))
        {
            ShowFeedback(
            "새 조건을 만들지 못했어요. 다시 한 번 눌러 주세요.",
            MvpStudentUiFactory.Coral);
            return;
        }

        ShowFeedback(
        "새 조건이 생겼어요. 이름을 바로 적어 보세요.",
        MvpStudentUiFactory.Mint);

        MvpNodeInteractionController interaction =
        FindFirstObjectByType<MvpNodeInteractionController>();
        interaction?.FocusNodeForRename(newId);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("[MVP+] AddButton OnPointerClick node=" + _nodeId +
        " btn=" + (eventData != null ? eventData.button.ToString() : "null"));
        if (eventData != null &&
        eventData.button != PointerEventData.InputButton.Left)
        return;

        InvokeAdd();
    }


    private static void ShowFeedback(string message, Color color)
    {
        MvpStudentWorkspaceGuide guide =
            FindFirstObjectByType<MvpStudentWorkspaceGuide>();
        if (guide != null)
            guide.ShowLinkFeedback(message, color);
        else
            Debug.Log("[MVP] " + message);
    }

    private void OnDestroy()
    {
        if (_button != null)
        _button.onClick.RemoveListener(InvokeAdd);
    }


    private void EnsureMvpRoute()
    {
        if (_button == null)
        return;

        // 원본 패널은 서버 등록 전 생성 자체를 막으므로 MVP 입력과 분리한다.
        if (_panel != null)
        _button.onClick.RemoveListener(_panel.InvokeAdd);

        // Button.onClick과 XR PointerClick 어느 쪽으로 들어와도 같은 로컬 생성으로 처리한다.
        _button.onClick.RemoveListener(InvokeAdd);
        _button.onClick.AddListener(InvokeAdd);

        Graphic target = _button.targetGraphic;
        if (target == null)
        target = _button.GetComponent<Graphic>() ??
                 _button.GetComponentInChildren<Graphic>(true);
        if (target != null)
        {
            target.raycastTarget = true;
            _button.targetGraphic = target;
        }
    }
}
