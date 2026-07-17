using UnityEngine;

// GraphManager의 "로컬 사용자 편집" 이벤트를 GraphNetworkManager(Fusion RPC)로 포워딩한다.
// 서버 WS(GraphSyncClient)와 별개로, 같은 Fusion 세션의 다른 참가자에게 그래프 변경을 전파한다.
//
// 에코 루프 방지:
//  - 원격 op는 GraphNetworkManager.Apply*가 GraphManager 저수준 메서드로 적용한다.
//    그 중 위치(RequestMoveNode)만 OnNodeMoved를 재발화하는데, 그때는
//    GraphNetworkManager.IsApplyingRemote == true 이므로 여기서 무시한다.
//  - 세션이 실제로 네트워킹 중일 때만 포워딩(_active).
[DisallowMultipleComponent]
public class MvpGraphNetworkBridge : MonoBehaviour
{
    private GraphManager _graph;
    private GraphNetworkManager _network;
    private bool _active;
    private bool _subscribed;

    // MvpNetworkSession이 GraphNetworkManager 스폰 후 주입한다.
    public void Bind(GraphManager graph, GraphNetworkManager network)
    {
        Unsubscribe();
        _graph = graph;
        _network = network;
        Subscribe();
    }

    // 러너가 실제로 돌 때만 켠다(오프라인/체험 모드에선 꺼서 중복 적용 방지).
    public void SetActive(bool active)
    {
        _active = active;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_graph == null || _subscribed) return;
        _graph.OnNodeCreated += HandleNodeCreated;
        _graph.OnNodeDeleted += HandleNodeDeleted;
        _graph.OnNodeMoved += HandleNodeMoved;
        _graph.OnNodeTextUpdated += HandleNodeTextUpdated;
        _graph.OnEdgeCreated += HandleEdgeCreated;
        _graph.OnEdgeDeleted += HandleEdgeDeleted;
        _graph.OnNodeRekeyed += HandleNodeRekeyed;
        _graph.OnEdgeRekeyed += HandleEdgeRekeyed;
        _graph.OnNodeActiveChanged += HandleNodeActiveChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_graph == null || !_subscribed) return;
        _graph.OnNodeCreated -= HandleNodeCreated;
        _graph.OnNodeDeleted -= HandleNodeDeleted;
        _graph.OnNodeMoved -= HandleNodeMoved;
        _graph.OnNodeTextUpdated -= HandleNodeTextUpdated;
        _graph.OnEdgeCreated -= HandleEdgeCreated;
        _graph.OnEdgeDeleted -= HandleEdgeDeleted;
        _graph.OnNodeRekeyed -= HandleNodeRekeyed;
        _graph.OnEdgeRekeyed -= HandleEdgeRekeyed;
        _graph.OnNodeActiveChanged -= HandleNodeActiveChanged;
        _subscribed = false;
    }

    private bool ShouldForward()
    {
        return _active &&
               _network != null &&
               !_network.IsApplyingRemote;
    }

    private void HandleNodeCreated(
        string nodeId, string text, string parentId,
        string subGraphId, Vector3 position)
    {
        if (!ShouldForward()) return;

        NodeData node = _graph != null ? _graph.GetNode(nodeId) : null;
        if (node != null)
            _network.RequestCreateNode(node, parentId);
        else
            _network.RequestCreateNode(
                nodeId, NodeType.PROPERTY, position, text, text, parentId);
    }

    private void HandleNodeDeleted(string nodeId)
    {
        if (!ShouldForward()) return;
        _network.RequestDeleteNode(nodeId);
    }

    private void HandleNodeMoved(string nodeId, Vector3 position)
    {
        if (!ShouldForward()) return;
        _network.RequestUpdateNodePosition(nodeId, position);
    }

    private void HandleNodeTextUpdated(string nodeId, string text)
    {
        if (!ShouldForward()) return;
        NodeData node = _graph != null ? _graph.GetNode(nodeId) : null;
        string label = node != null ? node.label : text;
        _network.RequestUpdateNodeText(nodeId, label, text);
    }

    private void HandleEdgeCreated(EdgeData edge)
    {
        if (!ShouldForward() || edge == null) return;
        _network.RequestCreateEdge(edge);
    }

    private void HandleEdgeDeleted(string edgeId)
    {
        if (!ShouldForward()) return;
        _network.RequestDeleteEdge(edgeId);
    }

    // 서버 ACK rekey(로컬 임시 id → 서버 id)를 피어에도 전파한다.
    // 이것이 없으면 피어는 임시 id 로 남아 이후 이동/삭제 RPC 를 놓친다.
    private void HandleNodeRekeyed(string oldId, string newId, string newText)
    {
        if (!ShouldForward()) return;
        _network.RequestRekeyNode(oldId, newId, newText ?? "");
    }

    private void HandleEdgeRekeyed(string oldId, string newId)
    {
        if (!ShouldForward()) return;
        _network.RequestRekeyEdge(oldId, newId);
    }

    private void HandleNodeActiveChanged(string nodeId, bool active)
    {
        if (!ShouldForward()) return;
        _network.RequestSetNodeActive(nodeId, active);
    }
}
