/*
 * 파일명: MainSketchView.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-27
 * 목적: 메인 그래프(2D 스케치 패널)의 루트 컨트롤러.
 *       GraphManager로부터 PART/ALL 노드 상태를 읽어 AllPort/PartPort/AddPartPort UI를 동기화한다.
 * 핵심 내용:
 * - Refresh() 시 PartPortContainer의 자식들을 부분 갱신한다.
 *   기존 PartPort는 node_id가 유지되면 재사용하고, 사라진 PART만 Destroy. AddPartPort는
 *   인스턴스 1개를 계속 유지하면서 SetSiblingIndex로 항상 마지막에 둔다 (깜빡임 최소화).
 * - AllPort는 패널 안에 1개 박혀 있고, PartPort/AddPartPort는 PartPortContainer 자식으로 인스턴스화한다.
 *   PartPortContainer에 HorizontalLayoutGroup이 붙어 있어 자동 정렬된다.
 * - AddPartPort는 + 버튼이 아니라 끝에 항상 따라붙는 빈 원 포트(Empty Part Port)다.
 *   채워진 PartPort가 늘어나면 자동으로 우측에 새 빈 원 포트가 다시 깔린다.
 * - ALL 시각 상태(빈/채워짐)는 GraphManager.GetEdgesConnectedToNode(allNodeId).Count > 0 으로 결정.
 * - PartPort/AddPartPort 의 onChanged 콜백 = this.Refresh.
 */
using System.Collections.Generic;
using UnityEngine;

public class MainSketchView : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [Tooltip("PART 노드 CRUD 서버 REST 경계. PartPort/AddPartPort 에 전달된다.")]
    [SerializeField] private PartNodeApiClient _apiClient;
    [SerializeField] private AllPort _allPort;
    [SerializeField] private Transform _partPortContainer;

    [Header("프리팹")]
    [SerializeField] private PartPort _partPortPrefab;
    [SerializeField] private AddPartPort _addPartPortPrefab;

    [Header("옵션")]
    [SerializeField] private bool _refreshOnStart = true;

    private bool _subscribed;

    private void Start()
    {
        if (_refreshOnStart) Refresh();
    }

    // 서버 그래프 로드/병합(LoadGraph·MergeServerGraph) 후 PART/ALL 포트를 다시 그린다.
    // 발화 응답으로 서버 PART 노드가 들어오면 이 경로로 메인그래프에 반영된다.
    private void OnEnable()
    {
        if (_graphManager == null || _subscribed) return;
        _graphManager.OnGraphChanged += Refresh;
        _subscribed = true;
    }

    private void OnDisable()
    {
        if (_graphManager == null || !_subscribed) return;
        _graphManager.OnGraphChanged -= Refresh;
        _subscribed = false;
    }

    public void Refresh()
    {
        if (!EnsureRefs()) return;

        var nodes = _graphManager.GetAllNodes();

        NodeData allNode = null;
        var partNodes = new List<NodeData>();
        foreach (var n in nodes)
        {
            if (n == null) continue;
            if (n.NodeType != NodeType.PART) continue;
            if (n.is_global)
            {
                // ALL은 1개만 사용. 중복 등록된 경우 첫 번째를 채택.
                if (allNode == null) allNode = n;
            }
            else
            {
                partNodes.Add(n);
            }
        }

        // ALL 포트
        bool allConnected = false;
        if (allNode != null)
        {
            var edges = _graphManager.GetEdgesConnectedToNode(allNode.node_id);
            allConnected = edges != null && edges.Count > 0;
        }
        if (_allPort != null) _allPort.Bind(allNode, _graphManager, Refresh, allConnected);

        // PartPortContainer 부분 갱신: 기존 자식 재사용 + 사라진 것만 Destroy.
        // 1) 기존 자식 분류
        var existingPartPorts = new Dictionary<string, PartPort>();
        AddPartPort existingAdder = null;
        var orphans = new List<GameObject>();
        for (int i = 0; i < _partPortContainer.childCount; i++)
        {
            var child = _partPortContainer.GetChild(i);
            var pp = child.GetComponent<PartPort>();
            if (pp != null && !string.IsNullOrEmpty(pp.NodeId))
            {
                existingPartPorts[pp.NodeId] = pp;
                continue;
            }
            var ap = child.GetComponent<AddPartPort>();
            if (ap != null)
            {
                if (existingAdder == null) existingAdder = ap;
                else                       orphans.Add(child.gameObject); // 중복 AddPartPort는 정리
                continue;
            }
            orphans.Add(child.gameObject);
        }

        // 2) PART 노드 매칭/생성 + sibling index 정렬
        int sibling = 0;
        foreach (var part in partNodes)
        {
            PartPort view;
            if (existingPartPorts.TryGetValue(part.node_id, out view))
            {
                existingPartPorts.Remove(part.node_id); // 사용됨 표시
            }
            else
            {
                view = Instantiate(_partPortPrefab, _partPortContainer);
            }
            var partEdges = _graphManager.GetEdgesConnectedToNode(part.node_id);
            bool partConnected = partEdges != null && partEdges.Count > 0;
            view.Bind(part, _graphManager, _apiClient, Refresh, partConnected);
            view.transform.SetSiblingIndex(sibling++);
        }

        // 3) 더 이상 매칭되지 않는 기존 PartPort 제거 (사용자가 직전에 삭제한 PART 등)
        foreach (var kv in existingPartPorts)
            if (kv.Value != null) Destroy(kv.Value.gameObject);

        // 4) AddPartPort는 1개를 계속 유지. 없으면 생성, 있으면 마지막 sibling으로.
        if (existingAdder == null)
            existingAdder = Instantiate(_addPartPortPrefab, _partPortContainer);
        existingAdder.Bind(_graphManager, _apiClient, Refresh);
        existingAdder.transform.SetSiblingIndex(sibling);

        // 5) 잘못된 자식 정리
        foreach (var go in orphans)
            if (go != null) Destroy(go);
    }

    private bool EnsureRefs()
    {
        if (_graphManager == null)
        {
            Debug.LogWarning("[MainSketchView] Refresh 실패: GraphManager가 연결되지 않았습니다.");
            return false;
        }
        if (_partPortContainer == null)
        {
            Debug.LogWarning("[MainSketchView] Refresh 실패: PartPortContainer가 연결되지 않았습니다.");
            return false;
        }
        if (_partPortPrefab == null || _addPartPortPrefab == null)
        {
            Debug.LogWarning("[MainSketchView] Refresh 실패: PartPort/AddPartPort 프리팹이 연결되지 않았습니다.");
            return false;
        }
        return true;
    }
}
