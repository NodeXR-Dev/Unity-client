/*
 * 파일명: NodeActionPanel.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 목적: 선택된 노드 위에 떠 있는 컨텍스트 패널.
 *       AddButton/DeleteButton 클릭을 GraphManager.RequestCreateNode / RequestDeleteNode 호출로 전달한다.
 * 핵심 내용:
 * - Bind(nodeId, manager) 로 대상 노드와 GraphManager를 설정한다.
 * - Unity UI Button 슬롯 두 개에 OnEnable/OnDisable 에서 클릭 리스너를 연결/해제한다.
 *   디자이너 IconButton 프리팹에는 Button 컴포넌트가 없으므로, Variant 단계에서
 *   같은 GameObject에 Unity Button 을 추가한 뒤 본 컴포넌트의 슬롯에 연결한다.
 *   (디자이너 원본 IconButton.cs 는 수정하지 않는다.)
 * - InvokeAdd() / InvokeDelete() 는 외부 UnityEvent 슬롯에서도 호출할 수 있도록 public.
 * - GraphManager.RequestCreateNode 는 현재 stub이지만 호출은 그대로 전달하고,
 *   디버깅 가시성을 위해 Debug.Log 를 함께 남긴다.
 * - 패널 표시/숨김 및 노드 selection 연동은 본 작업 범위 외 (GR-3-01 또는 후속 IS-* 작업).
 *
 * 사용 위치(예정):
 *   01_Prefabs/Graph/Designer/NodeActionPanel
 *     Canvas (World Space)
 *     ├── AddButton    (Variant of 05_Design/.../AddButton    + Unity Button 추가)  ← _addButton
 *     └── DeleteButton (Variant of 05_Design/.../DeleteButton + Unity Button 추가)  ← _deleteButton
 */
using UnityEngine;
using UnityEngine.UI;

public class NodeActionPanel : MonoBehaviour
{
    [Header("버튼")]
    [Tooltip("AddButton GameObject에 추가한 Unity Button. 클릭 시 RequestCreateNode를 호출한다.")]
    [SerializeField] private Button _addButton;
    [Tooltip("DeleteButton GameObject에 추가한 Unity Button. 클릭 시 RequestDeleteNode를 호출한다.")]
    [SerializeField] private Button _deleteButton;

    [Header("RequestCreateNode 기본 텍스트")]
    [SerializeField] private string _defaultCreateText = "new node";

    private string _nodeId;
    private GraphManager _manager;

    public string BoundNodeId => _nodeId;

    public void Bind(string nodeId, GraphManager manager)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            Debug.LogWarning("[NodeActionPanel] Bind 실패: nodeId가 비어 있습니다.");
            return;
        }
        if (manager == null)
        {
            Debug.LogWarning("[NodeActionPanel] Bind 실패: GraphManager가 null입니다.");
            return;
        }
        _nodeId = nodeId;
        _manager = manager;
    }

    private void OnEnable()
    {
        if (_addButton != null)    _addButton.onClick.AddListener(InvokeAdd);
        if (_deleteButton != null) _deleteButton.onClick.AddListener(InvokeDelete);
    }

    private void OnDisable()
    {
        if (_addButton != null)    _addButton.onClick.RemoveListener(InvokeAdd);
        if (_deleteButton != null) _deleteButton.onClick.RemoveListener(InvokeDelete);
    }

    // UnityEvent 슬롯에서도 호출 가능하도록 public.
    public void InvokeAdd()
    {
        if (!EnsureBound("InvokeAdd")) return;
        Debug.Log($"[NodeActionPanel] RequestCreateNode 호출: parent='{_nodeId}', text='{_defaultCreateText}'");
        _manager.RequestCreateNode(_nodeId, _defaultCreateText);
    }

    public void InvokeDelete()
    {
        if (!EnsureBound("InvokeDelete")) return;
        Debug.Log($"[NodeActionPanel] RequestDeleteNode 호출: nodeId='{_nodeId}'");
        _manager.RequestDeleteNode(_nodeId);
    }

    private bool EnsureBound(string from)
    {
        if (_manager == null || string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning($"[NodeActionPanel] {from} 실패: Bind()가 먼저 호출되어야 합니다.");
            return false;
        }
        return true;
    }
}
