using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// MVP.unity에 작업공간 레이아웃 컴포넌트를 배치하고 참조를 연결한다.
// 씬 YAML을 직접 수정하지 않고 이 메뉴를 통해서만 배치한다.
public static class MvpWorkspaceLayoutSetup
{
    [MenuItem("Tools/MVP/Arrange Water Rocket Workspace")]
    public static void ArrangeWaterRocketWorkspace()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[MVP Layout] Play 모드를 종료한 뒤 메뉴를 실행하세요.");
            return;
        }

        GraphManager graphManager = Object.FindFirstObjectByType<GraphManager>();
        MainSketchView sketchView = Object.FindFirstObjectByType<MainSketchView>();
        Canvas sketchCanvas = sketchView != null
            ? sketchView.GetComponentInParent<Canvas>()
            : null;
        Transform sketchPanel = sketchCanvas != null
            ? sketchCanvas.transform
            : GameObject.Find("MainSketchPanel")?.transform;
        Camera camera = Camera.main;

        if (graphManager == null || sketchPanel == null || camera == null)
        {
            Debug.LogError(
                "[MVP Layout] 배치 실패: GraphManager, MainSketchPanel, Main Camera가 모두 필요합니다.");
            return;
        }

        MvpWorkspaceLayout layout =
            Object.FindFirstObjectByType<MvpWorkspaceLayout>();

        if (layout == null)
        {
            var go = new GameObject("MvpWorkspaceLayout");
            Undo.RegisterCreatedObjectUndo(go, "Create MVP Workspace Layout");
            layout = Undo.AddComponent<MvpWorkspaceLayout>(go);
        }

        // 이전 씬에 저장된 과도한 축소값을 권장 가독성/조작성 프로필로 갱신한다.
        Undo.RecordObject(layout, nameof(MvpWorkspaceLayout));
        layout.ApplyRecommendedLayoutProfile();
        EditorUtility.SetDirty(layout);

        var serialized = new SerializedObject(layout);
        SetReference(serialized, "_graphManager", graphManager);
        SetReference(serialized, "_mainSketchPanel", sketchPanel);
        SetReference(serialized, "_camera", camera);
        serialized.ApplyModifiedProperties();

        Undo.RecordObject(sketchPanel, "Arrange MVP Main Sketch Panel");
        layout.ApplyPanelPreview();

        EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
        Selection.activeGameObject = layout.gameObject;
        EditorGUIUtility.PingObject(layout.gameObject);

        Debug.Log(
            "[MVP Layout] 물로켓 작업공간 배치 완료. Ctrl+S 후 Play하면 " +
            "중앙 스케치와 PROPERTY/REFERENCE 영역이 의미 기반으로 정렬됩니다.");
    }

    private static void SetReference(
        SerializedObject serialized,
        string propertyName,
        Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning(
                $"[MVP Layout] 직렬화 필드를 찾지 못했습니다: {propertyName}");
            return;
        }

        property.objectReferenceValue = value;
    }
}
