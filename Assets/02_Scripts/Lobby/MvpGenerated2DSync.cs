using System;
using System.Collections;
using System.Collections.Generic;
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
    private const string DefaultSeedRoomId =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Header("References")]
    [SerializeField] private GraphNetworkManager graphNetwork;
    [SerializeField] private Generate2DController generate2DController;
    [SerializeField] private GraphSyncClient graphSyncClient;
    [SerializeField] private MvpClassroomFlow classroomFlow;
    [SerializeField] private MvpWaterRocketGraphController waterRocketGraph;
    [SerializeField] private RawImage centerImage;
    [SerializeField] private Button generateButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool replaceGenerateButtonClick = true;
    [SerializeField] private bool broadcastDirectServerResults = true;
    [SerializeField] private bool restoreLatestSketchOnStart = true;
    [SerializeField] private float requestTimeoutSeconds = 125f;
    [SerializeField] private float restoreTimeoutSeconds = 90f;
    [SerializeField] private float restorePollSeconds = 3f;

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
    private Coroutine restoreCoroutine;
    private Texture2D downloadedTexture;
    // 보드가 꺼져 있어 아직 못 붙인 서버 이미지. RawImage 가 잡히면 Update 가 붙인다.
    private Texture2D pendingTexture;
    private string pendingAssetId;
    private string lastAppliedImageUrl;
    private string lastBroadcastImageUrl;

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
        StartLatestSketchRestore();
    }

    private void Update()
    {
        if (autoFindReferences && Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 1f;
            ResolveReferences();
            RefreshBindings();
        }

        // 2D 이미지를 만드는 동안에는 생성 버튼만 잠근다.
        //   중앙 이미지는 건드리지 않는다 — 새 그림이 도착할 때까지 직전 결과가 그대로 보인다.
        bool generating = IsBusy || IsControllerGenerating();
        if (generateButton != null && generateButton.interactable == generating)
            generateButton.interactable = !generating;

        // 보드가 꺼져 있는 동안 도착했던 이미지를 붙일 곳이 생기면 그때 붙인다.
        if (pendingTexture != null)
        {
            ResolveReferences();
            if (centerImage != null)
            {
                Texture2D texture = pendingTexture;
                string assetId = pendingAssetId;
                pendingTexture = null;
                pendingAssetId = null;
                ApplyServerTexture(texture, assetId);
            }
        }
    }

    private void OnDisable()
    {
        UnbindGraphNetwork();
        UnbindGraphSyncClient();
        UnbindButton();
        StopRequestTimeout();
        StopLatestSketchRestore();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        localRequestVersion = 0;
    }

    private void OnDestroy()
    {
        if (downloadedTexture != null)
            Destroy(downloadedTexture);
        if (pendingTexture != null)
            Destroy(pendingTexture);
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

        if (graphSyncClient != null &&
            graphSyncClient.IsConnected &&
            generate2DController != null)
        {
            SetUiGenerating(true, "2D 이미지를 생성 중입니다.");
            generate2DController.RequestGenerateGraphAll();
        }
        else
        {
            SetUiGenerating(
                false, "서버에 연결되어 있지 않아 2D 이미지를 만들 수 없습니다.");
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

    private void HandleServerImageFromGraphSync(GraphSyncClient.Image2DResult result)
    {
        string imgUrl = result.ImgUrl;
        if (string.IsNullOrWhiteSpace(imgUrl))
            return;

        if (localRequestVersion > 0 && graphNetwork != null)
        {
            StopRequestTimeout();
            int version = localRequestVersion;
            localRequestVersion = 0;
            lastBroadcastImageUrl = imgUrl;
            graphNetwork.RequestGenerated2DServerImage(
                version,
                result.AssetId,
                string.Empty,
                imgUrl);
            return;
        }

        if (!broadcastDirectServerResults ||
            graphNetwork == null ||
            !graphNetwork.IsRpcReady ||
            IsBusy ||
            string.Equals(lastBroadcastImageUrl, imgUrl, StringComparison.Ordinal))
        {
            // 왜 남에게 안 보내는지 남긴다. "내 화면에만 이미지가 뜬다"를
            // 실기에서 추적하려면 이 지점의 판단이 필요하다.
            Debug.LogWarning(
                "[MvpGenerated2DSync] 2D 결과를 방에 알리지 않았습니다 — " +
                "broadcast=" + broadcastDirectServerResults +
                " graphNetwork=" + (graphNetwork != null) +
                " rpcReady=" + (graphNetwork != null && graphNetwork.IsRpcReady) +
                " busy=" + IsBusy +
                " 같은주소=" + string.Equals(
                    lastBroadcastImageUrl, imgUrl, StringComparison.Ordinal));
            return;
        }

        lastBroadcastImageUrl = imgUrl;
        graphNetwork.RequestGenerated2DImageSnapshot(
            result.AssetId,
            string.Empty,
            imgUrl);
    }

    private void HandleGenerated2DServerImage(
        int version,
        string assetId,
        string mimeType,
        string imgUrl)
    {
        // version 0 은 "지금 방의 그림은 이것"이라는 스냅샷이라 언제나 받는다.
        // 예전에는 생성 중인 사람(activeVersion > 0)이 스냅샷을 전부 버려서,
        // 늦게 도착한 진짜 이미지를 못 받고 목업이 남았다.
        if (version > 0 && activeVersion > 0 && version != activeVersion)
            return;

        StopRequestTimeout();
        networkGenerating = false;
        waitingForStartAccept = false;
        activeVersion = 0;
        localRequestVersion = 0;
        SetUiGenerating(false, "2D 이미지 생성 완료");

        if (!string.IsNullOrEmpty(imgUrl))
            StartServerImageDownload(imgUrl, assetId);
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
        if (classroomFlow == null)
            classroomFlow = FindFirstObjectByType<MvpClassroomFlow>();
        if (waterRocketGraph == null)
            waterRocketGraph = FindFirstObjectByType<MvpWaterRocketGraphController>();

        if (centerImage == null && generate2DController != null)
            centerImage = GetPrivateField<RawImage>(generate2DController, "_centerImage");
        if (centerImage == null)
        {
            // 비활성까지 훑는다. 보드(MainSketchPanel)는 회의 목표 패널을 보는 동안 꺼져 있고,
            // 기본 FindFirstObjectByType 은 꺼진 오브젝트를 건너뛴다 → 그 사이 도착한
            // 서버 이미지를 붙일 곳을 못 찾는다.
            MainSketchView sketchView =
                FindFirstObjectByType<MainSketchView>(FindObjectsInactive.Include);
            if (sketchView != null)
                centerImage = sketchView.GetComponentInChildren<RawImage>(true);
        }

        Button preferredButton =
            FindSceneButton("Button_2D", "Generate2D", "Generate2DButton");
        if (preferredButton != null &&
            (generateButton == null ||
             !generateButton.gameObject.activeInHierarchy ||
             preferredButton.gameObject.activeInHierarchy))
        {
            generateButton = preferredButton;
        }
        if (generateButton == null && generate2DController != null)
            generateButton = GetPrivateField<Button>(generate2DController, "_generateButton");

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
            subscribedGraphSyncClient.OnImage2DResult += HandleServerImageFromGraphSync;
    }

    private void UnbindGraphSyncClient()
    {
        if (subscribedGraphSyncClient == null)
            return;

        subscribedGraphSyncClient.OnImage2DResult -= HandleServerImageFromGraphSync;
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

    private void StartServerImageDownload(string imgUrl, string assetId = "")
    {
        if (downloadCoroutine != null)
            StopCoroutine(downloadCoroutine);
        downloadCoroutine = StartCoroutine(DownloadAndShow(imgUrl, assetId));
    }

    private IEnumerator DownloadAndShow(string imgUrl, string assetId)
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
            ApplyServerTexture(texture, assetId);
            lastAppliedImageUrl = imgUrl;
            SetUiGenerating(false, "2D 이미지 생성 완료");
        }

        downloadCoroutine = null;
    }

    private void ApplyServerTexture(Texture2D texture, string assetId)
    {
        if (texture == null)
            return;

        ResolveReferences();
        Debug.Log(
            "[MVP 2D] 서버 이미지 적용 시도 — centerImage=" +
            (centerImage != null ? centerImage.name : "(없음)") +
            " assetId=" + assetId +
            " " + texture.width + "x" + texture.height);

        if (centerImage == null)
        {
            // 붙일 곳이 아직 없다(보드가 꺼져 있는 동안 도착한 경우).
            // 예전에는 여기서 텍스처를 파기해 버려 그 이미지를 영영 못 봤다.
            // 들고 있다가 RawImage 가 잡히면 Update 에서 붙인다.
            if (pendingTexture != null && pendingTexture != texture)
                Destroy(pendingTexture);
            pendingTexture = texture;
            pendingAssetId = assetId;
            return;
        }

        if (pendingTexture != null && pendingTexture != texture)
            Destroy(pendingTexture);
        pendingTexture = null;
        pendingAssetId = null;

        centerImage.texture = texture;
        centerImage.enabled = true;
        centerImage.color = Color.white;
        HideSketchPlaceholder();
        ApplyControllerAssetId(assetId);

        if (downloadedTexture != null && downloadedTexture != texture)
            Destroy(downloadedTexture);
        downloadedTexture = texture;
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

    private void StartLatestSketchRestore()
    {
        if (!restoreLatestSketchOnStart || restoreCoroutine != null)
            return;

        restoreCoroutine = StartCoroutine(RestoreLatestSketchWhenReady());
    }

    private void StopLatestSketchRestore()
    {
        if (restoreCoroutine == null)
            return;

        StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
    }

    private IEnumerator RestoreLatestSketchWhenReady()
    {
        float deadline = Time.unscaledTime + Mathf.Max(1f, restoreTimeoutSeconds);

        while (Time.unscaledTime < deadline)
        {
            ResolveReferences();
            RefreshBindings();

            if (ShouldSkipLatestRestore())
                yield break;

            if (!IsGraphSyncReadyForHistory())
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, restorePollSeconds));
                continue;
            }

            LatestSketchDto latest = null;
            yield return FetchLatestSketch(result => latest = result);

            if (ShouldSkipLatestRestore())
                yield break;

            if (latest != null && !string.IsNullOrWhiteSpace(latest.imageUrl))
            {
                if (!string.Equals(lastAppliedImageUrl, latest.imageUrl, StringComparison.Ordinal))
                    StartServerImageDownload(latest.imageUrl, latest.assetId);

                if (broadcastDirectServerResults &&
                    graphNetwork != null &&
                    graphNetwork.IsRpcReady &&
                    !string.Equals(lastBroadcastImageUrl, latest.imageUrl, StringComparison.Ordinal))
                {
                    lastBroadcastImageUrl = latest.imageUrl;
                    graphNetwork.RequestGenerated2DImageSnapshot(
                        latest.assetId,
                        latest.mimeType,
                        latest.imageUrl);
                }

                restoreCoroutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, restorePollSeconds));
        }

        restoreCoroutine = null;
    }

    private bool ShouldSkipLatestRestore()
    {
        ResolveReferences();

        if (IsBusy)
            return true;

        // 컨트롤러가 assetId 를 들고 있으면 '이미 표시됐다'고 보고 복원을 접는다.
        //   그런데 assetId 만 기록되고 실제 표시는 실패한 경우가 있었다(보드가 꺼져 있어
        //   RawImage 를 못 찾던 시절). 그때 복원까지 멈춰 이미지가 영영 안 나왔다.
        //   중앙 이미지에 텍스처가 실제로 들어가 있을 때만 접는다.
        if (generate2DController != null &&
            !string.IsNullOrWhiteSpace(generate2DController.CurrentAssetId))
        {
            if (centerImage != null && centerImage.texture != null)
                return true;

            Debug.Log(
                "[MVP 2D] assetId 는 있는데 중앙 이미지가 비어 있어 복원을 계속합니다 — " +
                "assetId=" + generate2DController.CurrentAssetId +
                " centerImage=" + (centerImage != null ? centerImage.name : "(없음)"));
        }

        return false;
    }

    private bool IsGraphSyncReadyForHistory()
    {
        if (graphSyncClient == null ||
            string.IsNullOrWhiteSpace(graphSyncClient.Host) ||
            string.IsNullOrWhiteSpace(graphSyncClient.RoomId))
            return false;

        return graphSyncClient.IsConnected ||
               !string.Equals(
                   graphSyncClient.RoomId,
                   DefaultSeedRoomId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerator FetchLatestSketch(Action<LatestSketchDto> onDone)
    {
        onDone?.Invoke(null);

        string url =
            $"{ServerAddress.Http(graphSyncClient.Host)}/api/history/" +
            UnityWebRequest.EscapeURL(graphSyncClient.RoomId);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[MvpGenerated2DSync] Failed to fetch latest 2D sketch: " +
                    request.error +
                    " (code=" +
                    request.responseCode +
                    ")");
                yield break;
            }

            HistoryResponseDto response = null;
            try
            {
                response = JsonUtility.FromJson<HistoryResponseDto>(
                    request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[MvpGenerated2DSync] Failed to parse latest 2D sketch: " +
                    exception.Message);
            }

            List<GraphSnapshotDto> history = response?.result?.history;
            if (history == null)
                yield break;

            CoreImageDto latestImage = null;
            int latestVersion = int.MinValue;
            foreach (GraphSnapshotDto snapshot in history)
            {
                if (snapshot?.core_2d_image == null ||
                    string.IsNullOrWhiteSpace(snapshot.core_2d_image.image_url))
                    continue;
                if (snapshot.graph_version < latestVersion)
                    continue;

                latestVersion = snapshot.graph_version;
                latestImage = snapshot.core_2d_image;
            }

            if (latestImage == null)
                yield break;

            onDone?.Invoke(new LatestSketchDto
            {
                assetId = latestImage.asset_id,
                mimeType = latestImage.mime_type,
                imageUrl = latestImage.image_url
            });
        }
    }

    private void SetUiGenerating(bool generating, string message)
    {
        if (generateButton != null)
            generateButton.interactable = !generating;
        if (statusText != null && !string.IsNullOrEmpty(message))
            statusText.text = message;
        if (classroomFlow != null && !string.IsNullOrEmpty(message))
        {
            Color color = generating
                ? MvpStudentUiFactory.Cyan
                : MvpStudentUiFactory.Mint;
            classroomFlow.SetWorkspaceMessage(message, color);
        }
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
        return generate2DController != null && generate2DController.IsGenerating;
    }

    private void ApplyControllerAssetId(string assetId)
    {
        if (generate2DController == null || string.IsNullOrWhiteSpace(assetId))
            return;

        SetPrivateField(generate2DController, "_currentAssetId", assetId);
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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceFlags);
        if (field != null)
            field.SetValue(target, value);
    }

    private static Button FindSceneButton(params string[] names)
    {
        Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
        Button inactiveMatch = null;

        foreach (Button button in buttons)
        {
            if (button == null || !button.gameObject.scene.IsValid())
                continue;

            foreach (string name in names)
            {
                if (button.name != name)
                    continue;

                if (button.gameObject.activeInHierarchy)
                    return button;
                inactiveMatch ??= button;
            }
        }

        return inactiveMatch;
    }

    private class LatestSketchDto
    {
        public string assetId;
        public string mimeType;
        public string imageUrl;
    }

    [Serializable]
    private class HistoryResponseDto
    {
        public HistoryResultDto result;
    }

    [Serializable]
    private class HistoryResultDto
    {
        public List<GraphSnapshotDto> history;
    }

    [Serializable]
    private class GraphSnapshotDto
    {
        public int graph_version;
        public CoreImageDto core_2d_image;
    }

    [Serializable]
    private class CoreImageDto
    {
        public string asset_id;
        public string mime_type;
        public string image_url;
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
