using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 히스토리 타임라인을 씬에 자동으로 구성해 주는 에디터 도구.
/// Unity 상단 메뉴 [Tools > History > Create Timeline (Graph + Bar)] 클릭 한 번으로
///  - HistoryNodeGraph(중심에서 뻗는 노드 그래프)
///  - HistoryTimeline(컨트롤러)
///  - World Space Canvas + Slider(하단 바, 좌우로 끄는 점)
/// 를 만들고 서로 배선까지 끝낸다. 이미 있으면 재사용한다.
///
/// 생성 후 Ctrl+S로 씬을 저장하면 된다.
/// </summary>
public static class HistoryTimelineSetup
{
    [MenuItem("Tools/History/Create Timeline (Graph + Bar)")]
    public static void CreateTimeline()
    {
        // 1) EventSystem 보장 (이미 있으면 그대로)
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
        }

        // 2) 노드 그래프 (있으면 재사용, 없으면 생성)
        var graph = Object.FindFirstObjectByType<HistoryNodeGraph>();
        if (graph == null)
        {
            var gGo = new GameObject("HistoryGraph", typeof(HistoryNodeGraph));
            gGo.transform.position = new Vector3(0f, 1.6f, 2f); // 사용자 정면, 눈높이쯤
            Undo.RegisterCreatedObjectUndo(gGo, "Create HistoryGraph");
            graph = gGo.GetComponent<HistoryNodeGraph>();
        }

        // 3) 타임라인 컨트롤러 (있으면 재사용)
        var timeline = Object.FindFirstObjectByType<HistoryTimeline>();
        if (timeline == null)
        {
            var tlGo = new GameObject("HistoryTimeline", typeof(HistoryTimeline));
            Undo.RegisterCreatedObjectUndo(tlGo, "Create HistoryTimeline");
            timeline = tlGo.GetComponent<HistoryTimeline>();
        }

        // 4) World Space Canvas (하단 바가 올라갈 판)
        var canvasGo = new GameObject("HistoryTimeline_Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Timeline Canvas");
        canvasGo.transform.SetParent(timeline.transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) canvas.worldCamera = Camera.main; // 에디터 마우스 클릭용

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(600f, 80f);
        canvasGo.transform.localScale = Vector3.one * 0.0015f; // World Space 캔버스는 기본이 매우 크다
        canvasGo.transform.position = new Vector3(0f, 0.95f, 2f); // 그래프 아래쪽

        // 5) Slider (Unity 표준 구조로 생성: Background / Fill / Handle 자동 포함)
        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.name = "HistoryTimeline_Slider";
        Undo.RegisterCreatedObjectUndo(sliderGo, "Create Timeline Slider");
        sliderGo.transform.SetParent(canvasGo.transform, false);

        var sliderRt = sliderGo.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.anchoredPosition = Vector2.zero;
        sliderRt.sizeDelta = new Vector2(560f, 36f);

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(1f); // 시작은 현재(오른쪽 끝)

        // 6) 배선: timeline.slider = slider, graph.timeline = timeline
        var soTimeline = new SerializedObject(timeline);
        var sliderProp = soTimeline.FindProperty("slider");
        if (sliderProp != null) sliderProp.objectReferenceValue = slider;
        soTimeline.ApplyModifiedProperties();

        var soGraph = new SerializedObject(graph);
        var tlProp = soGraph.FindProperty("timeline");
        if (tlProp != null) tlProp.objectReferenceValue = timeline;
        soGraph.ApplyModifiedProperties();

        // 7) 마무리
        EditorSceneManager.MarkSceneDirty(timeline.gameObject.scene);
        Selection.activeGameObject = timeline.gameObject;
        EditorGUIUtility.PingObject(timeline.gameObject);

        Debug.Log("[History] 타임라인 생성 완료 — 그래프/바/배선 끝. Ctrl+S로 씬을 저장하세요. " +
                  "Play 후 하단 슬라이더(또는 Game뷰에서 마우스)로 과거↔현재를 끌어보세요.");
    }
}
