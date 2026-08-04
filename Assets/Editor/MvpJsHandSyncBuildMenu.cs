using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

public static class MvpJsHandSyncBuildMenu
{
    private const string ScenePath = "Assets/00_Scenes/MVP/mvp_js.unity";
    private const string OutputPath = "Builds/MvpJsHandSync/mvp_js.exe";
    private const string SenderLogPath = "Builds/MvpJsHandSync/HandSyncSender.log";
    private const string WatcherLogPath = "Builds/MvpJsHandSync/HandSyncWatcher.log";
    private const string SessionName = "MvpJsHandSyncTestRoom";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    [MenuItem("Tools/MVP JS/Build And Run Hand Sync Test Pair")]
    public static void BuildAndRunHandSyncTestPair()
    {
        if (QueueUntilEditorReady(
                BuildAndRunHandSyncTestPair,
                "MVP JS hand sync test pair build"))
            return;

        if (!BuildMvpJs(BuildOptions.Development))
        {
            return;
        }

        LaunchTestClient(
            "MVP JS Hand Sender",
            SenderLogPath,
            "-mvpJsHandAutoStart -mvpJsHandAutoSend -presenterViewMockHands " +
            $"-mvpJsSession {SessionName} -mvpJsNickname Sender " +
            "-mvpJsAutoQuitSeconds 90");

        LaunchTestClient(
            "MVP JS Hand Watcher",
            WatcherLogPath,
            "-mvpJsHandAutoStart -mvpJsHandAutoWatch " +
            $"-mvpJsSession {SessionName} -mvpJsNickname Watcher " +
            "-mvpJsAutoQuitSeconds 90");

        Debug.Log(
            "MVP JS hand sync test pair launched. " +
            "Watch the second window for the remote gray hand skeleton. " +
            $"Logs: {Path.GetFullPath(SenderLogPath)}, {Path.GetFullPath(WatcherLogPath)}");
    }

    [MenuItem("Tools/MVP JS/Build mvp_js Scene")]
    public static void BuildMvpJsScene()
    {
        if (QueueUntilEditorReady(BuildMvpJsScene, "MVP JS scene build"))
            return;

        BuildMvpJs(BuildOptions.Development);
    }

    private static bool BuildMvpJs(BuildOptions options)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Debug.LogWarning(
                "mvp_js build skipped because Unity is still compiling or importing assets. " +
                "Use the menu again after the editor is ready.");
            return false;
        }

        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"mvp_js scene was not found: {ScenePath}");
            return false;
        }

        string outputDirectory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            DeleteRuntimeActionBindingsIfPresent(outputDirectory);
        }

        XRGeneralSettings xrSettings =
            XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
                BuildTargetGroup.Standalone);
        bool hasXrSettings = xrSettings != null;
        XRManagerSettings xrManagerSettings =
            hasXrSettings ? xrSettings.AssignedSettings : null;
        bool originalInitXrOnStart =
            hasXrSettings && xrSettings.InitManagerOnStart;
        List<XRLoader> originalXrLoaders =
            xrManagerSettings != null
                ? new List<XRLoader>(xrManagerSettings.activeLoaders)
                : null;
        FullScreenMode originalFullScreenMode = PlayerSettings.fullScreenMode;
        int originalScreenWidth = PlayerSettings.defaultScreenWidth;
        int originalScreenHeight = PlayerSettings.defaultScreenHeight;
        bool originalRunInBackground = PlayerSettings.runInBackground;
        bool originalForceSingleInstance = PlayerSettings.forceSingleInstance;
        EditorBuildSettingsScene[] originalBuildScenes =
            EditorBuildSettings.scenes;

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
                    Debug.LogWarning(
                        "mvp_js hand sync build could not temporarily clear Standalone XR loaders.");
                }

                EditorUtility.SetDirty(xrManagerSettings);
            }

            AssetDatabase.SaveAssets();

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = WindowWidth;
            PlayerSettings.defaultScreenHeight = WindowHeight;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = false;
            EditorBuildSettings.scenes =
                new[] { new EditorBuildSettingsScene(ScenePath, true) };

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
                Debug.Log($"mvp_js build succeeded: {Path.GetFullPath(OutputPath)}");
                return true;
            }

            Debug.LogError($"mvp_js build failed: {summary.result}");
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
                    Debug.LogWarning(
                        "mvp_js hand sync build could not restore Standalone XR loaders. " +
                        "Please check Project Settings > XR Plug-in Management.");
                }

                EditorUtility.SetDirty(xrManagerSettings);
            }

            AssetDatabase.SaveAssets();

            PlayerSettings.fullScreenMode = originalFullScreenMode;
            PlayerSettings.defaultScreenWidth = originalScreenWidth;
            PlayerSettings.defaultScreenHeight = originalScreenHeight;
            PlayerSettings.runInBackground = originalRunInBackground;
            PlayerSettings.forceSingleInstance = originalForceSingleInstance;
            EditorBuildSettings.scenes = originalBuildScenes;
        }
    }

    private static bool QueueUntilEditorReady(Action action, string description)
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
            return false;

        Debug.Log($"{description} queued until Unity finishes compiling/importing.");
        EditorApplication.delayCall += () => RunWhenEditorReady(action, description);
        return true;
    }

    private static void RunWhenEditorReady(Action action, string description)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += () => RunWhenEditorReady(action, description);
            return;
        }

        Debug.Log($"{description} starting after Unity became ready.");
        action();
    }

    private static void DeleteRuntimeActionBindingsIfPresent(string outputDirectory)
    {
        string bindingsPath = Path.Combine(outputDirectory, "RuntimeActionBindings.json");
        if (!File.Exists(bindingsPath))
            return;

        try
        {
            File.Delete(bindingsPath);
        }
        catch (IOException exception)
        {
            Debug.LogWarning(
                "Could not delete existing RuntimeActionBindings.json before build: " +
                exception.Message);
        }
    }

    private static void LaunchTestClient(
        string processName,
        string logPath,
        string extraArgs)
    {
        string exePath = Path.GetFullPath(OutputPath);
        if (!File.Exists(exePath))
        {
            Debug.LogError($"mvp_js executable was not found: {exePath}");
            return;
        }

        string logFullPath = Path.GetFullPath(logPath);
        string logDirectory = Path.GetDirectoryName(logFullPath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        string arguments =
            $"-screen-fullscreen 0 -screen-width {WindowWidth} " +
            $"-screen-height {WindowHeight} -logFile \"{logFullPath}\" {extraArgs}";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory =
                Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false
        };

        System.Diagnostics.Process process =
            System.Diagnostics.Process.Start(startInfo);
        Debug.Log(process != null
            ? $"Started {processName}: pid={process.Id}"
            : $"Failed to start {processName}");
    }
}
