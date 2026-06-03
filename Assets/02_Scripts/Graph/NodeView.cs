/*
 * 파일명: NodeView.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: NodeData를 3D 월드 공간 노드 오브젝트에 바인딩한다.
 * 핵심 내용:
 * - 라벨은 TMP_Text 베이스로 받아 TextMeshPro(3D)와 TextMeshProUGUI(UGUI) 모두 호환한다.
 * - NodeType별 머티리얼 슬롯이 연결되어 있으면 sharedMaterial 교체를 우선 사용하고,
 *   비어 있으면 기존 NodeType 기본 색상 fallback으로 동작한다.
 * - PART + is_global == true 는 ALL로 보고 _allMaterial을 사용한다.
 * - InputPort / OutputPort 자식 Transform은 EdgeView가 끝점 결정 시 우선 사용한다.
 * - 선택/호버 상태 메서드는 stub으로 둔다. 추후 개발자 2의 XR 입력과 연결 예정.
 *
 * 프리팹 구조 (01_Prefabs/Graph/NodePrefab):
 *   NodeRoot
 *   ├── Sphere (MeshRenderer + MeshFilter)  ← _meshRenderer 연결
 *   ├── Label (TextMeshPro 3D)              ← _labelText 연결
 *   ├── SphereCollider                      ← XR Ray/Hand 인터랙션용
 *   └── NodeView (이 컴포넌트)
 *
 *   MVP에서는 PART / PROPERTY / REFERENCE가 같은 프리팹을 공유하고,
 *   Bind() 시 NodeType에 따라 색상으로 구분한다. 추후 타입별 프리팹으로 분리 가능.
 */
using TMPro;
using UnityEngine;

public class NodeView : MonoBehaviour
{
    [SerializeField] private TMP_Text _labelText;
    [SerializeField] private MeshRenderer _meshRenderer;

    [Header("포트 앵커 (선택)")]
    [Tooltip("엣지 끝점으로 사용할 입력 포트. 미연결 시 EdgeView가 transform.position을 fallback으로 사용한다.")]
    [SerializeField] private Transform _inputPort;
    [Tooltip("엣지 시작점으로 사용할 출력 포트. 미연결 시 EdgeView가 transform.position을 fallback으로 사용한다.")]
    [SerializeField] private Transform _outputPort;

    [Header("NodeType 머티리얼 (선택, 우선 적용)")]
    [Tooltip("PART + is_global == true (ALL) 용. 보통 BOX_00.")]
    [SerializeField] private Material _allMaterial;
    [Tooltip("일반 PART 용. 보통 BOX_01.")]
    [SerializeField] private Material _partMaterial;
    [Tooltip("PROPERTY 용. 보통 BOX_02.")]
    [SerializeField] private Material _propertyMaterial;
    [Tooltip("REFERENCE 용. 보통 BOX_03.")]
    [SerializeField] private Material _referenceMaterial;
    [Tooltip("UNKNOWN 또는 예비. 비워두면 색상 fallback이 사용된다.")]
    [SerializeField] private Material _unknownMaterial;

    [Header("NodeType 기본 색상")]
    [SerializeField] private Color _partColor      = Color.blue;
    [SerializeField] private Color _propertyColor  = Color.green;
    [SerializeField] private Color _referenceColor = new Color(1f, 0.5f, 0f); // 주황
    [SerializeField] private Color _unknownColor   = Color.gray;

    private NodeData _data;

    public string NodeId => _data?.node_id;

    // EdgeView가 끝점 결정 시 사용. null이면 호출 측에서 transform.position fallback.
    public Transform InputPort  => _inputPort;
    public Transform OutputPort => _outputPort;

    public void Bind(NodeData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[NodeView] Bind 실패: data가 null입니다.");
            return;
        }
        _data = data;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_labelText != null)
            _labelText.text = _data.DisplayText;

        if (_meshRenderer == null) return;

        var mat = ResolveMaterial(_data);
        if (mat != null)
        {
            // sharedMaterial 교체: 머티리얼 인스턴스를 새로 만들지 않으므로 batching 친화적.
            _meshRenderer.sharedMaterial = mat;
        }
        else
        {
            // 머티리얼 슬롯이 비어 있으면 색상 fallback (기존 Mock NodePrefab 동작 유지).
            // _meshRenderer.material 접근은 런타임에 material instance를 생성한다. MVP 허용 범위.
            _meshRenderer.material.color = GetColorByType(_data.NodeType);
        }
    }

    private Color GetColorByType(NodeType nodeType)
    {
        switch (nodeType)
        {
            case NodeType.PART:      return _partColor;
            case NodeType.PROPERTY:  return _propertyColor;
            case NodeType.REFERENCE: return _referenceColor;
            default:                 return _unknownColor;
        }
    }

    private Material ResolveMaterial(NodeData data)
    {
        switch (data.NodeType)
        {
            case NodeType.PART:
                // PART + is_global == true 는 ALL로 본다.
                return (data.is_global && _allMaterial != null) ? _allMaterial : _partMaterial;
            case NodeType.PROPERTY:  return _propertyMaterial;
            case NodeType.REFERENCE: return _referenceMaterial;
            default:                 return _unknownMaterial;
        }
    }

    // TODO: 추후 개발자 2의 XR 입력(선택 이벤트)과 연결 예정
    public void SetSelected(bool selected)
    {
        // TODO: 선택 상태 하이라이트 처리 추가 예정
    }

    // TODO: 추후 개발자 2의 XR 입력(호버 이벤트)과 연결 예정
    public void SetHover(bool hovered)
    {
        // TODO: 호버 상태 하이라이트 처리 추가 예정
    }
}
