using System.Collections;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class MvpSpawnLayoutAdapter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MeetingRoomSpawnLayout spawnLayout;
    [SerializeField] private NetworkRunner runner;

    [Header("Targets")]
    [SerializeField] private bool moveLocalXrRig = true;
    [SerializeField] private string cameraRigObjectName = "[BuildingBlock] Camera Rig";
    [SerializeField] private bool moveLocalPlayerObject = true;

    [Header("Timing")]
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField, Min(0.1f)] private float waitTimeoutSeconds = 30f;
    [SerializeField, Min(0.02f)] private float retryIntervalSeconds = 0.1f;
    [SerializeField, Min(1)] private int stabilizeFrames = 45;
    [SerializeField] private bool logAppliedPose = true;

    private Coroutine applyRoutine;

    private void Awake()
    {
        ResolveSpawnLayout();
    }

    private void OnEnable()
    {
        if (applyOnEnable)
            RestartApplyRoutine();
    }

    private void OnDisable()
    {
        if (applyRoutine != null)
        {
            StopCoroutine(applyRoutine);
            applyRoutine = null;
        }
    }

    [ContextMenu("Apply MVP Spawn Layout")]
    public void ApplySpawnLayoutNow()
    {
        RestartApplyRoutine();
    }

    private void RestartApplyRoutine()
    {
        if (!Application.isPlaying)
            return;

        if (applyRoutine != null)
            StopCoroutine(applyRoutine);

        applyRoutine = StartCoroutine(ApplyWhenReady());
    }

    private IEnumerator ApplyWhenReady()
    {
        float startedAt = Time.realtimeSinceStartup;
        NetworkRunner targetRunner = null;
        NetworkObject localPlayerObject = null;

        while (Time.realtimeSinceStartup - startedAt <= waitTimeoutSeconds)
        {
            targetRunner = ResolveRunner();
            if (IsRunnerReady(targetRunner))
            {
                localPlayerObject =
                    targetRunner.GetPlayerObject(targetRunner.LocalPlayer);

                if (localPlayerObject != null || !moveLocalPlayerObject)
                    break;
            }

            yield return new WaitForSecondsRealtime(retryIntervalSeconds);
        }

        if (!IsRunnerReady(targetRunner))
        {
            Debug.LogWarning("[MvpSpawnLayoutAdapter] NetworkRunner was not ready before timeout.");
            applyRoutine = null;
            yield break;
        }

        if (moveLocalPlayerObject && localPlayerObject == null)
        {
            Debug.LogWarning("[MvpSpawnLayoutAdapter] Local player object was not found. Moving XR rig only.");
        }

        MeetingRoomSpawnLayout layout = ResolveSpawnLayout();
        if (layout == null)
        {
            Debug.LogWarning("[MvpSpawnLayoutAdapter] MeetingRoomSpawnLayout is missing.");
            applyRoutine = null;
            yield break;
        }

        if (!layout.TryGetSpawnPose(
                targetRunner,
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
        {
            Debug.LogWarning("[MvpSpawnLayoutAdapter] Failed to resolve spawn pose.");
            applyRoutine = null;
            yield break;
        }

        WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
        int frames = Mathf.Max(1, stabilizeFrames);

        for (int frame = 0; frame < frames; frame++)
        {
            if (moveLocalPlayerObject && localPlayerObject == null)
                localPlayerObject =
                    targetRunner.GetPlayerObject(targetRunner.LocalPlayer);

            ApplyPose(spawnPosition, spawnRotation, localPlayerObject);
            yield return waitForEndOfFrame;
        }

        if (logAppliedPose)
        {
            Debug.Log(
                "[MvpSpawnLayoutAdapter] Applied MVP spawn pose. " +
                $"position={spawnPosition}, rotation={spawnRotation.eulerAngles}");
        }

        applyRoutine = null;
    }

    private void ApplyPose(
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        NetworkObject localPlayerObject)
    {
        if (moveLocalXrRig)
            MoveLocalXrRig(spawnPosition, spawnRotation);

        if (moveLocalPlayerObject && localPlayerObject != null)
            localPlayerObject.transform.SetPositionAndRotation(
                spawnPosition,
                spawnRotation);
    }

    private MeetingRoomSpawnLayout ResolveSpawnLayout()
    {
        if (spawnLayout != null)
            return spawnLayout;

        spawnLayout = GetComponent<MeetingRoomSpawnLayout>();
        if (spawnLayout != null)
            return spawnLayout;

        spawnLayout = GetComponentInChildren<MeetingRoomSpawnLayout>(true);
        if (spawnLayout != null)
            return spawnLayout;

#if UNITY_2023_1_OR_NEWER
        spawnLayout = FindFirstObjectByType<MeetingRoomSpawnLayout>();
#else
        spawnLayout = FindObjectOfType<MeetingRoomSpawnLayout>();
#endif
        return spawnLayout;
    }

    private NetworkRunner ResolveRunner()
    {
        if (runner != null && runner.gameObject.scene.IsValid())
            return runner;

        NetworkRunner fallback = null;
        NetworkRunner[] candidates =
            Resources.FindObjectsOfTypeAll<NetworkRunner>();

        foreach (NetworkRunner candidate in candidates)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid() ||
                !candidate.gameObject.activeInHierarchy)
                continue;

            if (candidate.gameObject.scene == gameObject.scene)
            {
                runner = candidate;
                return runner;
            }

            if (fallback == null)
                fallback = candidate;
        }

        runner = fallback;
        return runner;
    }

    private static bool IsRunnerReady(NetworkRunner targetRunner)
    {
        return targetRunner != null &&
               targetRunner.IsRunning &&
               targetRunner.LocalPlayer != PlayerRef.None;
    }

    private void MoveLocalXrRig(
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        Transform rig = ResolveLocalXrRigTransform();
        if (rig == null)
            return;

        Vector3 eulerAngles = spawnRotation.eulerAngles;
        Quaternion yawOnlyRotation =
            Quaternion.Euler(0f, eulerAngles.y, 0f);
        rig.SetPositionAndRotation(spawnPosition, yawOnlyRotation);
    }

    private Transform ResolveLocalXrRigTransform()
    {
        if (!string.IsNullOrWhiteSpace(cameraRigObjectName))
        {
            GameObject rigObject = GameObject.Find(cameraRigObjectName);
            if (rigObject != null)
                return rigObject.transform;
        }

#if UNITY_2023_1_OR_NEWER
        Unity.XR.CoreUtils.XROrigin xrOrigin =
            FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
#else
        Unity.XR.CoreUtils.XROrigin xrOrigin =
            FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
#endif
        if (xrOrigin != null)
            return xrOrigin.transform;

        OVRCameraRig[] cameraRigs =
            Resources.FindObjectsOfTypeAll<OVRCameraRig>();
        foreach (OVRCameraRig cameraRig in cameraRigs)
        {
            if (cameraRig != null &&
                cameraRig.gameObject.scene.IsValid() &&
                cameraRig.gameObject.activeInHierarchy)
                return cameraRig.transform;
        }

        return null;
    }
}
