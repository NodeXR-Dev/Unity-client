using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

public static class PresenterViewTestBuildMenu
{
    private const string ScenePath = "Assets/00_Scenes/10_Lobby/PresenterViewTest.unity";
    private const string OutputPath = "Builds/PresenterViewTest/PresenterViewTest.exe";
    private const string SenderLogPath = "Builds/PresenterViewTest/HandSyncSender.log";
    private const string WatcherLogPath = "Builds/PresenterViewTest/HandSyncWatcher.log";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    [MenuItem("Tools/Presenter View Test/Build And Run Test Client")]
    public static void BuildAndRunTestClient()
    {
        BuildTestClient(BuildOptions.Development | BuildOptions.AutoRunPlayer);
    }

    [MenuItem("Tools/Presenter View Test/Build Test Client")]
    public static void BuildTestClient()
    {
        BuildTestClient(BuildOptions.Development);
    }

    [MenuItem("Tools/Presenter View Test/Build And Run Hand Sync Test Pair")]
    public static void BuildAndRunHandSyncTestPair()
    {
        if (!BuildTestClient(BuildOptions.Development))
        {
            return;
        }

        LaunchTestClient(
            "HandSyncSender",
            SenderLogPath,
            "-presenterViewMockHands -presenterViewHandAutoSend -presenterViewAutoQuitSeconds 90");
        LaunchTestClient(
            "HandSyncWatcher",
            WatcherLogPath,
            "-presenterViewHandAutoWatch -presenterViewAutoQuitSeconds 90");

        Debug.Log(
            "Hand sync test pair launched. Watch the second window for the remote gray hand skeleton. " +
            $"Logs: {Path.GetFullPath(SenderLogPath)}, {Path.GetFullPath(WatcherLogPath)}");
    }

    private static bool BuildTestClient(BuildOptions options)
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"Presenter view test scene was not found: {ScenePath}");
            return false;
        }

        string outputDirectory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        XRGeneralSettings xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        bool hasXrSettings = xrSettings != null;
        XRManagerSettings xrManagerSettings = hasXrSettings ? xrSettings.AssignedSettings : null;
        bool originalInitXrOnStart = hasXrSettings && xrSettings.InitManagerOnStart;
        List<XRLoader> originalXrLoaders = xrManagerSettings != null ? new List<XRLoader>(xrManagerSettings.activeLoaders) : null;
        FullScreenMode originalFullScreenMode = PlayerSettings.fullScreenMode;
        int originalScreenWidth = PlayerSettings.defaultScreenWidth;
        int originalScreenHeight = PlayerSettings.defaultScreenHeight;
        bool originalRunInBackground = PlayerSettings.runInBackground;
        bool originalForceSingleInstance = PlayerSettings.forceSingleInstance;

        try
        {
            if (hasXrSettings)
            {
                xrSettings.InitManagerOnStart = false;
                EditorUtility.SetDirty(xrSettings);
            }

            if (xrManagerSettings != null)
            {
                if (!xrManagerSettings.TrySetLoaders(new List<XRLoader>()))
                {
                    Debug.LogWarning("Presenter view test build could not temporarily clear Standalone XR loaders.");
                }

                EditorUtility.SetDirty(xrManagerSettings);
            }

            AssetDatabase.SaveAssets();

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = WindowWidth;
            PlayerSettings.defaultScreenHeight = WindowHeight;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = false;

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = options
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Presenter view test build succeeded: {Path.GetFullPath(OutputPath)}");
                return true;
            }

            Debug.LogError($"Presenter view test build failed: {summary.result}");
            return false;
        }
        finally
        {
            if (hasXrSettings)
            {
                xrSettings.InitManagerOnStart = originalInitXrOnStart;
                EditorUtility.SetDirty(xrSettings);
            }

            if (xrManagerSettings != null && originalXrLoaders != null)
            {
                if (!xrManagerSettings.TrySetLoaders(originalXrLoaders))
                {
                    Debug.LogWarning("Presenter view test build could not restore Standalone XR loaders. Please check Project Settings > XR Plug-in Management.");
                }

                EditorUtility.SetDirty(xrManagerSettings);
            }

            AssetDatabase.SaveAssets();

            PlayerSettings.fullScreenMode = originalFullScreenMode;
            PlayerSettings.defaultScreenWidth = originalScreenWidth;
            PlayerSettings.defaultScreenHeight = originalScreenHeight;
            PlayerSettings.runInBackground = originalRunInBackground;
            PlayerSettings.forceSingleInstance = originalForceSingleInstance;
        }
    }

    private static void LaunchTestClient(string processName, string logPath, string extraArgs)
    {
        string exePath = Path.GetFullPath(OutputPath);
        if (!File.Exists(exePath))
        {
            Debug.LogError($"Presenter view test executable was not found: {exePath}");
            return;
        }

        string logFullPath = Path.GetFullPath(logPath);
        string logDirectory = Path.GetDirectoryName(logFullPath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        string arguments =
            $"-screen-fullscreen 0 -screen-width {WindowWidth} -screen-height {WindowHeight} " +
            $"-logFile \"{logFullPath}\" {extraArgs}";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false
        };

        System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
        Debug.Log(process != null
            ? $"Started {processName}: pid={process.Id}"
            : $"Failed to start {processName}");
    }
}
