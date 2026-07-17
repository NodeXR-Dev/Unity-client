/*
 * 파일명: MeetingRoomHistorySetup.cs
 * 목적: 히스토리 기능을 씬에 한 번에 배치 + 배선하는 에디터 도구.
 *
 * Unity 상단 메뉴 [Tools > MeetingRoom > Create History (Bar + Graph)] 클릭 한 번으로:
 *   - HistoryView       : MeetingRoomHistoryGraphView (디자이너 NodeBox 프리팹으로 렌더) + GraphRoot
 *   - HistoryController : MeetingRoomHistoryController (바 ↔ 스냅샷 ↔ 뷰 오케스트레이터)
 *   - HistoryBar        : World Space Canvas + Slider + MeetingRoomHistoryTimeline
 * 를 만들고 서로 배선까지 끝낸다.
 *
 * 생성 후 Ctrl+S로 씬 저장 → Play → 하단 바(또는 Game뷰 마우스)로 과거↔현재를 드래그.
 * 기본 useMock=true 라 백엔드 없이 바로 동작한다.
 */
using Oculus.Interaction;          // PointableCanvas, PointableCanvasModule, RayInteractable
using Oculus.Interaction.Surfaces; // ColliderSurface
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class MeetingRoomHistorySetup
{
    private const string NodeBoxPrefabPath = "Assets/05_Design/JW/UI/Prefabs/NodeBox.prefab";

    [MenuItem("Tools/MeetingRoom/Create History (Bar + Graph)")]
    public static void CreateHistory()
    {
        // 0) 디자이너 NodeBox 프리팹 로드
        var nodeBoxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NodeBoxPrefabPath);
        if (nodeBoxPrefab == null)
            Debug.LogWarning($"[History] NodeBox 프리팹을 찾지 못함: {NodeBoxPrefabPath} (뷰에 수동 연결 필요)");

        // 1) EventSystem 보장 (마우스: StandaloneInputModule / VR 레이: PointableCanvasModule)
        var eventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
            eventSystem = esGo.GetComponent<EventSystem>();
        }
        // ISDK 레이 입력을 uGUI 이벤트로 변환하는 모듈 (없으면 추가)
        if (eventSystem.GetComponent<PointableCanvasModule>() == null)
            Undo.AddComponent<PointableCanvasModule>(eventSystem.gameObject);

        // 2) 루트
        var root = new GameObject("History");
        Undo.RegisterCreatedObjectUndo(root, "Create History Root");

        // 3) 히스토리 전용 뷰 (NodeBox 렌더) + GraphRoot
        var viewGo = new GameObject("HistoryView", typeof(MeetingRoomHistoryGraphView));
        Undo.RegisterCreatedObjectUndo(viewGo, "Create HistoryView");
        viewGo.transform.SetParent(root.transform, false);
        viewGo.transform.position = new Vector3(0f, 0f, 2f); // 노드 로컬좌표(y≈1.5~2.9)가 눈높이 앞쪽에 오도록
        var view = viewGo.GetComponent<MeetingRoomHistoryGraphView>();

        var graphRoot = new GameObject("GraphRoot");
        Undo.RegisterCreatedObjectUndo(graphRoot, "Create GraphRoot");
        graphRoot.transform.SetParent(viewGo.transform, false);

        var soView = new SerializedObject(view);
        SetRef(soView, "graphRoot", graphRoot.transform);
        if (nodeBoxPrefab != null) SetRef(soView, "nodeBoxPrefab", nodeBoxPrefab);
        soView.ApplyModifiedProperties();

        // 4) 하단 바: World Space Canvas + Slider + Timeline
        var canvasGo = new GameObject("HistoryBar",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MeetingRoomHistoryTimeline));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create HistoryBar");
        canvasGo.transform.SetParent(root.transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) canvas.worldCamera = Camera.main;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(600f, 80f);
        canvasGo.transform.localScale = Vector3.one * 0.0015f; // World Space 캔버스 기본이 매우 큼
        canvasGo.transform.position = new Vector3(0f, 0.9f, 2f); // 그래프 아래쪽

        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.name = "HistoryBar_Slider";
        Undo.RegisterCreatedObjectUndo(sliderGo, "Create History Slider");
        sliderGo.transform.SetParent(canvasGo.transform, false);

        var sliderRt = sliderGo.GetComponent<RectTransform>();
        sliderRt.anchorMin = sliderRt.anchorMax = sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.anchoredPosition = Vector2.zero;
        sliderRt.sizeDelta = new Vector2(560f, 36f);

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(1f); // 시작은 현재(오른쪽 끝)

        var timeline = canvasGo.GetComponent<MeetingRoomHistoryTimeline>();
        var soTl = new SerializedObject(timeline);
        SetRef(soTl, "slider", slider);
        soTl.ApplyModifiedProperties();

        // 4-a) 스냅샷 틱(눈금) 오버레이 — 슬라이더 위에 깔고 핸들 아래로 배치
        var ticksGo = new GameObject("Ticks", typeof(RectTransform), typeof(MeetingRoomHistoryTimelineTicks));
        Undo.RegisterCreatedObjectUndo(ticksGo, "Create Ticks");
        ticksGo.transform.SetParent(sliderGo.transform, false);
        var ticksRt = ticksGo.GetComponent<RectTransform>();
        ticksRt.anchorMin = Vector2.zero;
        ticksRt.anchorMax = Vector2.one;
        ticksRt.offsetMin = Vector2.zero;
        ticksRt.offsetMax = Vector2.zero;
        ticksGo.transform.SetSiblingIndex(2); // Background/Fill 위, Handle 아래
        var ticks = ticksGo.GetComponent<MeetingRoomHistoryTimelineTicks>();

        // 4-b) VR 손 레이/핀치로 바를 만질 수 있게 ISDK 배선 (씬의 ISDK_RayInteraction 이 이걸 잡는다)
        SetupVrRayInteraction(canvasGo, canvas);

        // 5) 컨트롤러 (바 ↔ 뷰 배선)
        var ctrlGo = new GameObject("HistoryController", typeof(MeetingRoomHistoryController));
        Undo.RegisterCreatedObjectUndo(ctrlGo, "Create HistoryController");
        ctrlGo.transform.SetParent(root.transform, false);
        var ctrl = ctrlGo.GetComponent<MeetingRoomHistoryController>();

        var soCtrl = new SerializedObject(ctrl);
        SetRef(soCtrl, "historyView", view);
        SetRef(soCtrl, "timeline", timeline);
        SetRef(soCtrl, "ticks", ticks);
        soCtrl.ApplyModifiedProperties();

        // 6) 마무리
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("[History] 생성 완료 — 바/뷰(NodeBox)/컨트롤러 + VR 레이(PointableCanvas) 배선 끝. " +
                  "Ctrl+S 후 Play. VR에서 손 레이로 슬라이더 핸들을 가리키고 핀치(집기)로 드래그하세요(마우스도 가능). " +
                  "노드가 겹치거나 안 보이면 HistoryView의 Node Scale/Layout Spread 조정.");
    }

    // World Space Canvas 를 ISDK 레이로 만질 수 있게 배선.
    // 완성형 샘플(OculusInteractionSamplesRayCanvas)과 동일 구성:
    //   Canvas 오브젝트에 [PointableCanvas + BoxCollider + ColliderSurface + RayInteractable] 를 얹고
    //   RayInteractable._surface = ColliderSurface, RayInteractable._pointableElement = PointableCanvas 로 연결.
    // 씬에 이미 있는 [BuildingBlock] ISDK_RayInteraction 의 RayInteractor 가 이 RayInteractable 을 자동으로 잡는다.
    private static void SetupVrRayInteraction(GameObject canvasGo, Canvas canvas)
    {
        var rt = canvasGo.GetComponent<RectTransform>();

        // 레이가 물리적으로 맞을 콜라이더 (캔버스 로컬 크기 = sizeDelta, 두께 약간)
        var box = Undo.AddComponent<BoxCollider>(canvasGo);
        box.center = Vector3.zero;
        box.size = new Vector3(rt.sizeDelta.x, rt.sizeDelta.y, 1f);

        var pointable = Undo.AddComponent<PointableCanvas>(canvasGo);
        var surface = Undo.AddComponent<ColliderSurface>(canvasGo);
        var interactable = Undo.AddComponent<RayInteractable>(canvasGo);

        var soP = new SerializedObject(pointable);
        SetRef(soP, "_canvas", canvas);
        soP.ApplyModifiedProperties();

        var soS = new SerializedObject(surface);
        SetRef(soS, "_collider", box);
        soS.ApplyModifiedProperties();

        var soI = new SerializedObject(interactable);
        SetRef(soI, "_surface", surface);          // ISurface
        SetRef(soI, "_pointableElement", pointable); // IPointableElement (= PointableCanvas)
        soI.ApplyModifiedProperties();
    }

    // private [SerializeField] 필드를 이름으로 찾아 연결. 없으면 경고.
    private static void SetRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop == null)
        {
            Debug.LogWarning($"[History] 필드를 찾지 못함: {so.targetObject.GetType().Name}.{propName}");
            return;
        }
        prop.objectReferenceValue = value;
    }
}
