using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// MVP 씬에 런타임으로 생기는 모든 월드 Canvas를 Meta 손 레이로 조작할 수 있게 연결한다.
// 그래프·UI 원본 코드는 수정하지 않고, Canvas와 입력 필드에 필요한 어댑터만 덧붙인다.
[DefaultExecutionOrder(700)]
public class MvpXrInteractionBridge : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float _rescanSeconds = 0.12f;
    // 포크/레이 상호작용 볼륨의 두께(m, 월드 고정). 캔버스 lossyScale 이 커도(노드 0.1)
    // z 가 0.8m 로 부풀어 손이 멀어져도 호버가 유지되던 문제를 막는다. (이슈 2·5)
    [SerializeField, Min(0.01f)] private float _interactionDepthMeters = 0.06f;

    private float _nextScanTime;
    private bool _reportedReady;

    public int WiredCanvasCount { get; private set; }
    public int WiredInputCount { get; private set; }

    private void Awake()
    {
        WireScene();
    }

    private void OnEnable()
    {
        _nextScanTime = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + _rescanSeconds;
        WireScene();
    }

    public void WireScene()
    {
        EnsurePointableCanvasModule();

        int canvasCount = 0;
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (Canvas canvas in canvases)
        {
            if (!IsSceneCanvas(canvas) ||
                canvas.renderMode != RenderMode.WorldSpace ||
                !canvas.isActiveAndEnabled ||
                !HasInteractiveContent(canvas) ||
                AlreadyWiredByDesigner(canvas))
                continue;

            if (WireCanvas(canvas))
                canvasCount++;
        }

        int inputCount = 0;
        TMP_InputField[] inputs =
            Resources.FindObjectsOfTypeAll<TMP_InputField>();
        foreach (TMP_InputField input in inputs)
        {
            if (input == null ||
                !input.gameObject.scene.IsValid() ||
                input.gameObject.scene != gameObject.scene)
                continue;

            MvpXrKeyboardInput keyboard =
                input.GetComponent<MvpXrKeyboardInput>();
            if (keyboard == null)
                keyboard =
                    input.gameObject.AddComponent<MvpXrKeyboardInput>();

            keyboard.Configure(input);
            inputCount++;
        }

        WiredCanvasCount = canvasCount;
        WiredInputCount = inputCount;

        if (!_reportedReady && canvasCount > 0)
        {
            _reportedReady = true;
            Debug.Log(
                "[MVP XR] 손 레이·핀치 입력 준비: Canvas " +
                canvasCount + "개, 입력칸 " + inputCount + "개");
        }
    }

    /// <summary>
    /// 디자이너 캔버스처럼 자식에 ISDK 리그(PointableCanvas)가 이미 붙어 있으면 건너뛴다.
    ///
    /// 여기서 또 PointableCanvas / RayInteractable 을 붙이면 같은 캔버스가 두 번
    /// 등록되어 포인터 이벤트가 중복되고, 호버가 매 프레임 뒤집혀 레이가 깜빡인다.
    /// 로비 캔버스 3개가 그 경우다(ISDK_RayCanvasInteraction 을 이미 갖고 있다).
    /// </summary>
    private static bool AlreadyWiredByDesigner(Canvas canvas)
    {
        PointableCanvas[] existing =
            canvas.GetComponentsInChildren<PointableCanvas>(true);

        foreach (PointableCanvas pointable in existing)
        {
            // 이 다리가 붙인 것은 캔버스 자신에 올라간다.
            // 자식에 있는 것은 원본 리그가 담당하고 있다는 뜻이다.
            if (pointable != null && pointable.gameObject != canvas.gameObject)
                return true;
        }

        return false;
    }

    private bool WireCanvas(Canvas canvas)
    {
        if (canvas == null)
            return false;

        Camera activeCamera = Camera.main;
        if (activeCamera != null &&
            activeCamera.isActiveAndEnabled &&
            canvas.worldCamera != activeCamera)
            canvas.worldCamera = activeCamera;

        PrepareSelectableTargets(canvas);

        GraphicRaycaster raycaster =
            canvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster =
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        raycaster.enabled = true;
        raycaster.ignoreReversedGraphics = false;
        raycaster.blockingObjects =
            GraphicRaycaster.BlockingObjects.None;

        RectTransform rect = canvas.transform as RectTransform;
        // 상호작용 볼륨을 캔버스 rect 전체가 아니라 실제 보이는 raycastTarget 그래픽 경계에 맞춘다.
        // 노드 내부 Canvas 처럼 rect(481)이 내용물(±4)보다 훨씬 큰 경우 월드 ~48m 볼륨을 막는다. (이슈 2)
        Vector3 contentCenter;
        Vector2 size;
        ComputeInteractiveContentBounds(canvas, rect, out contentCenter, out size);
        // z 두께는 캔버스 스케일과 무관하게 월드 고정 깊이로 환산한다(기존 상수 8 은 노드에서 ~0.8m).
        float depthLocalZ = _interactionDepthMeters /
            Mathf.Max(0.0001f, Mathf.Abs(canvas.transform.lossyScale.z));

        BoxCollider box = canvas.GetComponent<BoxCollider>();
        if (box == null)
            box = canvas.gameObject.AddComponent<BoxCollider>();
        box.center = contentCenter;
        box.size = new Vector3(
            Mathf.Max(1f, Mathf.Abs(size.x)),
            Mathf.Max(1f, Mathf.Abs(size.y)),
            depthLocalZ);
        box.isTrigger = true;

        PointableCanvas pointable =
            canvas.GetComponent<PointableCanvas>();
        if (pointable == null)
            pointable =
                canvas.gameObject.AddComponent<PointableCanvas>();
        pointable.InjectAllPointableCanvas(canvas);

        ColliderSurface colliderSurface =
            canvas.GetComponent<ColliderSurface>();
        if (colliderSurface == null)
            colliderSurface =
                canvas.gameObject.AddComponent<ColliderSurface>();
        colliderSurface.InjectAllColliderSurface(box);

        RayInteractable rayInteractable =
            canvas.GetComponent<RayInteractable>();
        if (rayInteractable == null)
            rayInteractable =
                canvas.gameObject.AddComponent<RayInteractable>();
        rayInteractable.InjectAllRayInteractable(colliderSurface);
        rayInteractable.InjectOptionalPointableElement(pointable);
        rayInteractable.enabled = true;

        PlaneSurface planeSurface =
            canvas.GetComponent<PlaneSurface>();
        if (planeSurface == null)
            planeSurface =
                canvas.gameObject.AddComponent<PlaneSurface>();
        // 포크는 손가락이 normal 의 +쪽(surface 위)에서 -normal 방향으로 눌러야 발동한다(Meta PokeInteractor).
        // 캔버스 +Z 는 읽기 정상을 위해 유저 반대편을 향하므로, poke normal 은 반대(-Z=유저 쪽)여야
        // 유저가 자기 쪽에서 자연스럽게 누른다. Forward(+Z=유저 반대) 였을 때 "바깥→안쪽"으로 눌러야 했던 원인.
        planeSurface.InjectAllPlaneSurface(
            PlaneSurface.NormalFacing.Backward,
            true);

        BoundsClipper boundsClipper =
            canvas.GetComponent<BoundsClipper>();
        if (boundsClipper == null)
            boundsClipper =
                canvas.gameObject.AddComponent<BoundsClipper>();
        boundsClipper.Position = contentCenter;
        boundsClipper.Size = new Vector3(
            Mathf.Max(1f, Mathf.Abs(size.x)),
            Mathf.Max(1f, Mathf.Abs(size.y)),
            depthLocalZ);

        ClippedPlaneSurface pokeSurface =
            canvas.GetComponent<ClippedPlaneSurface>();
        if (pokeSurface == null)
            pokeSurface =
                canvas.gameObject.AddComponent<ClippedPlaneSurface>();
        pokeSurface.InjectAllClippedPlaneSurface(
            planeSurface,
            new IBoundsClipper[] { boundsClipper });

        PokeInteractable pokeInteractable =
            canvas.GetComponent<PokeInteractable>();
        if (pokeInteractable == null)
            pokeInteractable =
                canvas.gameObject.AddComponent<PokeInteractable>();
        pokeInteractable.InjectAllPokeInteractable(pokeSurface);
        pokeInteractable.InjectOptionalPointableElement(pointable);
        pokeInteractable.enabled = true;

        return true;
    }

    // 캔버스 하위의 활성 raycastTarget 그래픽만 모아 캔버스-로컬 공간의 타이트한 경계를 구한다.
    // 그래픽이 하나도 없으면 기존 동작(rect 전체, 중심 0)을 유지한다.
    private static void ComputeInteractiveContentBounds(
        Canvas canvas,
        RectTransform rect,
        out Vector3 center,
        out Vector2 size)
    {
        bool has = false;
        Bounds local = default;
        Vector3[] corners = new Vector3[4];
        Graphic[] graphics =
            canvas.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null ||
                !graphic.raycastTarget ||
                !graphic.isActiveAndEnabled ||
                FindNearestCanvas(graphic.transform) != canvas)
                continue;

            graphic.rectTransform.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p =
                    canvas.transform.InverseTransformPoint(corners[i]);
                if (!has)
                {
                    local = new Bounds(p, Vector3.zero);
                    has = true;
                }
                else
                {
                    local.Encapsulate(p);
                }
            }
        }

        if (has)
        {
            center = new Vector3(local.center.x, local.center.y, 0f);
            size = new Vector2(local.size.x, local.size.y);
            return;
        }

        center = Vector3.zero;
        size = rect != null ? rect.rect.size : new Vector2(100f, 100f);
    }

    private static void PrepareSelectableTargets(Canvas canvas)
    {
        Selectable[] selectables =
            canvas.GetComponentsInChildren<Selectable>(true);
        foreach (Selectable selectable in selectables)
        {
            if (selectable == null ||
                FindNearestCanvas(selectable.transform) != canvas)
                continue;

            Graphic target = selectable.targetGraphic;
            if (target == null)
            {
                target = selectable.GetComponent<Graphic>();
                if (target == null)
                    target =
                        selectable.GetComponentInChildren<Graphic>(true);
                if (target != null)
                    selectable.targetGraphic = target;
            }

            if (target != null)
                target.raycastTarget = true;
        }
    }


    private bool HasInteractiveContent(Canvas canvas)
    {
        Selectable[] selectables =
            canvas.GetComponentsInChildren<Selectable>(true);
        foreach (Selectable selectable in selectables)
        {
            if (selectable != null &&
                FindNearestCanvas(selectable.transform) == canvas)
                return true;
        }

        Graphic[] graphics =
            canvas.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic != null &&
                graphic.raycastTarget &&
                FindNearestCanvas(graphic.transform) == canvas)
                return true;
        }

        return false;
    }

    private static Canvas FindNearestCanvas(Transform current)
    {
        while (current != null)
        {
            Canvas canvas = current.GetComponent<Canvas>();
            if (canvas != null)
                return canvas;
            current = current.parent;
        }
        return null;
    }

    private bool IsSceneCanvas(Canvas canvas)
    {
        return canvas != null &&
               canvas.gameObject.scene.IsValid() &&
               canvas.gameObject.scene == gameObject.scene;
    }

    private static void EnsurePointableCanvasModule()
    {
        EventSystem eventSystem =
            EventSystem.current ??
            FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
            return;

        PointableCanvasModule module =
            eventSystem.GetComponent<PointableCanvasModule>();
        if (module == null)
            module =
                eventSystem.gameObject.AddComponent<
                    PointableCanvasModule>();

        module.ExclusiveMode = false;

        // 손 poke 는 손가락이 미세하게 흔들려 기본 임계값(10px)을 쉽게 넘긴다. 그러면
        // PointableCanvasModule.ProcessDrag 가 이를 "드래그"로 오인해 노드 글씨를 누를 때
        // 노드가 손을 따라 움직이고(도망) 클릭(키보드 열기)이 취소된다.
        // 임계값을 크게 올려 짧은 poke 를 확실한 클릭으로 처리한다. (의도적인 큰 드래그만 이동)
        eventSystem.pixelDragThreshold =
            Mathf.Max(eventSystem.pixelDragThreshold, 140);
    }
}
