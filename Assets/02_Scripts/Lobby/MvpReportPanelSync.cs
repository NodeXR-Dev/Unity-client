using System;
using System.Reflection;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class MvpReportPanelSync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GraphNetworkManager graphNetworkManager;
    [SerializeField] private MvpClassroomFlow classroomFlow;
    [SerializeField] private ReportPanelBinder reportPanel;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Options")]
    [SerializeField] private bool broadcastLocalReportOpen = true;
    [SerializeField] private float pollIntervalSeconds = 0.2f;

    private MethodInfo showReportPanelMethod;
    private bool subscribed;
    private bool lastReportVisible;
    private float nextPollTime;

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        lastReportVisible = IsReportVisible();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (!broadcastLocalReportOpen ||
            Time.unscaledTime < nextPollTime)
            return;

        nextPollTime = Time.unscaledTime + Mathf.Max(0.05f, pollIntervalSeconds);
        ResolveReferences();

        bool visible = IsReportVisible();
        if (visible && !lastReportVisible)
            graphNetworkManager?.RequestReportPanelShow();

        lastReportVisible = visible;
    }

    public void ShowReportForEveryone()
    {
        ShowReportLocally();
        graphNetworkManager?.RequestReportPanelShow();
    }

    private void Subscribe()
    {
        if (subscribed || graphNetworkManager == null)
            return;

        graphNetworkManager.ReportPanelShowReceived += HandleReportPanelShowReceived;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || graphNetworkManager == null)
            return;

        graphNetworkManager.ReportPanelShowReceived -= HandleReportPanelShowReceived;
        subscribed = false;
    }

    private void HandleReportPanelShowReceived(PlayerRef requester)
    {
        if (graphNetworkManager != null &&
            requester != PlayerRef.None &&
            requester == graphNetworkManager.LocalPlayerRef)
            return;

        ShowReportLocally();
    }

    private void ShowReportLocally()
    {
        ResolveReferences();
        if (classroomFlow == null)
        {
            Debug.LogWarning("[MvpReportPanelSync] MvpClassroomFlow was not found.");
            return;
        }

        if (showReportPanelMethod == null)
        {
            showReportPanelMethod = typeof(MvpClassroomFlow).GetMethod(
                "ShowReportPanel",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (showReportPanelMethod == null)
        {
            Debug.LogWarning("[MvpReportPanelSync] MvpClassroomFlow.ShowReportPanel was not found.");
            return;
        }

        showReportPanelMethod.Invoke(classroomFlow, null);
        ResolveReportPanel();
        lastReportVisible = IsReportVisible();
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
            return;

        if (graphNetworkManager == null)
        {
            graphNetworkManager = FindFirstObjectByType<GraphNetworkManager>();
            if (graphNetworkManager != null && !subscribed)
                Subscribe();
        }

        if (classroomFlow == null)
            classroomFlow = FindFirstObjectByType<MvpClassroomFlow>();

        ResolveReportPanel();
    }

    private void ResolveReportPanel()
    {
        if (reportPanel == null)
            reportPanel = FindFirstObjectByType<ReportPanelBinder>(FindObjectsInactive.Include);
    }

    private bool IsReportVisible()
    {
        return reportPanel != null && reportPanel.gameObject.activeInHierarchy;
    }
}
