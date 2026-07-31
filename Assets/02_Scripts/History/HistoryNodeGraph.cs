using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타임라인 예시 시각화: 중심에서 노드가 뻗어나가는 그래프.
/// 타임라인 값(0~1)에 따라 과거→현재로 노드가 하나씩 중심에서 자라나며 퍼진다.
///  - 0 (과거): 중심 노드만
///  - 1 (현재): 전체 그래프
/// 좌우로 스크럽하면 그래프가 자라고/줄어들며 "어떻게 변해왔는지"를 보여준다.
///
/// 사용법:
///  1) 빈 GameObject에 이 스크립트를 붙인다. 이 오브젝트 위치가 그래프의 중심이 된다.
///  2) HistoryTimeline을 만들어 Timeline 슬롯에 연결한다.
///     (연결 안 하면 아래 Manual Scrub 값으로 인스펙터에서 미리볼 수 있다.)
///  3) 플레이 후 타임라인을 좌우로 움직이면 그래프가 변한다.
///
/// 노드 색/모양은 코드에서 자동 생성되므로 별도 프리팹/머티리얼이 없어도 동작한다.
/// </summary>
public class HistoryNodeGraph : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("비우면 아래 Manual Scrub 값으로 동작한다 (테스트용).")]
    public HistoryTimeline timeline;
    [Range(0f, 1f)] public float manualScrub = 1f;

    [Header("그래프 생성")]
    [Tooltip("중심 포함 전체 노드 수")]
    public int nodeCount = 24;
    [Tooltip("같은 seed면 항상 같은 모양이 나온다")]
    public int seed = 1234;
    [Tooltip("세대(깊이)마다 중심에서 멀어지는 거리(m)")]
    public float radiusStep = 0.22f;
    [Tooltip("가지가 퍼지는 각도 흔들림(도)")]
    public float angleJitter = 40f;
    [Tooltip("각 노드가 부모에서 자기 자리까지 자라는 데 걸리는 정규화 시간")]
    [Range(0.01f, 0.5f)] public float growDuration = 0.07f;

    [Header("외형")]
    public float nodeScale = 0.05f;
    public float edgeWidth = 0.008f;
    [Tooltip("과거→현재 색")]
    public Gradient timeColor = DefaultGradient();

    private class Node
    {
        public int parent;       // 부모 노드 인덱스(-1 = 중심/루트)
        public Vector3 target;   // 최종 로컬 위치
        public float birth;      // 0~1 등장 시점
        public Color color;
        public Transform tr;
        public Renderer rend;
        public LineRenderer edge;
    }

    private readonly List<Node> _nodes = new();
    private Material _nodeMat;
    private Material _edgeMat;
    private float _lastApplied = float.NaN;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Start()
    {
        Build();
        if (timeline != null) timeline.OnChanged += Apply;
        Apply(CurrentValue());
    }

    void OnDestroy()
    {
        if (timeline != null) timeline.OnChanged -= Apply;
    }

    void Update()
    {
        // 타임라인이 없으면 Manual Scrub 값으로 미리보기
        if (timeline == null) Apply(manualScrub);
    }

    private float CurrentValue() => timeline != null ? timeline.normalized : manualScrub;

    [ContextMenu("Rebuild")]
    private void Rebuild()
    {
        Build();
        Apply(CurrentValue());
    }

    // --- 그래프 구조 생성 ---
    private void Build()
    {
        foreach (var n in _nodes)
            if (n.tr != null) DestroySafe(n.tr.gameObject);
        _nodes.Clear();

        EnsureMaterials();

        int count = Mathf.Max(1, nodeCount);
        var rng = new System.Random(seed);
        var radius = new float[count];
        var angle = new float[count];

        for (int i = 0; i < count; i++)
        {
            var node = new Node();
            if (i == 0)
            {
                node.parent = -1;
                node.target = Vector3.zero;
                node.birth = 0f;
                radius[0] = 0f;
                angle[0] = 0f;
            }
            else
            {
                node.parent = rng.Next(0, i);             // 이미 만들어진 노드 중 하나에 연결
                node.birth = i / (float)(count - 1);      // 인덱스 순서대로 등장(과거→현재)

                // 중심에서 뻗는 첫 가지는 자유 각도, 그 이후는 부모 방향을 이어받아 퍼진다
                float baseAngle = (radius[node.parent] < 0.001f)
                    ? (float)(rng.NextDouble() * 360.0)
                    : angle[node.parent];
                float a = baseAngle + (float)(rng.NextDouble() * 2.0 - 1.0) * angleJitter;
                float r = radius[node.parent] + radiusStep;
                radius[i] = r;
                angle[i] = a;

                float rad = a * Mathf.Deg2Rad;
                node.target = new Vector3(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r, 0f);
            }

            node.color = timeColor.Evaluate(node.birth);
            CreateVisual(node, i);
            _nodes.Add(node);
        }

        _lastApplied = float.NaN;
    }

    private void CreateVisual(Node node, int index)
    {
        // 노드 = 구체
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"Node_{index}";
        var col = go.GetComponent<Collider>();
        if (col != null) DestroySafe(col); // VR 레이 간섭 방지

        node.tr = go.transform;
        node.tr.SetParent(transform, false);
        node.tr.localPosition = node.target;
        node.tr.localScale = Vector3.zero;

        node.rend = go.GetComponent<Renderer>();
        node.rend.sharedMaterial = _nodeMat;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(BaseColorId, node.color); // URP Lit
        mpb.SetColor(ColorId, node.color);     // Standard 폴백
        node.rend.SetPropertyBlock(mpb);

        // 엣지 = 부모와 잇는 선
        if (node.parent != -1)
        {
            var eGo = new GameObject($"Edge_{index}");
            eGo.transform.SetParent(transform, false);
            var lr = eGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; // 그래프 로컬 좌표 사용
            lr.sharedMaterial = _edgeMat;
            lr.widthMultiplier = edgeWidth;
            lr.numCapVertices = 2;
            lr.positionCount = 2;
            lr.startColor = lr.endColor = node.color;
            node.edge = lr;
        }
    }

    // --- 타임라인 값에 맞춰 그래프 갱신 ---
    private void Apply(float t)
    {
        t = Mathf.Clamp01(t);
        if (t.Equals(_lastApplied)) return;
        _lastApplied = t;

        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            if (n.tr == null) continue;

            // 아직 등장 전
            if (t < n.birth)
            {
                if (n.tr.gameObject.activeSelf) n.tr.gameObject.SetActive(false);
                if (n.edge != null && n.edge.gameObject.activeSelf) n.edge.gameObject.SetActive(false);
                continue;
            }

            if (!n.tr.gameObject.activeSelf) n.tr.gameObject.SetActive(true);

            // 부모에서 자기 자리까지 자라나는 정도 (0~1)
            float g = (n.parent == -1)
                ? 1f
                : Mathf.Clamp01((t - n.birth) / Mathf.Max(0.0001f, growDuration));

            // 부모의 현재 위치에서 출발 (부모는 인덱스가 작아 이미 갱신됨)
            Vector3 from = (n.parent == -1) ? Vector3.zero : _nodes[n.parent].tr.localPosition;
            Vector3 pos = Vector3.Lerp(from, n.target, g);

            n.tr.localPosition = pos;
            n.tr.localScale = Vector3.one * (nodeScale * (n.parent == -1 ? 1f : g));

            if (n.edge != null)
            {
                if (!n.edge.gameObject.activeSelf) n.edge.gameObject.SetActive(true);
                n.edge.SetPosition(0, from);
                n.edge.SetPosition(1, pos);
            }
        }
    }

    // --- 보조 ---
    private void EnsureMaterials()
    {
        if (_nodeMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _nodeMat = new Material(sh) { name = "HistoryNode (auto)" };
        }
        if (_edgeMat == null)
        {
            // Sprites/Default 는 항상 존재하고 LineRenderer 정점 색을 그대로 보여준다
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            _edgeMat = new Material(sh) { name = "HistoryEdge (auto)" };
        }
    }

    private static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.25f, 0.55f, 1f), 0f), // 과거: 파랑
                new GradientColorKey(new Color(1f, 0.85f, 0.2f), 1f),  // 현재: 노랑
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }
}
