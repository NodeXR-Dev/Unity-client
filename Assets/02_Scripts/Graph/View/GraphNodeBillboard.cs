using UnityEngine;

// 노드가 항상 사용자를 향하게 한다(빌보드).
//
// 원래 노드 회전은 MvpWorkspaceLayout 이 보드와 같은 값으로 맞춰 준다. 그러면 보드를 정면에서
// 볼 때만 글자가 반듯하고, 옆으로 걸어가면 노드 글자가 비스듬해져 읽기 어렵다.
//
// [실행 순서] MvpWorkspaceLayout 도 LateUpdate 에서 노드 회전을 넣는다. 나중에 실행돼야
//   덮어쓸 수 있으므로 실행 순서를 뒤로 민다(레이아웃 650 → 이 스크립트 800).
//
// [업라이트] 카메라를 그대로 LookAt 하면 사용자가 고개를 숙일 때 노드도 같이 누워 글자가
//   기울어진다. 수평 성분만 써서 항상 세워 둔다.
[DefaultExecutionOrder(800)]
[DisallowMultipleComponent]
public class GraphNodeBillboard : MonoBehaviour
{
    private Camera _camera;

    private void LateUpdate()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        if (_camera == null)
            return;

        // 캔버스/텍스트는 +Z 가 사용자 반대편을 향할 때 바로 읽힌다
        // (MvpXrCanvasAnchor·MainSketchPanel 과 같은 규약).
        Vector3 away = Vector3.ProjectOnPlane(
            transform.position - _camera.transform.position,
            Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
            return;

        transform.rotation =
            Quaternion.LookRotation(away.normalized, Vector3.up);
    }
}
