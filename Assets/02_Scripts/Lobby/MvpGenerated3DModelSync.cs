using System;
using System.Collections;
using System.Reflection;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 서버(Meshy)가 만든 GLB 를 방 전체가 같이 보게 만든다.
///
/// 이름이 비슷한 MvpGenerated3DSync 와는 하는 일이 다르다.
///   - MvpGenerated3DSync      : 디자인 값으로 로컬 목업 로켓(MvpRocket3DStage)을 만들어 띄운다.
///   - MvpGenerated3DModelSync : 서버가 실제로 만든 GLB 주소를 돌려 같은 모델을 띄운다.
///
/// 기존에는 3D 에 대해 "누가 시작했다 / 끝났다" 만 오갔고 완성된 GLB 주소를 돌리는
/// 통로가 없어서, Meshy 결과물은 요청한 사람에게만 보였다.
/// (2D 에는 Generated2DServerImageReceived 가 있는데 3D 에는 대응되는 것이 없었다)
///
/// 서버에 다시 요청하지 않고 이미 받은 주소만 돌리므로 중복 과금이 없다.
/// </summary>
[DefaultExecutionOrder(230)]
public class MvpGenerated3DModelSync : MonoBehaviour
{
    private const BindingFlags PrivateInstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private GraphNetworkManager graphNetwork;
    [SerializeField] private Generate3DController generate3DController;
    [SerializeField] private GraphSyncClient graphSyncClient;
    [SerializeField] private MvpClassroomFlow classroomFlow;
    [SerializeField] private TMP_Text statusText;

    [Header("Options")]
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool broadcastDirectServerResults = true;
    [SerializeField] private bool restoreLatestModelOnStart = true;
    [SerializeField] private float restoreTimeoutSeconds = 60f;
    [SerializeField] private float restorePollSeconds = 3f;

    private GraphNetworkManager subscribedGraphNetwork;
    private GraphSyncClient subscribedGraphSyncClient;

