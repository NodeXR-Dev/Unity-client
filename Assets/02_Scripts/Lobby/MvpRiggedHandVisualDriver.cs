using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

[DisallowMultipleComponent]
public class MvpRiggedHandVisualDriver : MonoBehaviour
{
    private const int MinimumRequiredBones = 12;

    private struct BoneSpec
    {
        public readonly string Name;
        public readonly int Joint;
        public readonly int AimJoint;

        public BoneSpec(string name, int joint, int aimJoint)
        {
            Name = name;
            Joint = joint;
            AimJoint = aimJoint;
        }
    }

    private class DrivenBone
    {
        public Transform Transform;
        public int Joint;
        public int AimJoint;
        public Vector3 BindDirection;
        public Quaternion BindRotation;
    }

    private static readonly BoneSpec[] BoneSpecs =
    {
        new BoneSpec("hand", 0, 1),
        new BoneSpec("palm.01", 6, 7),
        new BoneSpec("thumb.01", 2, 3),
        new BoneSpec("thumb.02", 3, 4),
        new BoneSpec("thumb.03", 4, 5),
        new BoneSpec("f_index.01", 7, 8),
        new BoneSpec("f_index.02", 8, 9),
        new BoneSpec("f_index.03", 9, 10),
        new BoneSpec("palm.02", 11, 12),
        new BoneSpec("f_middle.01", 12, 13),
        new BoneSpec("f_middle.02", 13, 14),
        new BoneSpec("f_middle.03", 14, 15),
        new BoneSpec("palm.03", 16, 17),
        new BoneSpec("f_ring.01", 17, 18),
        new BoneSpec("f_ring.02", 18, 19),
        new BoneSpec("f_ring.03", 19, 20),
        new BoneSpec("palm.04", 21, 22),
        new BoneSpec("f_pinky.01", 22, 23),
        new BoneSpec("f_pinky.02", 23, 24),
        new BoneSpec("f_pinky.03", 24, 25),
    };

    private readonly List<DrivenBone> drivenBones = new List<DrivenBone>();
    private readonly List<string> missingBoneNames = new List<string>();
    private readonly Vector3[] jointBuffer =
        new Vector3[MvpHandSkeletonSync.JointCount];

    private GameObject visualInstance;
    private bool initialized;
    private float followSpeed = 28f;

    public bool IsReady => initialized && visualInstance != null && drivenBones.Count > 0;
    public int DrivenBoneCount => drivenBones.Count;
    public string LastFailureReason { get; private set; }

    public bool Initialize(
        GameObject handPrefab,
        bool leftHand,
        Transform parent,
        float driverFollowSpeed)
    {
        Clear();

        if (handPrefab == null || parent == null)
        {
            LastFailureReason = handPrefab == null
                ? "Hand prefab is not assigned."
                : "Hand visual parent is not assigned.";
            return false;
        }

        followSpeed = Mathf.Max(0.01f, driverFollowSpeed);
        visualInstance = Instantiate(handPrefab, parent, false);
        visualInstance.name = leftHand
            ? "MvpRiggedLeftHandVisual"
            : "MvpRiggedRightHandVisual";

        CacheBones(leftHand);
        ApplyRenderSettings();
        initialized = drivenBones.Count >= MinimumRequiredBones;
        if (!initialized)
        {
            LastFailureReason =
                $"Rigged hand visual has only {drivenBones.Count} mapped bones. " +
                $"Expected at least {MinimumRequiredBones}. Missing: " +
                string.Join(", ", missingBoneNames);
            Debug.LogWarning(LastFailureReason, this);
        }
        else
        {
            LastFailureReason = string.Empty;
        }

        SetVisible(false);
        return initialized;
    }

    public void Clear()
    {
        if (visualInstance != null)
        {
            if (Application.isPlaying)
                Destroy(visualInstance);
            else
                DestroyImmediate(visualInstance);
        }

        visualInstance = null;
        drivenBones.Clear();
        missingBoneNames.Clear();
        initialized = false;
        LastFailureReason = string.Empty;
    }

