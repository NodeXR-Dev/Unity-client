using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class RotatingGradientBorderUI : MonoBehaviour
{
    [Header("셰이더")]
    public Shader shader;   // 비워두면 Shader.Find 로 탐색 (빌드 스트리핑 방지용으로 직접 넣는 걸 권장)

    [Header("애니메이션")]
    [Range(0.02f, 1f)]
    public float rotationSpeed = 0.15f;

    [Header("형태")]
    [Min(0f)] public float borderWidth  = 4f;
    [Min(0f)] public float cornerRadius = 40f;

    [Header("테두리 그라디언트")]
    public Gradient borderGradient = new Gradient();

    [Header("배경 그라디언트")]
    public Gradient bgGradient = new Gradient();
    [Tooltip("0 = 왼→오른쪽, 90 = 아래→위, 시계 반대 방향")]
    [Range(0f, 360f)] public float bgAngle = 0f;

    [Header("텍스처 해상도")]
    [Range(64, 512)]
    public int gradientResolution = 256;

    // ── 내부 ─────────────────────────────────────────────
    private Material      _mat;
    private Texture2D     _gradTex;     // 테두리용
    private Texture2D     _bgTex;       // 배경용
    private RectTransform _rt;

    // 변경 감지용 캐시
    private Gradient _cachedBorder;
    private Gradient _cachedBg;
    private int      _cachedResolution;

    private static readonly int ID_GradTex = Shader.PropertyToID("_GradientTex");
    private static readonly int ID_BgTex   = Shader.PropertyToID("_BgGradientTex");
    private static readonly int ID_BgAngle = Shader.PropertyToID("_BgAngle");
    private static readonly int ID_Speed   = Shader.PropertyToID("_Speed");
    private static readonly int ID_Border  = Shader.PropertyToID("_BorderWidth");
    private static readonly int ID_Radius  = Shader.PropertyToID("_CornerRadius");
    private static readonly int ID_Width   = Shader.PropertyToID("_RectWidth");
    private static readonly int ID_Height  = Shader.PropertyToID("_RectHeight");

    // ExecuteAlways 에서는 도메인 리로드 후 Awake 가 다시 안 불리므로 OnEnable 사용
    void OnEnable() => Init();

    void Init()
    {
        _rt = GetComponent<RectTransform>();
        var img = GetComponent<Image>();

        if (_mat == null)
        {
            var sh = shader != null ? shader : Shader.Find("UI/RotatingGradientBorder");
            if (sh == null)
            {
                Debug.LogError("UI/RotatingGradientBorder 셰이더를 찾을 수 없습니다.", this);
                return;
            }
            // DontSave: 씬/프리팹에 임시 머티리얼이 저장되는 걸 막음
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (img.material != _mat) img.material = _mat;
        img.color = Color.white;      // 틴트는 셰이더가 처리
        img.type  = Image.Type.Simple; // Sliced 면 UV 가 비선형이라 SDF 가 깨짐

        SetDefaultGradients();
        BakeAll();
    }

    // 기본 그라디언트 (처음 컴포넌트 추가 시)
    void SetDefaultGradients()
    {
        if (borderGradient == null || borderGradient.colorKeys.Length == 0)
        {
            borderGradient = new Gradient();
            borderGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.42f, 0.39f, 0.31f), 0.00f),
                    new GradientColorKey(new Color(0.95f, 0.89f, 0.74f), 0.35f),
                    new GradientColorKey(new Color(0.79f, 0.68f, 0.49f), 0.70f),
                    new GradientColorKey(new Color(0.42f, 0.39f, 0.31f), 1.00f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
        }

        if (bgGradient == null || bgGradient.colorKeys.Length == 0)
        {
            bgGradient = new Gradient();
            bgGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.55f, 0.53f, 0.48f), 0.0f),
                    new GradientColorKey(new Color(0.64f, 0.58f, 0.47f), 0.5f),
                    new GradientColorKey(new Color(0.54f, 0.52f, 0.47f), 1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.92f, 0f),
                    new GradientAlphaKey(0.92f, 1f),
                }
            );
        }
    }

    // Gradient → Texture2D 굽기
    void Bake(Gradient g, ref Texture2D tex, TextureWrapMode wrap, int propId)
    {
        if (g == null) return;

        int res = Mathf.Max(gradientResolution, 64);

        // 해상도가 바뀌면 텍스처 재생성
        if (tex == null || tex.width != res)
        {
            SafeDestroy(tex);
            tex = new Texture2D(res, 1, TextureFormat.RGBA32, false)
            {
                wrapMode   = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave
            };
        }

        var px  = new Color32[res];
        float inv = 1f / (res - 1);
        for (int x = 0; x < res; x++)
            px[x] = g.Evaluate(x * inv);

        tex.SetPixels32(px);
        tex.Apply(false);

        if (_mat != null) _mat.SetTexture(propId, tex);
    }

    public void BakeAll()
    {
        // 테두리는 회전 루프를 위해 Repeat, 배경은 한 방향 선형이라 Clamp
        Bake(borderGradient, ref _gradTex, TextureWrapMode.Repeat, ID_GradTex);
        Bake(bgGradient,     ref _bgTex,   TextureWrapMode.Clamp,  ID_BgTex);

        _cachedBorder     = CloneGradient(borderGradient);
        _cachedBg         = CloneGradient(bgGradient);
        _cachedResolution = Mathf.Max(gradientResolution, 64);
    }

    void Update()
    {
        if (_mat == null) { Init(); return; }
        if (_rt  == null) _rt = GetComponent<RectTransform>();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // colorKeys 접근은 매번 배열을 할당하므로 편집 중에만 비교
            if (!GradientEquals(borderGradient, _cachedBorder)
                || !GradientEquals(bgGradient, _cachedBg)
                || gradientResolution != _cachedResolution)
            {
                BakeAll();
            }
            // Play 없이도 _Time 이 흐르도록 에디터 루프를 돌림
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        Vector2 size = _rt.rect.size;
        _mat.SetFloat(ID_Speed,   rotationSpeed);
        _mat.SetFloat(ID_Border,  borderWidth);
        _mat.SetFloat(ID_Radius,  cornerRadius);
        _mat.SetFloat(ID_Width,   size.x);
        _mat.SetFloat(ID_Height,  size.y);
        _mat.SetFloat(ID_BgAngle, bgAngle);
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        // OnValidate 안에서 Material/Texture 를 만들면 Unity 가 경고를 뱉으므로 다음 틱으로 미룸
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Init();
            BakeAll();
        };
