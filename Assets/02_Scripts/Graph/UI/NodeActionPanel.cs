using UnityEngine;
using UnityEngine.UI;

// 서브그래프 노드 위에 표시되는 액션 패널.
// Bind(nodeId, manager) 호출 후 버튼이 활성화된다.
//
// 버튼 배치 (이미지 기준):
//   X (DeleteButton) — 왼쪽
//   + (AddButton)    — 오른쪽
//   R (ReferenceButton) — 위쪽 (stub, 패널 호출 미구현)
//
// 버튼 스프라이트: Assets/03_UI/Sprites/Graph/Buttons/
//   DeleteButton    ← Delete_Btn/Delete_Default.png 등
//   AddButton       ← Add_Btn/Add_Default.png 등
//   ReferenceButton ← Reference_Btn/Ref_Default.png 등
public class NodeActionPanel : MonoBehaviour
{
    [Header("버튼 (Inspector 에서 Unity Button 컴포넌트 연결)")]
    [SerializeField] private Button _deleteButton;
    [SerializeField] private Button _addButton;
    [SerializeField] private Button _referenceButton;

    private string       _nodeId;
    private GraphManager _manager;

    public string BoundNodeId => _nodeId;

    // 삭제 전 확인 UI 를 끼워 넣는 훅(MVP 가 설정). (nodeId, 확정 콜백)을 받아
    // true 를 반환하면 삭제를 훅이 책임진다(사용자가 확인하면 콜백 호출).
    // 훅이 없으면(다른 씬) 기존처럼 즉시 삭제한다.
    public static System.Func<string, System.Action, bool> ConfirmDeleteHook;

    // R(레퍼런스) 버튼 동작을 씬별로 주입하는 훅(MVP: 웹뷰 레퍼런스 검색 패널).
    // (nodeId) 를 받아 true 를 반환하면 훅이 처리한 것 — 기존 로그 동작을 건너뛴다.
    public static System.Func<string, bool> ReferenceHook;

    public void Bind(string nodeId, GraphManager manager)
    {
        if (string.IsNullOrEmpty(nodeId) || manager == null)
        {
            Debug.LogWarning("[NodeActionPanel] Bind 실패: nodeId 또는 manager가 null입니다.");
            return;
        }
        _nodeId  = nodeId;
        _manager = manager;

        // "+"(자식 추가)는 서버-known 노드에서만 허용한다. 빈 placeholder(서버 미등록)에서
        // 자식을 만들면 그 자식의 발화 parent 가 서버에 없어 실패하므로 버튼 자체를 비활성화한다.
        // placeholder 가 발화로 채워지면 서버 노드로 대체되고, 그 노드의 패널은 server-known → 활성.
        if (_addButton != null)
            _addButton.interactable = manager.IsServerKnown(nodeId);
    }

    private void OnEnable()
    {
        if (_deleteButton    != null) _deleteButton.onClick.AddListener(InvokeDelete);
        if (_addButton       != null) _addButton.onClick.AddListener(InvokeAdd);
        if (_referenceButton != null) _referenceButton.onClick.AddListener(InvokeReference);
    }

    private void OnDisable()
    {
        if (_deleteButton    != null) _deleteButton.onClick.RemoveListener(InvokeDelete);
        if (_addButton       != null) _addButton.onClick.RemoveListener(InvokeAdd);
        if (_referenceButton != null) _referenceButton.onClick.RemoveListener(InvokeReference);
    }

    // X 버튼: 자손 캐스케이드 삭제 (훅이 있으면 확인 후 삭제)
    public void InvokeDelete()
    {
        if (!EnsureBound("InvokeDelete")) return;

        if (ConfirmDeleteHook != null && ConfirmDeleteHook(_nodeId, ExecuteDelete))
            return;

        ExecuteDelete();
    }

    private void ExecuteDelete()
    {
        if (_manager == null || string.IsNullOrEmpty(_nodeId)) return;
        if (!_manager.RequestDeleteNode(_nodeId))
            Debug.LogWarning($"[NodeActionPanel] 노드 삭제 실패: {_nodeId}");
    }

    // + 버튼: PROPERTY 자식 노드 즉시 생성 (생성 후 노드의 LabelInputField에 키보드로 직접 입력).
    // 서버-known 노드에서만 허용(빈 placeholder 는 버튼 비활성). 방어적으로 한 번 더 확인한다.
    public void InvokeAdd()
    {
        if (!EnsureBound("InvokeAdd")) return;
        if (!_manager.IsServerKnown(_nodeId))
        {
            Debug.LogWarning($"[NodeActionPanel] 자식 추가 보류: 서버 미등록 노드입니다(먼저 발화로 채우세요). {_nodeId}");
            return;
        }
        string newId = _manager.RequestCreatePropertyNode(_nodeId);
        if (newId == null)
            Debug.LogWarning($"[NodeActionPanel] 자식 노드 생성 실패: {_nodeId}");
    }

    // R 버튼: 훅이 있으면(MVP 레퍼런스 패널) 위임, 없으면 컨텍스트 로그만.
    public void InvokeReference()
    {
        if (!EnsureBound("InvokeReference")) return;

        if (ReferenceHook != null && ReferenceHook(_nodeId))
            return;

        var ctx = _manager.CollectReferenceContext(_nodeId);
        Debug.Log($"[NodeActionPanel] R 버튼: chain=[{string.Join(" → ", ctx.chainLabels)}], part={ctx.partLabel ?? "없음"}");
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
