/*
 * 파일명: NodeRegistry.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: node_id를 키로 NodeData를 관리하는 레지스트리. GraphManager에서 사용한다.
 * 핵심 내용:
 * - MonoBehaviour가 아닌 순수 C# 클래스이다.
 * - GetAll()은 내부 컬렉션 원본을 노출하지 않고 복사본을 반환한다.
 */
using System.Collections.Generic;

public class NodeRegistry
{
    private readonly Dictionary<string, NodeData> _nodes = new Dictionary<string, NodeData>();

    public void Register(NodeData node)
    {
        _nodes[node.node_id] = node;
    }

    public void Unregister(string nodeId)
    {
        _nodes.Remove(nodeId);
    }

    public NodeData Get(string nodeId)
    {
        _nodes.TryGetValue(nodeId, out NodeData node);
        return node;
    }

    public bool Contains(string nodeId)
    {
        return _nodes.ContainsKey(nodeId);
    }

    // 내부 원본 노출 방지를 위해 복사본 반환
    public List<NodeData> GetAll()
    {
        return new List<NodeData>(_nodes.Values);
    }

    public void Clear()
    {
        _nodes.Clear();
    }
}
