using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MvpHandSkeletonSync))]
public class MvpRemoteHandSkeletonVisual : MonoBehaviour
{
    private const int HandCount = 2;

    private static readonly int[,] BonePairs =
    {
        { 0, 1 },
        { 1, 2 }, { 2, 3 }, { 3, 4 }, { 4, 5 },
        { 1, 6 }, { 6, 7 }, { 7, 8 }, { 8, 9 }, { 9, 10 },
        { 1, 11 }, { 11, 12 }, { 12, 13 }, { 13, 14 }, { 14, 15 },
        { 1, 16 }, { 16, 17 }, { 17, 18 }, { 18, 19 }, { 19, 20 },
        { 1, 21 }, { 21, 22 }, { 22, 23 }, { 23, 24 }, { 24, 25 },
    };

    [Header("Remote Visual")]
    [SerializeField, Min(0.001f)] private float jointRadius = 0.018f;
    [SerializeField, Min(0.001f)] private float palmRadius = 0.026f;
    [SerializeField, Min(0.001f)] private float boneRadius = 0.007f;
    [SerializeField, Min(0.01f)] private float followSpeed = 28f;
    [SerializeField] private Color handColor = new Color(0.58f, 0.58f, 0.58f, 0.92f);

    [Header("Rigged Hand Visual")]
    [SerializeField] private bool preferRiggedHandVisual = true;
    [SerializeField] private GameObject leftRiggedHandPrefab;
    [SerializeField] private GameObject rightRiggedHandPrefab;
    [SerializeField] private bool showSkeletonFallback = true;
    [SerializeField] private bool logRiggedFallbackWarning = true;

    private MvpHandSkeletonSync sync;
    private Transform root;
    private Transform riggedRoot;
    private Transform[,] joints;
    private Transform[,] bones;
    private Material material;
    private MvpRiggedHandVisualDriver leftRiggedDriver;
    private MvpRiggedHandVisualDriver rightRiggedDriver;
    private int lastRevision = -1;
    private bool warnedRiggedFallback;

    private bool HasRiggedVisuals =>
        leftRiggedDriver != null &&
        rightRiggedDriver != null &&
        leftRiggedDriver.IsReady &&
        rightRiggedDriver.IsReady;

    public bool IsShowingRemoteHands { get; private set; }

    private void Awake()
    {
        sync = GetComponent<MvpHandSkeletonSync>();
        BuildRiggedVisuals();
        BuildVisuals();
        SetAllVisible(false);
        SetRiggedVisible(false);
    }

    private void LateUpdate()
    {
        if (sync == null || sync.Object == null || sync.Object.HasInputAuthority)
        {
            SetAllVisible(false);
            SetRiggedVisible(false);
            IsShowingRemoteHands = false;
            return;
        }

        bool force = sync.PoseRevision != lastRevision;
        lastRevision = sync.PoseRevision;

        if (preferRiggedHandVisual && HasRiggedVisuals)
        {
            SetAllVisible(false);
            SetRiggedVisible(true);
            leftRiggedDriver.ApplyPose(sync, true, force);
            rightRiggedDriver.ApplyPose(sync, false, force);
            IsShowingRemoteHands =
                sync.IsHandTracked(true) ||
                sync.IsHandTracked(false);
            return;
        }

        SetRiggedVisible(false);
        if (!showSkeletonFallback)
        {
            SetAllVisible(false);
            IsShowingRemoteHands = false;
            return;
        }

        UpdateHand(0, true, force);
        UpdateHand(1, false, force);
        IsShowingRemoteHands = sync.IsHandTracked(true) || sync.IsHandTracked(false);
    }

