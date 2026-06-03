/*
 * 파일명: EdgeView.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: EdgeData와 두 NodeView를 받아 LineRenderer로 연결선을 그린다.
 * 핵심 내용:
 * - Bind() 시 data, fromView, toView를 저장하고 즉시 선을 갱신한다.
 * - LateUpdate에서 매 프레임 위치를 갱신하여 노드 이동 시 선이 따라오도록 한다.
 * - LineRenderer가 Inspector에 연결되지 않으면 GetComponent로 자동 탐색한다.
 * - 끝점 결정: fromView.OutputPort / toView.InputPort 가 있으면 우선 사용하고,
 *   없으면 각각 fromView.transform.position / toView.transform.position 으로 fallback.
 *   디자이너 NodeBox Variant에는 Port_Out/Port_In 앵커가 있고 기존 Mock NodePrefab에는 없으므로 양쪽 모두 호환된다.
 *
 * 프리팹 구조 (01_Prefabs/Graph/EdgePrefab):
 *   EdgeRoot
 *   ├── LineRenderer (positionCount = 2, width ≈ 0.02)  ← _lineRenderer 연결
 *   └── EdgeView (이 컴포넌트)
 */
using UnityEngine;

public class EdgeView : MonoBehaviour
{
    [SerializeField] private LineRenderer _lineRenderer;

    private EdgeData _data;
    private NodeView _fromView;
    private NodeView _toView;
    private bool _isBound = false;

    public void Bind(EdgeData data, NodeView fromView, NodeView toView)
    {
        if (data == null || fromView == null || toView == null)
        {
            Debug.LogWarning("[EdgeView] Bind 실패: data, fromView, toView 중 하나 이상이 null입니다.");
            return;
        }

        // Inspector에 연결되지 않았으면 자동 탐색
        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();

        if (_lineRenderer == null)
        {
            Debug.LogWarning("[EdgeView] Bind 실패: LineRenderer를 찾을 수 없습니다. 프리팹에 LineRenderer를 추가해주세요.");
            return;
        }

        _data = data;
        _fromView = fromView;
        _toView = toView;
        _lineRenderer.positionCount = 2;
        _isBound = true;

        UpdateLine();
    }

    public void UpdateLine()
    {
        if (!_isBound || _fromView == null || _toView == null || _lineRenderer == null)
            return;

        _lineRenderer.SetPosition(0, GetFromPosition());
        _lineRenderer.SetPosition(1, GetToPosition());
    }

    private Vector3 GetFromPosition()
    {
        var port = _fromView.OutputPort;
        return port != null ? port.position : _fromView.transform.position;
    }

    private Vector3 GetToPosition()
    {
        var port = _toView.InputPort;
        return port != null ? port.position : _toView.transform.position;
    }

    private void LateUpdate()
    {
        UpdateLine();
    }
}
