using TMPro;
using UnityEngine;
using UnityEngine.UI;

// NodeData 1개를 3D 월드 공간에 시각화한다.
// PROPERTY 노드 전용 프리팹(NodeView_Sub)과 레거시 NodePrefab 모두 지원한다.
//
// 프리팹 구조 (01_Prefabs/Graph/Designer/NodeView_Sub):
//   NodeView_Sub
//   ├── NodeBox2             ← _meshRenderer 연결
//   ├── InputSphere          ← _inputSphere  (ConnectorSphereView)
//   ├── OutputSphere         ← _outputSphere (ConnectorSphereView)
//   ├── LabelCanvas
//   │     └── LabelInput     ← _labelInput (TMP_InputField, 인라인 편집)
//   └── NodeActionPanel      ← _actionPanel
public class NodeView : MonoBehaviour
{
    [SerializeField] private TMP_InputField _labelInput;   // 인라인 편집용 (LabelCanvas 아래)
    [SerializeField] private MeshRenderer   _meshRenderer;

    [Header("ConnectorSphere 포트 (우선)")]
    [SerializeField] private ConnectorSphereView _inputSphere;
    [SerializeField] private ConnectorSphereView _outputSphere;

    [Header("레거시 포트 앵커 (ConnectorSphere 없을 때 fallback)")]
    [SerializeField] private Transform _inputPort;
    [SerializeField] private Transform _outputPort;

    [Header("액션 패널")]
    [SerializeField] private NodeActionPanel _actionPanel;

    [Header("깊이별 머티리얼 (index = depth, Box_00~04)")]
    [SerializeField] private Material[] _depthMaterials;

    [Header("타입별 머티리얼 (레거시 fallback)")]
    [SerializeField] private Material _allMaterial;
    [SerializeField] private Material _partMaterial;
    [SerializeField] private Material _propertyMaterial;
    [SerializeField] private Material _referenceMaterial;
    [SerializeField] private Material _unknownMaterial;

    [Header("기본 색상 (머티리얼 없을 때 최종 fallback)")]
    [SerializeField] private Color _partColor      = Color.blue;
    [SerializeField] private Color _propertyColor  = Color.green;
    [SerializeField] private Color _referenceColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color _unknownColor   = Color.gray;

    private NodeData _data;
    private GraphManager _manager;
    private int _currentDepth;

    public string NodeId           => _data?.node_id;
    public int    CurrentDepth     => _currentDepth;
    public NodeActionPanel ActionPanel => _actionPanel;

    // EdgeView 가 끝점 앵커로 사용. ConnectorSphere 우선, 없으면 레거시 Transform.
    public Transform InputPort  => _inputSphere  != null ? _inputSphere.transform  : _inputPort;
    public Transform OutputPort => _outputSphere != null ? _outputSphere.transform : _outputPort;

    public void Bind(NodeData data, GraphManager manager = null)
    {
        if (data == null)
        {
            Debug.LogWarning("[NodeView] Bind 실패: data가 null입니다.");
            return;
        }
        _data = data;
        _manager = manager;

        if (_labelInput != null)
        {
            _labelInput.text = data.label ?? "";
            _labelInput.onEndEdit.RemoveAllListeners();
            _labelInput.onEndEdit.AddListener(OnLabelSubmit);
        }

        ApplyInitialMaterial();
    }

    // 라벨 입력칸은 키보드/음성 공용 발화 입력칸이다.
    // 편집 완료 시 텍스트를 노드에 로컬 반영하고(오프라인 즉시), 서버 utterance 확장을 요청한다.
    //   → GraphManager.RequestNodeByUtterance: node.label/node_text 로컬 반영 + OnUtteranceNodeRequested 발행.
    //     UtteranceApiClient가 POST /api/utterances(parent=이 노드) 후 하위 그래프를 병합한다.
    // manager가 없으면(레거시/테스트) 로컬 label만 갱신한다.
    private void OnLabelSubmit(string newText)
    {
        if (_data == null) return;

        if (_manager != null)
            _manager.RequestNodeByUtterance(_data.node_id, newText);
        else
            _data.label = newText.Trim();
    }

    // ReflowAllSubtrees 완료 후 GraphManager 가 호출한다.
    // PROPERTY 노드에만 깊이별 머티리얼을 적용한다.
    public void SetDepth(int depth)
    {
        _currentDepth = depth;
        if (_data?.NodeType != NodeType.PROPERTY) return;
        if (_depthMaterials == null || _depthMaterials.Length == 0) return;

        int idx = Mathf.Clamp(depth, 0, _depthMaterials.Length - 1);
        if (_meshRenderer != null && _depthMaterials[idx] != null)
            _meshRenderer.sharedMaterial = _depthMaterials[idx];
    }

    private void ApplyInitialMaterial()
    {
        if (_meshRenderer == null) return;
        var mat = ResolveInitialMaterial();
        if (mat != null)
            _meshRenderer.sharedMaterial = mat;
        else
            _meshRenderer.material.color = GetFallbackColor();
    }

    private Material ResolveInitialMaterial()
    {
        if (_data == null) return _unknownMaterial;
        switch (_data.NodeType)
        {
            case NodeType.PART:
                return (_data.is_global && _allMaterial != null) ? _allMaterial : _partMaterial;
            case NodeType.PROPERTY:
                // depth 머티리얼이 있으면 depth=0 을 초기값으로, 없으면 propertyMaterial
                return (_depthMaterials != null && _depthMaterials.Length > 0 && _depthMaterials[0] != null)
                    ? _depthMaterials[0]
                    : _propertyMaterial;
            case NodeType.REFERENCE:
                return _referenceMaterial;
            default:
                return _unknownMaterial;
        }
    }

    private Color GetFallbackColor()
    {
        if (_data == null) return _unknownColor;
        switch (_data.NodeType)
        {
            case NodeType.PART:      return _partColor;
            case NodeType.PROPERTY:  return _propertyColor;
            case NodeType.REFERENCE: return _referenceColor;
            default:                 return _unknownColor;
        }
    }

    // 개발자 2의 XR 입력과 연결 예정
    public void SetSelected(bool selected) { }
    public void SetHover(bool hovered)     { }
}
