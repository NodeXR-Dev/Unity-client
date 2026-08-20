using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

[DisallowMultipleComponent]
public class MvpLocalRiggedHandVisual : MonoBehaviour
{
    [Header("Rigged Hand Prefabs")]
    [SerializeField] private GameObject leftHandPrefab;
    [SerializeField] private GameObject rightHandPrefab;

    [Header("Tracking")]
    [SerializeField, Min(0.05f)] private float handScanInterval = 0.5f;
    [SerializeField, Min(0.01f)] private float followSpeed = 34f;

    [Header("Existing Hand Visuals")]
    [SerializeField] private bool hideExistingHandRenderers = true;
    [SerializeField, Min(0.1f)] private float hideRendererRefreshInterval = 1f;
    [SerializeField] private bool restoreExistingHandRenderersOnDisable = true;

    private readonly List<IHand> hands = new List<IHand>();
    private readonly Dictionary<Renderer, bool> hiddenRendererOriginalStates =
        new Dictionary<Renderer, bool>();
    private IHand leftHand;
    private IHand rightHand;
    private MvpRiggedHandVisualDriver leftDriver;
    private MvpRiggedHandVisualDriver rightDriver;
    private Transform visualRoot;
    private float nextHandScanTime;
    private float nextHideRendererTime;
    private bool warnedLeftUnavailable;
    private bool warnedRightUnavailable;

    private void Awake()
    {
        BuildVisuals();
    }

    private void OnEnable()
    {
        ScanHands();
        WarnIfDriverUnavailable(leftDriver, "left", ref warnedLeftUnavailable);
        WarnIfDriverUnavailable(rightDriver, "right", ref warnedRightUnavailable);
        HideExistingHandRenderers();
    }

    private void OnDisable()
    {
        if (leftDriver != null)
            leftDriver.SetVisible(false);

        if (rightDriver != null)
            rightDriver.SetVisible(false);

        if (restoreExistingHandRenderersOnDisable)
            RestoreExistingHandRenderers();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextHandScanTime)
        {
            nextHandScanTime = Time.unscaledTime + handScanInterval;
            ScanHands();
        }

        if (hideExistingHandRenderers &&
            Time.unscaledTime >= nextHideRendererTime)
        {
            nextHideRendererTime =
                Time.unscaledTime + hideRendererRefreshInterval;
            HideExistingHandRenderers();
        }

        if (leftDriver != null)
            leftDriver.ApplyPose(leftHand, false);

        if (rightDriver != null)
            rightDriver.ApplyPose(rightHand, false);
    }

    private void BuildVisuals()
    {
        if (visualRoot == null)
        {
            visualRoot = new GameObject("MvpLocalRiggedHandVisualRoot").transform;
            visualRoot.SetParent(transform, false);
        }

        if (leftDriver == null)
        {
            GameObject left = new GameObject("LeftRiggedHandDriver");
            left.transform.SetParent(visualRoot, false);
            leftDriver = left.AddComponent<MvpRiggedHandVisualDriver>();
            leftDriver.Initialize(leftHandPrefab, true, left.transform, followSpeed);
        }

        if (rightDriver == null)
        {
            GameObject right = new GameObject("RightRiggedHandDriver");
            right.transform.SetParent(visualRoot, false);
            rightDriver = right.AddComponent<MvpRiggedHandVisualDriver>();
            rightDriver.Initialize(rightHandPrefab, false, right.transform, followSpeed);
        }
    }

    private void ScanHands()
    {
        hands.Clear();
        leftHand = null;
        rightHand = null;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (!(behaviour is IHand hand))
                continue;

            Handedness side;
            try
            {
                side = hand.Handedness;
            }
            catch
            {
                continue;
            }

            hands.Add(hand);
            if (side == Handedness.Left)
            {
                if (leftHand == null || PreferHand(hand, leftHand))
                    leftHand = hand;
            }
            else if (side == Handedness.Right)
            {
                if (rightHand == null || PreferHand(hand, rightHand))
                    rightHand = hand;
            }
        }
    }

    private static bool PreferHand(IHand candidate, IHand current)
    {
        if (candidate == null)
            return false;

        if (current == null)
            return true;

        bool candidateValid = candidate.IsConnected && candidate.IsTrackedDataValid;
        bool currentValid = current.IsConnected && current.IsTrackedDataValid;
        if (candidateValid != currentValid)
            return candidateValid;

        return candidate.GetType().Name == "Hand" &&
               current.GetType().Name != "Hand";
    }

    private void HideExistingHandRenderers()
    {
        if (!hideExistingHandRenderers)
            return;

        bool hideLeft = leftDriver != null && leftDriver.IsReady;
        bool hideRight = rightDriver != null && rightDriver.IsReady;
        if (!hideLeft && !hideRight)
        {
            RestoreExistingHandRenderers();
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                visualRoot != null && renderer.transform.IsChildOf(visualRoot))
            {
                continue;
            }

            if (LooksLikeHandVisual(renderer.transform, hideLeft, hideRight))
            {
                if (!hiddenRendererOriginalStates.ContainsKey(renderer))
                    hiddenRendererOriginalStates.Add(renderer, renderer.forceRenderingOff);

                renderer.forceRenderingOff = true;
            }
        }
    }

    private void RestoreExistingHandRenderers()
    {
        foreach (KeyValuePair<Renderer, bool> pair in hiddenRendererOriginalStates)
        {
            if (pair.Key != null)
                pair.Key.forceRenderingOff = pair.Value;
        }

        hiddenRendererOriginalStates.Clear();
    }

    private static bool LooksLikeHandVisual(
        Transform target,
        bool hideLeft,
        bool hideRight)
    {
        string path = GetPath(target);
        bool leftHandVisual =
            Contains(path, "LeftHand") ||
            Contains(path, "OVRLeftHand");
        bool rightHandVisual =
            Contains(path, "RightHand") ||
            Contains(path, "OVRRightHand");
        bool genericHandVisual =
            Contains(path, "HandVisual") ||
            Contains(path, "HandMesh") ||
            Contains(path, "HandSphereMap");

        return
            hideLeft && leftHandVisual ||
            hideRight && rightHandVisual ||
            hideLeft && hideRight && genericHandVisual;
    }

    private static void WarnIfDriverUnavailable(
        MvpRiggedHandVisualDriver driver,
        string side,
        ref bool warned)
    {
        if (warned || driver == null || driver.IsReady)
            return;

        warned = true;
        Debug.LogWarning(
            $"MVP local {side} rigged hand visual is not ready. " +
            driver.LastFailureReason);
    }

    private static bool Contains(string value, string token)
    {
        return value.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
