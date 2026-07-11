using System.Collections.Generic;
using UnityEngine;

// GraphData의 적용 엣지(PROPERTY/REFERENCE → PART)를 서버 2D 그래프 스케치 요청의 connections 배열로 변환한다.
// 명세(2026-07-10): POST /api/2d/generate/graph { room_id, user_id, connections=[{ part_node_id, node_id }] }.
//   node_id = 사용자가 PART에 연결한 서브그래프 기점(맨 하위 leaf). 서버가 거기서 상위 체인을 탐색.
// 규칙: 엣지 to = PART, from = PROPERTY 또는 REFERENCE 인 것만 적용 엣지로 본다.
// [주의] 서버 /api/2d/generate/graph 배포 전엔 전송 실패 가능. 이 빌더는 계약 대비 Unity 선구현.
public static class RegenerateConnectionBuilder
{
    // 모든 적용 엣지를 ConnectionDto 목록으로 변환.
    public static List<ConnectionDto> Build(GraphData graph)
        => Build(graph, null);

    // selectedPartNodeIds 가 주어지면 해당 PART 로 향하는 연결만 담는다(선택 기반 부분 재생성).
    public static List<ConnectionDto> Build(GraphData graph, ICollection<string> selectedPartNodeIds)
    {
        var result = new List<ConnectionDto>();
        if (graph?.nodes == null || graph.edges == null)
        {
            Debug.LogWarning("[RegenerateConnectionBuilder] Build 실패: graph/nodes/edges 가 null 입니다.");
            return result;
        }

        // node_id → NodeType 조회 맵
        var typeById = new Dictionary<string, NodeType>();
        foreach (var node in graph.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.node_id)) continue;
            typeById[node.node_id] = node.NodeType;
        }

        foreach (var edge in graph.edges)
        {
            if (edge == null ||
                string.IsNullOrEmpty(edge.from_node_id) ||
                string.IsNullOrEmpty(edge.to_node_id))
                continue;

            if (!typeById.TryGetValue(edge.to_node_id,   out NodeType toType))   continue;
            if (!typeById.TryGetValue(edge.from_node_id, out NodeType fromType)) continue;

            // 적용 엣지: to = PART, from = PROPERTY 또는 REFERENCE
            if (toType != NodeType.PART) continue;
            if (fromType != NodeType.PROPERTY && fromType != NodeType.REFERENCE) continue;

            // 부분 재생성: 선택된 PART 로 향하는 연결만
            if (selectedPartNodeIds != null && !selectedPartNodeIds.Contains(edge.to_node_id))
                continue;

            result.Add(new ConnectionDto
            {
                part_node_id = edge.to_node_id,
                node_id      = edge.from_node_id,
            });
        }

        return result;
    }

    // room_id / user_id / connections 를 채운 /api/2d/generate/graph 요청 DTO 생성.
    public static Generate2DGraphRequest BuildGraphRequest(
        GraphData graph, string userId, ICollection<string> selectedPartNodeIds = null)
    {
        return new Generate2DGraphRequest
        {
            room_id     = graph?.room_id,
            user_id     = userId,
            connections = Build(graph, selectedPartNodeIds),
        };
    }

    // JsonUtility 로 요청 바디(JSON) 직렬화. (POST body)
    public static string BuildGraphRequestJson(
        GraphData graph, string userId, ICollection<string> selectedPartNodeIds = null)
        => JsonUtility.ToJson(BuildGraphRequest(graph, userId, selectedPartNodeIds));
}
