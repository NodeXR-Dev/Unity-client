using System;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PresenterViewSession : NetworkBehaviour
{
    public static PresenterViewSession Instance { get; private set; }

    [Header("Permissions")]
    [SerializeField] private bool allowAnyPlayerToStartOwnShare = true;
    [SerializeField] private bool allowPresenterToStopShare = true;
    [SerializeField] private bool allowStateAuthorityToControlAll = true;

    [Header("Debug")]
    [SerializeField] private bool logRejectedRequests = true;

    [Networked, OnChangedRender(nameof(OnNetworkStateChanged))]
    private byte NetworkMode { get; set; }

    [Networked, OnChangedRender(nameof(OnNetworkStateChanged))]
    public PlayerRef Presenter { get; private set; }

    [Networked, OnChangedRender(nameof(OnNetworkStateChanged))]
    public int Revision { get; private set; }

    public event Action<PresenterViewState> StateChanged;

    private PresenterViewState lastNotifiedState;
    private bool hasNotifiedState;

    // [Networked] 프로퍼티는 Spawned() 이후에만 읽을 수 있다. 이 컴포넌트는 씬에 미리
    // 놓여 있어서(MVP_SH 등) 오프라인이면 영영 Spawn 되지 않는데, 그 상태로 값을 읽으면
    // Update() 에서 매 프레임 InvalidOperationException 이 난다. → 읽기 전에 항상 확인한다.
    private bool IsNetworkReady => Object != null && Object.IsValid;

    public FocusMode CurrentMode =>
        IsNetworkReady ? (FocusMode)NetworkMode : FocusMode.Personal;
    public bool IsPresenterViewActive =>
        IsNetworkReady && CurrentMode == FocusMode.PresenterView && Presenter != PlayerRef.None;
    private bool IsReadyForRpc => Runner != null && Runner.IsRunning && Object != null;

    public override void Spawned()
    {
        Instance = this;

        if (HasStateAuthority && Revision == 0)
        {
            NetworkMode = (byte)FocusMode.Personal;
            Presenter = PlayerRef.None;
        }

        NotifyStateChangedIfNeeded(true);
    }

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Spawn 전(오프라인·세션 시작 전)에는 알릴 상태 자체가 없다.
        if (!IsNetworkReady)
            return;

        NotifyStateChangedIfNeeded(false);
    }

    public PresenterViewState GetState()
    {
        // Spawn 전에는 Presenter/Revision 도 읽을 수 없으므로 기본(개인 모드) 상태로 답한다.
        if (!IsNetworkReady)
            return new PresenterViewState(FocusMode.Personal, PlayerRef.None, 0);

        return new PresenterViewState(CurrentMode, Presenter, Revision);
    }

    public void RequestStartLocalPresenterView()
    {
        PlayerRef localPlayer = Runner != null ? Runner.LocalPlayer : PlayerRef.None;
        RequestStartPresenterView(localPlayer);
    }

    public void RequestStartPresenterView(PlayerRef presenter)
    {
        if (!IsReadyForRpc || presenter == PlayerRef.None)
        {
            return;
        }

        if (HasStateAuthority)
        {
            TryStartPresenterViewAsAuthority(presenter, Runner.LocalPlayer);
            return;
        }

        RPC_RequestStartPresenterView(presenter);
    }

    public void RequestStopPresenterView()
    {
        if (!IsReadyForRpc)
        {
            return;
        }

        if (HasStateAuthority)
        {
            TryStopPresenterViewAsAuthority(Runner.LocalPlayer);
            return;
        }

        RPC_RequestStopPresenterView();
    }

    public void RequestPersonalMode()
    {
        RequestStopPresenterView();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestStartPresenterView(PlayerRef presenter, RpcInfo info = default)
    {
        TryStartPresenterViewAsAuthority(presenter, GetRequester(info));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestStopPresenterView(RpcInfo info = default)
    {
        TryStopPresenterViewAsAuthority(GetRequester(info));
    }

    private void TryStartPresenterViewAsAuthority(PlayerRef presenter, PlayerRef requester)
    {
        if (!HasStateAuthority || presenter == PlayerRef.None)
        {
            return;
        }

        if (IsPresenterViewActive && Presenter != presenter)
        {
            LogRejected($"Start share rejected. another presenter is already sharing. requester={requester}, current={Presenter}, requested={presenter}");
            return;
        }

        bool controlsAll = allowStateAuthorityToControlAll && requester == Runner.LocalPlayer;
        bool startsOwnShare = allowAnyPlayerToStartOwnShare && presenter == requester;

        if (!controlsAll && !startsOwnShare)
        {
            LogRejected($"Start share rejected. requester={requester}, presenter={presenter}");
            return;
        }

        NetworkMode = (byte)FocusMode.PresenterView;
        Presenter = presenter;
        Revision++;
        NotifyStateChangedIfNeeded(true);
    }

    private void TryStopPresenterViewAsAuthority(PlayerRef requester)
    {
        if (!HasStateAuthority)
        {
            return;
        }

        bool controlsAll = allowStateAuthorityToControlAll && requester == Runner.LocalPlayer;
        bool presenterStopsOwnShare = allowPresenterToStopShare && requester == Presenter;

        if (!controlsAll && !presenterStopsOwnShare)
        {
            LogRejected($"Stop share rejected. requester={requester}, presenter={Presenter}");
            return;
        }

        NetworkMode = (byte)FocusMode.Personal;
        Presenter = PlayerRef.None;
        Revision++;
        NotifyStateChangedIfNeeded(true);
    }

    private PlayerRef GetRequester(RpcInfo info)
    {
        if (info.Source != PlayerRef.None)
        {
            return info.Source;
        }

        return Runner != null ? Runner.LocalPlayer : PlayerRef.None;
    }

    private void OnNetworkStateChanged()
    {
        NotifyStateChangedIfNeeded(true);
    }

    private void NotifyStateChangedIfNeeded(bool force)
    {
        PresenterViewState state = GetState();

        if (!force && hasNotifiedState &&
            state.Mode == lastNotifiedState.Mode &&
            state.Presenter == lastNotifiedState.Presenter &&
            state.Revision == lastNotifiedState.Revision)
        {
            return;
        }

        hasNotifiedState = true;
        lastNotifiedState = state;
        StateChanged?.Invoke(state);
    }

    private void LogRejected(string message)
    {
        if (logRejectedRequests)
        {
            Debug.LogWarning($"[PresenterViewSession] {message}");
        }
    }
}