#endif
    }

    void OnDisable()
    {
        SafeDestroy(_mat);
        SafeDestroy(_gradTex);
        SafeDestroy(_bgTex);
        _mat = null; _gradTex = null; _bgTex = null;
    }

    // ── 유틸 ────────────────────────────────────────────
    static void SafeDestroy(UnityEngine.Object o)
    {
        if (o == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
        Destroy(o);
    }

    static bool GradientEquals(Gradient a, Gradient b)
    {
        if (a == null || b == null) return a == b;

        var ac = a.colorKeys; var bc = b.colorKeys;
        var aa = a.alphaKeys; var ba = b.alphaKeys;
        if (ac.Length != bc.Length || aa.Length != ba.Length) return false;

        for (int i = 0; i < ac.Length; i++)
        {
            if (ac[i].color != bc[i].color) return false;
            if (!Mathf.Approximately(ac[i].time, bc[i].time)) return false;
        }
        for (int i = 0; i < aa.Length; i++)
        {
            if (!Mathf.Approximately(aa[i].alpha, ba[i].alpha)) return false;
            if (!Mathf.Approximately(aa[i].time,  ba[i].time))  return false;
        }
        return true;
    }

    static Gradient CloneGradient(Gradient src)
    {
        var g = new Gradient();
        g.SetKeys(src.colorKeys, src.alphaKeys);
        g.mode = src.mode;
        return g;
    }
}