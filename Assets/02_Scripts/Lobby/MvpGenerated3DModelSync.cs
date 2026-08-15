using System;
using System.Collections;
using System.Reflection;
using Fusion;
using TMPro;
using UnityEngine;

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

    private GraphNetworkManager subscribedGraphNetwork;
    private GraphSyncClient subscribedGraphSyncClient;

    private float nextResolveTime;
    private string lastBroadcastModelUrl;

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
