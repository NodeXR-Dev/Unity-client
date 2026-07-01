using UnityEngine;

// NodeBox2 양끝 홈에 장착되는 구형 연결점.
// EdgeView 가 이 transform.position 을 끝점 앵커로 사용한다.
// 기본 → Sphere_Default 머티리얼, 선택(Grabbed) → Sphere_Grabbed 머티리얼.
//
// 머티리얼 위치: Assets/03_UI/FBX/Materials/
public class ConnectorSphereView : MonoBehaviour
{
    [Tooltip("Assets/03_UI/FBX/Materials/Sphere_Default.mat")]
    [SerializeField] private Material _defaultMaterial;

    [Tooltip("Assets/03_UI/FBX/Materials/Sphere_Grabbed.mat")]
    [SerializeField] private Material _grabbedMaterial;

    private MeshRenderer _meshRenderer;
    private bool _isGrabbed;

    public bool IsGrabbed => _isGrabbed;

    private void Awake()
    {
        _meshRenderer = GetComponentInChildren<MeshRenderer>();
        ApplyMaterial();
    }

    public void SetGrabbed(bool grabbed)
    {
        if (_isGrabbed == grabbed) return;
        _isGrabbed = grabbed;
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        if (_meshRenderer == null) return;
        var mat = _isGrabbed ? _grabbedMaterial : _defaultMaterial;
        if (mat != null)
            _meshRenderer.sharedMaterial = mat;
    }
}
