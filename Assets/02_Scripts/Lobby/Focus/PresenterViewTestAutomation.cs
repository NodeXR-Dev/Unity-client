using System;
using System.Collections;
using Fusion;
using UnityEngine;

public class PresenterViewTestAutomation : MonoBehaviour
{
    [SerializeField] private PresenterViewTestBootstrap bootstrap;
    [SerializeField] private PresenterViewUIActions uiActions;
    [SerializeField] private PresenterViewRenderer rendererController;
    [SerializeField] private float autoShareDelaySeconds = 5f;
    [SerializeField] private float localReadyTimeoutSeconds = 25f;
    [SerializeField] private float watchTimeoutSeconds = 35f;
    [SerializeField] private float autoQuitSeconds = 40f;

    private bool autoShare;
    private bool autoWatch;

    private void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        autoShare = HasArg(args, "-presenterViewAutoShare");
        autoWatch = HasArg(args, "-presenterViewAutoWatch");
        bool shouldRun = autoShare || autoWatch || HasArg(args, "-presenterViewAutoTest");

        if (!shouldRun)
        {
            return;
        }

        autoQuitSeconds = GetFloatArg(args, "-presenterViewAutoQuitSeconds", autoQuitSeconds);
        StartCoroutine(RunAutomation());
    }

    private IEnumerator RunAutomation()
    {
        float startedAt = Time.realtimeSinceStartup;
        ResolveReferences();
        bootstrap?.StartTestSession();

        string role = autoShare ? "share" : autoWatch ? "watch" : "smoke";
        Debug.Log($"[PresenterViewTestAutomation] BEGIN role={role}");

        yield return WaitForLocalPlayerObject(localReadyTimeoutSeconds);

        bool passed = false;
        if (autoShare)
        {
            yield return new WaitForSeconds(autoShareDelaySeconds);
            ResolveReferences();
            uiActions?.StartSharingMyView();
            yield return WaitForLocalShareReady(10f);

            passed = IsLocalShareReady();
            Debug.Log(passed
                ? "[PresenterViewTestAutomation] PASS: local share flag and pose are ready."
                : "[PresenterViewTestAutomation] FAIL: local share did not become ready.");
        }
        else if (autoWatch)
        {
            yield return WaitForRemoteViewReady(watchTimeoutSeconds);

            passed = IsRemoteViewReady();
            Debug.Log(passed
                ? "[PresenterViewTestAutomation] PASS: remote shared camera is visible through renderer."
                : "[PresenterViewTestAutomation] FAIL: remote shared camera was not shown.");
        }
        else
        {
            passed = HasLocalPlayerObject();
            Debug.Log(passed
                ? "[PresenterViewTestAutomation] PASS: local test session joined and player spawned."
                : "[PresenterViewTestAutomation] FAIL: local player was not spawned.");
        }

        float remainingSeconds = Mathf.Max(1f, autoQuitSeconds - (Time.realtimeSinceStartup - startedAt));
        yield return new WaitForSeconds(remainingSeconds);

        Debug.Log($"[PresenterViewTestAutomation] RESULT {(passed ? "PASS" : "FAIL")}");
        Application.Quit(passed ? 0 : 2);
    }

    private IEnumerator WaitForLocalPlayerObject(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (HasLocalPlayerObject())
            {
                LogState("local-ready");
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        LogState("local-timeout");
    }

    private IEnumerator WaitForLocalShareReady(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (IsLocalShareReady())
            {
                LogState("local-share-ready");
                yield break;
            }

            LogState("waiting-local-share");
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator WaitForRemoteViewReady(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (IsRemoteViewReady())
            {
                LogState("remote-view-ready");
                yield break;
            }

            LogState("waiting-remote-view");
            yield return new WaitForSeconds(1f);
        }
    }

    private bool HasLocalPlayerObject()
    {
        NetworkRunner runner = NetworkManager.runnerInsatance;
        return runner != null &&
               runner.IsRunning &&
               runner.LocalPlayer != PlayerRef.None &&
               runner.GetPlayerObject(runner.LocalPlayer) != null;
    }

    private bool IsLocalShareReady()
    {
        PresenterCameraPoseSync localPoseSync = FindLocalPoseSync();
        return localPoseSync != null &&
               localPoseSync.IsSharingView &&
               localPoseSync.TryGetSharedCameraPose(out _, out _, out _);
    }

    private bool IsRemoteViewReady()
    {
        ResolveReferences();
        return CountRemoteSharingPoseSyncs(out int readyCount) > 0 &&
               readyCount > 0 &&
               rendererController != null &&
               rendererController.IsShowingPresenterView;
    }

    private PresenterCameraPoseSync FindLocalPoseSync()
    {
        NetworkRunner runner = NetworkManager.runnerInsatance;
        if (runner != null && runner.LocalPlayer != PlayerRef.None)
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

    private int CountRemoteSharingPoseSyncs(out int readyCount)
    {
        readyCount = 0;
        int sharingCount = 0;

#if UNITY_2023_1_OR_NEWER
        PresenterCameraPoseSync[] poseSyncs = FindObjectsByType<PresenterCameraPoseSync>(FindObjectsSortMode.None);
#else
        PresenterCameraPoseSync[] poseSyncs = FindObjectsOfType<PresenterCameraPoseSync>();
#endif

        foreach (PresenterCameraPoseSync poseSync in poseSyncs)
        {
            if (poseSync == null ||
                poseSync.Object == null ||
                poseSync.Object.HasInputAuthority ||
                !poseSync.IsSharingView)
            {
                continue;
            }

            sharingCount++;

            if (poseSync.TryGetSharedCameraPose(out _, out _, out _))
            {
                readyCount++;
            }
        }

        return sharingCount;
    }

    private void LogState(string phase)
    {
        NetworkRunner runner = NetworkManager.runnerInsatance;
        bool hasRunner = runner != null;
        bool runnerReady = hasRunner && runner.IsRunning;
        int playerId = hasRunner && runner.LocalPlayer != PlayerRef.None ? runner.LocalPlayer.PlayerId : -1;
        bool hasLocalObject = HasLocalPlayerObject();
        bool localSharing = FindLocalPoseSync()?.IsSharingView ?? false;
        int remoteSharing = CountRemoteSharingPoseSyncs(out int remoteReady);
        bool rendererShowing = rendererController != null && rendererController.IsShowingPresenterView;

        Debug.Log(
            $"[PresenterViewTestAutomation] TEST_STATE phase={phase} runner={runnerReady} player={playerId} " +
            $"localObject={hasLocalObject} localSharing={localSharing} " +
            $"remoteSharing={remoteSharing} remoteReady={remoteReady} rendererShowing={rendererShowing}");
    }

    private void ResolveReferences()
    {
        if (bootstrap == null)
        {
#if UNITY_2023_1_OR_NEWER
            bootstrap = FindFirstObjectByType<PresenterViewTestBootstrap>();
#else
            bootstrap = FindObjectOfType<PresenterViewTestBootstrap>();
#endif
        }

        if (uiActions == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiActions = FindFirstObjectByType<PresenterViewUIActions>();
#else
            uiActions = FindObjectOfType<PresenterViewUIActions>();
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

    private bool HasArg(string[] args, string argName)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private float GetFloatArg(string[] args, string argName, float fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (float.TryParse(args[i + 1], out float value))
            {
                return value;
            }
        }

        return fallback;
    }
}
