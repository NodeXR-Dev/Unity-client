#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MvpFullFlowSetup
{
    private const string ScenePath =
        "Assets/00_Scenes/MVP/MVP.unity";

    [MenuItem("Tools/MVP/Build Full Water Rocket Flow")]
    public static void BuildFullFlow()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning(
                "[MVP Setup] MVP.unity를 연 뒤 실행해 주세요. 현재 씬: " +
                scene.path);
            return;
        }

        GraphManager graphManager =
            Object.FindFirstObjectByType<GraphManager>();
        GraphSyncClient syncClient =
            Object.FindFirstObjectByType<GraphSyncClient>();
        Generate2DController generate2D =
            Object.FindFirstObjectByType<Generate2DController>();
        MainSketchView sketchView =
            Object.FindFirstObjectByType<MainSketchView>();
        MvpWorkspaceLayout layout =
            Object.FindFirstObjectByType<MvpWorkspaceLayout>();

        if (graphManager == null || sketchView == null)
        {
            Debug.LogError(
                "[MVP Setup] GraphManager 또는 MainSketchView가 없습니다. " +
                "기존 MVP 그래프 배선을 먼저 확인해 주세요.");
            return;
        }

        GameObject app = GameObject.Find("MvpApp");
        if (app == null)
        {
            app = new GameObject("MvpApp");
            Undo.RegisterCreatedObjectUndo(
                app, "Create MvpApp");
        }

        MvpWaterRocketGraphController graphFlow =
            GetOrAdd<MvpWaterRocketGraphController>(app);
        MvpClassroomFlow classroomFlow =
            GetOrAdd<MvpClassroomFlow>(app);

        Assign(
            graphFlow,
            "_graphManager",
            graphManager);

        SerializedObject flowObject =
            new SerializedObject(classroomFlow);
        SetObject(flowObject, "_graphManager", graphManager);
        SetObject(
            flowObject,
            "_waterRocketGraph",
            graphFlow);
        SetObject(
            flowObject,
            "_graphSyncClient",
            syncClient);
        SetObject(
            flowObject,
            "_generate2DController",
            generate2D);
        SetObject(
            flowObject,
            "_mainSketchView",
            sketchView);
        SetObject(
            flowObject,
            "_workspaceLayout",
            layout);
        SetString(
            flowObject,
            "_backendHost",
            "127.0.0.1:8000");
        SetBool(
            flowObject,
            "_tryBackendFirst",
            true);
        flowObject.ApplyModifiedPropertiesWithoutUndo();

        SeedGraphLoader seed =
            Object.FindFirstObjectByType<SeedGraphLoader>();
        if (seed != null)
        {
            Undo.RecordObject(seed, "Disable MVP Seed");
            seed.enabled = false;
            EditorUtility.SetDirty(seed);
        }

        if (syncClient != null)
        {
            SerializedObject syncObject =
                new SerializedObject(syncClient);
            SetBool(syncObject, "_autoConnect", false);
            SetBool(syncObject, "_sendToServer", false);
            syncObject.ApplyModifiedPropertiesWithoutUndo();
        }

        PartNodeApiClient partApi =
            Object.FindFirstObjectByType<PartNodeApiClient>();
        if (partApi != null)
        {
            SerializedObject partObject =
                new SerializedObject(partApi);
            SetBool(partObject, "_offlineFallback", true);
            partObject.ApplyModifiedPropertiesWithoutUndo();
        }

        if (layout != null)
        {
            Undo.RecordObject(
                layout, "Apply MVP Student Layout");
            layout.ApplyRecommendedLayoutProfile();
            layout.ApplyPanelPreview();
            EditorUtility.SetDirty(layout);
        }

        MvpWorkspacePolish polish =
            Object.FindFirstObjectByType<MvpWorkspacePolish>();
        if (polish != null)
        {
            Undo.RecordObject(
                polish, "Apply MVP Student Polish");
            polish.ApplyNow();
            EditorUtility.SetDirty(polish);
        }

        EditorUtility.SetDirty(app);
        EditorUtility.SetDirty(graphFlow);
        EditorUtility.SetDirty(classroomFlow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = app;
        Debug.Log(
            "[MVP Setup] 전체 물로켓 수업 흐름 설치 완료. " +
            "Play 후 '바로 체험하기'로 전체 경로를 확인하세요.");
    }

    [MenuItem("Tools/MVP/Validate Full Water Rocket Flow")]
    public static void ValidateFullFlow()
    {
        bool ok = true;

        MvpClassroomFlow flow =
            Object.FindFirstObjectByType<MvpClassroomFlow>();
        MvpWaterRocketGraphController graphFlow =
            Object.FindFirstObjectByType<MvpWaterRocketGraphController>();
        GraphManager graphManager =
            Object.FindFirstObjectByType<GraphManager>();
        MainSketchView sketchView =
            Object.FindFirstObjectByType<MainSketchView>();

        if (flow == null)
        {
            ok = false;
            Debug.LogError(
                "[MVP Validate] MvpClassroomFlow 누락");
        }
        if (graphFlow == null)
        {
            ok = false;
            Debug.LogError(
                "[MVP Validate] MvpWaterRocketGraphController 누락");
        }
        if (graphManager == null)
        {
            ok = false;
            Debug.LogError(
                "[MVP Validate] GraphManager 누락");
        }
        if (sketchView == null)
        {
            ok = false;
            Debug.LogError(
                "[MVP Validate] MainSketchView 누락");
        }

        if (ok)
            Debug.Log(
                "[MVP Validate] 필수 흐름·그래프·스케치 참조가 모두 준비되었습니다.");
    }

    private static T GetOrAdd<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
            component = Undo.AddComponent<T>(target);
        return component;
    }

    private static void Assign(
        Object target,
        string propertyName,
        Object value)
    {
        SerializedObject serialized =
            new SerializedObject(target);
        SetObject(serialized, propertyName, value);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObject(
        SerializedObject serialized,
        string propertyName,
        Object value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetString(
        SerializedObject serialized,
        string propertyName,
        string value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetBool(
        SerializedObject serialized,
        string propertyName,
        bool value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }
}
#endif
