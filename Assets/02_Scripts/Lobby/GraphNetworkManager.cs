using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class GraphNetworkManager : NetworkBehaviour
{
    [Header("Graph")]
    [SerializeField] private GraphManager graphManager;
    [SerializeField] private bool autoFindGraphManager = true;
    [SerializeField] private bool renderAfterApply = true;
    [SerializeField] private bool applyLocallyWhenOffline = true;

    [Header("Node Lock")]
    [SerializeField] private bool requireLockForNodeEdits = true;
    [SerializeField] private bool allowStateAuthorityLockOverride = true;
    [SerializeField] private bool logLockConflicts = true;

    [Networked, Capacity(128)]
    private NetworkDictionary<NetworkString<_128>, PlayerRef> NodeLocks => default;

    public event Action<string, PlayerRef> NodeLockAcquired;
    public event Action<string, PlayerRef> NodeLockReleased;
    public event Action<string, PlayerRef, PlayerRef> NodeLockDenied;

    private readonly Dictionary<string, PlayerRef> lockCache = new Dictionary<string, PlayerRef>();

    private bool IsReadyForRpc => Runner != null && Runner.IsRunning && Object != null;
    private bool CanBroadcast => IsReadyForRpc && HasStateAuthority;

    public override void Spawned()
    {
        ResolveGraphManager();
        RebuildLockCacheFromNetworkState();
    }

    public bool TryBeginNodeEdit(string nodeId)
    {
        if (CanLocalPlayerEditNode(nodeId))
            return true;

        RequestLockNode(nodeId);
        return false;
    }

    public void EndNodeEdit(string nodeId)
    {
        RequestUnlockNode(nodeId);
    }

    public void RequestLockNode(string nodeId)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            TryAcquireNodeLockAsAuthority(safeNodeId, Runner.LocalPlayer);
        else
            RPC_RequestLockNode(safeNodeId);
    }

    public void RequestUnlockNode(string nodeId)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            TryReleaseNodeLockAsAuthority(safeNodeId, Runner.LocalPlayer);
        else
            RPC_RequestUnlockNode(safeNodeId);
    }

    public void RequestReleaseLocalNodeLocks()
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            ReleaseLocksForPlayerAsAuthority(Runner.LocalPlayer);
        else
            RPC_RequestReleaseLocksForPlayer();
    }

    public bool IsNodeLocked(string nodeId)
    {
        return TryGetNodeLockOwner(nodeId, out _);
    }

    public bool IsNodeLockedByLocalPlayer(string nodeId)
    {
        if (!IsReadyForRpc)
            return false;

        return TryGetNodeLockOwner(nodeId, out PlayerRef owner) && owner == Runner.LocalPlayer;
    }

    public bool CanLocalPlayerEditNode(string nodeId)
    {
        if (!requireLockForNodeEdits)
            return true;

        if (!IsReadyForRpc)
            return applyLocallyWhenOffline;

        if (allowStateAuthorityLockOverride && HasStateAuthority)
            return true;

        return IsNodeLockedByLocalPlayer(nodeId);
    }

    public bool ShouldDisableNodeInteraction(string nodeId)
    {
        return IsNodeLocked(nodeId) && !IsNodeLockedByLocalPlayer(nodeId);
    }

    public bool TryGetNodeLockOwner(string nodeId, out PlayerRef owner)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId))
        {
            owner = PlayerRef.None;
            return false;
        }

        if (lockCache.TryGetValue(safeNodeId, out owner))
            return true;

        if (IsReadyForRpc && NodeLocks.TryGet(ToLockKey(safeNodeId), out owner))
        {
            lockCache[safeNodeId] = owner;
            return true;
        }

        owner = PlayerRef.None;
        return false;
    }

    public void RequestCreateNode(NodeData node, string parentNodeId = "", string createdBy = "")
    {
        if (node == null)
        {
            Debug.LogWarning("[GraphNetworkManager] CreateNode ignored: node is null.");
            return;
        }

        RequestCreateNode(
            node.node_id,
            node.NodeType,
            node.Position,
            node.label,
            node.node_text,
            parentNodeId,
            createdBy,
            node.property_category,
            node.is_global);
    }

    public void RequestCreateNode(
        string nodeId,
        NodeType nodeType,
        Vector3 position,
        string label,
        string description = "",
        string parentNodeId = "",
        string createdBy = "",
        string propertyCategory = "",
        bool isGlobal = false)
    {
        string safeNodeId = EnsureId(nodeId, "node");
        string safeCreatedBy = string.IsNullOrWhiteSpace(createdBy) ? GetLocalUserName() : createdBy.Trim();

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyCreateNode(safeNodeId, (int)nodeType, position, label, description, parentNodeId, safeCreatedBy, propertyCategory, isGlobal));
            return;
        }

        if (CanBroadcast)
        {
            RPC_BroadcastCreateNode(safeNodeId, (int)nodeType, position, Safe(label), Safe(description), Safe(parentNodeId), safeCreatedBy, Safe(propertyCategory), isGlobal);
        }
        else
        {
            RPC_RequestCreateNode(safeNodeId, (int)nodeType, position, Safe(label), Safe(description), Safe(parentNodeId), safeCreatedBy, Safe(propertyCategory), isGlobal);
        }
    }

    public void RequestDeleteNode(string nodeId)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!CanRequestNodeMutation(safeNodeId, "DeleteNode"))
            return;

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyDeleteNode(safeNodeId));
            return;
        }

        if (CanBroadcast)
            DeleteNodeAsAuthority(safeNodeId);
        else
            RPC_RequestDeleteNode(safeNodeId);
    }

    public void RequestUpdateNodePosition(string nodeId, Vector3 position)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!CanRequestNodeMutation(safeNodeId, "UpdateNodePosition"))
            return;

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyUpdateNodePosition(safeNodeId, position));
            return;
        }

        if (CanBroadcast)
            RPC_BroadcastUpdateNodePosition(safeNodeId, position);
        else
            RPC_RequestUpdateNodePosition(safeNodeId, position);
    }

    public void RequestUpdateNodeText(string nodeId, string label, string description = "")
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!CanRequestNodeMutation(safeNodeId, "UpdateNodeText"))
            return;

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyUpdateNodeText(safeNodeId, label, description));
            return;
        }

        if (CanBroadcast)
            RPC_BroadcastUpdateNodeText(safeNodeId, Safe(label), Safe(description));
        else
            RPC_RequestUpdateNodeText(safeNodeId, Safe(label), Safe(description));
    }

    public void RequestCreateEdge(EdgeData edge, string edgeType = "")
    {
        if (edge == null)
        {
            Debug.LogWarning("[GraphNetworkManager] CreateEdge ignored: edge is null.");
            return;
        }

        RequestCreateEdge(edge.edge_id, edge.from_node_id, edge.to_node_id, edgeType);
    }

    public void RequestCreateEdge(string edgeId, string fromNodeId, string toNodeId, string edgeType = "")
    {
        string safeEdgeId = EnsureId(edgeId, "edge");
        string safeFromNodeId = Safe(fromNodeId);
        string safeToNodeId = Safe(toNodeId);

        if (string.IsNullOrWhiteSpace(safeFromNodeId) || string.IsNullOrWhiteSpace(safeToNodeId))
        {
            Debug.LogWarning("[GraphNetworkManager] CreateEdge ignored: from/to node id is empty.");
            return;
        }

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyCreateEdge(safeEdgeId, safeFromNodeId, safeToNodeId, edgeType));
            return;
        }

        if (CanBroadcast)
            RPC_BroadcastCreateEdge(safeEdgeId, safeFromNodeId, safeToNodeId, Safe(edgeType));
        else
            RPC_RequestCreateEdge(safeEdgeId, safeFromNodeId, safeToNodeId, Safe(edgeType));
    }

    public void RequestDeleteEdge(string edgeId)
    {
        string safeEdgeId = Safe(edgeId);
        if (string.IsNullOrWhiteSpace(safeEdgeId)) return;

        if (!IsReadyForRpc)
        {
            ApplyOffline(() => ApplyDeleteEdge(safeEdgeId));
            return;
        }

        if (CanBroadcast)
            RPC_BroadcastDeleteEdge(safeEdgeId);
        else
            RPC_RequestDeleteEdge(safeEdgeId);
    }

    public void RequestBroadcastCurrentGraph()
    {
        ResolveGraphManager();
        if (graphManager == null) return;

        GraphData data = graphManager.GetGraphData();
        if (data == null) return;

        foreach (NodeData node in data.nodes)
        {
            if (node == null) continue;
            RequestCreateNode(node);
        }

        foreach (EdgeData edge in data.edges)
        {
            if (edge == null) continue;
            RequestCreateEdge(edge);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestLockNode(string nodeId, RpcInfo info = default)
    {
        TryAcquireNodeLockAsAuthority(Safe(nodeId), GetRequester(info));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestUnlockNode(string nodeId, RpcInfo info = default)
    {
        TryReleaseNodeLockAsAuthority(Safe(nodeId), GetRequester(info));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestReleaseLocksForPlayer(RpcInfo info = default)
    {
        ReleaseLocksForPlayerAsAuthority(GetRequester(info));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastNodeLockChanged(string nodeId, PlayerRef owner, bool locked)
    {
        UpdateCachedLock(Safe(nodeId), owner, locked);

        if (locked)
            NodeLockAcquired?.Invoke(nodeId, owner);
        else
            NodeLockReleased?.Invoke(nodeId, owner);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastNodeLockDenied(string nodeId, PlayerRef requester, PlayerRef currentOwner)
    {
        if (Runner != null && requester == Runner.LocalPlayer)
        {
            NodeLockDenied?.Invoke(nodeId, requester, currentOwner);

            if (logLockConflicts)
                Debug.LogWarning($"[GraphNetworkManager] Node edit blocked. nodeId={nodeId}, owner={currentOwner}");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCreateNode(
        string nodeId,
        int nodeType,
        Vector3 position,
        string label,
        string description,
        string parentNodeId,
        string createdBy,
        string propertyCategory,
        bool isGlobal)
    {
        RPC_BroadcastCreateNode(EnsureId(nodeId, "node"), nodeType, position, Safe(label), Safe(description), Safe(parentNodeId), Safe(createdBy), Safe(propertyCategory), isGlobal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastCreateNode(
        string nodeId,
        int nodeType,
        Vector3 position,
        string label,
        string description,
        string parentNodeId,
        string createdBy,
        string propertyCategory,
        bool isGlobal)
    {
        ApplyCreateNode(nodeId, nodeType, position, label, description, parentNodeId, createdBy, propertyCategory, isGlobal);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeleteNode(string nodeId, RpcInfo info = default)
    {
        string safeNodeId = Safe(nodeId);
        PlayerRef requester = GetRequester(info);

        if (!HasNodeEditPermissionAsAuthority(safeNodeId, requester))
            return;

        DeleteNodeAsAuthority(safeNodeId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastDeleteNode(string nodeId)
    {
        ApplyDeleteNode(Safe(nodeId));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestUpdateNodePosition(string nodeId, Vector3 position, RpcInfo info = default)
    {
        string safeNodeId = Safe(nodeId);
        PlayerRef requester = GetRequester(info);

        if (!HasNodeEditPermissionAsAuthority(safeNodeId, requester))
            return;

        RPC_BroadcastUpdateNodePosition(safeNodeId, position);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastUpdateNodePosition(string nodeId, Vector3 position)
    {
        ApplyUpdateNodePosition(Safe(nodeId), position);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestUpdateNodeText(string nodeId, string label, string description, RpcInfo info = default)
    {
        string safeNodeId = Safe(nodeId);
        PlayerRef requester = GetRequester(info);

        if (!HasNodeEditPermissionAsAuthority(safeNodeId, requester))
            return;

        RPC_BroadcastUpdateNodeText(safeNodeId, Safe(label), Safe(description));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastUpdateNodeText(string nodeId, string label, string description)
    {
        ApplyUpdateNodeText(Safe(nodeId), Safe(label), Safe(description));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCreateEdge(string edgeId, string fromNodeId, string toNodeId, string edgeType)
    {
        RPC_BroadcastCreateEdge(EnsureId(edgeId, "edge"), Safe(fromNodeId), Safe(toNodeId), Safe(edgeType));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastCreateEdge(string edgeId, string fromNodeId, string toNodeId, string edgeType)
    {
        ApplyCreateEdge(EnsureId(edgeId, "edge"), Safe(fromNodeId), Safe(toNodeId), Safe(edgeType));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeleteEdge(string edgeId)
    {
        RPC_BroadcastDeleteEdge(Safe(edgeId));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastDeleteEdge(string edgeId)
    {
        ApplyDeleteEdge(Safe(edgeId));
    }

    private void ApplyCreateNode(
        string nodeId,
        int nodeType,
        Vector3 position,
        string label,
        string description,
        string parentNodeId,
        string createdBy,
        string propertyCategory,
        bool isGlobal)
    {
        ResolveGraphManager();
        if (graphManager == null) return;

        NodeData node = new NodeData
        {
            node_id = EnsureId(nodeId, "node"),
            type = ToNodeType(nodeType).ToString(),
            label = Safe(label),
            node_text = string.IsNullOrEmpty(description) ? Safe(label) : Safe(description),
            property_category = Safe(propertyCategory),
            is_global = isGlobal
        };
        node.SetPosition(position);

        bool changed = graphManager.AddNode(node);
        RenderIfChanged(changed);
    }

    private void ApplyDeleteNode(string nodeId)
    {
        ResolveGraphManager();
        if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

        UpdateCachedLock(nodeId, PlayerRef.None, false);
        bool changed = graphManager.RemoveNode(nodeId);
        RenderIfChanged(changed);
    }

    private void ApplyUpdateNodePosition(string nodeId, Vector3 position)
    {
        ResolveGraphManager();
        if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

        bool changed = graphManager.RequestMoveNode(nodeId, position);
        RenderIfChanged(changed);
    }

    private void ApplyUpdateNodeText(string nodeId, string label, string description)
    {
        ResolveGraphManager();
        if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

        NodeData node = graphManager.GetNode(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[GraphNetworkManager] UpdateNodeText failed: node not found ({nodeId}).");
            return;
        }

        node.label = Safe(label);
        node.node_text = string.IsNullOrEmpty(description) ? Safe(label) : Safe(description);
        RenderIfChanged(true);
    }

    private void ApplyCreateEdge(string edgeId, string fromNodeId, string toNodeId, string edgeType)
    {
        ResolveGraphManager();
        if (graphManager == null) return;

        EdgeData edge = new EdgeData
        {
            edge_id = EnsureId(edgeId, "edge"),
            from_node_id = Safe(fromNodeId),
            to_node_id = Safe(toNodeId)
        };

        bool changed = graphManager.AddEdge(edge);
        RenderIfChanged(changed);
    }

    private void ApplyDeleteEdge(string edgeId)
    {
        ResolveGraphManager();
        if (graphManager == null || string.IsNullOrWhiteSpace(edgeId)) return;

        bool changed = graphManager.RemoveEdge(edgeId);
        RenderIfChanged(changed);
    }

    private void ResolveGraphManager()
    {
        if (graphManager != null || !autoFindGraphManager) return;
        graphManager = FindFirstObjectByType<GraphManager>();
    }

    private bool CanRequestNodeMutation(string nodeId, string action)
    {
        if (!requireLockForNodeEdits)
            return true;

        if (!IsReadyForRpc)
            return applyLocallyWhenOffline;

        if (CanLocalPlayerEditNode(nodeId))
            return true;

        RequestLockNode(nodeId);

        if (logLockConflicts)
            Debug.LogWarning($"[GraphNetworkManager] {action} blocked until node lock is acquired. nodeId={nodeId}");

        return false;
    }

    private bool HasNodeEditPermissionAsAuthority(string nodeId, PlayerRef requester)
    {
        if (!requireLockForNodeEdits)
            return true;

        if (string.IsNullOrWhiteSpace(nodeId) || requester == PlayerRef.None)
            return false;

        if (allowStateAuthorityLockOverride && requester == Runner.LocalPlayer)
            return true;

        if (!TryGetAuthorityLockOwner(nodeId, out PlayerRef owner))
        {
            RPC_BroadcastNodeLockDenied(nodeId, requester, PlayerRef.None);
            return false;
        }

        if (owner == requester)
            return true;

        RPC_BroadcastNodeLockDenied(nodeId, requester, owner);
        return false;
    }

    private void TryAcquireNodeLockAsAuthority(string nodeId, PlayerRef requester)
    {
        if (!HasStateAuthority || string.IsNullOrWhiteSpace(nodeId) || requester == PlayerRef.None)
            return;

        if (TryGetAuthorityLockOwner(nodeId, out PlayerRef currentOwner))
        {
            if (currentOwner == requester)
                RPC_BroadcastNodeLockChanged(nodeId, requester, true);
            else
                RPC_BroadcastNodeLockDenied(nodeId, requester, currentOwner);

            return;
        }

        NetworkString<_128> key = ToLockKey(nodeId);
        if (NodeLocks.Add(key, requester))
            RPC_BroadcastNodeLockChanged(nodeId, requester, true);
    }

    private void TryReleaseNodeLockAsAuthority(string nodeId, PlayerRef requester)
    {
        if (!HasStateAuthority || string.IsNullOrWhiteSpace(nodeId) || requester == PlayerRef.None)
            return;

        if (!TryGetAuthorityLockOwner(nodeId, out PlayerRef currentOwner))
            return;

        bool canRelease = currentOwner == requester ||
                          (allowStateAuthorityLockOverride && requester == Runner.LocalPlayer);

        if (!canRelease)
        {
            RPC_BroadcastNodeLockDenied(nodeId, requester, currentOwner);
            return;
        }

        if (NodeLocks.Remove(ToLockKey(nodeId)))
            RPC_BroadcastNodeLockChanged(nodeId, currentOwner, false);
    }

    private void ReleaseLocksForPlayerAsAuthority(PlayerRef player)
    {
        if (!HasStateAuthority || player == PlayerRef.None)
            return;

        List<string> releaseList = new List<string>();
        foreach (KeyValuePair<NetworkString<_128>, PlayerRef> pair in NodeLocks)
        {
            if (pair.Value == player)
                releaseList.Add(pair.Key.ToString());
        }

        foreach (string nodeId in releaseList)
        {
            if (NodeLocks.Remove(ToLockKey(nodeId)))
                RPC_BroadcastNodeLockChanged(nodeId, player, false);
        }
    }

    private void DeleteNodeAsAuthority(string nodeId)
    {
        if (TryGetAuthorityLockOwner(nodeId, out PlayerRef owner) && NodeLocks.Remove(ToLockKey(nodeId)))
            RPC_BroadcastNodeLockChanged(nodeId, owner, false);

        RPC_BroadcastDeleteNode(nodeId);
    }

    private bool TryGetAuthorityLockOwner(string nodeId, out PlayerRef owner)
    {
        if (HasStateAuthority && !string.IsNullOrWhiteSpace(nodeId))
            return NodeLocks.TryGet(ToLockKey(nodeId), out owner);

        owner = PlayerRef.None;
        return false;
    }

    private void UpdateCachedLock(string nodeId, PlayerRef owner, bool locked)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return;

        if (locked)
            lockCache[nodeId] = owner;
        else
            lockCache.Remove(nodeId);
    }

    private void RebuildLockCacheFromNetworkState()
    {
        lockCache.Clear();

        if (!IsReadyForRpc)
            return;

        foreach (KeyValuePair<NetworkString<_128>, PlayerRef> pair in NodeLocks)
            lockCache[pair.Key.ToString()] = pair.Value;
    }

    private PlayerRef GetRequester(RpcInfo info)
    {
        if (info.Source != PlayerRef.None)
            return info.Source;

        return Runner != null ? Runner.LocalPlayer : PlayerRef.None;
    }

    private void RenderIfChanged(bool changed)
    {
        if (changed && renderAfterApply && graphManager != null)
            graphManager.RenderGraph();
    }

    private void ApplyOffline(Action applyAction)
    {
        if (!applyLocallyWhenOffline)
        {
            Debug.LogWarning("[GraphNetworkManager] Runner is not ready, so the graph event was ignored.");
            return;
        }

        applyAction?.Invoke();
    }

    private string GetLocalUserName()
    {
        string fallback = Runner != null ? Runner.LocalPlayer.ToString() : "Unknown";
        return PlayerPrefs.GetString("PlayerNickname", fallback);
    }

    private static string EnsureId(string id, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(id))
            return id.Trim();

        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string Safe(string value)
    {
        return value ?? string.Empty;
    }

    private static NetworkString<_128> ToLockKey(string nodeId)
    {
        NetworkString<_128> key = Safe(nodeId);
        return key;
    }

    private static NodeType ToNodeType(int value)
    {
        return Enum.IsDefined(typeof(NodeType), value) ? (NodeType)value : NodeType.UNKNOWN;
    }
}
