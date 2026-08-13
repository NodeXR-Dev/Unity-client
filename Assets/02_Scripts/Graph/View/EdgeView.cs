using UnityEngine;

// EdgeData 1개를 Cubic Bezier 커브로 시각화한다.
// 끝점 구(ConnectorSphere)는 엣지 자식으로 관리 — 엣지와 함께 생성/삭제/이동된다.
//
// 프리팹 구조 (01_Prefabs/Graph/EdgePrefab):
//   EdgeRoot  ← EdgeView 컴포넌트
//   ├── LineRenderer  ← _lineRenderer 연결
//   ├── FromSphere    ← ConnectorSphere FBX + ConnectorSphereView  (_fromConnector)
//   └── ToSphere      ← ConnectorSphere FBX + ConnectorSphereView  (_toConnector)
public class EdgeView : MonoBehaviour
{
    [SerializeField] private LineRenderer        _lineRenderer;
    [SerializeField] private int                 _bezierSegments = 20;

    [Header("라인 외형")]
    [SerializeField] private Color _lineColor = Color.white;
    [SerializeField] private float _lineWidth = 0.004f;

    [Header("엔드포인트 구 (엣지 자식으로 배치)")]
    [SerializeField] private ConnectorSphereView _fromConnector;
    [SerializeField] private ConnectorSphereView _toConnector;
    // 엣지가 양 끝에 놓는 구체의 월드 스케일.
    // (노드 프리팹 Port_In/Port_Out 아래의 구체 크기와는 다른 값이다 — 그쪽은 노드 로컬 0.02.)
    [SerializeField] private float _connectorScale = 0.0003f;

    // PART/ALL 연결선(MvpXrGraphLinkController)도 끝에 구체를 얹는다.
    // 그쪽이 이 값을 그대로 쓰게 해서 두 종류의 구체가 같은 크기로 보이게 한다
    // — 값을 복사해 두면 프리팹만 고쳤을 때 한쪽만 바뀐다.
    public float ConnectorScale => _connectorScale;

    private static Material _sharedLineMaterial;

    private EdgeData _data;
    private NodeView _fromView;
    private NodeView _toView;
    private bool     _isBound;

    public void Bind(EdgeData data, NodeView fromView, NodeView toView)
    {
        if (data == null || fromView == null || toView == null)
        {
            Debug.LogWarning("[EdgeView] Bind 실패: data, fromView, toView 중 하나 이상이 null입니다.");
            return;
        }

        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null)
        {
            Debug.LogWarning("[EdgeView] Bind 실패: LineRenderer를 찾을 수 없습니다.");
            return;
        }

        _data     = data;
        _fromView = fromView;
        _toView   = toView;
        _isBound  = true;

        ApplyLineStyle();
        _lineRenderer.positionCount = _bezierSegments + 1;

        UpdateLine();
    }

    private void ApplyLineStyle()
    {
        if (_lineRenderer.sharedMaterial == null)
        {
            if (_sharedLineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (shader != null)
                    _sharedLineMaterial = new Material(shader);
            }
            if (_sharedLineMaterial != null)
                _lineRenderer.sharedMaterial = _sharedLineMaterial;
        }

        _lineRenderer.startColor    = _lineColor;
        _lineRenderer.endColor      = _lineColor;
        _lineRenderer.startWidth    = _lineWidth;
        _lineRenderer.endWidth      = _lineWidth;
        _lineRenderer.useWorldSpace = true;
    }

    public void UpdateLine()
    {
        if (!_isBound || _fromView == null || _toView == null || _lineRenderer == null)
            return;

        Vector3 p0 = GetFromPosition();
        Vector3 p3 = GetToPosition();

        // 엔드포인트 구를 라인 시작/끝 위치로 이동 + 크기 통일
        var scale = Vector3.one * _connectorScale;
        if (_fromConnector != null)
        {
            _fromConnector.transform.position = p0;
            _fromConnector.transform.localScale = scale;
        }
        if (_toConnector != null)
        {
            _toConnector.transform.position = p3;
            _toConnector.transform.localScale = scale;
        }

        // 수평 탄젠트 — 부모(왼쪽)→자식(오른쪽) S커브
        float tangentLen = Mathf.Max(Vector3.Distance(p0, p3) * 0.5f, 0.2f);
        Vector3 p1 = p0 + Vector3.right * tangentLen;
        Vector3 p2 = p3 + Vector3.left  * tangentLen;

        for (int i = 0; i <= _bezierSegments; i++)
        {
            float t = i / (float)_bezierSegments;
            _lineRenderer.SetPosition(i, CubicBezier(t, p0, p1, p2, p3));
        }
    }

    // 외부에서 구의 pressed 상태 전환 (예: XR Grab)
    public void SetFromConnectorPressed(bool pressed) => _fromConnector?.SetGrabbed(pressed);
    public void SetToConnectorPressed(bool pressed)   => _toConnector?.SetGrabbed(pressed);

    // MVP 자식 활성화용: 이 엣지의 to(자식) node_id.
    public string ToNodeId => _data != null ? _data.to_node_id : null;

    // 선 색 변경(활성 자식은 초록 등).
    public void SetLineColor(Color color)
    {
        _lineColor = color;
        if (_lineRenderer != null)
        {
            _lineRenderer.startColor = color;
            _lineRenderer.endColor   = color;
        }
    }

    // 선/엔드포인트 구 표시 토글(비활성 자식은 부모와의 선을 숨긴다).
    public void SetLineVisible(bool visible)
    {
        if (_lineRenderer != null)
            _lineRenderer.enabled = visible;
        if (_fromConnector != null)
            _fromConnector.gameObject.SetActive(visible);
        if (_toConnector != null)
            _toConnector.gameObject.SetActive(visible);
    }

    private static Vector3 CubicBezier(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1f - t;
        return u * u * u * p0
             + 3f * u * u * t * p1
             + 3f * u * t * t * p2
             + t * t * t * p3;
    }

    private Vector3 GetFromPosition()
    {
        var port = _fromView?.OutputPort;
        return port != null ? port.position : _fromView.transform.position;
    }

    private Vector3 GetToPosition()
    {
        var port = _toView?.InputPort;
        return port != null ? port.position : _toView.transform.position;
    }

    private void LateUpdate() => UpdateLine();
}
