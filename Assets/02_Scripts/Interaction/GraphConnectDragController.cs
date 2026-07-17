using System.Collections.Generic;
using UnityEngine;

/*
 * 파일명: GraphConnectDragController.cs
 * 목적: 파트 포트(ALL/PART)에서 속성 노드로 "드래그해서 연결". 데이터는 항상 PROPERTY→PART로 저장.
 *       GraphManager.RequestConnectFromPort 어댑터 사용(기존 그래프 코드 미변경).
 *
 * [MVP: 마우스 전용] 포인터에 가장 가까운 포트/노드를 화면 거리로 고른다(노드 콜라이더 불필요).
 *   _verbose=true 면 매 단계 진단 로그를 남긴다(무장 성공/실패·거리, 드롭 대상·거리, 연결 실패 이유).
 *   왜 안 되는지 콘솔 로그로 진단하기 위한 버전.
 */
public class GraphConnectDragController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private GraphManager _graphManager;
    [Tooltip("비우면 Camera.main")]
    [SerializeField] private Camera _camera;

    [Header("옵션")]
    [Tooltip("포트/노드를 집는 화면 반경(픽셀). 안 잡히면 키워보세요.")]
    [SerializeField] private float _pickRadiusPixels = 140f;
    [SerializeField] private float _lineWidth = 0.01f;
    [SerializeField] private Color _lineColor = new Color(1f, 0.9f, 0.2f);
    [Tooltip("연결 성공 후 남는 영구 연결선 색(초록).")]
    [SerializeField] private Color _connectedColor = new Color(0.2f, 0.85f, 0.3f);

    [Header("진단")]
    [Tooltip("켜면 매 단계 콘솔 로그. 문제 진단용.")]
    [SerializeField] private bool _verbose = true;

    private string _armedPortId;
    private Transform _armedPortTr;
    private LineRenderer _line;
    private bool _dragging;

    // 연결 성공 시 남는 영구 연결선 목록(포트 id/노드 id/캐시 Transform/선 렌더러).
    private readonly List<ConnectedLink> _links = new List<ConnectedLink>();

    // 영구 연결선 한 가닥의 상태.
    private class ConnectedLink
    {
        public string portNodeId;
        public string nodeNodeId;
        public Transform portTr;   // 캐시된 포트 Transform(끊기면 id로 재탐색)
        public Transform nodeTr;   // 캐시된 노드 Transform(끊기면 id로 재탐색)
        public LineRenderer line;
    }

    private Camera Cam => _camera != null ? _camera : Camera.main;

    private void Awake()
    {
        if (_graphManager == null) _graphManager = FindFirstObjectByType<GraphManager>();

        var go = new GameObject("ConnectTempLine");
        go.transform.SetParent(transform, false);
        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.widthMultiplier = _lineWidth;
        _line.numCapVertices = 4;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) _line.material = new Material(sh);
        _line.startColor = _line.endColor = _lineColor;
        _line.enabled = false;

        if (_verbose)
        {
            if (_graphManager == null) Debug.LogWarning("[Connect] GraphManager를 못 찾음 — 연결 불가.");
            if (Cam == null) Debug.LogWarning("[Connect] Camera.main이 없음(카메라에 MainCamera 태그 필요) — 픽 불가.");
        }
    }

    private void Update()
    {
        if (Cam == null || _graphManager == null) return;

        Vector3 ptr = Input.mousePosition;

        if (Input.GetMouseButtonDown(0)) TryArm(ptr);
        else if (_dragging && Input.GetMouseButton(0)) DrawLine(ptr);
        else if (_dragging && Input.GetMouseButtonUp(0)) { TryDrop(ptr); Disarm(); }
    }

    // 영구 연결선들의 양끝을 매 프레임 갱신(노드가 움직여도 따라오게).
    // 패널 새로고침이 PartPort를 파괴·재생성하면 캐시 Transform이 null → id로 재탐색.
    private void LateUpdate()
    {
        if (_links.Count == 0) return;

        foreach (var link in _links)
        {
            if (link.line == null) continue;

            if (link.portTr == null) link.portTr = FindPortTransform(link.portNodeId);
            if (link.nodeTr == null) link.nodeTr = FindNodeTransform(link.nodeNodeId);

            if (link.portTr == null || link.nodeTr == null)
            {
                link.line.enabled = false;   // 한쪽이라도 못 찾으면 이 선은 숨김
                continue;
            }

            link.line.enabled = true;
            link.line.SetPosition(0, link.portTr.position);
            link.line.SetPosition(1, link.nodeTr.position);
        }
    }

    // 눌린 지점 근처에서 가장 가까운 포트(ALL/PART)를 무장.
    private void TryArm(Vector3 ptr)
    {
        Transform best = null; string bestId = null; float bestD = float.MaxValue;
        int portCount = 0;

        foreach (var pp in FindObjectsByType<PartPort>(FindObjectsSortMode.None))
        { portCount++; Consider(pp.transform, pp.NodeId, ptr, ref best, ref bestId, ref bestD); }
        foreach (var ap in FindObjectsByType<AllPort>(FindObjectsSortMode.None))
        { portCount++; Consider(ap.transform, ap.NodeId, ptr, ref best, ref bestId, ref bestD); }

        if (best != null && bestD <= _pickRadiusPixels)
        {
            _armedPortTr = best;
            _armedPortId = bestId;
            _dragging = true;
            _line.enabled = true;
            DrawLine(ptr);
            if (_verbose) Debug.Log($"[Connect] 무장: 포트 {Short(bestId)} (거리 {bestD:F0}px). 이제 속성 노드로 드래그하세요.");
        }
        else if (_verbose)
        {
            string near = best != null ? $"가장 가까운 포트 {bestD:F0}px" : "포트 없음";
            Debug.Log($"[Connect] 무장 실패: 반경 {_pickRadiusPixels:F0}px 안에 포트 없음 ({near}, 씬 포트 {portCount}개). " +
                      "포트를 더 정확히 누르거나 Pick Radius를 키우세요.");
        }
    }

    private void Consider(Transform tr, string id, Vector3 ptr,
        ref Transform best, ref string bestId, ref float bestD)
    {
        if (tr == null || string.IsNullOrEmpty(id)) return;
        Vector3 sp = Cam.WorldToScreenPoint(tr.position);
        if (sp.z <= 0f) return;
        float d = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(ptr.x, ptr.y));
        if (d < bestD) { bestD = d; best = tr; bestId = id; }
    }

    private void DrawLine(Vector3 ptr)
    {
        if (_armedPortTr == null) return;
        _line.SetPosition(0, _armedPortTr.position);
        _line.SetPosition(1, PointerWorld(ptr, _armedPortTr.position));
    }

    private Vector3 PointerWorld(Vector3 ptr, Vector3 refWorld)
    {
        var cam = Cam;
        var plane = new Plane(-cam.transform.forward, refWorld);
        var ray = cam.ScreenPointToRay(ptr);
        return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : refWorld;
    }

    // 놓은 지점 근처에서 가장 가까운 속성 노드를 찾아 연결.
    private void TryDrop(Vector3 ptr)
    {
        NodeView best = null; float bestD = float.MaxValue; int nodeCount = 0;

        foreach (var nv in FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            nodeCount++;
            if (string.IsNullOrEmpty(nv.NodeId) || nv.NodeId == _armedPortId) continue;
            Vector3 sp = Cam.WorldToScreenPoint(nv.transform.position);
            if (sp.z <= 0f) continue;
            float d = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(ptr.x, ptr.y));
            if (d < bestD) { bestD = d; best = nv; }
        }

        if (best == null || bestD > _pickRadiusPixels)
        {
            string near = best != null ? $"가장 가까운 노드 {bestD:F0}px" : "노드 없음";
            Debug.Log($"[Connect] 드롭 실패: 반경 {_pickRadiusPixels:F0}px 안에 속성 노드 없음 ({near}, 씬 노드 {nodeCount}개).");
            return;
        }

        // 왜 실패하는지 이유까지 로그: CanConnect(속성, 파트) — 데이터 방향은 PROPERTY→PART.
        if (_graphManager.CanConnect(best.NodeId, _armedPortId, out string reason))
        {
            bool ok = _graphManager.RequestConnectFromPort(_armedPortId, best.NodeId);
            Debug.Log($"[Connect] 포트 {Short(_armedPortId)} → 노드 {Short(best.NodeId)} : {(ok ? "연결됨 ✅" : "AddEdge 실패")} (거리 {bestD:F0}px)");

            if (ok)
            {
                // 성공 시각 피드백: (1) 영구 초록선 추가, (2) 파트 포트가 "연결됨" 스프라이트로 바뀌도록 패널 새로고침.
                AddConnectedLine(_armedPortId, _armedPortTr, best.NodeId, best.transform);
                FindFirstObjectByType<MainSketchView>()?.Refresh();
            }
        }
        else
        {
            Debug.Log($"[Connect] 연결 거부: {reason}  (포트 {Short(_armedPortId)} ↔ 노드 {Short(best.NodeId)}). " +
                      "규칙: 속성→파트만 허용, 자기 부모/중복/사이클은 금지. 다른 조합으로 해보세요.");
        }
    }

    private void Disarm()
    {
        _dragging = false;
        _armedPortId = null;
        _armedPortTr = null;
        if (_line != null) _line.enabled = false;
    }

    // 연결 성공 시 포트→노드 사이에 남는 영구 초록선을 만든다(드래그 임시선과 별개).
    private void AddConnectedLine(string portId, Transform portTr, string nodeId, Transform nodeTr)
    {
        var go = new GameObject($"ConnectedLine_{Short(portId)}_{Short(nodeId)}");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = _lineWidth;
        lr.numCapVertices = 4;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) lr.material = new Material(sh);
        lr.startColor = lr.endColor = _connectedColor;
        if (portTr != null) lr.SetPosition(0, portTr.position);
        if (nodeTr != null) lr.SetPosition(1, nodeTr.position);

        _links.Add(new ConnectedLink
        {
            portNodeId = portId,
            nodeNodeId = nodeId,
            portTr = portTr,
            nodeTr = nodeTr,
            line = lr
        });
    }

    // 포트 id로 Transform 재탐색(PART 먼저, 없으면 ALL). 없으면 null.
    private Transform FindPortTransform(string portNodeId)
    {
        if (string.IsNullOrEmpty(portNodeId)) return null;
        foreach (var pp in FindObjectsByType<PartPort>(FindObjectsSortMode.None))
            if (pp.NodeId == portNodeId) return pp.transform;
        foreach (var ap in FindObjectsByType<AllPort>(FindObjectsSortMode.None))
            if (ap.NodeId == portNodeId) return ap.transform;
        return null;
    }

    // 노드 id로 Transform 재탐색. 없으면 null.
    private Transform FindNodeTransform(string nodeNodeId)
    {
        if (string.IsNullOrEmpty(nodeNodeId)) return null;
        foreach (var nv in FindObjectsByType<NodeView>(FindObjectsSortMode.None))
            if (nv.NodeId == nodeNodeId) return nv.transform;
        return null;
    }

    private static string Short(string id) =>
        string.IsNullOrEmpty(id) ? "(null)" : (id.Length > 8 ? id.Substring(0, 8) : id);
}
