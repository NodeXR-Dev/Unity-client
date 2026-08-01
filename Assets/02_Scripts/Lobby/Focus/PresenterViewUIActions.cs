using Fusion;
using UnityEngine;

public class PresenterViewUIActions : MonoBehaviour
{
    [SerializeField] private PresenterViewSession session;
    [SerializeField] private PresenterViewRenderer rendererController;
    [SerializeField] private bool autoFindReferences = true;

    private void Awake()
    {
        ResolveReferences();
    }

    public void StartSharingMyView()
    {
        ResolveReferences();

        PresenterCameraPoseSync localPoseSync = FindLocalPoseSync();
        if (localPoseSync == null || !CanStartSharingMyView(localPoseSync))
        {
            return;
        }

        localPoseSync.RequestStartSharing();
        session?.RequestStartLocalPresenterView();
        rendererController?.ExitToPersonalMode();
    }

    public void StopSharingMyView()
    {
        ResolveReferences();

        FindLocalPoseSync()?.RequestStopSharing();
        session?.RequestStopPresenterView();
    }

    public void ToggleSharingMyView()
    {
        ResolveReferences();

        PresenterCameraPoseSync localPoseSync = FindLocalPoseSync();
        if (localPoseSync == null)
        {
            return;
        }

        if (localPoseSync.IsSharingView)
        {
            StopSharingMyView();
        }
        else
        {
            StartSharingMyView();
        }
    }

    public bool IsSharingMyView()
    {
        ResolveReferences();

        PresenterCameraPoseSync localPoseSync = FindLocalPoseSync();
        if (localPoseSync == null || !localPoseSync.IsSharingView)
        {
            return false;
        }

        if (session == null || !session.IsPresenterViewActive)
        {
            return true;
        }

        PlayerRef localPlayer = GetLocalPlayer(localPoseSync);
        return localPlayer != PlayerRef.None && session.Presenter == localPlayer;
    }

    public void StartWatchingPlayer(NetworkObject presenterObject)
    {
        ResolveReferences();

        if (session == null || presenterObject == null)
        {
            return;
        }

        PlayerRef presenter = presenterObject.InputAuthority != PlayerRef.None
            ? presenterObject.InputAuthority
            : presenterObject.StateAuthority;

        session.RequestStartPresenterView(presenter);
        rendererController?.RejoinPresenterView();
    }

    public void StopSharedViewForEveryone()
    {
        ResolveReferences();
        FindLocalPoseSync()?.RequestStopSharing();
        session?.RequestStopPresenterView();
    }

    public void ExitToPersonalMode()
    {
        ResolveReferences();
        rendererController?.ExitToPersonalMode();
    }

    public void RejoinSharedView()
    {
        ResolveReferences();
        rendererController?.RejoinPresenterView();
    }

    public void TogglePersonalMode()
    {
        ResolveReferences();
        rendererController?.TogglePersonalMode();
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (session == null)
        {
            session = PresenterViewSession.Instance;
        }

        if (session == null)
        {
#if UNITY_2023_1_OR_NEWER
            session = FindFirstObjectByType<PresenterViewSession>();
#else
            session = FindObjectOfType<PresenterViewSession>();
#endif
        }

        if (rendererController == null)
        {
#if UNITY_2023_1_OR_NEWER
            rendererController = FindFirstObjectByType<PresenterViewRenderer>();
#else
            rendererController = FindObjectOfType<PresenterViewRenderer>();
#endif
        }
    }

    private PresenterCameraPoseSync FindLocalPoseSync()
    {
        NetworkRunner runner = NetworkManager.runnerInsatance;
        if (runner != null)
        {
            NetworkObject localPlayerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (localPlayerObject != null)
            {
                PresenterCameraPoseSync poseSync = localPlayerObject.GetComponentInChildren<PresenterCameraPoseSync>();
                if (poseSync != null)
                {
                    return poseSync;
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        PresenterCameraPoseSync[] poseSyncs = FindObjectsByType<PresenterCameraPoseSync>(FindObjectsSortMode.None);
#else
        PresenterCameraPoseSync[] poseSyncs = FindObjectsOfType<PresenterCameraPoseSync>();
#endif

        foreach (PresenterCameraPoseSync poseSync in poseSyncs)
        {
            if (poseSync != null && poseSync.Object != null && poseSync.Object.HasInputAuthority)
            {
                return poseSync;
            }
        }

        return null;
    }

    private bool CanStartSharingMyView(PresenterCameraPoseSync localPoseSync)
    {
        if (session == null || !session.IsPresenterViewActive)
        {
            return true;
        }

        PlayerRef localPlayer = GetLocalPlayer(localPoseSync);
        if (localPlayer != PlayerRef.None && session.Presenter == localPlayer)
        {
            return true;
        }

        Debug.LogWarning($"[PresenterViewUIActions] Share blocked because another presenter is already sharing. current={session.Presenter}, local={localPlayer}");
        return false;
    }

    private PlayerRef GetLocalPlayer(PresenterCameraPoseSync localPoseSync)
    {
        NetworkRunner runner = session != null && session.Runner != null
            ? session.Runner
            : NetworkManager.runnerInsatance;

        if (runner != null && runner.LocalPlayer != PlayerRef.None)
        {
            return runner.LocalPlayer;
        }

        if (localPoseSync != null && localPoseSync.Object != null)
        {
            if (localPoseSync.Object.InputAuthority != PlayerRef.None)
            {
                return localPoseSync.Object.InputAuthority;
            }

            return localPoseSync.Object.StateAuthority;
        }

        return PlayerRef.None;
    }
}
