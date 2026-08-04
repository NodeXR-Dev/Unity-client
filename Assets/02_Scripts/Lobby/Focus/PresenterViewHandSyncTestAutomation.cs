using System;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PresenterViewHandSyncTestAutomation : MonoBehaviour
{
    [SerializeField] private PresenterViewTestBootstrap bootstrap;
    [SerializeField] private float localReadyTimeoutSeconds = 25f;
    [SerializeField] private float watchTimeoutSeconds = 35f;
    [SerializeField] private float autoQuitSeconds = 70f;

    private bool autoSend;
    private bool autoWatch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForPresenterViewTest()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "PresenterViewTest")
        {
            return;
        }

        string[] args = Environment.GetCommandLineArgs();
        if (!HasArg(args, "-presenterViewHandAutoSend") &&
            !HasArg(args, "-presenterViewHandAutoWatch") &&
            !HasArg(args, "-presenterViewHandAutoTest"))
        {
            return;
        }

        GameObject go = new GameObject("PresenterViewHandSyncTestAutomation");
        go.AddComponent<PresenterViewHandSyncTestAutomation>();
    }

    private void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        autoSend = HasArg(args, "-presenterViewHandAutoSend") ||
                   HasArg(args, "-presenterViewHandAutoTest");
        autoWatch = HasArg(args, "-presenterViewHandAutoWatch");
        autoQuitSeconds = GetFloatArg(args, "-presenterViewAutoQuitSeconds", autoQuitSeconds);

        StartCoroutine(RunAutomation());
    }

    private IEnumerator RunAutomation()
    {
        float startedAt = Time.realtimeSinceStartup;
        ResolveReferences();
        bootstrap?.StartTestSession();

        string role = autoSend ? "send" : "watch";
        Debug.Log($"[PresenterViewHandSyncTestAutomation] BEGIN role={role}");

        yield return WaitForLocalPlayerObject(localReadyTimeoutSeconds);

        bool passed;
        if (autoSend)
        {
            yield return WaitForLocalMockHands(10f);
            passed = IsLocalMockHandsPublishing();
            Debug.Log(passed
                ? "[PresenterViewHandSyncTestAutomation] PASS: local mock hand joints are publishing."
                : "[PresenterViewHandSyncTestAutomation] FAIL: local mock hand joints did not publish.");
        }
        else
        {
            yield return WaitForRemoteHands(watchTimeoutSeconds);
            passed = IsRemoteHandsVisible();
            Debug.Log(passed
                ? "[PresenterViewHandSyncTestAutomation] PASS: remote hand skeleton is visible."
                : "[PresenterViewHandSyncTestAutomation] FAIL: remote hand skeleton was not visible.");
        }

        float remainingSeconds = Mathf.Max(
            1f,
            autoQuitSeconds - (Time.realtimeSinceStartup - startedAt));
        yield return new WaitForSeconds(remainingSeconds);

        Debug.Log($"[PresenterViewHandSyncTestAutomation] RESULT {(passed ? "PASS" : "FAIL")}");
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

    private IEnumerator WaitForLocalMockHands(float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (IsLocalMockHandsPublishing())
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
        NetworkRunner runner = NetworkManager.runnerInsatance;
        return runner != null &&
               runner.IsRunning &&
               runner.LocalPlayer != PlayerRef.None &&
               runner.GetPlayerObject(runner.LocalPlayer) != null;
    }

    private bool IsLocalMockHandsPublishing()
    {
        MvpHandSkeletonSync sync = FindLocalHandSync();
        return sync != null &&
               sync.IsPublishingMockHands &&
               sync.PoseRevision > 0 &&
               sync.LeftHandTracked &&
               sync.RightHandTracked;
    }

    private bool IsRemoteHandsVisible()
    {
#if UNITY_2023_1_OR_NEWER
        MvpRemoteHandSkeletonVisual[] visuals = FindObjectsByType<MvpRemoteHandSkeletonVisual>(FindObjectsSortMode.None);
#else
        MvpRemoteHandSkeletonVisual[] visuals = FindObjectsOfType<MvpRemoteHandSkeletonVisual>();
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
        NetworkRunner runner = NetworkManager.runnerInsatance;
        if (runner != null && runner.LocalPlayer != PlayerRef.None)
        {
            NetworkObject localPlayerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (localPlayerObject != null)
            {
                MvpHandSkeletonSync sync = localPlayerObject.GetComponentInChildren<MvpHandSkeletonSync>();
                if (sync != null)
                {
                    return sync;
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        MvpHandSkeletonSync[] syncs = FindObjectsByType<MvpHandSkeletonSync>(FindObjectsSortMode.None);
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

    private void LogState(string phase)
    {
        NetworkRunner runner = NetworkManager.runnerInsatance;
        bool runnerReady = runner != null && runner.IsRunning;
        int playerId = runnerReady && runner.LocalPlayer != PlayerRef.None
            ? runner.LocalPlayer.PlayerId
            : -1;
        MvpHandSkeletonSync local = FindLocalHandSync();
        int remoteVisible = CountRemoteVisibleHands();

        Debug.Log(
            $"[PresenterViewHandSyncTestAutomation] TEST_STATE phase={phase} runner={runnerReady} " +
            $"player={playerId} localPoseRevision={(local != null ? local.PoseRevision : -1)} " +
            $"localMock={(local != null && local.IsPublishingMockHands)} remoteVisible={remoteVisible}");
    }

    private int CountRemoteVisibleHands()
    {
        int count = 0;
#if UNITY_2023_1_OR_NEWER
        MvpRemoteHandSkeletonVisual[] visuals = FindObjectsByType<MvpRemoteHandSkeletonVisual>(FindObjectsSortMode.None);
#else
        MvpRemoteHandSkeletonVisual[] visuals = FindObjectsOfType<MvpRemoteHandSkeletonVisual>();
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

    private void ResolveReferences()
    {
        if (bootstrap != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        bootstrap = FindFirstObjectByType<PresenterViewTestBootstrap>();
#else
        bootstrap = FindObjectOfType<PresenterViewTestBootstrap>();
#endif
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

    private static float GetFloatArg(string[] args, string argName, float fallback)
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
