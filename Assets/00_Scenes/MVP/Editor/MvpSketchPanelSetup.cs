/*
 * 파일명: MvpSketchPanelSetup.cs
 * 목적: [Step 1] 파트 추가 UI(MainSketchPanel)를 현재 열린 씬에 넣고 배선하는 에디터 도구.
 *
 * 메뉴 [Tools > MVP > Add Sketch Panel (Parts UI)] 클릭 한 번으로:
 *   - MainSketchPanel 프리팹(월드 캔버스, ALL포트+파트포트줄 내장)을 씬에 인스턴스화
 *   - MainSketchView 의 씬 참조 2개(_graphManager, _apiClient) 연결
 *   - PartNodeApiClient 를 offlineFallback=true 로 (백엔드 없이 로컬 파트 생성 = 마우스 검증용)
 *   - 카메라 앞에 배치 + worldCamera 설정(마우스/VR 레이캐스트)
 *
 * 사용: MVP.unity 열고 이 메뉴 실행 → Ctrl+S → Play → 빈 원 포트 클릭으로 파트 추가.
 * (VR 손레이/핀치 배선은 다음 단계에서 PointableCanvas 로 추가)
 */
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class MvpSketchPanelSetup
{
    private const string PanelPrefabPath = "Assets/01_Prefabs/Graph/Main/MainSketchPanel.prefab";

    [MenuItem("Tools/MVP/Add Sketch Panel (Parts UI)")]
    public static void AddSketchPanel()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[MVP] MainSketchPanel 프리팹을 찾지 못함: {PanelPrefabPath}");
            return;
        }

        // 씬의 핵심 참조 수집
        var graphManager = Object.FindFirstObjectByType<GraphManager>();
        var apiClient    = Object.FindFirstObjectByType<PartNodeApiClient>();
        var syncClient   = Object.FindFirstObjectByType<GraphSyncClient>();
        if (graphManager == null)
            Debug.LogWarning("[MVP] 씬에 GraphManager가 없습니다. MainSketchView가 동작하지 않습니다.");
        if (apiClient == null)
            Debug.LogWarning("[MVP] 씬에 PartNodeApiClient가 없습니다. 파트 생성이 안 됩니다.");

        // EventSystem 보장 (uGUI 포인터 이벤트용)
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        // 패널 인스턴스화 (프리팹 링크 유지 → 이후 프리팹 수정 반영됨)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Add Sketch Panel");
        instance.name = "MainSketchPanel";

        // 카메라 앞 배치 + worldCamera (마우스/VR 레이가 캔버스를 맞추도록)
        var cam = Camera.main;
        var canvas = instance.GetComponentInChildren<Canvas>();
        if (canvas != null)
        {
            if (cam != null) canvas.worldCamera = cam;
            // uGUI 포인터 이벤트를 받으려면 GraphicRaycaster 필요
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        if (cam != null)
            instance.transform.position = cam.transform.position + cam.transform.forward * 1.5f + Vector3.down * 0.3f;

        // MainSketchView 배선 (씬 참조 2개만; 내부 포트/프리팹은 프리팹에 이미 배선됨)
        var view = instance.GetComponentInChildren<MainSketchView>();
        if (view != null)
        {
            var so = new SerializedObject(view);
            SetRef(so, "_graphManager", graphManager);
            SetRef(so, "_apiClient", apiClient);
            so.ApplyModifiedProperties();
        }
        else Debug.LogWarning("[MVP] 패널에서 MainSketchView를 찾지 못했습니다.");

        // PartNodeApiClient: 백엔드 없이 파트 생성되도록 offlineFallback + 참조 보강
        if (apiClient != null)
        {
            var so = new SerializedObject(apiClient);
            var off = so.FindProperty("_offlineFallback");
            if (off != null) off.boolValue = true; // 서버 미배포 상태 UI 테스트
            EnsureRef(so, "_graphManager", graphManager);
            EnsureRef(so, "_syncClient", syncClient);
            so.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(instance.scene);
        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);

        Debug.Log("[MVP] Sketch Panel 추가 완료. Ctrl+S 후 Play → 패널 끝의 빈 원 포트를 클릭해 파트 추가(백엔드 없이 로컬 생성). " +
                  "패널이 안 보이면 위치/스케일을 조정하세요. VR 손레이 배선은 다음 단계.");
    }

    private static void SetRef(SerializedObject so, string prop, Object val)
    {
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogWarning($"[MVP] 필드 없음: {so.targetObject.GetType().Name}.{prop}"); return; }
        p.objectReferenceValue = val;
    }

    // 이미 연결돼 있으면 유지, 비어있을 때만 채운다.
    private static void EnsureRef(SerializedObject so, string prop, Object val)
    {
        var p = so.FindProperty(prop);
        if (p == null || p.objectReferenceValue != null) return;
        p.objectReferenceValue = val;
    }
}
