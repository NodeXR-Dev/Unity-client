/*
 * 파일명: MvpConnectSetup.cs
 * 목적: [Step: 연결] 드래그 연결 컨트롤러를 현재 씬에 넣고 배선하는 에디터 도구.
 * 메뉴 [Tools > MVP > Add Connect Dragger (Drag to Connect)] 클릭 한 번.
 */
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MvpConnectSetup
{
    [MenuItem("Tools/MVP/Add Connect Dragger (Drag to Connect)")]
    public static void AddConnectDragger()
    {
        if (Object.FindFirstObjectByType<GraphConnectDragController>() != null)
        {
            Debug.Log("[MVP] GraphConnectDragController가 이미 씬에 있습니다.");
            return;
        }

        var go = new GameObject("GraphConnectDragger", typeof(GraphConnectDragController));
        Undo.RegisterCreatedObjectUndo(go, "Add Connect Dragger");

        var ctrl = go.GetComponent<GraphConnectDragController>();
        var so = new SerializedObject(ctrl);
        SetRef(so, "_graphManager", Object.FindFirstObjectByType<GraphManager>());
        if (Camera.main != null) SetRef(so, "_camera", Camera.main);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log("[MVP] Connect Dragger 추가 완료. Ctrl+S 후 Play → 파트 포트(윗줄)에서 속성 노드로 " +
                  "마우스 드래그하면 노란 선이 생기고 연결됩니다. 콘솔에 [Connect] 로그.");
    }

    private static void SetRef(SerializedObject so, string prop, Object val)
    {
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogWarning($"[MVP] 필드 없음: {so.targetObject.GetType().Name}.{prop}"); return; }
        p.objectReferenceValue = val;
    }
}
