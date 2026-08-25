using UnityEngine;

/// <summary>
/// 생성된 3D 모델을 제자리에서 천천히 돌린다.
///
/// 회전을 매 프레임 방송하지 않는다. 방 전체가 공유하는 시각
/// (GraphNetworkManager.SharedTime)으로 각도를 계산하면 메시지 없이도 모두가
/// 같은 자세를 본다. 방송 방식은 대역폭도 크고 지연 때문에 서로 어긋난다.
///
/// 모델의 피벗은 시각적 중심이 아니다 — Generate3DController.FitAndPlace 가
/// 바닥을 원판에 맞추려고 루트를 옮기기 때문이다. 그래서 루트를 그냥 돌리면
/// 제자리가 아니라 비스듬히 도는 것처럼 보인다. 받침 중심을 축으로 잡는다.
/// </summary>
[DisallowMultipleComponent]
public class MvpModelSpin : MonoBehaviour
{
    [Tooltip("초당 회전 각도. 음수면 반대로 돈다.")]
    [SerializeField] private float _degreesPerSecond = 12f;

    private Transform _pivotSource;      // 받침(스테이지). 없으면 생성 시점 위치를 쓴다.
    private Vector3 _pivotFallback;
    private Vector3 _baseOffset;         // 축에서 모델 루트까지의 수평 오프셋
    private float _baseHeight;           // 축 대비 높이(회전해도 그대로 유지)
    private Quaternion _baseRotation;
    private bool _ready;

    private GraphNetworkManager _network;
    private float _nextResolveTime;

    /// <summary>모델을 붙인 직후 호출한다. pivot 은 받침(원판) Transform.</summary>
    public void Configure(Transform pivot, float degreesPerSecond)
    {
        _pivotSource = pivot;
        _degreesPerSecond = degreesPerSecond;
        _pivotFallback = ResolvePivot();
        Vector3 delta = transform.position - _pivotFallback;
        _baseHeight = delta.y;           // 높이는 그대로 두고 수평으로만 돈다
        delta.y = 0f;
        _baseOffset = delta;
        _baseRotation = transform.rotation;
        _ready = true;
    }

    private Vector3 ResolvePivot()
    {
        if (_pivotSource != null)
            return _pivotSource.position;
        return _pivotFallback == Vector3.zero ? transform.position : _pivotFallback;
    }

    private void LateUpdate()
    {
        if (!_ready)
            return;

        if (Time.unscaledTime >= _nextResolveTime)
        {
            _nextResolveTime = Time.unscaledTime + 1f;
            if (_network == null)
                _network = FindFirstObjectByType<GraphNetworkManager>();
        }

        float t = _network != null ? _network.SharedTime : Time.time;
        Quaternion spin = Quaternion.Euler(0f, t * _degreesPerSecond, 0f);

        Vector3 pivot = ResolvePivot();
        Vector3 next = pivot + spin * _baseOffset;
        next.y = pivot.y + _baseHeight;
        transform.position = next;
        transform.rotation = spin * _baseRotation;
    }
}
