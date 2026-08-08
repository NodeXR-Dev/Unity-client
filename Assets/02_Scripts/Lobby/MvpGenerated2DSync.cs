using System;
using System.Collections;
using System.Reflection;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[DefaultExecutionOrder(200)]
public class MvpGenerated2DSync : MonoBehaviour
{
    private const BindingFlags PrivateInstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private GraphNetworkManager graphNetwork;
    [SerializeField] private Generate2DController generate2DController;
    [SerializeField] private GraphSyncClient graphSyncClient;
    [SerializeField] private MvpWaterRocketGraphController waterRocketGraph;
    [SerializeField] private RawImage centerImage;
    [SerializeField] private Button generateButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool replaceGenerateButtonClick = true;
    [SerializeField] private float requestTimeoutSeconds = 125f;

    private GraphNetworkManager subscribedGraphNetwork;
    private GraphSyncClient subscribedGraphSyncClient;
    private Button boundButton;

    private bool networkGenerating;
    private bool waitingForStartAccept;
    private int activeVersion;
    private int localRequestVersion;
    private int designVariant;
    private float nextResolveTime;

    private Coroutine requestTimeoutCoroutine;
    private Coroutine downloadCoroutine;
    private Texture2D mockTexture;
    private Texture2D downloadedTexture;

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
        UnbindGraphSyncClient();
        UnbindButton();
        StopRequestTimeout();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        localRequestVersion = 0;
    }

    private void OnDestroy()
    {
        if (mockTexture != null)
            Destroy(mockTexture);
        if (downloadedTexture != null)
            Destroy(downloadedTexture);
    }

    public void RequestGenerateGraphAll()
    {
        if (!TryRequestGenerateGraphAll())
            RequestLocalOnlyGenerate();
    }

    public bool TryRequestGenerateGraphAll()
    {
        ResolveReferences();
        RefreshBindings();

        if (IsBusy)
        {
            SetUiGenerating(true, "2D 이미지 생성 중입니다.");
            return true;
        }

        if (graphNetwork == null || !graphNetwork.IsRpcReady)
            return false;

        waitingForStartAccept = true;
        SetUiGenerating(true, "2D 이미지 생성 요청 중입니다.");
        graphNetwork.RequestGenerated2DStart(BuildCurrentDesignJson());
        return true;
    }

    private void RequestLocalOnlyGenerate()
    {
        if (IsBusy)
            return;

        MvpRocketDesign design = GetCurrentDesign();
        ApplyMockImage(design);

        if (graphSyncClient != null &&
            graphSyncClient.IsConnected &&
            generate2DController != null)
        {
            generate2DController.RequestGenerateGraphAll();
        }
        else
        {
            SetUiGenerating(false, "2D 목 이미지가 표시되었습니다.");
        }
    }

    private void HandleGenerated2DStart(
        int version,
        string designJson,
        PlayerRef requester)
    {
        waitingForStartAccept = false;
        networkGenerating = true;
        activeVersion = version;

        bool localRequester = IsLocalRequester(requester);
        SetUiGenerating(
            true,
            localRequester
                ? "내 2D 이미지를 생성 중입니다."
                : "다른 사용자가 2D 이미지를 생성 중입니다.");

        ApplyMockImage(DesignFromJson(designJson));

        if (!localRequester)
            return;

        localRequestVersion = version;
        if (generate2DController == null)
        {
            localRequestVersion = 0;
            graphNetwork?.RequestGenerated2DFinish(version);
            return;
        }

        generate2DController.RequestGenerateGraphAll();
        if (!IsControllerGenerating())
        {
            localRequestVersion = 0;
            graphNetwork?.RequestGenerated2DFinish(version);
            return;
        }

        RestartRequestTimeout(version);
    }

    private void HandleGenerated2DStartRejected(PlayerRef requester)
    {
        if (!IsLocalRequester(requester))
            return;

        waitingForStartAccept = false;
        SetUiGenerating(networkGenerating, "다른 사용자가 2D 이미지를 생성 중입니다.");
    }

    private void HandleServerImageFromGraphSync(string imgUrl)
    {
        if (localRequestVersion <= 0 || graphNetwork == null)
            return;

        StopRequestTimeout();
        int version = localRequestVersion;
        localRequestVersion = 0;
        graphNetwork.RequestGenerated2DServerImage(
            version,
            string.Empty,
            string.Empty,
            imgUrl);
    }

    private void HandleGenerated2DServerImage(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        if (activeVersion > 0 && version != activeVersion)
            return;

        StopRequestTimeout();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        localRequestVersion = 0;
        SetUiGenerating(false, "2D 이미지 생성 완료");

        if (!string.IsNullOrEmpty(imgUrl))
            StartServerImageDownload(imgUrl);
    }

    private void HandleGenerated2DFinished(int version)
    {
        if (activeVersion > 0 && version != activeVersion)
            return;

        StopRequestTimeout();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        localRequestVersion = 0;
        SetUiGenerating(false, "2D 이미지 생성이 종료되었습니다.");
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
            return;

        if (generate2DController == null)
            generate2DController = FindFirstObjectByType<Generate2DController>();
        if (graphSyncClient == null)
            graphSyncClient =
                generate2DController != null
                    ? generate2DController.GetComponent<GraphSyncClient>()
                    : FindFirstObjectByType<GraphSyncClient>();
        if (graphNetwork == null)
            graphNetwork = FindFirstObjectByType<GraphNetworkManager>();
        if (waterRocketGraph == null)
            waterRocketGraph = FindFirstObjectByType<MvpWaterRocketGraphController>();

        if (centerImage == null && generate2DController != null)
            centerImage = GetPrivateField<RawImage>(generate2DController, "_centerImage");
        if (centerImage == null)
        {
            MainSketchView sketchView = FindFirstObjectByType<MainSketchView>();
            if (sketchView != null)
                centerImage = sketchView.GetComponentInChildren<RawImage>(true);
        }

        if (generateButton == null && generate2DController != null)
            generateButton = GetPrivateField<Button>(generate2DController, "_generateButton");
        if (generateButton == null)
            generateButton = FindSceneButton("Generate2DButton", "Generate2D");

        if (statusText == null && generate2DController != null)
            statusText = GetPrivateField<TMP_Text>(generate2DController, "_statusText");
    }

    private void RefreshBindings()
    {
        BindGraphNetwork();
        BindGraphSyncClient();
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

        subscribedGraphNetwork.Generated2DStartReceived += HandleGenerated2DStart;
        subscribedGraphNetwork.Generated2DStartRejected += HandleGenerated2DStartRejected;
        subscribedGraphNetwork.Generated2DServerImageReceived += HandleGenerated2DServerImage;
        subscribedGraphNetwork.Generated2DFinishedReceived += HandleGenerated2DFinished;
    }

    private void UnbindGraphNetwork()
    {
        if (subscribedGraphNetwork == null)
            return;

        subscribedGraphNetwork.Generated2DStartReceived -= HandleGenerated2DStart;
        subscribedGraphNetwork.Generated2DStartRejected -= HandleGenerated2DStartRejected;
        subscribedGraphNetwork.Generated2DServerImageReceived -= HandleGenerated2DServerImage;
        subscribedGraphNetwork.Generated2DFinishedReceived -= HandleGenerated2DFinished;
        subscribedGraphNetwork = null;
    }

    private void BindGraphSyncClient()
    {
        if (subscribedGraphSyncClient == graphSyncClient)
            return;

        UnbindGraphSyncClient();

        subscribedGraphSyncClient = graphSyncClient;
        if (subscribedGraphSyncClient != null)
            subscribedGraphSyncClient.OnImage2DGenerated += HandleServerImageFromGraphSync;
    }

    private void UnbindGraphSyncClient()
    {
        if (subscribedGraphSyncClient == null)
            return;

        subscribedGraphSyncClient.OnImage2DGenerated -= HandleServerImageFromGraphSync;
        subscribedGraphSyncClient = null;
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

        boundButton.onClick.RemoveListener(RequestGenerateGraphAll);
        boundButton = null;
    }

    private void EnsureBoundButtonListener()
    {
        if (boundButton == null)
            return;

        boundButton.onClick.RemoveAllListeners();
        boundButton.onClick.AddListener(RequestGenerateGraphAll);
    }

    private string BuildCurrentDesignJson()
    {
        return JsonUtility.ToJson(DesignDto.FromDesign(GetCurrentDesign()));
    }

    private MvpRocketDesign GetCurrentDesign()
    {
        int variant = designVariant++;
        if (waterRocketGraph != null)
            return waterRocketGraph.GetRocketDesign(variant);

        return new MvpRocketDesign { accentIndex = variant };
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
            Debug.LogWarning("[MvpGenerated2DSync] Failed to parse design json: " + exception.Message);
            return new MvpRocketDesign();
        }
    }

    private void ApplyMockImage(MvpRocketDesign design)
    {
        ResolveReferences();
        if (centerImage == null)
            return;

        Texture2D nextMock = MvpFallbackSketchGenerator.CreateWaterRocketSketch(design);
        Texture oldTexture = centerImage.texture;
        centerImage.texture = nextMock;
        centerImage.enabled = true;
        centerImage.color = Color.white;
        HideSketchPlaceholder();

        if (mockTexture != null && mockTexture != nextMock)
            Destroy(mockTexture);
        mockTexture = nextMock;

        if (oldTexture == downloadedTexture)
        {
            Destroy(downloadedTexture);
            downloadedTexture = null;
        }
    }

    private void StartServerImageDownload(string imgUrl)
    {
        if (downloadCoroutine != null)
            StopCoroutine(downloadCoroutine);
        downloadCoroutine = StartCoroutine(DownloadAndShow(imgUrl));
    }

    private IEnumerator DownloadAndShow(string imgUrl)
    {
        string url = ResolveImageUrl(imgUrl);
        if (string.IsNullOrEmpty(url))
        {
            SetUiGenerating(false, "2D 이미지 주소를 확인해 주세요.");
            yield break;
        }

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[MvpGenerated2DSync] Failed to download 2D image: " +
                    request.error +
                    " (code=" +
                    request.responseCode +
                    ")");
                SetUiGenerating(false, "2D 이미지를 불러오지 못했습니다.");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            ApplyServerTexture(texture);
            SetUiGenerating(false, "2D 이미지 생성 완료");
        }

        downloadCoroutine = null;
    }

    private void ApplyServerTexture(Texture2D texture)
    {
        ResolveReferences();
        if (centerImage == null || texture == null)
        {
            if (texture != null)
                Destroy(texture);
            return;
        }

        centerImage.texture = texture;
        centerImage.enabled = true;
        centerImage.color = Color.white;
        HideSketchPlaceholder();

        if (downloadedTexture != null && downloadedTexture != texture)
            Destroy(downloadedTexture);
        downloadedTexture = texture;

        if (mockTexture != null)
        {
            Destroy(mockTexture);
            mockTexture = null;
        }
    }

    private string ResolveImageUrl(string imgUrl)
    {
        if (string.IsNullOrWhiteSpace(imgUrl))
            return string.Empty;

        if (imgUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return imgUrl;

        if (graphSyncClient == null || string.IsNullOrWhiteSpace(graphSyncClient.Host))
            return imgUrl;

        return ServerAddress.Http(graphSyncClient.Host) +
               (imgUrl.StartsWith("/") ? "" : "/") +
               imgUrl;
    }

    private void RestartRequestTimeout(int version)
    {
        StopRequestTimeout();
        requestTimeoutCoroutine = StartCoroutine(WaitForRequestTimeout(version));
    }

    private void StopRequestTimeout()
    {
        if (requestTimeoutCoroutine == null)
            return;

        StopCoroutine(requestTimeoutCoroutine);
        requestTimeoutCoroutine = null;
    }

    private IEnumerator WaitForRequestTimeout(int version)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(10f, requestTimeoutSeconds));
        requestTimeoutCoroutine = null;

        if (localRequestVersion == version)
        {
            localRequestVersion = 0;
            graphNetwork?.RequestGenerated2DFinish(version);
        }
    }

    private void SetUiGenerating(bool generating, string message)
    {
        if (generateButton != null)
            generateButton.interactable = !generating;
        if (statusText != null && !string.IsNullOrEmpty(message))
            statusText.text = message;
    }

    private void HideSketchPlaceholder()
    {
        if (centerImage == null)
            return;

        Canvas canvas = centerImage.GetComponentInParent<Canvas>(true);
        Transform root = canvas != null ? canvas.transform : centerImage.transform.root;
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == "SketchPlaceholder")
            {
                children[i].gameObject.SetActive(false);
                return;
            }
        }
    }

    private bool IsLocalRequester(PlayerRef requester)
    {
        return graphNetwork != null &&
               requester != PlayerRef.None &&
               requester == graphNetwork.LocalPlayerRef;
    }

    private bool IsControllerGenerating()
    {
        return generate2DController != null &&
               GetPrivateField<bool>(generate2DController, "_isGenerating");
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

        private static void FillRequirements(int count, System.Collections.Generic.List<string> target)
        {
            if (target == null)
                return;

            for (int i = 0; i < Mathf.Clamp(count, 0, 3); i++)
                target.Add("requirement");
        }
    }
}
