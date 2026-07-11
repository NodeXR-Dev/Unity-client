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
    private string _lastSubmittedText;   // onEndEdit 중복 발화(Enter+포커스이탈) 방지용

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
        _lastSubmittedText = data.label ?? "";   // 이미 반영된 텍스트 → 변경 없는 재제출은 무시

        if (_labelInput != null)
        {
            _labelInput.text = data.label ?? "";
            _labelInput.onEndEdit.RemoveAllListeners();
            _labelInput.onEndEdit.AddListener(OnLabelSubmit);
            CenterLabelAlignment();
        }

        ApplyInitialMaterial();
    }

    // 라벨 입력칸은 키보드 직접 입력칸이다.
    // 편집 완료 시 GraphManager.RequestSubmitNodeText 로 전달한다.
    //   - 서버 미등록 노드: WS NODE_CREATE(직접 생성, 클라 node_id 그대로).
    //   - 이미 서버 등록된 노드: NODE_TEXT_UPDATE(텍스트 수정).
    //   (LLM 확장이 필요한 음성 발화는 별도 경로 RequestNodeByUtterance → /api/utterances.)
    // manager가 없으면(레거시/테스트) 로컬 label만 갱신한다.
    private void OnLabelSubmit(string newText)
    {
        if (_data == null) return;

        CenterLabelAlignment();   // 편집 사이클에서 좌측으로 어긋난 정렬을 다시 가운데로.

        string text = (newText ?? "").Trim();

        // 빈 입력은 발화하지 않는다(서버가 blank utterance 를 422로 거부, 노드도 안 생김).
        if (string.IsNullOrEmpty(text)) return;

        // onEndEdit 는 Enter + 포커스 이탈로 한 입력에 여러 번 발화한다. 같은 텍스트 중복 제출을
        // 막는다 — 안 막으면 서버에 발화가 중복 전송돼 서브그래프가 두 번 생성되고, placeholder 가
        // 응답으로 제거된 뒤 재제출되면 "존재하지 않는 node_id" 경고가 뜬다.
        if (text == _lastSubmittedText) return;
        _lastSubmittedText = text;

        if (_manager != null)
            _manager.RequestSubmitNodeText(_data.node_id, text);
        else
            _data.label = text;
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

    // TMP_InputField 는 편집 사이클을 거치면 텍스트 정렬이 좌측으로 어긋날 수 있다.
    // 표시 텍스트와 placeholder 를 항상 가운데(수평+수직 중앙) 정렬로 강제한다.
    private void CenterLabelAlignment()
    {
        if (_labelInput == null) return;
        if (_labelInput.textComponent != null)
            _labelInput.textComponent.alignment = TextAlignmentOptions.Center;
        if (_labelInput.placeholder is TMP_Text placeholder)
            placeholder.alignment = TextAlignmentOptions.Center;
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
                // 현재 깊이(_currentDepth)의 머티리얼을 적용한다 — Bind 가 재호출돼도(예: ACK rekey) 계층 색을 유지.
                // (depth 머티리얼 없으면 propertyMaterial fallback.)
                if (_depthMaterials != null && _depthMaterials.Length > 0)
                {
                    int idx = Mathf.Clamp(_currentDepth, 0, _depthMaterials.Length - 1);
                    if (_depthMaterials[idx] != null) return _depthMaterials[idx];
                }
                return _propertyMaterial;
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
