using UnityEngine;

/// <summary>
/// VR 손 조작 인터랙션.
/// - 한 손으로 잡으면: 그 손을 따라 이동 + 회전
/// - 두 손으로 잡으면: 두 손 거리로 확대/축소, 중점으로 이동, 두 손을 잇는 축으로 회전
///
/// 사용법:
///   1. 조작할 3D 객체에 이 스크립트를 붙인다 (Collider 필수 — 없으면 자동 경고).
///   2. Inspector의 Left Hand / Right Hand 슬롯에 씬의 LeftHandAnchor / RightHandAnchor를 드래그.
///   3. Quest 컨트롤러의 그립(Grip) 버튼을 손이 객체 근처(Grab Radius 안)일 때 누르면 잡힌다.
///
/// 입력은 OVRInput(컨트롤러 그립) 기준. Camera Rig에 OVRManager가 있어야 동작한다(회의실 씬에 이미 있음).
/// </summary>
[RequireComponent(typeof(Collider))]
public class TwoHandGrabTransform : MonoBehaviour
{
    [Header("손 앵커 (씬의 LeftHandAnchor / RightHandAnchor 드래그)")]
    public Transform leftHand;
    public Transform rightHand;

    [Header("잡기 설정")]
    [Tooltip("손이 객체로부터 이 거리(m) 안에 있을 때만 잡을 수 있다. 잡은 후에는 멀어져도 유지된다.")]
    public float grabRadius = 0.15f;

    [Tooltip("그립 버튼을 이 값 이상 당기면 '잡음'으로 인식 (0~1).")]
    [Range(0.1f, 1f)] public float gripThreshold = 0.5f;

    [Header("스케일 제한 (객체 localScale.x 기준)")]
    public float minScale = 0.1f;
    public float maxScale = 10f;

    // --- 내부 상태 ---
    private Collider _col;

    private bool _leftHeld;
    private bool _rightHeld;

    // 한 손 grab offset (잡은 손 기준 객체의 상대 위치/회전)
    private Transform _activeHand;
    private Vector3 _oneGrabLocalPos;
    private Quaternion _oneGrabLocalRot;

    // 두 손 grab 기준값
    private float _initialHandDist;
    private Vector3 _initialScale;
    private Quaternion _initialRotation;
    private Quaternion _initialHandsRotation;
    private Vector3 _midOffset; // 두 손 중점 기준 객체 위치 offset (점프 방지)

    void Awake()
    {
        _col = GetComponent<Collider>();
    }

    void Update()
    {
        // 1) 각 손이 현재 잡고 있는지 판정
        //    - 잡기 시작은 'Grab Radius 안'에서만 가능
        //    - 한 번 잡으면 그립을 유지하는 동안 멀어져도 계속 잡음
        bool left = IsGrip(OVRInput.Controller.LTouch) && (_leftHeld || InRange(leftHand));
        bool right = IsGrip(OVRInput.Controller.RTouch) && (_rightHeld || InRange(rightHand));

        // 2) 잡은 손 구성이 바뀌면 offset 재계산 (손이 바뀌거나 손 개수가 바뀔 때 점프 방지)
        if (left != _leftHeld || right != _rightHeld)
        {
            _leftHeld = left;
            _rightHeld = right;
            BeginGrab();
        }

        // 3) 현재 모드에 따라 변형 적용
        if (_leftHeld && _rightHeld)
            UpdateTwoHand();
        else if (_leftHeld)
            UpdateOneHand(leftHand);
        else if (_rightHeld)
            UpdateOneHand(rightHand);
    }

    // 잡기 시작 시점의 기준값 저장
    private void BeginGrab()
    {
        if (_leftHeld && _rightHeld)
        {
            Vector3 l = leftHand.position;
            Vector3 r = rightHand.position;
            Vector3 mid = (l + r) * 0.5f;

            _initialHandDist = Mathf.Max(0.0001f, Vector3.Distance(l, r));
            _initialScale = transform.localScale;
            _initialRotation = transform.rotation;
            _initialHandsRotation = HandsRotation(l, r);
            _midOffset = Quaternion.Inverse(_initialHandsRotation) * (transform.position - mid);
        }
        else if (_leftHeld || _rightHeld)
        {
            _activeHand = _leftHeld ? leftHand : rightHand;
            // 잡은 손 기준 객체의 상대 위치/회전을 기억 → 매 프레임 손 기준으로 복원
            _oneGrabLocalPos = _activeHand.InverseTransformPoint(transform.position);
            _oneGrabLocalRot = Quaternion.Inverse(_activeHand.rotation) * transform.rotation;
        }
    }

    // 한 손: 손을 따라 이동 + 회전
    private void UpdateOneHand(Transform hand)
    {
        if (hand == null) return;
        transform.rotation = hand.rotation * _oneGrabLocalRot;
        transform.position = hand.TransformPoint(_oneGrabLocalPos);
    }

    // 두 손: 거리=스케일, 중점=이동, 두 손 축=회전
    private void UpdateTwoHand()
    {
        if (leftHand == null || rightHand == null) return;

        Vector3 l = leftHand.position;
        Vector3 r = rightHand.position;
        Vector3 mid = (l + r) * 0.5f;
        float dist = Mathf.Max(0.0001f, Vector3.Distance(l, r));

        float ratio = dist / _initialHandDist;
        Quaternion handsRot = HandsRotation(l, r);
        Quaternion deltaRot = handsRot * Quaternion.Inverse(_initialHandsRotation);

        // 회전
        transform.rotation = deltaRot * _initialRotation;

        // 스케일 (비율 유지하며 min/max 클램프)
        transform.localScale = ClampScale(_initialScale * ratio);

        // 위치 (중점 + 회전·스케일이 반영된 offset)
        transform.position = mid + deltaRot * (_midOffset * ratio);
    }

    // 두 손을 잇는 벡터로부터 안정적인 회전 산출 (up 기준 보정)
    private Quaternion HandsRotation(Vector3 l, Vector3 r)
    {
        Vector3 dir = r - l;
        if (dir.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private Vector3 ClampScale(Vector3 s)
    {
        if (Mathf.Abs(s.x) < 1e-5f) return s;
        float clampedX = Mathf.Clamp(s.x, minScale, maxScale);
        float k = clampedX / s.x;
        return s * k; // x,y,z 비율 유지
    }

    private bool IsGrip(OVRInput.Controller controller)
    {
        return OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) >= gripThreshold;
    }

    private bool InRange(Transform hand)
    {
        if (hand == null || _col == null) return false;
        // 점(손)과 콜라이더 경계 사이 거리로 판정 → 큰 객체도 가장자리에서 잡힌다
        return _col.bounds.SqrDistance(hand.position) <= grabRadius * grabRadius;
    }

    // 에디터에서 잡기 범위 시각화
    void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
        Bounds b = col.bounds;
        Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (grabRadius * 2f));
    }
}
