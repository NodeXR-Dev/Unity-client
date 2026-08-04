using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(220)]
public class MvpGenerated3DSync : MonoBehaviour
{
    private const BindingFlags PrivateInstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private GraphNetworkManager graphNetwork;
    [SerializeField] private MvpClassroomFlow classroomFlow;
    [SerializeField] private MvpWaterRocketGraphController waterRocketGraph;
    [SerializeField] private MvpWorkspaceLayout workspaceLayout;
    [SerializeField] private Transform boardOverride;
    [SerializeField] private Button generateButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool replaceGenerateButtonClick = true;
    [SerializeField] private bool keepExistingToggleBehavior = true;
    [SerializeField] private bool exposeStageToClassroomFlow = true;
    [SerializeField] private float requestTimeoutSeconds = 10f;
    [SerializeField] private float finishDelaySeconds = 2.5f;
    [SerializeField] private string stageObjectName = "MvpRocket3DStage";
    [SerializeField] private float boardRightOffset = 0.55f;
    [SerializeField] private float boardForwardOffset = -0.38f;
    [SerializeField] private float verticalOffset = 0.05f;
    [SerializeField] private float stageScale = 1.25f;

    private GraphNetworkManager subscribedGraphNetwork;
    private Button boundButton;
    private bool networkGenerating;
    private bool waitingForStartAccept;
    private int activeVersion;
    private int designVariant;
    private float nextResolveTime;
    private Coroutine requestTimeoutCoroutine;
    private Coroutine finishCoroutine;

    public bool IsBusy => networkGenerating || waitingForStartAccept;

    private void OnEnable()
    {
        ResolveReferences();
        RefreshBindings();
    }

    private IEnumerator Start()
    {
        yield return null;
        ResolveReferences();
        RefreshBindings();
    }

    private void Update()
    {
        if (autoFindReferences && Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 1f;
            ResolveReferences();
            RefreshBindings();
        }

        if (IsBusy && generateButton != null && generateButton.interactable)
            generateButton.interactable = false;
    }

    private void OnDisable()
    {
        UnbindGraphNetwork();
        UnbindButton();
        StopRequestTimeout();
        StopFinishWait();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
    }

    public void RequestGenerate3D()
    {
        if (!TryRequestGenerate3D())
            RequestLocalOnlyGenerate();
    }

    public bool TryRequestGenerate3D()
    {
        ResolveReferences();
        RefreshBindings();

        if (IsBusy)
        {
            SetUiGenerating(true, "3D generation is in progress.");
            return true;
        }

        if (keepExistingToggleBehavior && ToggleExistingStageIfPresent())
            return true;

        if (graphNetwork == null || !graphNetwork.IsRpcReady)
            return false;

        waitingForStartAccept = true;
        SetUiGenerating(true, "Requesting 3D generation.");
        RestartRequestTimeout();
        graphNetwork.RequestGenerated3DStart(BuildCurrentDesignJson());
        return true;
    }

    private void RequestLocalOnlyGenerate()
    {
        if (IsBusy)
            return;

        SpawnOrReplaceStage(GetCurrentDesign());
        SetUiGenerating(false, "3D model created locally.");
    }

    private void HandleGenerated3DStart(
        int version,
        string designJson,
        PlayerRef requester)
    {
        StopRequestTimeout();
        waitingForStartAccept = false;
        networkGenerating = true;
        activeVersion = version;

        SetUiGenerating(
            true,
            IsLocalRequester(requester)
                ? "Building 3D model."
                : "Another user is generating a 3D model.");

        MvpRocket3DStage stage = SpawnOrReplaceStage(DesignFromJson(designJson));
        StartFinishWait(version, stage);
    }

    private void HandleGenerated3DStartRejected(PlayerRef requester)
    {
        if (!IsLocalRequester(requester))
            return;

        StopRequestTimeout();
        waitingForStartAccept = false;
        SetUiGenerating(networkGenerating, "Another 3D generation is in progress.");
    }

