using System;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MvpJsHandSyncTestAutomation : MonoBehaviour
{
    private const string DefaultSessionName = "MvpJsHandSyncTestRoom";

    [SerializeField] private string sessionName = DefaultSessionName;
    [SerializeField] private string nickname = "MVP JS Hand Test";
    [SerializeField] private float localReadyTimeoutSeconds = 35f;
    [SerializeField] private float watchTimeoutSeconds = 45f;
    [SerializeField] private float autoQuitSeconds = 90f;

    private MvpNetworkSession session;
    private bool autoSend;
    private bool autoWatch;
    private bool shouldAutoQuit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForMvpJs()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool isMvpJsScene =
            string.Equals(scene.name, "mvp_js", StringComparison.OrdinalIgnoreCase) ||
            scene.path.EndsWith("/mvp_js.unity", StringComparison.OrdinalIgnoreCase);
        if (!isMvpJsScene)
        {
            return;
        }

        string[] args = Environment.GetCommandLineArgs();
        if (!HasArg(args, "-mvpJsHandAutoStart") &&
            !HasArg(args, "-mvpJsHandAutoSend") &&
            !HasArg(args, "-mvpJsHandAutoWatch") &&
            !HasArg(args, "-mvpJsHandAutoTest"))
        {
            return;
        }

        GameObject go = new GameObject("MvpJsHandSyncTestAutomation");
        go.AddComponent<MvpJsHandSyncTestAutomation>();
    }

    private void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        autoSend = HasArg(args, "-mvpJsHandAutoSend") ||
                   HasArg(args, "-mvpJsHandAutoTest");
        autoWatch = HasArg(args, "-mvpJsHandAutoWatch");
        shouldAutoQuit = autoSend || autoWatch;

        sessionName = GetStringArg(args, "-mvpJsSession", sessionName);
        nickname = GetStringArg(args, "-mvpJsNickname", nickname);
        localReadyTimeoutSeconds = GetFloatArg(
            args,
            "-mvpJsLocalReadyTimeoutSeconds",
            localReadyTimeoutSeconds);
        watchTimeoutSeconds = GetFloatArg(
            args,
            "-mvpJsWatchTimeoutSeconds",
            watchTimeoutSeconds);
        autoQuitSeconds = GetFloatArg(
            args,
            "-mvpJsAutoQuitSeconds",
            autoQuitSeconds);

        StartCoroutine(RunAutomation());
    }

    private IEnumerator RunAutomation()
    {
        float startedAt = Time.realtimeSinceStartup;
        ResolveReferences();

        if (session == null)
        {
            Debug.LogError("[MvpJsHandSyncTestAutomation] MvpNetworkSession not found.");
            yield break;
        }

        Debug.Log(
            $"[MvpJsHandSyncTestAutomation] BEGIN session={sessionName} " +
            $"role={(autoSend ? "send" : autoWatch ? "watch" : "join")}");

        session.BeginSession(sessionName, nickname);
        yield return WaitForLocalPlayerObject(localReadyTimeoutSeconds);

        if (!shouldAutoQuit)
        {
            Debug.Log("[MvpJsHandSyncTestAutomation] Joined test session.");
            yield break;
        }

        bool passed;
        if (autoSend)
        {
            yield return WaitForLocalHandPublish(10f);
            passed = IsLocalHandPublishing();
            Debug.Log(passed
                ? "[MvpJsHandSyncTestAutomation] PASS: local hand joints are publishing."
                : "[MvpJsHandSyncTestAutomation] FAIL: local hand joints did not publish.");
        }
        else
        {
            yield return WaitForRemoteHands(watchTimeoutSeconds);
            passed = IsRemoteHandsVisible();
            Debug.Log(passed
                ? "[MvpJsHandSyncTestAutomation] PASS: remote hand skeleton is visible."
                : "[MvpJsHandSyncTestAutomation] FAIL: remote hand skeleton was not visible.");
        }

        float remainingSeconds = Mathf.Max(
            1f,
            autoQuitSeconds - (Time.realtimeSinceStartup - startedAt));
        yield return new WaitForSeconds(remainingSeconds);

        Debug.Log($"[MvpJsHandSyncTestAutomation] RESULT {(passed ? "PASS" : "FAIL")}");
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

            LogState("waiting-local-player");
            yield return new WaitForSeconds(1f);
        }

        LogState("local-timeout");
    }

    private IEnumerator WaitForLocalHandPublish(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (IsLocalHandPublishing())
            {
                LogState("local-hands-ready");
                yield break;
            }

            LogState("waiting-local-hands");
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator WaitForRemoteHands(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (IsRemoteHandsVisible())
            {
                LogState("remote-hands-ready");
                yield break;
            }

            LogState("waiting-remote-hands");
            yield return new WaitForSeconds(1f);
        }
    }

    private bool HasLocalPlayerObject()
    {
        NetworkRunner runner = FindRunningRunner();
        return runner != null &&
               runner.LocalPlayer != PlayerRef.None &&
               runner.GetPlayerObject(runner.LocalPlayer) != null;
    }

    private bool IsLocalHandPublishing()
    {
        MvpHandSkeletonSync sync = FindLocalHandSync();
        return sync != null &&
               sync.PoseRevision > 0 &&
               sync.LeftHandTracked &&
               sync.RightHandTracked;
    }

    private bool IsRemoteHandsVisible()
    {
#if UNITY_2023_1_OR_NEWER
        MvpRemoteHandSkeletonVisual[] visuals =
            FindObjectsByType<MvpRemoteHandSkeletonVisual>(
                FindObjectsSortMode.None);
#else
        MvpRemoteHandSkeletonVisual[] visuals =
            FindObjectsOfType<MvpRemoteHandSkeletonVisual>();
#endif

        foreach (MvpRemoteHandSkeletonVisual visual in visuals)
        {
            if (visual == null ||
                visual.GetComponent<MvpHandSkeletonSync>()?.Object?.HasInputAuthority == true)
            {
                continue;
            }

            if (visual.IsShowingRemoteHands)
            {
                return true;
            }
        }

        return false;
    }

    private MvpHandSkeletonSync FindLocalHandSync()
    {
        NetworkRunner runner = FindRunningRunner();
        if (runner != null && runner.LocalPlayer != PlayerRef.None)
        {
            NetworkObject localPlayerObject =
                runner.GetPlayerObject(runner.LocalPlayer);
            if (localPlayerObject != null)
            {
                MvpHandSkeletonSync sync =
                    localPlayerObject.GetComponentInChildren<MvpHandSkeletonSync>();
                if (sync != null)
                {
                    return sync;
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        MvpHandSkeletonSync[] syncs =
            FindObjectsByType<MvpHandSkeletonSync>(FindObjectsSortMode.None);
#else
        MvpHandSkeletonSync[] syncs = FindObjectsOfType<MvpHandSkeletonSync>();
#endif

        foreach (MvpHandSkeletonSync sync in syncs)
        {
            if (sync != null && sync.Object != null && sync.Object.HasInputAuthority)
            {
                return sync;
            }
        }

        return null;
    }

    private NetworkRunner FindRunningRunner()
    {
#if UNITY_2023_1_OR_NEWER
        NetworkRunner[] runners =
            FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
#else
        NetworkRunner[] runners = FindObjectsOfType<NetworkRunner>();
#endif

        foreach (NetworkRunner runner in runners)
        {
            if (runner != null && runner.IsRunning)
            {
                return runner;
            }
        }

        return null;
    }

    private void ResolveReferences()
    {
        if (session != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        session = FindFirstObjectByType<MvpNetworkSession>();
#else
        session = FindObjectOfType<MvpNetworkSession>();
#endif
    }

    private void LogState(string phase)
    {
        NetworkRunner runner = FindRunningRunner();
        bool runnerReady = runner != null && runner.IsRunning;
        int playerId = runnerReady && runner.LocalPlayer != PlayerRef.None
            ? runner.LocalPlayer.PlayerId
            : -1;
        MvpHandSkeletonSync local = FindLocalHandSync();
        int remoteVisible = CountRemoteVisibleHands();

        Debug.Log(
            $"[MvpJsHandSyncTestAutomation] TEST_STATE phase={phase} " +
            $"runner={runnerReady} player={playerId} " +
            $"localPoseRevision={(local != null ? local.PoseRevision : -1)} " +
            $"remoteVisible={remoteVisible}");
    }

    private int CountRemoteVisibleHands()
    {
        int count = 0;
#if UNITY_2023_1_OR_NEWER
        MvpRemoteHandSkeletonVisual[] visuals =
            FindObjectsByType<MvpRemoteHandSkeletonVisual>(
                FindObjectsSortMode.None);
#else
        MvpRemoteHandSkeletonVisual[] visuals =
            FindObjectsOfType<MvpRemoteHandSkeletonVisual>();
#endif

        foreach (MvpRemoteHandSkeletonVisual visual in visuals)
        {
            if (visual != null && visual.IsShowingRemoteHands)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasArg(string[] args, string argName)
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

    private static string GetStringArg(
        string[] args,
        string argName,
        string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static float GetFloatArg(
        string[] args,
        string argName,
        float fallback)
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
