using UnityEngine;

// 확인 팝업처럼 "반드시 응답해야 하는" UI 를 사용자 시야에 유지한다.
// 화면 중앙을 고집하지 않고, 시선에서 일정 각도 이상 벗어났을 때만
// 부드럽게 따라와 어지러움 없이 항상 손 닿는 곳에 있게 한다.
[DisallowMultipleComponent]
public class MvpGentleFollow : MonoBehaviour
{
    [SerializeField] private float _distance = 0.85f;
    [SerializeField] private float _maxAngle = 32f;     // 이 각도 넘게 벗어나면 따라오기 시작
    [SerializeField] private float _followSpeed = 4.5f;

    private bool _chasing;

    public void Configure(float distance)
    {
        _distance = distance;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 headPos = cam.transform.position;
        Vector3 flatForward = cam.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.001f)
            return;
        flatForward.Normalize();

        Vector3 targetPos = headPos + flatForward * _distance;
        Vector3 toPanel = transform.position - headPos;
        toPanel.y = 0f;

        float angle = Vector3.Angle(flatForward, toPanel);
        if (!_chasing && angle > _maxAngle)
            _chasing = true;

        if (_chasing)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetPos,
                _followSpeed * Time.unscaledDeltaTime);
            if (angle < 4f)
                _chasing = false;   // 시야 중앙 근처로 오면 다시 고정
        }

        // 항상 사용자를 바라보게 (수평 회전만)
        Vector3 look = transform.position - headPos;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(look.normalized),
                8f * Time.unscaledDeltaTime);
    }
}
