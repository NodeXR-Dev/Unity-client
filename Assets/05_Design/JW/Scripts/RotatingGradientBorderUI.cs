using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]  // ← 에디터 Play 없이도 실시간 미리보기
[RequireComponent(typeof(Image))]
public class RotatingGradientBorderUI : MonoBehaviour
{
    [Header("애니메이션")]
    [Range(0.02f, 1f)]
    public float rotationSpeed = 0.15f;

    [Header("형태")]
    public float borderWidth  = 4f;
    public float cornerRadius = 40f;

    [Header("그라디언트 (Unity Gradient 에디터 사용)")]
    public Gradient borderGradient = new Gradient();   // ← Inspector에서 직접 편집

    [Header("배경")]
    public Color bgColor = new Color(0.15f, 0.15f, 0.16f, 0.92f);

    [Header("텍스처 해상도")]
    [Range(64, 512)]
    public int gradientResolution = 256;

    // ── 내부 ─────────────────────────────────────────────
    private Material      _mat;
    private Texture2D     _gradTex;
    private RectTransform _rt;

    // 변경 감지용 캐시
    private Gradient      _cachedGradient;
    private int           _cachedResolution;

    private static readonly int ID_GradTex  = Shader.PropertyToID("_GradientTex");
    private static readonly int ID_BgColor  = Shader.PropertyToID("_BgColor");
    private static readonly int ID_Speed    = Shader.PropertyToID("_Speed");
    private static readonly int ID_Border   = Shader.PropertyToID("_BorderWidth");
    private static readonly int ID_Radius   = Shader.PropertyToID("_CornerRadius");
    private static readonly int ID_Width    = Shader.PropertyToID("_RectWidth");
    private static readonly int ID_Height   = Shader.PropertyToID("_RectHeight");

    void Awake()
    {
        Init();
    }

    void Init()
    {
        _rt = GetComponent<RectTransform>();
        var img = GetComponent<Image>();

        if (_mat == null)
        {
            _mat = new Material(Shader.Find("UI/RotatingGradientBorder"));
            img.material = _mat;
            img.color    = Color.white;
        }

        SetDefaultGradient();
        BakeGradient();
    }

    // 기본 그라디언트 (처음 컴포넌트 추가 시)
    void SetDefaultGradient()
    {
        if (borderGradient == null || borderGradient.colorKeys.Length == 0)
        {
            borderGradient = new Gradient();
            borderGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.36f, 0.13f, 1.00f), 0.0f),
                    new GradientColorKey(new Color(0.00f, 0.78f, 0.97f), 0.5f),
                    new GradientColorKey(new Color(1.00f, 0.24f, 0.71f), 1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
        }
    }

    // Gradient → Texture2D 굽기
    public void BakeGradient()
    {
        if (borderGradient == null) return;

        int res = Mathf.Max(gradientResolution, 64);

        // 해상도가 바뀌면 텍스처 재생성
        if (_gradTex == null || _gradTex.width != res)
        {
            if (_gradTex != null) DestroyImmediate(_gradTex);
            _gradTex           = new Texture2D(res, 1, TextureFormat.RGBA32, false);
            _gradTex.wrapMode  = TextureWrapMode.Repeat; // ← 회전 루프를 위해 Repeat
            _gradTex.filterMode = FilterMode.Bilinear;
        }

        // 픽셀 채우기
        for (int x = 0; x < res; x++)
        {
            float t = (float)x / (res - 1);
            _gradTex.SetPixel(x, 0, borderGradient.Evaluate(t));
        }
        _gradTex.Apply();

        if (_mat != null)
            _mat.SetTexture(ID_GradTex, _gradTex);

        // 캐시 갱신 (변경 감지용)
        _cachedGradient   = CloneGradient(borderGradient);
        _cachedResolution = res;
    }

    void Update()
    {
        if (_mat == null) { Init(); return; }

        // Inspector에서 Gradient가 변경됐는지 감지 → 재굽기
        if (!GradientEquals(borderGradient, _cachedGradient)
            || gradientResolution != _cachedResolution)
        {
            BakeGradient();
        }

        // 매 프레임 셰이더 파라미터 갱신
        Vector2 size = _rt.rect.size;
        _mat.SetFloat(ID_Speed,  rotationSpeed);
        _mat.SetFloat(ID_Border, borderWidth);
        _mat.SetFloat(ID_Radius, cornerRadius);
        _mat.SetFloat(ID_Width,  size.x);
        _mat.SetFloat(ID_Height, size.y);
        _mat.SetColor(ID_BgColor, bgColor);
    }

    // ── OnValidate: Inspector 값 변경 시 에디터에서 즉시 반영 ──
    void OnValidate()
    {
        if (_mat == null) Init();
        BakeGradient();
    }

    void OnDestroy()
    {
        if (_mat     != null) DestroyImmediate(_mat);
        if (_gradTex != null) DestroyImmediate(_gradTex);
    }

    // ── Gradient 비교 / 복사 유틸 ───────────────────────────
    static bool GradientEquals(Gradient a, Gradient b)
    {
        if (a == null || b == null) return a == b;
        if (a.colorKeys.Length != b.colorKeys.Length) return false;
        if (a.alphaKeys.Length != b.alphaKeys.Length) return false;

        for (int i = 0; i < a.colorKeys.Length; i++)
        {
            if (a.colorKeys[i].color != b.colorKeys[i].color) return false;
            if (!Mathf.Approximately(a.colorKeys[i].time, b.colorKeys[i].time)) return false;
        }
        for (int i = 0; i < a.alphaKeys.Length; i++)
        {
            if (!Mathf.Approximately(a.alphaKeys[i].alpha, b.alphaKeys[i].alpha)) return false;
            if (!Mathf.Approximately(a.alphaKeys[i].time,  b.alphaKeys[i].time))  return false;
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