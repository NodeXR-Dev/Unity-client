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

    // 활성(grabbed) 구체가 화면에서 내는 색.
    // 연결선을 이 색으로 그리면 선과 구체가 완전히 같은 초록이 된다
    // — 값을 코드에 복사해 두면 머티리얼을 손볼 때마다 어긋난다.
    public static Color GrabbedColor(Material material)
    {
        if (material == null) return new Color(0.51806414f, 1f, 0.495283f, 1f);

        // 발광(Emission)이 실제로 보이는 밝기를 좌우한다. 없으면 기본 색을 쓴다.
        if (material.HasProperty("_EmissionColor"))
        {
            Color emission = material.GetColor("_EmissionColor");
            if (emission.maxColorComponent > 0.01f)
                return new Color(emission.r, emission.g, emission.b, 1f);
        }
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return new Color(0.51806414f, 1f, 0.495283f, 1f);
    }

    // 이 구체의 활성 색(위 규칙을 자기 머티리얼에 적용한 값).
    public Color ActiveColor => GrabbedColor(_grabbedMaterial);

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
