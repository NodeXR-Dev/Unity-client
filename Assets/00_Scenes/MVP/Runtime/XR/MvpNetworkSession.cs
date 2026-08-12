using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

// MVP 수업 씬을 자기완결형 멀티플레이로 만드는 세션 관리자.
// 로비를 거치지 않고, MVP가 방을 만들거나 입장하면(= 서버 room_id 확보) 그 room_id로
// Photon Fusion Shared 세션을 시작한다. 같은 room_id에 들어온 참가자끼리 한 세션이 되어
//  - 서로의 아바타가 보이고(Player 프리팹 스폰),
//  - GraphNetworkManager(Fusion RPC)로 노드 그래프를 실시간 협업한다.
// 서버 GRAPH_UPDATED 브로드캐스트가 죽어 있으므로 그래프 동기화의 실채널은 Fusion이다.
[DisallowMultipleComponent]
public class MvpNetworkSession : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkRunner _runner;
    [SerializeField] private GameObject _playerPrefab;          // NetworkObject 아바타(선택)
    [SerializeField] private GameObject _graphNetworkPrefab;    // NetworkObject + GraphNetworkManager
    [SerializeField] private Transform _spawnPoint;
    [SerializeField] private MvpGraphNetworkBridge _bridge;
    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private bool _spawnAvatar = true;
    [SerializeField] private int _playerCount = 8;

    private bool _starting;
    private GraphNetworkManager _boundNetwork;   // 어트리뷰션 이벤트를 구독한 GNM

    public bool IsRunning => _runner != null && _runner.IsRunning;

    // 세션에서 일어난 사람/협업 이벤트를 짧은 문구로 알린다(MvpClassroomFlow 가 구독).
    public event Action<string> OnSessionNotice;

    public int ParticipantCount
    {
        get
        {
            if (!IsRunning) return 0;
            int count = 0;
            foreach (PlayerRef _ in _runner.ActivePlayers) count++;
            return count;
        }
    }

    private void Awake()
    {
        if (_bridge == null)
            _bridge = GetComponent<MvpGraphNetworkBridge>();
        if (_graphManager == null)
            _graphManager = FindFirstObjectByType<GraphManager>();
    }

    // 방 입장/생성 성공 시 MvpClassroomFlow가 호출.
    public async void BeginSession(string sessionName, string nickname)
    {
        if (_starting) return;
        if (string.IsNullOrEmpty(sessionName))
        {
            Debug.LogWarning("[MvpNetworkSession] sessionName이 비어 세션을 시작하지 않습니다.");
            return;
        }
        if (IsRunning &&
            _runner.SessionInfo != null &&
            _runner.SessionInfo.Name == sessionName)
            return;

        // 로비(MvpLobby)가 이미 같은 방으로 러너를 띄웠으면 새로 StartGame 하지 않는다.
        // 그대로 두 번 시작하면 Fusion 세션이 둘이 되어 아바타·그래프가 갈린다.
        if (_runner == null || !_runner.IsRunning)
        {
            foreach (NetworkRunner existing in
                     FindObjectsByType<NetworkRunner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing == null || !existing.IsRunning)
                    continue;
                _runner = existing;
                _runner.AddCallbacks(this);
                Debug.Log(
                    "[MvpNetworkSession] 이미 실행 중인 러너에 붙습니다 — session=" +
                    (existing.SessionInfo != null ? existing.SessionInfo.Name : "?"));
                StartCoroutine(AfterStart());
                return;
            }
        }

        _starting = true;

        if (!string.IsNullOrEmpty(nickname))
            PlayerPrefs.SetString("PlayerNickname", nickname);

        if (_runner == null)
        {
            _runner = GetComponent<NetworkRunner>();
            if (_runner == null)
                _runner = gameObject.AddComponent<NetworkRunner>();
        }
        _runner.ProvideInput = false;
        _runner.AddCallbacks(this);   // 입장/퇴장 알림용(중복 등록은 Fusion 이 무시)

        StartGameResult result;
        try
        {
            result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = _playerCount,
            });
        }
        catch (System.Exception e)
        {
            Debug.LogError("[MvpNetworkSession] StartGame 예외: " + e.Message);
            _starting = false;
            return;
        }

        _starting = false;

        if (!result.Ok)
        {
            Debug.LogError(
                "[MvpNetworkSession] 세션 시작 실패: " + result.ShutdownReason);
            return;
        }

        Debug.Log("[MvpNetworkSession] Shared 세션 시작: " + sessionName);
        StartCoroutine(AfterStart());
    }


    // 아바타를 리그 자리에서 시작시키기 위해 쓴다(_spawnPoint 미할당 폴백).
    private static Transform ResolveCameraRig()
    {
        OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
        return rig != null ? rig.transform : null;
    }

    /// <summary>
    /// 로비에서 씬을 갈아탄 직후에는 프리팹 에셋 로드가 끝나지 않아 동기 Spawn 이
    /// NetworkObjectSpawnException 을 던진다. 그러면 아바타가 안 생겨
    /// 다른 참가자에게 보이지 않는다.
    ///
    /// Prefabs.Load(동기) 를 먼저 불러도 로드가 완료되기 전이면 Spawn 이 여전히
    /// 실패하므로(실측), 성공할 때까지 프레임을 넘기며 재시도한다.
    /// EnqueueIncompleteSynchronousSpawns 를 켜는 방법도 있지만 그 경우 Spawn 이
    /// null 을 돌려줘 SetPlayerObject 를 못 한다.
    /// </summary>
    private IEnumerator SpawnWhenPrefabReady(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        System.Action<NetworkObject> onSpawned)
    {
        const int MaxAttempts = 120;   // 넉넉히 2초(60fps 기준)

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (_runner == null || !_runner.IsRunning)
                yield break;

            NetworkObject spawned = null;
            bool notReady = false;

            NetworkObject source = prefab.GetComponent<NetworkObject>();
            if (source != null && source.NetworkTypeId.IsPrefab)
                _runner.Prefabs.Load(source.NetworkTypeId.AsPrefabId, true);

            try
            {
                spawned = _runner.Spawn(
                    prefab, position, rotation, _runner.LocalPlayer);
            }
            catch (NetworkObjectSpawnException)
            {
                notReady = true;   // 아직 로드 중 — 다음 프레임에 다시 시도
            }

            if (!notReady && spawned != null)
            {
                onSpawned?.Invoke(spawned);
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            "[MvpNetworkSession] 프리팹 로드를 기다렸지만 스폰하지 못했습니다: " +
            prefab.name);
    }

    private IEnumerator AfterStart()
    {
        float t = 0f;
        while ((_runner == null || !_runner.IsRunning) && t < 5f)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (_runner == null || !_runner.IsRunning)
            yield break;

        // 1) 로컬 아바타 스폰
        if (_spawnAvatar &&
            _playerPrefab != null &&
            _runner.GetPlayerObject(_runner.LocalPlayer) == null)
        {
            // _spawnPoint 는 씬에서 비어 있는 경우가 많다. 그때 Vector3.zero 로
            // 스폰하면 아바타가 월드 원점에 뜬다 — 회의실 바닥은 y≈-2.1 이라
            // 첫 프레임에 카메라와 캐릭터가 크게 어긋나 보인다.
            // XRPlayerBinder 가 곧 리그를 따라오지만, 처음부터 리그 자리에서 시작한다.
            Transform rig = ResolveCameraRig();
            Vector3 pos = _spawnPoint != null ? _spawnPoint.position
                        : rig != null ? rig.position
                        : Vector3.zero;
            Quaternion rot = _spawnPoint != null ? _spawnPoint.rotation
                           : rig != null ? rig.rotation
                           : Quaternion.identity;
            NetworkObject avatar = null;
            yield return SpawnWhenPrefabReady(
                _playerPrefab, pos, rot, spawned => avatar = spawned);
            if (avatar != null)
                _runner.SetPlayerObject(_runner.LocalPlayer, avatar);
            else
                Debug.LogWarning(
                    "[MvpNetworkSession] 로컬 아바타를 스폰하지 못했습니다.");
        }

        // 2) 그래프 네트워크 오브젝트는 마스터가 한 번만 스폰(다른 참가자는 복제로 수신)
        if (_graphNetworkPrefab != null &&
            _runner.IsSharedModeMasterClient &&
            FindFirstObjectByType<GraphNetworkManager>() == null)
        {
            yield return SpawnWhenPrefabReady(
                _graphNetworkPrefab, Vector3.zero, Quaternion.identity, null);
        }

        // 3) GNM(스폰/원격 복제) 준비되면 로컬→네트워크 브리지 바인딩·활성화
        GraphNetworkManager gnm = null;
        t = 0f;
        while (t < 8f)
        {
            gnm = FindFirstObjectByType<GraphNetworkManager>();
            if (gnm != null) break;
            t += Time.deltaTime;
            yield return null;
        }

        if (gnm != null && _bridge != null)
        {
            if (_graphManager == null)
                _graphManager = FindFirstObjectByType<GraphManager>();
            _bridge.Bind(_graphManager, gnm);
            _bridge.SetActive(true);
            BindNetworkNotices(gnm);
            _sessionReadyTime = Time.unscaledTime;
            Debug.Log("[MvpNetworkSession] 그래프 네트워크 브리지 활성화");
        }
    }

    // 늦게 합류한 참가자에게 마스터가 현재 그래프를 재전송한다.
    // (이 호출이 없으면 합류 이후의 변경만 받고 기존 노드는 영영 못 받는다.)
    // 합류자의 GraphNetworkManager 복제가 끝날 시간을 주기 위해 잠시 기다린다.
    private IEnumerator BroadcastGraphToLateJoiner()
    {
        yield return new WaitForSecondsRealtime(2f);
        if (_boundNetwork != null &&
            _runner != null && _runner.IsRunning &&
            _runner.IsSharedModeMasterClient)
            _boundNetwork.RequestBroadcastCurrentGraph();
    }

    // '방 나가기' — 브리지를 끄고 Fusion 세션을 종료한다(잠금도 함께 반납).
    public void EndSession()
    {
        if (_bridge != null)
            _bridge.SetActive(false);

        if (_boundNetwork != null)
        {
            _boundNetwork.RequestReleaseLocalNodeLocks();
            UnbindNetworkNotices();
        }

        if (_runner != null && _runner.IsRunning)
            _runner.Shutdown();
    }

    // ─────────────────────────────────────────────
    // 원격 협업 어트리뷰션(누가 만들었는지) 알림
    // ─────────────────────────────────────────────

    private void BindNetworkNotices(GraphNetworkManager gnm)
    {
        UnbindNetworkNotices();
        _boundNetwork = gnm;
        if (_boundNetwork == null) return;
        _boundNetwork.RemoteNodeCreated += HandleRemoteNodeCreated;
        _boundNetwork.RemoteNodeDeleted += HandleRemoteNodeDeleted;
    }

    private void UnbindNetworkNotices()
    {
        if (_boundNetwork == null) return;
        _boundNetwork.RemoteNodeCreated -= HandleRemoteNodeCreated;
        _boundNetwork.RemoteNodeDeleted -= HandleRemoteNodeDeleted;
        _boundNetwork = null;
    }

    private void OnDestroy()
    {
        UnbindNetworkNotices();
    }

    private float _sessionReadyTime = -999f;

    // 합류 직후에는 기존 그래프의 초기 동기화가 밀려 들어온다 —
    // 그때의 노드 생성은 "지금 누가 만든 것"이 아니므로 알림하지 않는다.
    private bool InInitialSyncWindow =>
        Time.unscaledTime - _sessionReadyTime < 8f;

    private void HandleRemoteNodeCreated(string nodeId, string createdBy)
    {
        if (InInitialSyncWindow)
            return;
        string who = string.IsNullOrWhiteSpace(createdBy) ? "친구" : createdBy;
        OnSessionNotice?.Invoke(who + "님이 아이디어를 추가했어요.");
    }

    private void HandleRemoteNodeDeleted(string nodeId)
    {
        if (InInitialSyncWindow)
            return;
        OnSessionNotice?.Invoke("친구가 노드 하나를 지웠어요.");
    }

    // ─────────────────────────────────────────────
    // INetworkRunnerCallbacks — 입장/퇴장만 사용
    // ─────────────────────────────────────────────

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
        {
            OnSessionNotice?.Invoke(
                "우리 방에 연결됐어요. (현재 " + ParticipantCount + "명)");
            return;
        }

        OnSessionNotice?.Invoke(
            "친구가 들어왔어요! (현재 " + ParticipantCount + "명)");

        // 마스터가 새 참가자에게 현재 그래프를 재전송한다(늦합류 초기 동기화).
        if (runner.IsSharedModeMasterClient)
            StartCoroutine(BroadcastGraphToLateJoiner());
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 편집 중 이탈한 참가자의 노드 잠금을 마스터가 정리한다(영구 잠금 방지).
        if (_boundNetwork != null)
            _boundNetwork.ReleaseLocksOf(player);

        OnSessionNotice?.Invoke(
            "친구가 나갔어요. (현재 " + ParticipantCount + "명)");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        OnSessionNotice?.Invoke("연결이 끊겼어요. 체험 모드로 계속할 수 있어요.");
    }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