    private float nextResolveTime;
    private string lastBroadcastModelUrl;
    private Coroutine restoreCoroutine;

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
        StartLatestModelRestore();
    }

    private void Update()
    {
        if (!autoFindReferences || Time.unscaledTime < nextResolveTime)
            return;

        nextResolveTime = Time.unscaledTime + 1f;
        ResolveReferences();
        RefreshBindings();
    }

    private void OnDisable()
    {
        UnbindGraphNetwork();
        UnbindGraphSyncClient();
        StopLatestModelRestore();
    }

    // ── 놓친 3D 결과 따라잡기 ────────────────────────────────────────
    //
    // 3D 는 Meshy 때문에 1~2분이 걸린다. 그 사이 WS 가 끊기면 서버가 완료를
    // 보낼 곳이 없어 결과가 그대로 사라진다.
    // (실측 2026-08-15: 완료 6초 전 ws_closed → ws_send_to_user_skip → 유실)
    //
    // 생성물 자체는 서버에 남으므로 방에 들어올 때 한 번 확인한다.
    // 앱을 다시 켠 경우와 늦게 합류한 참가자도 같이 해결된다.

    private void StartLatestModelRestore()
    {
        if (!restoreLatestModelOnStart || restoreCoroutine != null)
            return;

        restoreCoroutine = StartCoroutine(RestoreLatestModelWhenReady());
    }

    private void StopLatestModelRestore()
    {
        if (restoreCoroutine == null)
            return;

        StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
    }

    private IEnumerator RestoreLatestModelWhenReady()
    {
        float deadline = Time.unscaledTime + Mathf.Max(5f, restoreTimeoutSeconds);

        while (Time.unscaledTime < deadline)
        {
            ResolveReferences();

            // 이미 모델이 떠 있으면 건드리지 않는다(내가 방금 만든 경우).
            if (generate3DController != null && generate3DController.HasModel)
                break;

            if (graphSyncClient == null ||
                string.IsNullOrWhiteSpace(graphSyncClient.Host) ||
                string.IsNullOrWhiteSpace(graphSyncClient.RoomId))
            {
                yield return new WaitForSecondsRealtime(
                    Mathf.Max(0.5f, restorePollSeconds));
                continue;
            }

            string url =
                $"{ServerAddress.Http(graphSyncClient.Host)}/api/3d/latest/" +
                UnityWebRequest.EscapeURL(graphSyncClient.RoomId);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        "[MvpGenerated3DModelSync] 최신 3D 조회 실패: " +
                        request.error + " (code=" + request.responseCode + ")");
                    yield return new WaitForSecondsRealtime(
                        Mathf.Max(0.5f, restorePollSeconds));
                    continue;
                }

                string modelUrl = null;
                string assetId = null;
                try
                {
                    LatestModelResponseDto response =
                        JsonUtility.FromJson<LatestModelResponseDto>(
                            request.downloadHandler.text);
                    if (response?.result != null)
                    {
                        modelUrl = response.result.model_url;
                        assetId = response.result.asset_id;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[MvpGenerated3DModelSync] 최신 3D 응답 해석 실패: " +
                        exception.Message);
                }

                if (!string.IsNullOrWhiteSpace(modelUrl))
                {
                    Debug.Log(
                        "[MvpGenerated3DModelSync] 놓친 3D 결과를 서버에서 받아 띄웁니다.");
                    HandleGenerated3DModel(0, assetId, string.Empty, modelUrl);

                    // 같은 방 참가자에게도 알려 늦게 들어온 사람까지 맞춘다.
                    if (broadcastDirectServerResults &&
                        graphNetwork != null &&
                        graphNetwork.IsRpcReady)
                    {
                        lastBroadcastModelUrl = modelUrl;
                        graphNetwork.RequestGenerated3DModelSnapshot(
                            assetId, string.Empty, modelUrl);
                    }
                    break;
                }
            }

            // 아직 없으면(생성 중일 수 있다) 잠시 뒤 다시 본다.
            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.5f, restorePollSeconds));
        }

        restoreCoroutine = null;
    }

    [Serializable]
    private class LatestModelResponseDto
    {
        public LatestModelResultDto result;
    }

    [Serializable]
    private class LatestModelResultDto
    {
        public string asset_id;
        public string model_url;
        public string mime_type;
    }

    /// <summary>
    /// 서버에서 내 3D 결과가 도착했다. 방 전체에 주소를 돌린다.
    /// </summary>
    private void HandleServerModelFromGraphSync(GraphSyncClient.Model3DResult result)
    {
        string modelUrl = result.ModelUrl;
        if (!broadcastDirectServerResults ||
            string.IsNullOrWhiteSpace(modelUrl) ||
            graphNetwork == null ||
            !graphNetwork.IsRpcReady ||
            string.Equals(lastBroadcastModelUrl, modelUrl, StringComparison.Ordinal))
            return;

        lastBroadcastModelUrl = modelUrl;
        graphNetwork.RequestGenerated3DModelSnapshot(
            result.AssetId,
            result.MimeType,
            modelUrl);
    }

    /// <summary>
    /// 방에서 GLB 주소가 왔다. 만든 사람이 아니어도 같은 모델을 띄운다.
    /// 보낸 본인은 Generate3DController 가 이미 같은 주소를 띄웠으므로 그쪽에서 걸러진다.
    /// </summary>
    private void HandleGenerated3DModel(
        int version,
        string assetId,
        string mimeType,
        string modelUrl)
    {
        if (string.IsNullOrWhiteSpace(modelUrl))
            return;

        // 되돌아온 내 결과를 다시 방송하지 않도록 기억해 둔다.
        lastBroadcastModelUrl = modelUrl;

        ResolveReferences();
        if (generate3DController == null)
        {
            Debug.LogWarning(
                "[MvpGenerated3DModelSync] Generate3DController 를 찾지 못해 공유된 3D 모델을 띄우지 못했습니다.");
            return;
        }

        // 받침대는 요청한 사람 쪽에서만 세워졌다(MvpClassroomFlow 가 로컬로 만든다).
        // 받는 쪽도 같은 자리에 세워 두지 않으면 모델이 엉뚱한 곳에 붙는다.
        if (classroomFlow != null)
        {
            Transform stage = classroomFlow.EnsureServerModelStage();
            if (stage != null)
                generate3DController.SetModelParent(stage);
        }

        SetStatus("3D 모델을 불러오는 중...");
        generate3DController.ShowSharedModel(modelUrl, assetId);
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
            return;

        if (generate3DController == null)
            generate3DController = FindFirstObjectByType<Generate3DController>();
        if (graphSyncClient == null && generate3DController != null)
            graphSyncClient = generate3DController.GetComponent<GraphSyncClient>();
        if (graphSyncClient == null)
            graphSyncClient = FindFirstObjectByType<GraphSyncClient>();
        if (graphNetwork == null)
            graphNetwork = FindFirstObjectByType<GraphNetworkManager>();
        if (classroomFlow == null)
            classroomFlow = FindFirstObjectByType<MvpClassroomFlow>();
        if (statusText == null && classroomFlow != null)
            statusText = GetPrivateField<TMP_Text>(classroomFlow, "_workspaceStatus");
    }

    private void RefreshBindings()
    {
        BindGraphNetwork();
        BindGraphSyncClient();
    }

    private void BindGraphNetwork()
    {
        if (subscribedGraphNetwork == graphNetwork)
            return;

        UnbindGraphNetwork();

        subscribedGraphNetwork = graphNetwork;
        if (subscribedGraphNetwork != null)
            subscribedGraphNetwork.Generated3DModelReceived += HandleGenerated3DModel;
    }

    private void UnbindGraphNetwork()
    {
        if (subscribedGraphNetwork == null)
            return;

        subscribedGraphNetwork.Generated3DModelReceived -= HandleGenerated3DModel;
        subscribedGraphNetwork = null;
    }

    private void BindGraphSyncClient()
    {
        if (subscribedGraphSyncClient == graphSyncClient)
            return;

        UnbindGraphSyncClient();

        subscribedGraphSyncClient = graphSyncClient;
        if (subscribedGraphSyncClient != null)
            subscribedGraphSyncClient.OnModel3DGenerated += HandleServerModelFromGraphSync;
    }

    private void UnbindGraphSyncClient()
    {
        if (subscribedGraphSyncClient == null)
            return;

        subscribedGraphSyncClient.OnModel3DGenerated -= HandleServerModelFromGraphSync;
        subscribedGraphSyncClient = null;
    }

    private void SetStatus(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (statusText != null)
            statusText.text = message;
        if (classroomFlow != null)
            classroomFlow.SetWorkspaceMessage(message, MvpStudentUiFactory.Cyan);
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
}
