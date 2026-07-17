/*
 * 파일명: MeetingRoomHistoryGraphView.cs
 * 목적: 디자이너 NodeBox 프리팹으로 그래프(스냅샷)를 렌더하는 히스토리 전용 뷰.
 *       기존 GraphManager(구체 NodePrefab 전용) 대신, NodeBox 를 쓰기 위해 별도로 둔다.
 *
 * Render(GraphData) 호출 시:
 *  - 노드마다 NodeBox 인스턴스 생성 → 라벨/색 채우고 위치 배치
 *  - 엣지마다 LineRenderer 로 from.PortOut → to.PortIn 연결
 * 스냅샷은 정적이므로 매 Render 마다 통째로 다시 그린다.
 *
 * 참고: NodeBox 는 크기가 큰 3D 카드라, mock 좌표와 겹칠 수 있어 nodeScale/layoutSpread 로 조정한다.
 */
using System.Collections.Generic;
using UnityEngine;

public class MeetingRoomHistoryGraphView : MonoBehaviour
{
    [Header("프리팹 / 루트")]
    [Tooltip("디자이너 NodeBox 프리팹 (05_Design/JW/UI/Prefabs/NodeBox)")]
    [SerializeField] private GameObject nodeBoxPrefab;
    [Tooltip("생성물이 담길 부모. 비우면 자기 자신.")]
    [SerializeField] private Transform graphRoot;

    [Header("레이아웃 / 외형")]
    [Tooltip("NodeBox 는 크므로 작게. 노드 겹치면 줄인다.")]
    [SerializeField] private float nodeScale = 0.1f;
    [Tooltip("노드 좌표 간격 배수(겹침 방지)")]
    [SerializeField] private float layoutSpread = 1f;
    [SerializeField] private float edgeWidth = 0.02f;

    [Header("노드 타입 색")]
    [SerializeField] private Color partColor = new Color(0.3f, 0.55f, 1f);
    [SerializeField] private Color propertyColor = new Color(0.4f, 0.85f, 0.4f);
    [SerializeField] private Color referenceColor = new Color(1f, 0.6f, 0.2f);
    [SerializeField] private Color unknownColor = Color.gray;

    private Material _edgeMat;
    private readonly Dictionary<string, MeetingRoomHistoryNodeBox> _nodeMap = new Dictionary<string, MeetingRoomHistoryNodeBox>();

    private Transform Root => graphRoot != null ? graphRoot : transform;

    public void Render(GraphData data)
    {
        Clear();
        if (data == null) return;

        if (nodeBoxPrefab == null)
        {
            Debug.LogWarning("[History] nodeBoxPrefab 이 연결되지 않았습니다. NodeBox 프리팹을 연결하세요.");
            return;
        }

        EnsureEdgeMaterial();

        // 1) 노드
        if (data.nodes != null)
        {
            foreach (var node in data.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.node_id)) continue;

                var go = Instantiate(nodeBoxPrefab, Root);
                go.name = $"NodeBox_{node.node_id}";
                go.transform.localPosition = node.Position * layoutSpread;
                go.transform.localScale = Vector3.one * nodeScale;

                var box = go.GetComponent<MeetingRoomHistoryNodeBox>();
                if (box == null) box = go.AddComponent<MeetingRoomHistoryNodeBox>();
                box.Init();
                box.SetText(node.DisplayText);
                box.SetColor(GetColor(node.NodeType));

                _nodeMap[node.node_id] = box;
            }
        }

        // 2) 엣지
        if (data.edges != null)
        {
            foreach (var edge in data.edges)
            {
                if (edge == null) continue;
                if (!_nodeMap.TryGetValue(edge.from_node_id, out var from) ||
                    !_nodeMap.TryGetValue(edge.to_node_id, out var to))
                    continue;

                SpawnEdge(edge.edge_id, from.PortOut, to.PortIn);
            }
        }
    }

    public void Clear()
    {
        var root = Root;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        _nodeMap.Clear();
    }

    private void SpawnEdge(string edgeId, Transform from, Transform to)
    {
        var go = new GameObject($"Edge_{edgeId}");
        go.transform.SetParent(Root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = _edgeMat;
        lr.widthMultiplier = edgeWidth;
        lr.numCapVertices = 2;
        lr.positionCount = 2;
        lr.startColor = lr.endColor = Color.white;
        lr.SetPosition(0, from.position);
        lr.SetPosition(1, to.position);
    }

    private void EnsureEdgeMaterial()
    {
        if (_edgeMat != null) return;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _edgeMat = new Material(sh) { name = "HistoryEdge (auto)" };
    }

    private Color GetColor(NodeType type)
    {
        switch (type)
        {
            case NodeType.PART: return partColor;
            case NodeType.PROPERTY: return propertyColor;
            case NodeType.REFERENCE: return referenceColor;
            default: return unknownColor;
        }
    }
}