    private void HandleGenerated3DFinished(int version)
    {
        if (activeVersion > 0 && version != activeVersion)
            return;

        StopRequestTimeout();
        StopFinishWait();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        SetUiGenerating(false, "3D model generated.");
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
            return;

        if (graphNetwork == null)
            graphNetwork = FindFirstObjectByType<GraphNetworkManager>();
        if (classroomFlow == null)
            classroomFlow = FindFirstObjectByType<MvpClassroomFlow>();
        if (waterRocketGraph == null)
            waterRocketGraph = FindFirstObjectByType<MvpWaterRocketGraphController>();
        if (workspaceLayout == null)
            workspaceLayout = FindFirstObjectByType<MvpWorkspaceLayout>();

        if (generateButton == null)
            generateButton = FindSceneButton("Generate3D", "Generate3DButton");
        if (statusText == null && classroomFlow != null)
            statusText = GetPrivateField<TMP_Text>(classroomFlow, "_workspaceStatus");
    }

    private void RefreshBindings()
    {
        BindGraphNetwork();
        BindButton();
    }

    private void BindGraphNetwork()
    {
        if (subscribedGraphNetwork == graphNetwork)
            return;

        UnbindGraphNetwork();

        subscribedGraphNetwork = graphNetwork;
        if (subscribedGraphNetwork == null)
            return;

        subscribedGraphNetwork.Generated3DStartReceived += HandleGenerated3DStart;
        subscribedGraphNetwork.Generated3DStartRejected += HandleGenerated3DStartRejected;
        subscribedGraphNetwork.Generated3DFinishedReceived += HandleGenerated3DFinished;
    }

    private void UnbindGraphNetwork()
    {
        if (subscribedGraphNetwork == null)
            return;

        subscribedGraphNetwork.Generated3DStartReceived -= HandleGenerated3DStart;
        subscribedGraphNetwork.Generated3DStartRejected -= HandleGenerated3DStartRejected;
        subscribedGraphNetwork.Generated3DFinishedReceived -= HandleGenerated3DFinished;
        subscribedGraphNetwork = null;
    }

    private void BindButton()
    {
        if (!replaceGenerateButtonClick)
            return;

        if (boundButton == generateButton)
        {
            EnsureBoundButtonListener();
            return;
        }

        UnbindButton();

        boundButton = generateButton;
        if (boundButton == null)
            return;

        EnsureBoundButtonListener();
    }

    private void UnbindButton()
    {
        if (boundButton == null)
            return;

        boundButton.onClick.RemoveListener(RequestGenerate3D);
        boundButton = null;
    }

    private void EnsureBoundButtonListener()
    {
        if (boundButton == null)
            return;

        boundButton.onClick.RemoveAllListeners();
        boundButton.onClick.AddListener(RequestGenerate3D);
    }

    private string BuildCurrentDesignJson()
    {
        return JsonUtility.ToJson(DesignDto.FromDesign(GetCurrentDesign()));
    }

    private MvpRocketDesign GetCurrentDesign()
    {
        int variant = GetCurrentDesignVariant();
        if (waterRocketGraph != null)
            return waterRocketGraph.GetRocketDesign(variant);

        return new MvpRocketDesign { accentIndex = variant };
    }

    private int GetCurrentDesignVariant()
    {
        int fallback = designVariant++;
        if (classroomFlow == null)
            return fallback;

        ICollection history =
            GetPrivateField<ICollection>(classroomFlow, "_history");
        return history != null ? history.Count : fallback;
    }

    private MvpRocket3DStage SpawnOrReplaceStage(MvpRocketDesign design)
    {
        ClearExistingStages();

        string safeName =
            string.IsNullOrWhiteSpace(stageObjectName)
                ? "MvpRocket3DStage"
                : stageObjectName;
        GameObject go = new GameObject(safeName);
        MvpRocket3DStage stage = go.AddComponent<MvpRocket3DStage>();

        ApplyStagePose(go.transform);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, stageScale);
        go.SetActive(true);
        stage.Build(design);

