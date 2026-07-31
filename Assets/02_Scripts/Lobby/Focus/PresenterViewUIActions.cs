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

        FindLocalPoseSync()?.RequestStartSharing();
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
        PresenterCameraPoseSync localPoseSync = FindLocalPoseSync();
        return localPoseSync != null && localPoseSync.IsSharingView;
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
}
