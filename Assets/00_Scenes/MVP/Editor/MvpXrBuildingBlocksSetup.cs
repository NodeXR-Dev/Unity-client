#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MvpXrBuildingBlocksSetup
{
    private const string ScenePath =
        "Assets/00_Scenes/MVP/MVP.unity";

    private const string CameraRigBlockId =
        "e47682b9-c270-40b1-b16d-90b627a5ce1b";
    private const string HandInteractionBlockId =
        "0393ca30-f2a9-4865-a40f-f9a68d01c3a9";
    private const string HandRayInteractorBlockId =
        "bc3f7b21-a55c-4cf7-b9c6-78d4341573a4";

    [MenuItem("Tools/MVP/Install Meta XR Building Blocks")]
    public static async void InstallMetaXrBuildingBlocks()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning(
                "[MVP XR] MVP.unity를 연 뒤 실행해 주세요. 현재 씬: " +
                scene.path);
            return;
        }

        try
        {
            await InstallBlockIfMissing(
                CameraRigBlockId,
                "[BuildingBlock] Camera Rig");
            await InstallBlockIfMissing(
                HandInteractionBlockId,
                "[BuildingBlock] Hand Tracking");
            await InstallBlockIfMissing(
                HandRayInteractorBlockId,
                "HandRayInteractor");

            DisableDuplicateStandaloneHands();

            PrepareMvpRuntime();
            DisableLegacyCamera();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[MVP XR] Meta Camera Rig · Hand Tracking · Hand Ray " +
                "Building Block 설치와 공간 노드 제스처 배선을 완료했습니다.");
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[MVP XR] Building Block 설치 실패\n" + exception);
        }
    }

    [MenuItem("Tools/MVP/Validate Meta XR Flow")]
    public static void ValidateMetaXrFlow()
    {
        bool hasRig =
            UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>() != null;
        bool hasHands = HasSceneObjectName("[BuildingBlock] Hand Tracking");
        bool hasRay =
            HasSceneObjectName("HandRayInteractor") ||
            HasSceneObjectName("RayInteractor");
        bool hasGesture =
            UnityEngine.Object.FindFirstObjectByType<
                MvpSpatialNodeGestureController>() != null;

        if (hasRig && hasHands && hasRay && hasGesture)
        {
            Debug.Log(
                "[MVP XR Validate] Camera Rig · 손 추적 · 손 레이 · " +
                "공간 노드 생성기가 모두 준비되었습니다.");
            return;
        }

        Debug.LogError(
            "[MVP XR Validate] 누락 항목 — CameraRig=" + hasRig +
            ", HandTracking=" + hasHands +
            ", HandRay=" + hasRay +
            ", SpatialGesture=" + hasGesture);
    }

    private static async Task InstallBlockIfMissing(
        string blockId,
        string objectNameFragment)
    {
        if (HasSceneObjectName(objectNameFragment))
        {
            Debug.Log(
                "[MVP XR] 기존 Building Block 사용: " +
                objectNameFragment);
            return;
        }

        Type utilsType = FindType(
            "Meta.XR.BuildingBlocks.Editor.Utils");
        if (utilsType == null)
            throw new InvalidOperationException(
                "Meta XR Building Blocks Editor API를 찾을 수 없습니다.");

        MethodInfo getBlockData = utilsType.GetMethod(
            "GetBlockData",
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);
        object blockData =
            getBlockData?.Invoke(null, new object[] { blockId });
        if (blockData == null)
            throw new InvalidOperationException(
                "Building Block 데이터를 찾을 수 없습니다: " + blockId);

        MethodInfo addMethod = FindInstanceMethod(
            blockData.GetType(),
            "AddToProject",
            2);
        if (addMethod == null)
            throw new MissingMethodException(
                blockData.GetType().FullName,
                "AddToProject");

        object result = addMethod.Invoke(
            blockData,
            new object[] { null, null });
        if (result is Task task)
            await task;

        await Task.Yield();
    }

    private static void PrepareMvpRuntime()
    {
        GameObject app = GameObject.Find("MvpApp");
        if (app == null)
        {
            app = new GameObject("MvpApp");
            Undo.RegisterCreatedObjectUndo(
                app,
                "Create MVP App");
        }

        MvpSpatialNodeGestureController gesture =
            app.GetComponent<MvpSpatialNodeGestureController>();
        if (gesture == null)
            gesture = Undo.AddComponent<
                MvpSpatialNodeGestureController>(app);

        GraphManager graphManager =
            UnityEngine.Object.FindFirstObjectByType<GraphManager>();
        SerializedObject serialized =
            new SerializedObject(gesture);
        SerializedProperty graph =
            serialized.FindProperty("_graphManager");
        if (graph != null)
            graph.objectReferenceValue = graphManager;
        SetFloat(serialized, "_fistHoldSeconds", 2f);
        SetFloat(serialized, "_fistDetectionDelay", 0.22f);
        SetFloat(serialized, "_fistReleaseGrace", 0.18f);
        SetFloat(serialized, "_commitHoldSeconds", 0.45f);
        SetFloat(serialized, "_openRearmSeconds", 0.3f);
        SetFloat(serialized, "_closedTipDistance", 0.065f);
        SetFloat(serialized, "_feedbackVerticalOffset", 0.12f);
        serialized.ApplyModifiedPropertiesWithoutUndo();


        MvpXrCameraModeController cameraMode =
            app.GetComponent<MvpXrCameraModeController>();
        if (cameraMode == null)
            cameraMode = Undo.AddComponent<
                MvpXrCameraModeController>(app);

        OVRCameraRig rig =
            UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
        Camera desktopCamera = null;
        Camera[] cameras =
            UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.name == "Main Camera")
            {
                desktopCamera = candidate;
                break;
            }
        }
        cameraMode.Configure(
            rig != null ? rig.gameObject : null,
            desktopCamera);

        MvpXrInteractionBridge interaction =
            app.GetComponent<MvpXrInteractionBridge>();
        if (interaction == null)
            interaction =
                Undo.AddComponent<MvpXrInteractionBridge>(app);

        MvpMeetingRoomPlayerController player =
            app.GetComponent<MvpMeetingRoomPlayerController>();
        if (player == null)
            player =
                Undo.AddComponent<MvpMeetingRoomPlayerController>(app);

        MvpNodeInteractionController nodeInteraction =
            app.GetComponent<MvpNodeInteractionController>();
        if (nodeInteraction == null)
            nodeInteraction =
                Undo.AddComponent<MvpNodeInteractionController>(app);

        EditorUtility.SetDirty(app);
        EditorUtility.SetDirty(gesture);
        EditorUtility.SetDirty(cameraMode);
        EditorUtility.SetDirty(interaction);
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(nodeInteraction);
    }

    private static void SetFloat(
        SerializedObject serialized,
        string propertyName,
        float value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void DisableLegacyCamera()
    {
        OVRCameraRig rig =
            UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
        if (rig == null) return;

        Camera[] cameras =
            UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera == null ||
                camera.transform.IsChildOf(rig.transform))
                continue;

            if (camera.gameObject.name == "Main Camera")
            {
                Undo.RecordObject(
                    camera.gameObject,
                    "Disable Legacy MVP Camera");
                camera.gameObject.SetActive(false);
                EditorUtility.SetDirty(camera.gameObject);
            }
        }
    }

    private static bool HasSceneObjectName(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
            return false;

        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid())
                continue;

            if (candidate.name.IndexOf(
                    fragment,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly assembly in
                 AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName);
            if (type != null)
                return type;
        }
        return null;
    }

    private static MethodInfo FindInstanceMethod(
        Type type,
        string methodName,
        int parameterCount)
    {
        while (type != null)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in methods)
            {
                if (method.Name == methodName &&
                    method.GetParameters().Length == parameterCount)
                    return method;
            }
            type = type.BaseType;
        }
        return null;
    }


    private static void DisableDuplicateStandaloneHands()
    {
        if (!HasSceneObjectName(
                "[BuildingBlock] OVRInteractionComprehensive"))
            return;

        Transform[] transforms =
            Resources.FindObjectsOfTypeAll<Transform>();
        int disabled = 0;
        foreach (Transform candidate in transforms)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                !candidate.name.StartsWith(
                    "[BuildingBlock] Hand Tracking ",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            Undo.RecordObject(
                candidate.gameObject,
                "Disable Duplicate MVP Hand Tracking");
            candidate.gameObject.SetActive(false);
            EditorUtility.SetDirty(candidate.gameObject);
            disabled++;
        }

        Debug.Log(
            "[MVP XR] Comprehensive 손을 사용하도록 standalone Hand Tracking " +
            disabled + "개를 비활성화했습니다.");
    }


    [MenuItem("Tools/MVP/Fix XR Input and Hand Duplicates")]
    public static void FixXrInputAndHandDuplicates()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning(
                "[MVP XR] MVP.unity를 연 뒤 실행해 주세요. 현재 씬: " +
                scene.path);
            return;
        }

        PrepareMvpRuntime();
        DisableDuplicateStandaloneHands();
        DisableLegacyCamera();
        MvpFourPersonRoomSetup.ApplyFourPersonLayout();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[MVP XR] 카메라 모드 배선과 중복 standalone 손 메시를 정리했습니다.");
    }
}
#endif
