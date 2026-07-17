using UnityEngine;

// AI 추천은 공용 보드를 덮지 않는 낮은 오른쪽 작업 트레이다.
// 중앙 스케치를 보면서도 1.3m 안쪽의 편한 포킹 거리에서 읽을 수 있게 둔다.
[DisallowMultipleComponent]
public class MvpWorkspaceSidePanelFollower : MonoBehaviour
{
    private Transform _target;
    private RectTransform _panel;
    private float _worldScale = 0.00080f;
    private float _edgeGap = 0.10f;
    private float _verticalOffset = -0.19f;
    private const float DepthOffset = 0.075f;

    public void Configure(
        Transform target,
        float worldScale,
        float edgeGap,
        float verticalOffset)
    {
        _target = target;
        _panel = transform as RectTransform;
        _worldScale = Mathf.Max(0.0001f, worldScale);
        _edgeGap = Mathf.Max(0.02f, edgeGap);
        _verticalOffset = verticalOffset;
        AlignNow();
    }

    private void LateUpdate()
    {
        if (gameObject.activeInHierarchy)
            AlignNow();
    }

    public void AlignNow()
    {
        if (_target == null || _panel == null)
            return;

        transform.localScale = Vector3.one * _worldScale;

        // 추천 단계는 선택이 끝날 때까지 보드 중앙의 모달 시트로 표시한다.
        // 별도 사이드 패널처럼 멀리 밀지 않아 모든 좌석에서 같은 거리와 크기로 읽힌다.
        transform.SetPositionAndRotation(
            _target.position -
            _target.forward * DepthOffset +
            _target.up * 0.01f,
            _target.rotation);
    }
}