        ExposeStageToClassroomFlow(stage);
        return stage;
    }

    private void ApplyStagePose(Transform stageTransform)
    {
        Transform board = boardOverride;
        if (board == null && workspaceLayout != null)
            board = workspaceLayout.MainSketchPanel;

        if (board != null)
        {
            stageTransform.position =
                board.position +
                board.right * boardRightOffset +
                board.forward * boardForwardOffset +
                Vector3.up * verticalOffset;
            stageTransform.rotation =
                Quaternion.LookRotation(board.forward, Vector3.up);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 forward =
            Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        stageTransform.position =
            cam.transform.position +
            forward * 1.05f -
            Vector3.up * 0.28f;
        stageTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    private bool ToggleExistingStageIfPresent()
    {
        MvpRocket3DStage stage = FindExistingStage();
        if (stage == null)
            return false;

        bool nextActive = !stage.gameObject.activeSelf;
        stage.gameObject.SetActive(nextActive);
        ExposeStageToClassroomFlow(stage);
        return true;
    }

    private void ClearExistingStages()
    {
        MvpRocket3DStage[] stages =
            Resources.FindObjectsOfTypeAll<MvpRocket3DStage>();
        for (int i = stages.Length - 1; i >= 0; i--)
        {
            MvpRocket3DStage stage = stages[i];
            if (!IsSceneStage(stage))
                continue;

            stage.Clear();
            if (Application.isPlaying)
                Destroy(stage.gameObject);
            else
                DestroyImmediate(stage.gameObject);
        }
    }

    private MvpRocket3DStage FindExistingStage()
    {
        MvpRocket3DStage[] stages =
            Resources.FindObjectsOfTypeAll<MvpRocket3DStage>();
        for (int i = 0; i < stages.Length; i++)
        {
            MvpRocket3DStage stage = stages[i];
            if (IsSceneStage(stage) && stage.name == stageObjectName)
                return stage;
        }

        for (int i = 0; i < stages.Length; i++)
        {
            MvpRocket3DStage stage = stages[i];
            if (IsSceneStage(stage))
                return stage;
        }

        return null;
    }

    private static bool IsSceneStage(MvpRocket3DStage stage)
    {
        return stage != null && stage.gameObject.scene.IsValid();
    }

    private void ExposeStageToClassroomFlow(MvpRocket3DStage stage)
    {
        if (!exposeStageToClassroomFlow)
            return;

        ResolveReferences();
        if (classroomFlow == null)
            return;

        SetPrivateField(classroomFlow, "_rocketStage", stage);
        InvokePrivateMethod(classroomFlow, "RefreshThreeDViewButton");
    }

    private void StartFinishWait(int version, MvpRocket3DStage stage)
    {
        StopFinishWait();
        finishCoroutine = StartCoroutine(WaitThenFinish(version, stage));
    }

    private void StopFinishWait()
    {
        if (finishCoroutine == null)
            return;

        StopCoroutine(finishCoroutine);
        finishCoroutine = null;
    }

    private IEnumerator WaitThenFinish(int version, MvpRocket3DStage stage)
    {
        float deadline =
            Time.unscaledTime + Mathf.Max(0.25f, finishDelaySeconds);
        while (Time.unscaledTime < deadline)
        {
            if (stage != null && stage.BuildComplete)
                break;
            yield return null;
        }

        finishCoroutine = null;
        if (activeVersion == version)
            graphNetwork?.RequestGenerated3DFinish(version);
    }

    private void RestartRequestTimeout()
    {
        StopRequestTimeout();
        requestTimeoutCoroutine = StartCoroutine(WaitForRequestTimeout());
    }

    private void StopRequestTimeout()
    {
        if (requestTimeoutCoroutine == null)
            return;

        StopCoroutine(requestTimeoutCoroutine);
        requestTimeoutCoroutine = null;
    }

    private IEnumerator WaitForRequestTimeout()
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(1f, requestTimeoutSeconds));
        requestTimeoutCoroutine = null;

        if (waitingForStartAccept)
        {
            waitingForStartAccept = false;
            SetUiGenerating(false, "3D generation request timed out.");
        }
    }

    private void SetUiGenerating(bool generating, string message)
    {
        if (generateButton != null)
            generateButton.interactable = !generating;
        if (statusText != null && !string.IsNullOrEmpty(message))
            statusText.text = message;
    }

    private bool IsLocalRequester(PlayerRef requester)
    {
        return graphNetwork != null &&
               requester != PlayerRef.None &&
               requester == graphNetwork.LocalPlayerRef;
    }

    private static MvpRocketDesign DesignFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MvpRocketDesign();

        try
        {
            DesignDto dto = JsonUtility.FromJson<DesignDto>(json);
            return dto != null ? dto.ToDesign() : new MvpRocketDesign();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[MvpGenerated3DSync] Failed to parse design json: " +
                exception.Message);
            return new MvpRocketDesign();
        }
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return default;

        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceFlags);
        if (field == null)
            return default;

        object value = field.GetValue(target);
        return value is T typed ? typed : default;
    }

    private static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceFlags);
        if (field != null)
            field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
            return;

        MethodInfo method =
            target.GetType().GetMethod(methodName, PrivateInstanceFlags);
        if (method != null && method.GetParameters().Length == 0)
            method.Invoke(target, null);
    }

    private static Button FindSceneButton(params string[] names)
    {
        Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button button in buttons)
        {
            if (button == null || !button.gameObject.scene.IsValid())
                continue;

            foreach (string name in names)
            {
                if (button.name == name)
                    return button;
            }
        }

        return null;
    }

    [Serializable]
    private class DesignDto
    {
        public bool hasBody;
        public bool hasFins;
        public bool hasNose;
        public float bodyLength = 1f;
        public float bodySlim = 1f;
        public float finSpan = 1f;
        public int finCount = 3;
        public bool pointedNose;
        public float waterFill = 0.42f;
        public int accentIndex;
        public int partCount;
        public int requirementCount;
        public int connectionCount;
        public int bodyReqCount;
        public int finReqCount;
        public int noseReqCount;

        public static DesignDto FromDesign(MvpRocketDesign design)
        {
            if (design == null)
                design = new MvpRocketDesign();

            return new DesignDto
            {
                hasBody = design.hasBody,
                hasFins = design.hasFins,
                hasNose = design.hasNose,
                bodyLength = design.bodyLength,
                bodySlim = design.bodySlim,
                finSpan = design.finSpan,
                finCount = design.finCount,
                pointedNose = design.pointedNose,
                waterFill = design.waterFill,
                accentIndex = design.accentIndex,
                partCount = design.partCount,
                requirementCount = design.requirementCount,
                connectionCount = design.connectionCount,
                bodyReqCount = design.bodyReqs.Count,
                finReqCount = design.finReqs.Count,
                noseReqCount = design.noseReqs.Count
            };
        }

        public MvpRocketDesign ToDesign()
        {
            MvpRocketDesign design = new MvpRocketDesign
            {
                hasBody = hasBody,
                hasFins = hasFins,
                hasNose = hasNose,
                bodyLength = bodyLength,
                bodySlim = bodySlim,
                finSpan = finSpan,
                finCount = finCount,
                pointedNose = pointedNose,
                waterFill = waterFill,
                accentIndex = accentIndex,
                partCount = partCount,
                requirementCount = requirementCount,
                connectionCount = connectionCount
            };

            FillRequirements(bodyReqCount, design.bodyReqs);
            FillRequirements(finReqCount, design.finReqs);
            FillRequirements(noseReqCount, design.noseReqs);
            return design;
        }

        private static void FillRequirements(int count, List<string> target)
        {
            if (target == null)
                return;

            for (int i = 0; i < Mathf.Clamp(count, 0, 3); i++)
                target.Add("requirement");
        }
    }
}
