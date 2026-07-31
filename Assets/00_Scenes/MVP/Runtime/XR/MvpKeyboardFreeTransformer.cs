using Oculus.Interaction;
using Oculus.Interaction.GrabAPI;
using UnityEngine;

// 키보드 전용 이동 변환기. 키보드의 현재 방향은 유지하고 위치만 손 또는 컨트롤러를 따라간다.
[DisallowMultipleComponent]
public class MvpKeyboardFreeTransformer : MonoBehaviour, ITransformer
{
    private Transform _target;
    private IGrabbable _grabbable;
    private Vector3 _grabOffset;
    private Quaternion _rotation;

    public void Configure(Transform target)
    {
        _target = target;
    }

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    public void BeginTransform()
    {
        if (_target == null || _grabbable == null || _grabbable.GrabPoints.Count == 0)
            return;

        _grabOffset = _target.position - _grabbable.GrabPoints[0].position;
        _rotation = _target.rotation;
    }

    public void UpdateTransform()
    {
        if (_target == null || _grabbable == null || _grabbable.GrabPoints.Count == 0)
            return;

        _target.position = _grabbable.GrabPoints[0].position + _grabOffset;
        _target.rotation = _rotation;
    }

    public void EndTransform()
    {
    }
}
