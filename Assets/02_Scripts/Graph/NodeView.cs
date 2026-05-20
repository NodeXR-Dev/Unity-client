/*
 * 파일명: NodeView.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: NodeData를 3D 월드 공간 노드 오브젝트에 바인딩한다.
 * 핵심 내용:
 * - TextMeshPro 3D로 label을 표시한다.
 * - NodeType에 따라 기본 색상을 구분한다.
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
    [SerializeField] private TextMeshPro _labelText;
    [SerializeField] private MeshRenderer _meshRenderer;

    [Header("NodeType 기본 색상")]
    [SerializeField] private Color _partColor      = Color.blue;
    [SerializeField] private Color _propertyColor  = Color.green;
    [SerializeField] private Color _referenceColor = new Color(1f, 0.5f, 0f); // 주황
    [SerializeField] private Color _unknownColor   = Color.gray;

    private NodeData _data;

    public string NodeId => _data?.node_id;

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

        if (_meshRenderer != null)
        {
            // _meshRenderer.material.color를 직접 변경하면 런타임에 material instance가 생성된다.
            // MVP에서는 허용하며, 추후 draw call 최적화가 필요할 경우 MaterialPropertyBlock으로 교체를 고려한다.
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
