using Fusion;
using Oculus.Interaction.Input;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class MvpHandSkeletonSync : NetworkBehaviour
{
    public const int JointCount = 26;

    private const int TotalJointCount = JointCount * 2;

    private static readonly HandJointId[] JointIds =
    {
        HandJointId.HandWristRoot,
        HandJointId.HandPalm,
        HandJointId.HandThumb1,
        HandJointId.HandThumb2,
        HandJointId.HandThumb3,
        HandJointId.HandThumbTip,
        HandJointId.HandIndex0,
        HandJointId.HandIndex1,
        HandJointId.HandIndex2,
        HandJointId.HandIndex3,
        HandJointId.HandIndexTip,
        HandJointId.HandMiddle0,
        HandJointId.HandMiddle1,
        HandJointId.HandMiddle2,
        HandJointId.HandMiddle3,
        HandJointId.HandMiddleTip,
        HandJointId.HandRing0,
        HandJointId.HandRing1,
        HandJointId.HandRing2,
        HandJointId.HandRing3,
        HandJointId.HandRingTip,
        HandJointId.HandPinky0,
        HandJointId.HandPinky1,
        HandJointId.HandPinky2,
        HandJointId.HandPinky3,
        HandJointId.HandPinkyTip,
    };

    [Header("Network Publish")]
    [SerializeField, Min(1f)] private float sendRate = 20f;
    [SerializeField, Min(0f)] private float positionThreshold = 0.003f;
    [SerializeField, Min(0.05f)] private float handScanInterval = 0.5f;

    [Header("Desktop Test")]
    [SerializeField] private bool enableMockHandsForDesktopTest;
    [SerializeField] private string mockHandsCommandLineArg = "-presenterViewMockHands";

    [Networked] public NetworkBool LeftHandTracked { get; private set; }
    [Networked] public NetworkBool RightHandTracked { get; private set; }
    [Networked] public int PoseRevision { get; private set; }

    [Networked] private Vector3 Joint00 { get; set; }
    [Networked] private Vector3 Joint01 { get; set; }
    [Networked] private Vector3 Joint02 { get; set; }
    [Networked] private Vector3 Joint03 { get; set; }
    [Networked] private Vector3 Joint04 { get; set; }
    [Networked] private Vector3 Joint05 { get; set; }
    [Networked] private Vector3 Joint06 { get; set; }
    [Networked] private Vector3 Joint07 { get; set; }
    [Networked] private Vector3 Joint08 { get; set; }
    [Networked] private Vector3 Joint09 { get; set; }
    [Networked] private Vector3 Joint10 { get; set; }
    [Networked] private Vector3 Joint11 { get; set; }
    [Networked] private Vector3 Joint12 { get; set; }
    [Networked] private Vector3 Joint13 { get; set; }
    [Networked] private Vector3 Joint14 { get; set; }
    [Networked] private Vector3 Joint15 { get; set; }
    [Networked] private Vector3 Joint16 { get; set; }
    [Networked] private Vector3 Joint17 { get; set; }
    [Networked] private Vector3 Joint18 { get; set; }
    [Networked] private Vector3 Joint19 { get; set; }
    [Networked] private Vector3 Joint20 { get; set; }
    [Networked] private Vector3 Joint21 { get; set; }
    [Networked] private Vector3 Joint22 { get; set; }
    [Networked] private Vector3 Joint23 { get; set; }
    [Networked] private Vector3 Joint24 { get; set; }
    [Networked] private Vector3 Joint25 { get; set; }
    [Networked] private Vector3 Joint26 { get; set; }
    [Networked] private Vector3 Joint27 { get; set; }
    [Networked] private Vector3 Joint28 { get; set; }
    [Networked] private Vector3 Joint29 { get; set; }
    [Networked] private Vector3 Joint30 { get; set; }
    [Networked] private Vector3 Joint31 { get; set; }
    [Networked] private Vector3 Joint32 { get; set; }
    [Networked] private Vector3 Joint33 { get; set; }
    [Networked] private Vector3 Joint34 { get; set; }
    [Networked] private Vector3 Joint35 { get; set; }
    [Networked] private Vector3 Joint36 { get; set; }
    [Networked] private Vector3 Joint37 { get; set; }
    [Networked] private Vector3 Joint38 { get; set; }
    [Networked] private Vector3 Joint39 { get; set; }
    [Networked] private Vector3 Joint40 { get; set; }
    [Networked] private Vector3 Joint41 { get; set; }
    [Networked] private Vector3 Joint42 { get; set; }
    [Networked] private Vector3 Joint43 { get; set; }
    [Networked] private Vector3 Joint44 { get; set; }
    [Networked] private Vector3 Joint45 { get; set; }
    [Networked] private Vector3 Joint46 { get; set; }
    [Networked] private Vector3 Joint47 { get; set; }
    [Networked] private Vector3 Joint48 { get; set; }
    [Networked] private Vector3 Joint49 { get; set; }
    [Networked] private Vector3 Joint50 { get; set; }
    [Networked] private Vector3 Joint51 { get; set; }

    private readonly Vector3[] captureBuffer = new Vector3[TotalJointCount];
    private readonly Vector3[] lastSentPositions = new Vector3[TotalJointCount];

    private IHand leftHand;
    private IHand rightHand;
    private float nextHandScanTime;
    private float lastSendTime;
    private bool hasSentPose;
    private bool lastLeftTracked;
    private bool lastRightTracked;
    private bool commandLineMockHands;

    private float SendInterval => 1f / Mathf.Max(1f, sendRate);

    public override void Spawned()
    {
        commandLineMockHands = HasCommandLineArg(mockHandsCommandLineArg) ||
                               HasCommandLineArg("-mvpMockHands");
    }

    private void LateUpdate()
    {
        if (Object == null || !Object.HasInputAuthority || !Object.HasStateAuthority)
        {
            return;
        }

        if (Time.time - lastSendTime < SendInterval)
        {
            return;
        }

        if (Time.unscaledTime >= nextHandScanTime)
        {
            nextHandScanTime = Time.unscaledTime + handScanInterval;
            ScanHands();
        }

        bool useMockHands = enableMockHandsForDesktopTest || commandLineMockHands;
        bool leftTracked;
        bool rightTracked;
        if (useMockHands)
        {
            CaptureMockHands(captureBuffer);
            leftTracked = true;
            rightTracked = true;
        }
        else
        {
            leftTracked = CaptureHand(leftHand, 0, captureBuffer);
            rightTracked = CaptureHand(rightHand, JointCount, captureBuffer);
        }

        if (hasSentPose &&
            leftTracked == lastLeftTracked &&
            rightTracked == lastRightTracked &&
            !HasMeaningfulMovement(captureBuffer, leftTracked, rightTracked))
        {
            return;
        }

        lastSendTime = Time.time;
        ApplyCapturedPose(captureBuffer, leftTracked, rightTracked);
    }

    public static HandJointId GetJointId(int jointIndex)
    {
        return JointIds[Mathf.Clamp(jointIndex, 0, JointIds.Length - 1)];
    }

    public bool IsHandTracked(bool left)
    {
        return left ? LeftHandTracked : RightHandTracked;
    }

    public bool TryGetJointPosition(bool left, int jointIndex, out Vector3 position)
    {
        if (jointIndex < 0 || jointIndex >= JointCount || !IsHandTracked(left))
        {
            position = default;
            return false;
        }

        position = GetNetworkJointPosition((left ? 0 : JointCount) + jointIndex);
        return true;
    }

    public bool IsPublishingMockHands =>
        Object != null &&
        Object.HasInputAuthority &&
        (enableMockHandsForDesktopTest || commandLineMockHands);

    private void ScanHands()
    {
        leftHand = null;
        rightHand = null;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (!(behaviour is IHand candidate))
            {
                continue;
            }

            Handedness side;
            try
            {
                side = candidate.Handedness;
            }
            catch
            {
                continue;
            }

            if (side == Handedness.Left)
            {
                if (leftHand == null || PreferHand(candidate, leftHand))
                {
                    leftHand = candidate;
                }
            }
            else if (side == Handedness.Right)
            {
                if (rightHand == null || PreferHand(candidate, rightHand))
                {
                    rightHand = candidate;
                }
            }
        }
    }

    private static bool PreferHand(IHand candidate, IHand current)
    {
        if (candidate == null)
        {
            return false;
        }

        if (current == null)
        {
            return true;
        }

        bool candidateValid = candidate.IsConnected && candidate.IsTrackedDataValid;
        bool currentValid = current.IsConnected && current.IsTrackedDataValid;
        if (candidateValid != currentValid)
        {
            return candidateValid;
        }

        return candidate.GetType().Name == "Hand" &&
               current.GetType().Name != "Hand";
    }

    private bool CaptureHand(IHand hand, int offset, Vector3[] target)
    {
        if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid)
        {
            return false;
        }

        bool capturedAny = false;
        for (int i = 0; i < JointIds.Length; i++)
        {
            if (!hand.GetJointPose(JointIds[i], out Pose pose))
            {
                continue;
            }

            target[offset + i] = pose.position;
            capturedAny = true;
        }

        return capturedAny;
    }

    private void CaptureMockHands(Vector3[] target)
    {
        Transform reference = Camera.main != null ? Camera.main.transform : transform;
        Vector3 origin = reference.position +
                         reference.forward * 0.62f -
                         reference.up * 0.18f;
        float time = Time.time;

        CaptureMockHand(
            target,
            0,
            origin - reference.right * 0.24f,
            -reference.right,
            reference.forward,
            reference.up,
            time);

        CaptureMockHand(
            target,
            JointCount,
            origin + reference.right * 0.24f,
            reference.right,
            reference.forward,
            reference.up,
            time + 0.35f);
    }

    private void CaptureMockHand(
        Vector3[] target,
        int offset,
        Vector3 wrist,
        Vector3 outer,
        Vector3 forward,
        Vector3 up,
        float time)
    {
        Vector3 palm = wrist + forward * 0.075f + up * 0.015f;
        target[offset] = wrist;
        target[offset + 1] = palm;

        float curl = Mathf.Lerp(0.15f, 0.85f, (Mathf.Sin(time * 2.4f) + 1f) * 0.5f);

        FillFinger(target, offset + 2, palm + outer * 0.045f - up * 0.02f, outer * 0.05f + forward * 0.035f, up, 4, curl * 0.65f);
        FillFinger(target, offset + 6, palm + outer * -0.055f + up * 0.02f, forward, -up, 5, curl * 0.35f);
        FillFinger(target, offset + 11, palm + outer * -0.018f + up * 0.03f, forward, -up, 5, curl);
        FillFinger(target, offset + 16, palm + outer * 0.018f + up * 0.02f, forward, -up, 5, curl * 0.85f);
        FillFinger(target, offset + 21, palm + outer * 0.052f + up * 0.005f, forward, -up, 5, curl * 0.7f);
    }

    private void FillFinger(
        Vector3[] target,
        int startIndex,
        Vector3 basePosition,
        Vector3 direction,
        Vector3 curlDirection,
        int count,
        float curl)
    {
        Vector3 dir = direction.normalized;
        Vector3 curlDir = curlDirection.normalized;
        float segmentLength = 0.035f;

        for (int i = 0; i < count; i++)
        {
            float step = i + 1f;
            float curlAmount = curl * step * step * 0.012f;
            target[startIndex + i] =
                basePosition +
                dir * (segmentLength * step) +
                curlDir * curlAmount;
        }
    }

    private bool HasMeaningfulMovement(
        Vector3[] positions,
        bool leftTracked,
        bool rightTracked)
    {
        float sqrThreshold = positionThreshold * positionThreshold;
        int start = leftTracked ? 0 : JointCount;
        int end = rightTracked ? TotalJointCount : JointCount;

        if (leftTracked)
        {
            for (int i = 0; i < JointCount; i++)
            {
                if ((positions[i] - lastSentPositions[i]).sqrMagnitude >= sqrThreshold)
                {
                    return true;
                }
            }
        }

        if (rightTracked)
        {
            for (int i = JointCount; i < TotalJointCount; i++)
            {
                if ((positions[i] - lastSentPositions[i]).sqrMagnitude >= sqrThreshold)
                {
                    return true;
                }
            }
        }

        return start < end && !hasSentPose;
    }

    private void ApplyCapturedPose(
        Vector3[] positions,
        bool leftTracked,
        bool rightTracked)
    {
        LeftHandTracked = leftTracked;
        RightHandTracked = rightTracked;
        lastLeftTracked = leftTracked;
        lastRightTracked = rightTracked;

        for (int i = 0; i < TotalJointCount; i++)
        {
            lastSentPositions[i] = positions[i];
            SetNetworkJointPosition(i, positions[i]);
        }

        hasSentPose = true;
        PoseRevision++;
    }

    private Vector3 GetNetworkJointPosition(int index)
    {
        switch (index)
        {
            case 0: return Joint00;
            case 1: return Joint01;
            case 2: return Joint02;
            case 3: return Joint03;
            case 4: return Joint04;
            case 5: return Joint05;
            case 6: return Joint06;
            case 7: return Joint07;
            case 8: return Joint08;
            case 9: return Joint09;
            case 10: return Joint10;
            case 11: return Joint11;
            case 12: return Joint12;
            case 13: return Joint13;
            case 14: return Joint14;
            case 15: return Joint15;
            case 16: return Joint16;
            case 17: return Joint17;
            case 18: return Joint18;
            case 19: return Joint19;
            case 20: return Joint20;
            case 21: return Joint21;
            case 22: return Joint22;
            case 23: return Joint23;
            case 24: return Joint24;
            case 25: return Joint25;
            case 26: return Joint26;
            case 27: return Joint27;
            case 28: return Joint28;
            case 29: return Joint29;
            case 30: return Joint30;
            case 31: return Joint31;
            case 32: return Joint32;
            case 33: return Joint33;
            case 34: return Joint34;
            case 35: return Joint35;
            case 36: return Joint36;
            case 37: return Joint37;
            case 38: return Joint38;
            case 39: return Joint39;
            case 40: return Joint40;
            case 41: return Joint41;
            case 42: return Joint42;
            case 43: return Joint43;
            case 44: return Joint44;
            case 45: return Joint45;
            case 46: return Joint46;
            case 47: return Joint47;
            case 48: return Joint48;
            case 49: return Joint49;
            case 50: return Joint50;
            case 51: return Joint51;
            default: return default;
        }
    }

    private void SetNetworkJointPosition(int index, Vector3 position)
    {
        switch (index)
        {
            case 0: Joint00 = position; break;
            case 1: Joint01 = position; break;
            case 2: Joint02 = position; break;
            case 3: Joint03 = position; break;
            case 4: Joint04 = position; break;
            case 5: Joint05 = position; break;
            case 6: Joint06 = position; break;
            case 7: Joint07 = position; break;
            case 8: Joint08 = position; break;
            case 9: Joint09 = position; break;
            case 10: Joint10 = position; break;
            case 11: Joint11 = position; break;
            case 12: Joint12 = position; break;
            case 13: Joint13 = position; break;
            case 14: Joint14 = position; break;
            case 15: Joint15 = position; break;
            case 16: Joint16 = position; break;
            case 17: Joint17 = position; break;
            case 18: Joint18 = position; break;
            case 19: Joint19 = position; break;
            case 20: Joint20 = position; break;
            case 21: Joint21 = position; break;
            case 22: Joint22 = position; break;
            case 23: Joint23 = position; break;
            case 24: Joint24 = position; break;
            case 25: Joint25 = position; break;
            case 26: Joint26 = position; break;
            case 27: Joint27 = position; break;
            case 28: Joint28 = position; break;
            case 29: Joint29 = position; break;
            case 30: Joint30 = position; break;
            case 31: Joint31 = position; break;
            case 32: Joint32 = position; break;
            case 33: Joint33 = position; break;
            case 34: Joint34 = position; break;
            case 35: Joint35 = position; break;
            case 36: Joint36 = position; break;
            case 37: Joint37 = position; break;
            case 38: Joint38 = position; break;
            case 39: Joint39 = position; break;
            case 40: Joint40 = position; break;
            case 41: Joint41 = position; break;
            case 42: Joint42 = position; break;
            case 43: Joint43 = position; break;
            case 44: Joint44 = position; break;
            case 45: Joint45 = position; break;
            case 46: Joint46 = position; break;
            case 47: Joint47 = position; break;
            case 48: Joint48 = position; break;
            case 49: Joint49 = position; break;
            case 50: Joint50 = position; break;
            case 51: Joint51 = position; break;
        }
    }

    private static bool HasCommandLineArg(string argName)
    {
        if (string.IsNullOrWhiteSpace(argName))
        {
            return false;
        }

        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (string.Equals(arg, argName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