    private void OnDestroy()
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }

    private void BuildRiggedVisuals()
    {
        if (leftRiggedDriver != null || rightRiggedDriver != null)
        {
            return;
        }

        riggedRoot = new GameObject("MvpRemoteRiggedHandVisual").transform;
        riggedRoot.SetParent(transform, false);

        GameObject left = new GameObject("LeftRiggedHandDriver");
        left.transform.SetParent(riggedRoot, false);
        leftRiggedDriver = left.AddComponent<MvpRiggedHandVisualDriver>();
        leftRiggedDriver.Initialize(
            leftRiggedHandPrefab,
            true,
            left.transform,
            followSpeed);

        GameObject right = new GameObject("RightRiggedHandDriver");
        right.transform.SetParent(riggedRoot, false);
        rightRiggedDriver = right.AddComponent<MvpRiggedHandVisualDriver>();
        rightRiggedDriver.Initialize(
            rightRiggedHandPrefab,
            false,
            right.transform,
            followSpeed);

        WarnIfRiggedFallbackNeeded();
    }

    private void BuildVisuals()
    {
        if (root != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        material = new Material(shader);
        material.name = "MvpRemoteHandSkeletonMaterial";
        material.color = handColor;

        root = new GameObject("MvpRemoteHandSkeletonVisual").transform;
        root.SetParent(transform, false);

        joints = new Transform[HandCount, MvpHandSkeletonSync.JointCount];
        bones = new Transform[HandCount, BonePairs.GetLength(0)];

        for (int hand = 0; hand < HandCount; hand++)
        {
            Transform handRoot = new GameObject(hand == 0 ? "LeftHand" : "RightHand").transform;
            handRoot.SetParent(root, false);

            for (int joint = 0; joint < MvpHandSkeletonSync.JointCount; joint++)
            {
                GameObject jointGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                jointGo.name = MvpHandSkeletonSync.GetJointId(joint).ToString();
                jointGo.transform.SetParent(handRoot, false);
                jointGo.transform.localScale = Vector3.one *
                    (joint <= 1 ? palmRadius : jointRadius);
                ApplyRenderOnlySetup(jointGo);
                joints[hand, joint] = jointGo.transform;
            }

            for (int bone = 0; bone < BonePairs.GetLength(0); bone++)
            {
                GameObject boneGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                boneGo.name = "Bone_" + BonePairs[bone, 0] + "_" + BonePairs[bone, 1];
                boneGo.transform.SetParent(handRoot, false);
                ApplyRenderOnlySetup(boneGo);
                bones[hand, bone] = boneGo.transform;
            }
        }
    }

    private void ApplyRenderOnlySetup(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private void UpdateHand(int handIndex, bool left, bool force)
    {
        bool tracked = sync.IsHandTracked(left);
        SetHandVisible(handIndex, tracked);
        if (!tracked)
        {
            return;
        }

        float t = force
            ? 1f
            : 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);

        for (int joint = 0; joint < MvpHandSkeletonSync.JointCount; joint++)
        {
            if (!sync.TryGetJointPosition(left, joint, out Vector3 targetPosition))
            {
                continue;
            }

            Transform jointTransform = joints[handIndex, joint];
            jointTransform.position = Vector3.Lerp(
                jointTransform.position,
                targetPosition,
                t);
        }

        for (int bone = 0; bone < BonePairs.GetLength(0); bone++)
        {
            Transform a = joints[handIndex, BonePairs[bone, 0]];
            Transform b = joints[handIndex, BonePairs[bone, 1]];
            Transform boneTransform = bones[handIndex, bone];

            Vector3 delta = b.position - a.position;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                boneTransform.gameObject.SetActive(false);
                continue;
            }

            boneTransform.gameObject.SetActive(true);
            boneTransform.position = (a.position + b.position) * 0.5f;
            boneTransform.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            boneTransform.localScale = new Vector3(boneRadius, length * 0.5f, boneRadius);
        }
    }

    private void SetAllVisible(bool visible)
    {
        if (root != null && root.gameObject.activeSelf != visible)
        {
            root.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            IsShowingRemoteHands = false;
        }
    }

    private void SetRiggedVisible(bool visible)
    {
        if (riggedRoot != null && riggedRoot.gameObject.activeSelf != visible)
        {
            riggedRoot.gameObject.SetActive(visible);
        }

        if (leftRiggedDriver != null)
        {
            leftRiggedDriver.SetVisible(visible);
        }

        if (rightRiggedDriver != null)
        {
            rightRiggedDriver.SetVisible(visible);
        }
    }

    private void WarnIfRiggedFallbackNeeded()
    {
        if (!logRiggedFallbackWarning ||
            warnedRiggedFallback ||
            !preferRiggedHandVisual ||
            HasRiggedVisuals)
        {
            return;
        }

        warnedRiggedFallback = true;
        string leftReason = leftRiggedDriver != null
            ? leftRiggedDriver.LastFailureReason
            : "Left rigged driver was not created.";
        string rightReason = rightRiggedDriver != null
            ? rightRiggedDriver.LastFailureReason
            : "Right rigged driver was not created.";

        Debug.LogWarning(
            "MVP remote rigged hand visual is not ready. " +
            "Skeleton fallback will be used if enabled. " +
            $"Left: {leftReason} Right: {rightReason}",
            this);
    }

    private void SetHandVisible(int handIndex, bool visible)
    {
        bool anyVisible = sync != null &&
            ((handIndex == 0 && visible) ||
             (handIndex == 1 && visible) ||
             sync.IsHandTracked(true) ||
             sync.IsHandTracked(false));

        SetAllVisible(anyVisible);

        for (int joint = 0; joint < MvpHandSkeletonSync.JointCount; joint++)
        {
            GameObject go = joints[handIndex, joint].gameObject;
            if (go.activeSelf != visible)
            {
                go.SetActive(visible);
            }
        }

        for (int bone = 0; bone < BonePairs.GetLength(0); bone++)
        {
            GameObject go = bones[handIndex, bone].gameObject;
            if (go.activeSelf != visible)
            {
                go.SetActive(visible);
            }
        }
    }
}
