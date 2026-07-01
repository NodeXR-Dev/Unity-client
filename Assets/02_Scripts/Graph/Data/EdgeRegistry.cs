/*
 * 파일명: EdgeRegistry.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: edge_id를 키로 EdgeData를 관리하고, 노드 기준 엣지 조회를 지원한다. GraphManager에서 사용한다.
 * 핵심 내용:
 * - MonoBehaviour가 아닌 순수 C# 클래스이다.
 * - 엣지 관계는 from/to 노드 타입 조합으로 해석한다. 별도 엣지 타입은 없다.
 *   허용 조합: PROPERTY→PROPERTY, PROPERTY→PART, REFERENCE→PROPERTY, REFERENCE→PART
 * - GetAll(), GetEdgesFrom(), GetEdgesTo(), GetEdgesConnectedTo()는 복사본을 반환한다.
 */
using System.Collections.Generic;

public class EdgeRegistry
{
    private readonly Dictionary<string, EdgeData> _edges = new Dictionary<string, EdgeData>();

    public void Register(EdgeData edge)
    {
        _edges[edge.edge_id] = edge;
    }

    public void Unregister(string edgeId)
    {
        _edges.Remove(edgeId);
    }

    public EdgeData Get(string edgeId)
    {
        _edges.TryGetValue(edgeId, out EdgeData edge);
        return edge;
    }

    public bool Contains(string edgeId)
    {
        return _edges.ContainsKey(edgeId);
    }

    // 내부 원본 노출 방지를 위해 복사본 반환
    public List<EdgeData> GetAll()
    {
        return new List<EdgeData>(_edges.Values);
    }

    public void Clear()
    {
        _edges.Clear();
    }

    // 해당 노드가 from인 엣지 목록
    public List<EdgeData> GetEdgesFrom(string nodeId)
    {
        var result = new List<EdgeData>();
        foreach (var edge in _edges.Values)
            if (edge.from_node_id == nodeId) result.Add(edge);
        return result;
    }

    // 해당 노드가 to인 엣지 목록
    public List<EdgeData> GetEdgesTo(string nodeId)
    {
        var result = new List<EdgeData>();
        foreach (var edge in _edges.Values)
            if (edge.to_node_id == nodeId) result.Add(edge);
        return result;
    }

    // 해당 노드가 from 또는 to인 엣지 전체 (노드 삭제 시 연결 정리용)
    public List<EdgeData> GetEdgesConnectedTo(string nodeId)
    {
        var result = new List<EdgeData>();
        foreach (var edge in _edges.Values)
            if (edge.from_node_id == nodeId || edge.to_node_id == nodeId) result.Add(edge);
        return result;
    }

    // 동일 방향 중복 엣지 여부 확인
    public bool HasEdge(string fromNodeId, string toNodeId)
    {
        foreach (var edge in _edges.Values)
            if (edge.from_node_id == fromNodeId && edge.to_node_id == toNodeId) return true;
        return false;
    }
}
