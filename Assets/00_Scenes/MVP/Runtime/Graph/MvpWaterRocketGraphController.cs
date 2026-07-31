using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class MvpWaterRocketGraphController : MonoBehaviour
{
    [SerializeField] private GraphManager _graphManager;

    private readonly Dictionary<string, string> _partIds =
        new Dictionary<string, string>();
    private bool _subscribed;

    public event Action OnDesignChanged;

    public int RequirementCount
    {
        get
        {
            int count = 0;
            if (_graphManager == null) return count;
            foreach (NodeData node in _graphManager.GetAllNodes())
                if (node != null && node.NodeType == NodeType.PROPERTY)
                    count++;
            return count;
        }
    }

    public int PartCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<string, string> pair in _partIds)
                if (pair.Key != "전체" &&
                    !string.IsNullOrEmpty(pair.Value))
                    count++;
            return count;
        }
    }

    public int AppliedConnectionCount
    {
        get
        {
            int count = 0;
            GraphData graph = _graphManager != null
                ? _graphManager.GetGraphData()
                : null;
            if (graph?.edges == null) return count;

            foreach (EdgeData edge in graph.edges)
            {
                NodeData from = _graphManager.GetNode(edge.from_node_id);
                NodeData to = _graphManager.GetNode(edge.to_node_id);
                if (from != null && to != null &&
                    from.NodeType == NodeType.PROPERTY &&
                    to.NodeType == NodeType.PART)
                    count++;
            }
            return count;
        }
    }

    public bool HasMinimumDesign =>
        RequirementCount >= 3 && CorePartConnectionCount >= 3;

    public int CorePartConnectionCount
    {
        get
        {
            int connected = 0;
            if (IsPartConnected("몸통")) connected++;
            if (IsPartConnected("날개")) connected++;
            if (IsPartConnected("노즈콘")) connected++;
            return connected;
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void InitializeWaterRocketGraph()
    {
        ResolveReferences();
        if (_graphManager == null)
        {
            Debug.LogWarning("[MVP Flow] GraphManager를 찾을 수 없습니다.");
            return;
        }

        ClearGraph();

        _partIds.Clear();
        // ALL('전체') 포트는 MVP에서 제거 — 학생은 부품(몸통/날개/노즈콘)에 직접 연결한다.

        _graphManager.RenderGraph();
        NotifyDesignChanged();
        Debug.Log(
            "[MVP Flow] 빈 물로켓 설계를 준비했습니다. " +
            "학생이 AI 추천을 선택하면 PART가 추가됩니다.");
    }

    public bool AddPart(string partLabel)
    {
        ResolveReferences();
        string label = NormalizePartLabel(partLabel);
        if (_graphManager == null || string.IsNullOrEmpty(label))
            return false;

        if (_partIds.TryGetValue(label, out string existingId) &&
            !string.IsNullOrEmpty(existingId))
            return true;

        string partId =
            _graphManager.RequestCreatePartNode(label, false);
        if (string.IsNullOrEmpty(partId))
            return false;

        _partIds[label] = partId;
        _graphManager.RenderGraph();
        NotifyDesignChanged();
        Debug.Log("[MVP Flow] 부품 추가: " + label);
        return true;
    }

    public bool HasPart(string partLabel)
    {
        string label = NormalizePartLabel(partLabel);
        return !string.IsNullOrEmpty(label) &&
               _partIds.TryGetValue(label, out string partId) &&
               !string.IsNullOrEmpty(partId);
    }


    public bool IsPartConnected(string partLabel)
    {
        if (_graphManager == null ||
            !_partIds.TryGetValue(partLabel, out string partId))
            return false;

        List<EdgeData> edges =
            _graphManager.GetEdgesConnectedToNode(partId);
        foreach (EdgeData edge in edges)
        {
            if (edge == null || edge.to_node_id != partId) continue;
            NodeData source = _graphManager.GetNode(edge.from_node_id);
            if (source != null && source.NodeType == NodeType.PROPERTY)
                return true;
        }
        return false;
    }

    public string GetDesignSummary()
    {
        if (_graphManager == null)
            return "아직 설계 정보가 없습니다.";

        GraphData graph = _graphManager.GetGraphData();
        if (graph?.edges == null)
            return "아직 연결된 요구사항이 없습니다.";

        StringBuilder builder = new StringBuilder();
        AppendPartSummary(builder, "몸통");
        AppendPartSummary(builder, "날개");
        AppendPartSummary(builder, "노즈콘");

        if (builder.Length == 0)
            return "요구사항을 부품에 연결해 주세요.";
        return builder.ToString().TrimEnd();
    }

    // 그래프를 읽어 "이 팀이 설계한 로켓"의 형태 요약을 만든다.
    // GraphManager 공개 API만 읽으며 GraphData를 수정하지 않는다.
    public MvpRocketDesign GetRocketDesign(int variant)
    {
        MvpRocketDesign design = new MvpRocketDesign();
        design.accentIndex = variant;

        if (_graphManager == null)
            return design;

        design.partCount = PartCount;
        design.requirementCount = RequirementCount;
        design.connectionCount = AppliedConnectionCount;

        List<string> bodyReqs = GetPartRequirements("몸통");
        List<string> finReqs = GetPartRequirements("날개");
        List<string> noseReqs = GetPartRequirements("노즈콘");

        design.hasBody = bodyReqs.Count > 0;
        design.hasFins = finReqs.Count > 0;
        design.hasNose = noseReqs.Count > 0;

        CopyTop(bodyReqs, design.bodyReqs, 3);
        CopyTop(finReqs, design.finReqs, 3);
        CopyTop(noseReqs, design.noseReqs, 3);

        // 몸통: 가벼움/슬림/길다 → 길고 얇게, 무거움/두껍다/짧다 → 짧고 통통하게.
        float length = 1f;
        float slim = 1f;
        foreach (string r in bodyReqs)
        {
            if (HasAny(r, "가벼", "얇", "슬림", "날씬", "길", "높")) { length += 0.12f; slim += 0.06f; }
            if (HasAny(r, "무겁", "두껍", "짧", "통통", "튼튼", "안정")) { length -= 0.1f; slim -= 0.06f; }
        }
        design.bodyLength = Mathf.Clamp(length, 0.78f, 1.35f);
        design.bodySlim = Mathf.Clamp(slim, 0.82f, 1.16f);

        // 날개: 넓다/크다/많다 → 넓게, 작다/좁다 → 작게. 개수도 키워드로 살짝 조정.
        float span = 1f;
        int fins = 3;
        foreach (string r in finReqs)
        {
            if (HasAny(r, "넓", "큰", "크게", "많", "길")) { span += 0.16f; }
            if (HasAny(r, "작", "좁", "적", "짧")) { span -= 0.14f; }
            if (HasAny(r, "4", "네", "많")) fins = 4;
            if (HasAny(r, "2", "두")) fins = 2;
        }
        design.finSpan = Mathf.Clamp(span, 0.6f, 1.5f);
        design.finCount = Mathf.Clamp(fins, 2, 4);

        // 노즈: 뾰족/멀리/빠름 → 뾰족, 안전/둥글 → 둥글게. 기본은 뾰족(전형적 로켓).
        bool pointed = true;
        foreach (string r in noseReqs)
        {
            if (HasAny(r, "안전", "둥", "부드", "보호", "무디")) pointed = false;
            if (HasAny(r, "뾰족", "날카", "멀리", "빠르", "스피드", "속도")) pointed = true;
        }
        design.pointedNose = design.hasNose ? pointed : true;

        // 물 양: 모든 요구사항에서 물 관련 키워드 스캔.
        float fill = 0.42f;
        foreach (string r in AllRequirementLabels())
        {
            if (HasAny(r, "물 많", "물많", "가득", "물 가득", "높은 물", "최대")) fill += 0.14f;
            if (HasAny(r, "물 적", "물적", "조금", "낮은 물", "최소", "가벼")) fill -= 0.1f;
        }
        design.waterFill = Mathf.Clamp(fill, 0.2f, 0.72f);

        return design;
    }

    private List<string> GetPartRequirements(string partLabel)
    {
        List<string> labels = new List<string>();
        if (_graphManager == null ||
            !_partIds.TryGetValue(partLabel, out string partId) ||
            string.IsNullOrEmpty(partId))
            return labels;

        foreach (EdgeData edge in
                 _graphManager.GetEdgesConnectedToNode(partId))
        {
            if (edge == null || edge.to_node_id != partId) continue;
            NodeData source = _graphManager.GetNode(edge.from_node_id);
            if (source == null ||
                source.NodeType != NodeType.PROPERTY)
                continue;
            string text = source.DisplayText;
            if (!string.IsNullOrEmpty(text))
                labels.Add(text);
            // 활성화된 자식 후손의 특성도 반영(비활성 자식은 이미지에 영향 없음).
            AppendActiveDescendants(source.node_id, labels);
        }
        return labels;
    }

    // 부모 아래의 '활성' PROPERTY 후손 텍스트를 재귀 수집한다(비활성 가지는 건너뜀).
    private void AppendActiveDescendants(string parentId, List<string> labels)
    {
        foreach (NodeData node in _graphManager.GetAllNodes())
        {
            if (node == null ||
                node.NodeType != NodeType.PROPERTY ||
                string.IsNullOrEmpty(node.node_id))
                continue;
            if (_graphManager.GetPropertyParentId(node.node_id) != parentId)
                continue;
            if (!_graphManager.IsNodeActive(node.node_id))
                continue;
            if (!string.IsNullOrEmpty(node.DisplayText))
                labels.Add(node.DisplayText);
            AppendActiveDescendants(node.node_id, labels);
        }
    }

    private List<string> AllRequirementLabels()
    {
        List<string> labels = new List<string>();
        if (_graphManager == null) return labels;
        foreach (NodeData node in _graphManager.GetAllNodes())
            if (node != null &&
                node.NodeType == NodeType.PROPERTY &&
                !string.IsNullOrEmpty(node.DisplayText))
                labels.Add(node.DisplayText);
        return labels;
    }

    private static void CopyTop(
        List<string> source, List<string> target, int max)
    {
        for (int i = 0; i < source.Count && i < max; i++)
            target.Add(source[i]);
    }

    private static bool HasAny(string text, params string[] needles)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string needle in needles)
            if (text.IndexOf(needle, StringComparison.Ordinal) >= 0)
                return true;
        return false;
    }

    public void ClearGraph()
    {
        if (_graphManager == null) return;

        int guard = 0;
        while (guard++ < 100)
        {
            List<NodeData> nodes = _graphManager.GetAllNodes();
            if (nodes == null || nodes.Count == 0) break;

            NodeData first = nodes[0];
            if (first == null ||
                string.IsNullOrEmpty(first.node_id) ||
                !_graphManager.RequestDeleteNode(
                    first.node_id, emitSync: false))
                break;
        }

        _partIds.Clear();
        _graphManager.RenderGraph();
        NotifyDesignChanged();
    }

    private void AppendPartSummary(
        StringBuilder builder,
        string partLabel)
    {
        if (!_partIds.TryGetValue(partLabel, out string partId))
            return;

        List<string> labels = new List<string>();
        foreach (EdgeData edge in
                 _graphManager.GetEdgesConnectedToNode(partId))
        {
            if (edge == null || edge.to_node_id != partId) continue;
            NodeData source = _graphManager.GetNode(edge.from_node_id);
            if (source == null ||
                source.NodeType != NodeType.PROPERTY)
                continue;
            labels.Add(source.DisplayText);
        }

        if (labels.Count == 0)
        {
            builder.Append("• ");
            builder.Append(partLabel);
            builder.Append(": 연결된 요구사항 없음\n");
            return;
        }

        builder.Append("• ");
        builder.Append(partLabel);
        builder.Append(": ");
        builder.Append(string.Join(", ", labels));
        builder.Append('\n');
    }


    private string NormalizePartLabel(string value)
    {
        string label = (value ?? "").Trim();
        if (label.Length > 12)
            label = label.Substring(0, 12).Trim();
        return label;
    }

    private void ResolveReferences()
    {
        if (_graphManager == null)
            _graphManager =
                FindFirstObjectByType<GraphManager>();
    }

    private void Subscribe()
    {
        if (_graphManager == null || _subscribed) return;
        _graphManager.OnEdgeCreated += HandleEdgeCreated;
        _graphManager.OnEdgeDeleted += HandleEdgeDeleted;
        _graphManager.OnNodeDeleted += HandleNodeDeleted;
        _graphManager.OnNodeTextUpdated += HandleNodeTextUpdated;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graphManager == null || !_subscribed) return;
        _graphManager.OnEdgeCreated -= HandleEdgeCreated;
        _graphManager.OnEdgeDeleted -= HandleEdgeDeleted;
        _graphManager.OnNodeDeleted -= HandleNodeDeleted;
        _graphManager.OnNodeTextUpdated -= HandleNodeTextUpdated;
        _subscribed = false;
    }

    private void HandleEdgeCreated(EdgeData edge)
    {
        NotifyDesignChanged();
    }

    private void HandleEdgeDeleted(string edgeId)
    {
        NotifyDesignChanged();
    }

    private void HandleNodeDeleted(string nodeId)
    {
        NotifyDesignChanged();
    }

    private void HandleNodeTextUpdated(string nodeId, string text)
    {
        NotifyDesignChanged();
    }

    private void NotifyDesignChanged()
    {
        OnDesignChanged?.Invoke();
    }
}

