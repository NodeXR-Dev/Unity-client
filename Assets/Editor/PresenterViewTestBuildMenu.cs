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

    private static void BuildTestClient(BuildOptions options)
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"Presenter view test scene was not found: {ScenePath}");
            return;
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
            }
            else
            {
                Debug.LogError($"Presenter view test build failed: {summary.result}");
            }
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
}
