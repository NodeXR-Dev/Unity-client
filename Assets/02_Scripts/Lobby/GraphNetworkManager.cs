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

    [Networked, Capacity(32)]
    private NetworkDictionary<NetworkString<_64>, PlayerRef> NodeLocks => default;

    public event Action<string, PlayerRef> NodeLockAcquired;
    public event Action<string, PlayerRef> NodeLockReleased;
    public event Action<string, PlayerRef, PlayerRef> NodeLockDenied;

    // 원격 op 가 이 클라이언트의 그래프를 실제로 바꿨을 때 발행(요청자 본인에게는 발행되지 않음 —
    // 본인은 로컬 적용이 먼저라 Apply 가 no-op 이 된다). MVP UX 가 "누가 했는지" 안내에 사용.
    public event Action<string, string> RemoteNodeCreated;  // (nodeId, createdBy)
    public event Action<string> RemoteNodeDeleted;          // (nodeId)
    public event Action<int, string, PlayerRef> Generated2DStartReceived;
    public event Action<PlayerRef> Generated2DStartRejected;
    public event Action<int, string, string, string> Generated2DServerImageReceived;
    public event Action<int> Generated2DFinishedReceived;
    public event Action<int, string, PlayerRef> Generated3DStartReceived;
    public event Action<PlayerRef> Generated3DStartRejected;
    public event Action<int> Generated3DFinishedReceived;
    // 완성된 GLB 주소. 2D 의 Generated2DServerImageReceived 와 같은 역할이다.
    public event Action<int, string, string, string> Generated3DModelReceived;
    public event Action<PlayerRef> ReportPanelShowReceived;

    // 작업판(스케치 보드) 위치. 3D 받침대는 보드의 자식이라 이것만 맞추면 함께 따라온다.
    // 예전에는 각자 자기 카메라 앞에 놓아 사람마다 다른 자리에 떠 있었다.
    public event Action<Vector3, Quaternion, float> WorkspacePoseReceived;

    private readonly Dictionary<string, PlayerRef> lockCache = new Dictionary<string, PlayerRef>();
    private bool workspacePoseKnown;
    private Vector3 workspacePosePosition;
    private Quaternion workspacePoseRotation = Quaternion.identity;
    private float workspacePoseScale = 1f;

    private bool generated2DInProgress;
    private int generated2DVersion;
    private bool generated3DInProgress;
    private int generated3DVersion;

    // 원격(RPC/오프라인)으로 그래프 op를 GraphManager에 적용하는 동안 true.
    // 로컬→네트워크 브리지가 이 플래그를 보고 재브로드캐스트(에코 루프)를 막는다.
    private int _remoteApplyDepth;
    public bool IsApplyingRemote => _remoteApplyDepth > 0;

    private bool IsReadyForRpc => Runner != null && Runner.IsRunning && Object != null;
    private bool CanBroadcast => IsReadyForRpc && HasStateAuthority;
    public bool IsRpcReady => IsReadyForRpc;
    public PlayerRef LocalPlayerRef =>
        IsReadyForRpc ? Runner.LocalPlayer : PlayerRef.None;

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

    // 떠난 참가자의 잠금을 정리한다(마스터 클라이언트가 OnPlayerLeft 에서 호출).
    // 이것이 없으면 편집 중 이탈한 참가자의 노드가 영구 잠금으로 남는다.
    public void ReleaseLocksOf(PlayerRef player)
    {
        if (!IsReadyForRpc || !HasStateAuthority)
            return;

        ReleaseLocksForPlayerAsAuthority(player);
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

        if (!CanRequestNodePositionUpdate(safeNodeId))
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

    // 서버 ACK 로 로컬 임시 id 가 서버 발급 id 로 바뀐 것(rekey)을 모든 피어에 전파한다.
    // 이것이 없으면 요청자만 서버 id 를 갖고 피어는 임시 id 로 남아,
    // 이후 이동/삭제/텍스트 RPC 가 피어에서 대상 노드를 찾지 못한다.
    public void RequestRekeyNode(string oldNodeId, string newNodeId, string newText = "")
    {
        string safeOld = Safe(oldNodeId);
        string safeNew = Safe(newNodeId);
        if (string.IsNullOrWhiteSpace(safeOld) ||
            string.IsNullOrWhiteSpace(safeNew) ||
            safeOld == safeNew)
            return;

        if (!IsReadyForRpc)
            return; // 오프라인이면 피어가 없다 — 로컬은 이미 rekey 완료 상태.

        if (CanBroadcast)
            RPC_BroadcastRekeyNode(safeOld, safeNew, Safe(newText));
        else
            RPC_RequestRekeyNode(safeOld, safeNew, Safe(newText));
    }

    public void RequestRekeyEdge(string oldEdgeId, string newEdgeId)
    {
        string safeOld = Safe(oldEdgeId);
        string safeNew = Safe(newEdgeId);
        if (string.IsNullOrWhiteSpace(safeOld) ||
            string.IsNullOrWhiteSpace(safeNew) ||
            safeOld == safeNew)
            return;

        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            RPC_BroadcastRekeyEdge(safeOld, safeNew);
        else
            RPC_RequestRekeyEdge(safeOld, safeNew);
    }

    // 자식 노드 활성/비활성 상태를 모든 피어에 전파한다(MVP 초록선/흐림 동기화).
    public void RequestSetNodeActive(string nodeId, bool active)
    {
        string safeNodeId = Safe(nodeId);
        if (string.IsNullOrWhiteSpace(safeNodeId)) return;

        if (!IsReadyForRpc)
            return; // 로컬은 GraphManager 가 이미 반영했다.

        if (CanBroadcast)
            RPC_BroadcastSetNodeActive(safeNodeId, active);
        else
            RPC_RequestSetNodeActive(safeNodeId, active);
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

        // 작업판 위치도 함께 맞춰 준다(늦게 들어온 사람만 다른 자리에 뜨지 않도록).
        if (workspacePoseKnown && CanBroadcast)
            RPC_BroadcastWorkspacePose(
                workspacePosePosition,
                workspacePoseRotation,
                workspacePoseScale);
    }

    public void RequestGenerated2DStart(string designJson)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            TryBeginGenerated2DAsAuthority(Safe(designJson), Runner.LocalPlayer);
        else
            RPC_RequestGenerated2DStart(Safe(designJson));
    }

    public void RequestGenerated2DServerImage(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            FinishGenerated2DWithImageAsAuthority(version, assetId, mimeType, imgUrl);
        else
            RPC_RequestGenerated2DServerImage(version, Safe(assetId), Safe(mimeType), Safe(imgUrl));
    }

    public void RequestGenerated2DImageSnapshot(
        string assetId,
        string mimeType,
        string imgUrl)
    {
        if (!IsReadyForRpc || string.IsNullOrWhiteSpace(imgUrl))
            return;

        if (CanBroadcast)
            RPC_BroadcastGenerated2DServerImage(
                0,
                Safe(assetId),
                Safe(mimeType),
                Safe(imgUrl));
        else
            RPC_RequestGenerated2DImageSnapshot(
                Safe(assetId),
                Safe(mimeType),
                Safe(imgUrl));
    }

    public void RequestGenerated2DFinish(int version)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            FinishGenerated2DAsAuthority(version);
        else
            RPC_RequestGenerated2DFinish(version);
    }

    public void RequestGenerated3DStart(string designJson)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            TryBeginGenerated3DAsAuthority(Safe(designJson), Runner.LocalPlayer);
        else
            RPC_RequestGenerated3DStart(Safe(designJson));
    }

    public void RequestGenerated3DFinish(int version)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            FinishGenerated3DAsAuthority(version);
        else
            RPC_RequestGenerated3DFinish(version);
    }

    /// <summary>
    /// 내가 요청해서 완성된 GLB 주소를 방 전체에 알린다.
    /// 이게 없으면 3D 는 "누가 시작했다/끝났다" 만 오가고 결과물은 만든 사람만 본다.
    /// </summary>
    public void RequestGenerated3DModel(
        int version,
        string assetId,
        string mimeType,
        string modelUrl)
    {
        if (!IsReadyForRpc || string.IsNullOrWhiteSpace(modelUrl))
            return;

        if (CanBroadcast)
            FinishGenerated3DWithModelAsAuthority(
                version,
                Safe(assetId),
                Safe(mimeType),
                Safe(modelUrl));
        else
            RPC_RequestGenerated3DModel(
                version,
                Safe(assetId),
                Safe(mimeType),
                Safe(modelUrl));
    }

    /// <summary>
    /// 생성 절차를 거치지 않고 이미 알고 있는 GLB 주소를 공유한다
    /// (늦게 들어온 사람에게 현재 모델을 맞춰 줄 때).
    /// </summary>
    public void RequestGenerated3DModelSnapshot(
        string assetId,
        string mimeType,
        string modelUrl)
    {
        if (!IsReadyForRpc || string.IsNullOrWhiteSpace(modelUrl))
            return;

        if (CanBroadcast)
            RPC_BroadcastGenerated3DModel(
                0,
                Safe(assetId),
                Safe(mimeType),
                Safe(modelUrl));
        else
            RPC_RequestGenerated3DModelSnapshot(
                Safe(assetId),
                Safe(mimeType),
                Safe(modelUrl));
    }

    /// <summary>
    /// 작업판 위치를 방 전체에 맞춘다. 마지막에 제안한 것이 기준이 된다.
    /// </summary>
    public void RequestWorkspacePose(Vector3 position, Quaternion rotation, float scale)
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            ApplyWorkspacePoseAsAuthority(position, rotation, scale);
        else
            RPC_RequestWorkspacePose(position, rotation, scale);
    }

    public void RequestReportPanelShow()
    {
        if (!IsReadyForRpc)
            return;

        if (CanBroadcast)
            RPC_BroadcastReportPanelShow(Runner.LocalPlayer);
        else
            RPC_RequestReportPanelShow();
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

        if (!HasNodePositionPermissionAsAuthority(safeNodeId, requester))
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

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRekeyNode(string oldNodeId, string newNodeId, string newText)
    {
        RPC_BroadcastRekeyNode(Safe(oldNodeId), Safe(newNodeId), Safe(newText));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRekeyNode(string oldNodeId, string newNodeId, string newText)
    {
        ApplyRekeyNode(Safe(oldNodeId), Safe(newNodeId), Safe(newText));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRekeyEdge(string oldEdgeId, string newEdgeId)
    {
        RPC_BroadcastRekeyEdge(Safe(oldEdgeId), Safe(newEdgeId));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRekeyEdge(string oldEdgeId, string newEdgeId)
    {
        ApplyRekeyEdge(Safe(oldEdgeId), Safe(newEdgeId));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetNodeActive(string nodeId, bool active)
    {
        RPC_BroadcastSetNodeActive(Safe(nodeId), active);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastSetNodeActive(string nodeId, bool active)
    {
        ApplySetNodeActive(Safe(nodeId), active);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated2DStart(string designJson, RpcInfo info = default)
    {
        TryBeginGenerated2DAsAuthority(Safe(designJson), GetRequester(info));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated2DStart(
        int version,
        string designJson,
        PlayerRef requester)
    {
        Generated2DStartReceived?.Invoke(version, Safe(designJson), requester);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated2DStartRejected(PlayerRef requester)
    {
        Generated2DStartRejected?.Invoke(requester);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated2DServerImage(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        FinishGenerated2DWithImageAsAuthority(version, assetId, mimeType, imgUrl);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated2DImageSnapshot(
        string assetId,
        string mimeType,
        string imgUrl)
    {
        if (!HasStateAuthority || string.IsNullOrWhiteSpace(imgUrl))
            return;

        RPC_BroadcastGenerated2DServerImage(
            0,
            Safe(assetId),
            Safe(mimeType),
            Safe(imgUrl));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated2DServerImage(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        Generated2DServerImageReceived?.Invoke(
            version,
            Safe(assetId),
            Safe(mimeType),
            Safe(imgUrl));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated2DFinish(int version)
    {
        FinishGenerated2DAsAuthority(version);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated2DFinish(int version)
    {
        Generated2DFinishedReceived?.Invoke(version);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated3DStart(string designJson, RpcInfo info = default)
    {
        TryBeginGenerated3DAsAuthority(Safe(designJson), GetRequester(info));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated3DStart(
        int version,
        string designJson,
        PlayerRef requester)
    {
        Generated3DStartReceived?.Invoke(version, Safe(designJson), requester);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated3DStartRejected(PlayerRef requester)
    {
        Generated3DStartRejected?.Invoke(requester);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated3DFinish(int version)
    {
        FinishGenerated3DAsAuthority(version);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated3DFinish(int version)
    {
        Generated3DFinishedReceived?.Invoke(version);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated3DModel(
        int version,
        string assetId,
        string mimeType,
        string modelUrl)
    {
        FinishGenerated3DWithModelAsAuthority(version, assetId, mimeType, modelUrl);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGenerated3DModelSnapshot(
        string assetId,
        string mimeType,
        string modelUrl)
    {
        if (!HasStateAuthority || string.IsNullOrWhiteSpace(modelUrl))
            return;

        RPC_BroadcastGenerated3DModel(
            0,
            Safe(assetId),
            Safe(mimeType),
            Safe(modelUrl));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGenerated3DModel(
        int version,
        string assetId,
        string mimeType,
        string modelUrl)
    {
        Generated3DModelReceived?.Invoke(
            version,
            Safe(assetId),
            Safe(mimeType),
            Safe(modelUrl));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWorkspacePose(Vector3 position, Quaternion rotation, float scale)
    {
        ApplyWorkspacePoseAsAuthority(position, rotation, scale);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastWorkspacePose(Vector3 position, Quaternion rotation, float scale)
    {
        WorkspacePoseReceived?.Invoke(position, rotation, scale);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestReportPanelShow(RpcInfo info = default)
    {
        RPC_BroadcastReportPanelShow(GetRequester(info));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastReportPanelShow(PlayerRef requester)
    {
        ReportPanelShowReceived?.Invoke(requester);
    }

    private void ApplyWorkspacePoseAsAuthority(
        Vector3 position,
        Quaternion rotation,
        float scale)
    {
        if (!HasStateAuthority)
            return;

        // 먼저 정해진 자리를 유지한다.
        // 나중에 들어온 사람이 자기 앞에 놓은 위치를 방송하면, 이미 작업 중인
        // 사람들의 보드가 통째로 끌려간다. 처음 한 번만 받아 모두가 같은 자리를
        // 보게 하고, 늦게 온 사람에게는 이 자리를 다시 내려 준다.
        if (workspacePoseKnown)
        {
            RPC_BroadcastWorkspacePose(
                workspacePosePosition,
                workspacePoseRotation,
                workspacePoseScale);
            return;
        }

        workspacePoseKnown = true;
        workspacePosePosition = position;
        workspacePoseRotation = rotation;
        workspacePoseScale = scale;

        RPC_BroadcastWorkspacePose(position, rotation, scale);
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
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null) return;

            NodeType resolvedNodeType = ToNodeType(nodeType);
            string safeNodeId = EnsureId(nodeId, "node");
            string safeParentNodeId = Safe(parentNodeId);

            NodeData node = new NodeData
            {
                node_id = safeNodeId,
                type = resolvedNodeType.ToString(),
                label = Safe(label),
                node_text = string.IsNullOrEmpty(description) ? Safe(label) : Safe(description),
                parent_node_id = safeParentNodeId,
                property_category = Safe(propertyCategory),
                is_global = isGlobal
            };
            node.SetPosition(position);

            bool nodeCreated = graphManager.AddNode(node);
            bool changed = nodeCreated;

            NodeData appliedNode = nodeCreated ? node : graphManager.GetNode(safeNodeId);

            // 이미 아는 노드로 다시 들어온 경우에도 글자는 반영한다.
            // 보내는 쪽은 글자가 정해질 때 생성부터 다시 보내는데(상대가 아직
            // 이 노드를 모를 수 있으므로), 여기서 흡수만 하고 끝내면 글자가
            // 영영 안 붙는다. 텍스트 전용 RPC 는 잠금이 필요해 방금 만든
            // 노드의 첫 글자가 막히는 경우가 있어 이 경로가 필요하다.
            if (!nodeCreated && appliedNode != null)
            {
                string incomingLabel = Safe(label);
                string incomingText = string.IsNullOrEmpty(description)
                    ? incomingLabel
                    : Safe(description);

                if (!string.IsNullOrWhiteSpace(incomingLabel) &&
                    appliedNode.label != incomingLabel)
                {
                    appliedNode.label = incomingLabel;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(incomingText) &&
                    appliedNode.node_text != incomingText)
                {
                    appliedNode.node_text = incomingText;
                    changed = true;
                }
            }

            if (appliedNode != null && !string.IsNullOrWhiteSpace(safeParentNodeId))
            {
                appliedNode.parent_node_id = safeParentNodeId;

                NodeData parentNode = graphManager.GetNode(safeParentNodeId);
                if (string.IsNullOrEmpty(appliedNode.sub_graph_id) && parentNode != null)
                    appliedNode.sub_graph_id = string.IsNullOrEmpty(parentNode.sub_graph_id)
                        ? parentNode.node_id
                        : parentNode.sub_graph_id;

                if (resolvedNodeType == NodeType.PROPERTY)
                    changed |= EnsurePropertyParentEdge(safeParentNodeId, safeNodeId);
            }

            // 이 노드를 기다리던 연결이 있으면 지금 붙인다.
            // (서로 다른 참가자가 보낸 노드와 엣지는 도착 순서가 보장되지 않는다)
            if (nodeCreated)
                changed |= FlushPendingEdges();

            RenderIfChanged(changed);
            if (nodeCreated)
                RemoteNodeCreated?.Invoke(node.node_id, Safe(createdBy));
        }
        finally { _remoteApplyDepth--; }
    }

    private bool EnsurePropertyParentEdge(string parentNodeId, string childNodeId)
    {
        if (string.IsNullOrWhiteSpace(parentNodeId) || string.IsNullOrWhiteSpace(childNodeId))
            return false;

        NodeData parentNode = graphManager.GetNode(parentNodeId);
        NodeData childNode = graphManager.GetNode(childNodeId);
        if (parentNode == null || childNode == null)
            return false;

        if (parentNode.NodeType != NodeType.PROPERTY || childNode.NodeType != NodeType.PROPERTY)
            return false;

        foreach (EdgeData edge in graphManager.GetEdgesOutgoingFromNode(parentNodeId))
        {
            if (edge != null && edge.to_node_id == childNodeId)
                return false;
        }

        return graphManager.AddEdge(new EdgeData
        {
            edge_id = BuildPropertyParentEdgeId(parentNodeId, childNodeId),
            from_node_id = parentNodeId,
            to_node_id = childNodeId
        });
    }

    private static string BuildPropertyParentEdgeId(string parentNodeId, string childNodeId)
    {
        string source = $"{Safe(parentNodeId)}>{Safe(childNodeId)}";
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source));
            return new Guid(hash).ToString();
        }
    }

    private void ApplyDeleteNode(string nodeId)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

            UpdateCachedLock(nodeId, PlayerRef.None, false);
            bool changed = graphManager.RemoveNode(nodeId);
            RenderIfChanged(changed);
            if (changed)
                RemoteNodeDeleted?.Invoke(nodeId);
        }
        finally { _remoteApplyDepth--; }
    }

    private void ApplyUpdateNodePosition(string nodeId, Vector3 position)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

            graphManager.RequestMoveNode(nodeId, position);
        }
        finally { _remoteApplyDepth--; }
    }

    private void ApplyUpdateNodeText(string nodeId, string label, string description)
    {
        _remoteApplyDepth++;
        try
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
        finally { _remoteApplyDepth--; }
    }

    // 아직 못 붙인 엣지. 양쪽 노드가 다 와야 AddEdge 가 성공하는데, 서로 다른
    // 참가자가 보낸 경우에는 도착 순서가 보장되지 않는다(A 가 만든 PART 보다
    // B 가 만든 연결이 먼저 올 수 있다). 예전에는 그때 엣지가 그냥 사라졌다.
    // 노드가 도착할 때마다 여기 있는 것들을 다시 시도한다.
    private readonly List<EdgeData> pendingEdges = new List<EdgeData>();
    private const int MaxPendingEdges = 256;

    private void ApplyCreateEdge(string edgeId, string fromNodeId, string toNodeId, string edgeType)
    {
        _remoteApplyDepth++;
        try
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
            if (!changed && graphManager.GetEdge(edge.edge_id) == null)
                RememberPendingEdge(edge);

            RenderIfChanged(changed);
        }
        finally { _remoteApplyDepth--; }
    }

    private void RememberPendingEdge(EdgeData edge)
    {
        if (edge == null) return;

        foreach (EdgeData pending in pendingEdges)
        {
            if (pending != null && pending.edge_id == edge.edge_id)
                return;
        }

        if (pendingEdges.Count >= MaxPendingEdges)
            pendingEdges.RemoveAt(0);

        pendingEdges.Add(edge);
    }

    // 노드가 새로 들어온 뒤 호출한다. 붙는 것만 붙이고 나머지는 남겨 둔다.
    private bool FlushPendingEdges()
    {
        if (pendingEdges.Count == 0 || graphManager == null)
            return false;

        bool changed = false;
        for (int i = pendingEdges.Count - 1; i >= 0; i--)
        {
            EdgeData edge = pendingEdges[i];
            if (edge == null)
            {
                pendingEdges.RemoveAt(i);
                continue;
            }

            if (graphManager.GetEdge(edge.edge_id) != null)
            {
                pendingEdges.RemoveAt(i);
                continue;
            }

            if (graphManager.AddEdge(edge))
            {
                pendingEdges.RemoveAt(i);
                changed = true;
            }
        }
        return changed;
    }

    private void ApplyDeleteEdge(string edgeId)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null || string.IsNullOrWhiteSpace(edgeId)) return;

            bool changed = graphManager.RemoveEdge(edgeId);
            RenderIfChanged(changed);
        }
        finally { _remoteApplyDepth--; }
    }

    private void ApplyRekeyNode(string oldNodeId, string newNodeId, string newText)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null ||
                string.IsNullOrWhiteSpace(oldNodeId) ||
                string.IsNullOrWhiteSpace(newNodeId))
                return;

            // 요청자 본인(이미 rekey 완료)과 늦게 합류해 이미 새 id 로 받은 피어는 건너뛴다.
            if (graphManager.GetNode(newNodeId) != null)
            {
                RekeyLockAsAuthority(oldNodeId, newNodeId);
                return;
            }

            graphManager.ApplyServerNodeId(
                oldNodeId,
                newNodeId,
                string.IsNullOrEmpty(newText) ? null : newText);
            RekeyLockAsAuthority(oldNodeId, newNodeId);
        }
        finally { _remoteApplyDepth--; }
    }

    private void ApplyRekeyEdge(string oldEdgeId, string newEdgeId)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null ||
                string.IsNullOrWhiteSpace(oldEdgeId) ||
                string.IsNullOrWhiteSpace(newEdgeId))
                return;

            if (graphManager.GetEdge(newEdgeId) != null)
                return; // 이미 새 id 보유(요청자 본인 에코 포함)

            graphManager.ApplyServerEdgeId(oldEdgeId, newEdgeId);
        }
        finally { _remoteApplyDepth--; }
    }

    private void ApplySetNodeActive(string nodeId, bool active)
    {
        _remoteApplyDepth++;
        try
        {
            ResolveGraphManager();
            if (graphManager == null || string.IsNullOrWhiteSpace(nodeId)) return;

            // 같은 값이면 GraphManager 가 이벤트를 내지 않으므로 에코 루프가 없다.
            graphManager.RequestSetNodeActive(nodeId, active);
        }
        finally { _remoteApplyDepth--; }
    }

    // StateAuthority가 2D 생성 요청을 직렬화해서 같은 방에서 한 번에 하나만 진행되게 한다.
    private void TryBeginGenerated2DAsAuthority(string designJson, PlayerRef requester)
    {
        if (!HasStateAuthority)
            return;

        PlayerRef safeRequester =
            requester != PlayerRef.None
                ? requester
                : Runner != null
                    ? Runner.LocalPlayer
                    : PlayerRef.None;

        if (generated2DInProgress)
        {
            RPC_BroadcastGenerated2DStartRejected(safeRequester);
            return;
        }

        generated2DInProgress = true;
        generated2DVersion++;
        if (generated2DVersion <= 0)
            generated2DVersion = 1;

        RPC_BroadcastGenerated2DStart(
            generated2DVersion,
            Safe(designJson),
            safeRequester);
    }

    private void FinishGenerated2DWithImageAsAuthority(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        if (!HasStateAuthority)
            return;

        // 버전이 안 맞아도 이미지를 버리지 않는다.
        //
        // 예전에는 IsCurrentGenerated2DVersion 이 false 면 그냥 return 했다.
        // 그런데 그 전에 Finish 가 한 번이라도 들어오면(컨트롤러가 생성을
        // 시작하지 못했거나 타임아웃) generated2DInProgress 가 false 가 되어,
        // 뒤늦게 도착한 진짜 이미지가 조용히 폐기됐다.
        // 그 결과 요청한 사람만(자기 WS 로 따로 받으므로) 진짜 그림을 보고
        // 나머지는 시작할 때 띄운 목업이 그대로 남았다.
        //   실기 증상: "만든 사람한테만 보이고 나머지는 목업"
        //
        // 진행 중이던 건이면 그 버전으로, 아니면 스냅샷(0)으로 알린다.
        bool matchesCurrent = IsCurrentGenerated2DVersion(version);
        if (matchesCurrent)
            generated2DInProgress = false;

        RPC_BroadcastGenerated2DServerImage(
            matchesCurrent ? version : 0,
            Safe(assetId),
            Safe(mimeType),
            Safe(imgUrl));
    }

    private void FinishGenerated2DAsAuthority(int version)
    {
        if (!HasStateAuthority || !IsCurrentGenerated2DVersion(version))
            return;

        generated2DInProgress = false;
        RPC_BroadcastGenerated2DFinish(version);
    }

    private bool IsCurrentGenerated2DVersion(int version)
    {
        return generated2DInProgress &&
               version > 0 &&
               version == generated2DVersion;
    }

    // rekey 시 잠금 사전의 키도 새 id 로 옮긴다(StateAuthority 만 네트워크 사전을 수정할 수 있다).
    private void TryBeginGenerated3DAsAuthority(string designJson, PlayerRef requester)
    {
        if (!HasStateAuthority)
            return;

        PlayerRef safeRequester =
            requester != PlayerRef.None
                ? requester
                : Runner != null
                    ? Runner.LocalPlayer
                    : PlayerRef.None;

        if (generated3DInProgress)
        {
            RPC_BroadcastGenerated3DStartRejected(safeRequester);
            return;
        }

        generated3DInProgress = true;
        generated3DVersion++;
        if (generated3DVersion <= 0)
            generated3DVersion = 1;

        RPC_BroadcastGenerated3DStart(
            generated3DVersion,
            Safe(designJson),
            safeRequester);
    }

    private void FinishGenerated3DWithModelAsAuthority(
        int version,
        string assetId,
        string mimeType,
        string modelUrl)
    {
        if (!HasStateAuthority || !IsCurrentGenerated3DVersion(version))
            return;

        generated3DInProgress = false;
        RPC_BroadcastGenerated3DModel(
            version,
            Safe(assetId),
            Safe(mimeType),
            Safe(modelUrl));
    }

    private void FinishGenerated3DAsAuthority(int version)
    {
        if (!HasStateAuthority || !IsCurrentGenerated3DVersion(version))
            return;

        generated3DInProgress = false;
        RPC_BroadcastGenerated3DFinish(version);
    }

    private bool IsCurrentGenerated3DVersion(int version)
    {
        return generated3DInProgress &&
               version > 0 &&
               version == generated3DVersion;
    }

    private void RekeyLockAsAuthority(string oldNodeId, string newNodeId)
    {
        if (!HasStateAuthority)
            return;

        if (!NodeLocks.TryGet(ToLockKey(oldNodeId), out PlayerRef owner))
            return;

        NodeLocks.Remove(ToLockKey(oldNodeId));
        NodeLocks.Add(ToLockKey(newNodeId), owner);
        RPC_BroadcastNodeLockChanged(oldNodeId, owner, false);
        RPC_BroadcastNodeLockChanged(newNodeId, owner, true);
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

    private bool CanRequestNodePositionUpdate(string nodeId)
    {
        if (!requireLockForNodeEdits)
            return true;

        if (!IsReadyForRpc)
            return applyLocallyWhenOffline;

        if (CanLocalPlayerEditNode(nodeId))
            return true;

        if (TryGetNodeLockOwner(nodeId, out PlayerRef owner) && owner != Runner.LocalPlayer)
        {
            if (logLockConflicts)
                Debug.LogWarning($"[GraphNetworkManager] UpdateNodePosition blocked by node lock. nodeId={nodeId}, owner={owner}");
            return false;
        }

        return true;
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

    private bool HasNodePositionPermissionAsAuthority(string nodeId, PlayerRef requester)
    {
        if (!requireLockForNodeEdits)
            return true;

        if (string.IsNullOrWhiteSpace(nodeId) || requester == PlayerRef.None)
            return false;

        if (allowStateAuthorityLockOverride && requester == Runner.LocalPlayer)
            return true;

        if (!TryGetAuthorityLockOwner(nodeId, out PlayerRef owner))
            return true;

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

        NetworkString<_64> key = ToLockKey(nodeId);
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
        foreach (KeyValuePair<NetworkString<_64>, PlayerRef> pair in NodeLocks)
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

        foreach (KeyValuePair<NetworkString<_64>, PlayerRef> pair in NodeLocks)
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

    private static NetworkString<_64> ToLockKey(string nodeId)
    {
        NetworkString<_64> key = Safe(nodeId);
        return key;
    }

    private static NodeType ToNodeType(int value)
    {
        return Enum.IsDefined(typeof(NodeType), value) ? (NodeType)value : NodeType.UNKNOWN;
    }
}