    public void SetVisible(bool visible)
    {
        if (visualInstance != null && visualInstance.activeSelf != visible)
            visualInstance.SetActive(visible);
    }

    public void ApplyPose(IHand hand, bool force)
    {
        if (!IsReady ||
            hand == null ||
            !hand.IsConnected ||
            !hand.IsTrackedDataValid)
        {
            SetVisible(false);
            return;
        }

        for (int i = 0; i < jointBuffer.Length; i++)
        {
            if (!hand.GetJointPose(MvpHandSkeletonSync.GetJointId(i), out Pose pose))
            {
                SetVisible(false);
                return;
            }

            jointBuffer[i] = pose.position;
        }

        ApplyPose(jointBuffer, true, force);
    }

    public void ApplyPose(MvpHandSkeletonSync sync, bool leftHand, bool force)
    {
        if (!IsReady || sync == null || !sync.IsHandTracked(leftHand))
        {
            SetVisible(false);
            return;
        }

        for (int i = 0; i < jointBuffer.Length; i++)
        {
            if (!sync.TryGetJointPosition(leftHand, i, out jointBuffer[i]))
            {
                SetVisible(false);
                return;
            }
        }

        ApplyPose(jointBuffer, true, force);
    }

    public void ApplyPose(Vector3[] joints, bool tracked, bool force)
    {
        if (!IsReady || !tracked || !IsValidPose(joints))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        float t = force
            ? 1f
            : 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);

        foreach (DrivenBone bone in drivenBones)
        {
            Vector3 target = joints[bone.Joint];
            Vector3 aim = joints[bone.AimJoint];
            Vector3 direction = aim - target;

            bone.Transform.position = Vector3.Lerp(
                bone.Transform.position,
                target,
                t);

            if (direction.sqrMagnitude > 0.000001f)
            {
                Quaternion targetRotation =
                    Quaternion.FromToRotation(
                        bone.BindDirection,
                        direction.normalized) *
                    bone.BindRotation;
                bone.Transform.rotation = Quaternion.Slerp(
                    bone.Transform.rotation,
                    targetRotation,
                    t);
            }
        }
    }

    private void CacheBones(bool leftHand)
    {
        missingBoneNames.Clear();
        Dictionary<string, Transform> byName =
            new Dictionary<string, Transform>();
        foreach (Transform t in visualInstance.GetComponentsInChildren<Transform>(true))
        {
            if (!byName.ContainsKey(t.name))
                byName.Add(t.name, t);
        }

        string suffix = leftHand ? ".L" : ".R";
        foreach (BoneSpec spec in BoneSpecs)
        {
            string boneName = spec.Name + suffix;
            if (!byName.TryGetValue(boneName, out Transform bone))
            {
                missingBoneNames.Add(boneName);
                continue;
            }

            DrivenBone driven = new DrivenBone
            {
                Transform = bone,
                Joint = spec.Joint,
                AimJoint = spec.AimJoint,
                BindRotation = bone.rotation,
                BindDirection = ResolveBindDirection(bone)
            };
            drivenBones.Add(driven);
        }
    }

    private static Vector3 ResolveBindDirection(Transform bone)
    {
        if (bone.childCount > 0)
        {
            Vector3 direction = bone.GetChild(0).position - bone.position;
            if (direction.sqrMagnitude > 0.000001f)
                return direction.normalized;
        }

        return bone.forward.sqrMagnitude > 0.000001f
            ? bone.forward.normalized
            : Vector3.forward;
    }

    private void ApplyRenderSettings()
    {
        if (visualInstance == null)
            return;

        Renderer[] renderers =
            visualInstance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static bool IsValidPose(Vector3[] joints)
    {
        if (joints == null || joints.Length < MvpHandSkeletonSync.JointCount)
            return false;

        for (int i = 0; i < MvpHandSkeletonSync.JointCount; i++)
        {
            if (!IsFinite(joints[i]))
                return false;
        }

        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return
            IsFinite(value.x) &&
            IsFinite(value.y) &&
            IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
