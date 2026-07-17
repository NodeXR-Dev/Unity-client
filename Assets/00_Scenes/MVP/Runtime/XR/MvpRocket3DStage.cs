using System.Collections.Generic;
using UnityEngine;

// 노드 설계(MvpRocketDesign)를 반영해 프리미티브로 3D 물로켓을 조립하는 mock 스테이지.
// Build() 이후 부품이 아래→위로 팝인되며(생성 연출), 완성되면 천천히 회전하는 턴테이블이 된다.
[DisallowMultipleComponent]
public class MvpRocket3DStage : MonoBehaviour
{
    private struct Piece
    {
        public Transform tf;
        public Vector3 targetScale;
        public float delay;   // 팝인 시작 지연(초)
    }

    private readonly List<Piece> _pieces = new List<Piece>();
    private Transform _spin;         // 회전하는 조립체
    private float _elapsed;
    private float _totalBuild;
    private bool _built;

    private static readonly Color BodyColor = new Color(0.96f, 0.97f, 1f);
    private static readonly Color NozzleColor = new Color(0.96f, 0.6f, 0.29f);
    private static readonly Color PadColor = new Color(0.16f, 0.22f, 0.36f, 1f);

    public bool BuildComplete => _built && _elapsed >= _totalBuild;

    public void Build(MvpRocketDesign design)
    {
        if (design == null)
            design = new MvpRocketDesign();

        Clear();

        _spin = new GameObject("Spin").transform;
        _spin.SetParent(transform, false);

        Color accent = design.Accent;
        Color accentDark = accent * 0.8f;
        accentDark.a = 1f;

        float slim = Mathf.Clamp(design.bodySlim, 0.82f, 1.16f);
        float bodyRadius = 0.058f / slim;
        float bodyHeight = 0.26f * Mathf.Clamp(design.bodyLength, 0.78f, 1.35f);
        float bodyTop = bodyHeight * 0.5f;
        float bodyBottom = -bodyHeight * 0.5f;

        // 받침 패드(바닥에 놓인 느낌)
        Transform pad = MakeCylinder("Pad", bodyRadius * 3.4f, 0.008f, PadColor);
        pad.SetParent(_spin, false);
        pad.localPosition = new Vector3(0f, bodyBottom - 0.06f, 0f);
        AddPiece(pad, 0f);

        // 몸통
        Transform body = MakeCylinder("Body", bodyRadius, bodyHeight, BodyColor);
        body.SetParent(_spin, false);
        body.localPosition = new Vector3(0f, 0f, 0f);
        AddPiece(body, 0.15f);

        // 라벨 밴드(accent)
        Transform band = MakeCylinder("Band", bodyRadius * 1.04f, bodyHeight * 0.16f, accent);
        band.SetParent(_spin, false);
        band.localPosition = new Vector3(0f, bodyHeight * 0.08f, 0f);
        AddPiece(band, 0.25f);

        // 노즐(몸통 아래)
        Transform nozzle = MakeCylinder("Nozzle", bodyRadius * 0.62f, 0.05f, NozzleColor);
        nozzle.SetParent(_spin, false);
        nozzle.localPosition = new Vector3(0f, bodyBottom - 0.02f, 0f);
        AddPiece(nozzle, 0.35f);

        // 노즈콘
        Transform nose;
        if (design.pointedNose)
        {
            nose = MakeCone("Nose", bodyRadius * 1.02f, 0.14f, accent);
            nose.SetParent(_spin, false);
            nose.localPosition = new Vector3(0f, bodyTop, 0f);
        }
        else
        {
            nose = MakeSphere("Nose", accent);
            nose.SetParent(_spin, false);
            nose.localScale =
                new Vector3(bodyRadius * 2.1f, 0.11f, bodyRadius * 2.1f);
            nose.localPosition = new Vector3(0f, bodyTop + 0.005f, 0f);
        }
        AddPiece(nose, 0.55f);

        // 날개(finCount 개를 Y축 둘레에 균등 배치)
        int fins = Mathf.Clamp(design.finCount, 2, 4);
        float finSpan = Mathf.Clamp(design.finSpan, 0.6f, 1.5f);
        float finOut = bodyRadius * (1.4f + finSpan);
        float finHeight = bodyHeight * 0.34f;
        for (int i = 0; i < fins; i++)
        {
            float angle = 360f / fins * i;
            Transform fin = MakeCube(
                "Fin" + i,
                new Vector3(0.006f, finHeight, finOut),
                accentDark);
            fin.SetParent(_spin, false);
            Quaternion yaw = Quaternion.Euler(0f, angle, 0f);
            fin.localRotation = yaw * Quaternion.Euler(18f, 0f, 0f);
            Vector3 outDir = yaw * new Vector3(0f, 0f, finOut * 0.5f);
            fin.localPosition =
                new Vector3(outDir.x, bodyBottom + finHeight * 0.25f, outDir.z);
            AddPiece(fin, 0.65f + i * 0.06f);
        }

        _totalBuild = 0f;
        foreach (Piece p in _pieces)
            _totalBuild = Mathf.Max(_totalBuild, p.delay + PopDuration);
        _elapsed = 0f;
        _built = true;

        // 팝인 시작: 전부 0 스케일에서 출발
        foreach (Piece p in _pieces)
            p.tf.localScale = Vector3.zero;
    }

    private const float PopDuration = 0.4f;

    private void Update()
    {
        if (!_built) return;

        _elapsed += Time.deltaTime;

        foreach (Piece p in _pieces)
        {
            float local = Mathf.Clamp01((_elapsed - p.delay) / PopDuration);
            float eased = EaseOutBack(local);
            p.tf.localScale = p.targetScale * eased;
        }

        if (_spin != null)
        {
            float spinSpeed = BuildComplete ? 26f : 8f;
            _spin.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
            if (BuildComplete)
            {
                float bob = Mathf.Sin(_elapsed * 1.6f) * 0.008f;
                _spin.localPosition = new Vector3(0f, bob, 0f);
            }
        }
    }

    private void AddPiece(Transform tf, float delay)
    {
        _pieces.Add(new Piece
        {
            tf = tf,
            targetScale = tf.localScale,
            delay = delay
        });
    }

    public void Clear()
    {
        _pieces.Clear();

        // GameObject만 지우면 런타임 생성 Material/절차적 Mesh가 누수된다.
        // 모든 sharedMaterial은 MakeMaterial로 새로 만든 것이라 파괴 안전.
        // Mesh는 내가 만든 콘("RocketCone")만 파괴(프리미티브 공유 메시는 건드리면 안 됨).
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer mr in renderers)
            if (mr != null && mr.sharedMaterial != null)
                Destroy(mr.sharedMaterial);

        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in filters)
            if (mf != null &&
                mf.sharedMesh != null &&
                mf.sharedMesh.name == "RocketCone")
                Destroy(mf.sharedMesh);

        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        _spin = null;
        _built = false;
        _elapsed = 0f;
    }

    // ----- 프리미티브 빌더 -----

    private static Transform MakeCylinder(
        string name, float radius, float height, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        StripCollider(go);
        // 기본 실린더: 반지름 0.5, 높이 2(-1~1) → 원하는 치수로 스케일
        go.transform.localScale =
            new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        Paint(go, color);
        return go.transform;
    }

    private static Transform MakeSphere(string name, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        StripCollider(go);
        Paint(go, color);
        return go.transform;
    }

    private static Transform MakeCube(string name, Vector3 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        StripCollider(go);
        go.transform.localScale = size;
        Paint(go, color);
        return go.transform;
    }

    private static Transform MakeCone(
        string name, float radius, float height, Color color)
    {
        GameObject go = new GameObject(name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = BuildConeMesh(radius, height, 24);
        mr.sharedMaterial = MakeMaterial(color);
        return go.transform;
    }

    private static Mesh BuildConeMesh(float radius, float height, int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "RocketCone";

        Vector3[] vertices = new Vector3[segments + 2];
        vertices[0] = Vector3.zero;                 // 밑면 중심
        vertices[1] = new Vector3(0f, height, 0f);  // 꼭짓점
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            vertices[2 + i] =
                new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        List<int> tris = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            int cur = 2 + i;
            int next = 2 + (i + 1) % segments;
            // 옆면
            tris.Add(1); tris.Add(next); tris.Add(cur);
            // 밑면
            tris.Add(0); tris.Add(cur); tris.Add(next);
        }

        mesh.vertices = vertices;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // 즉시 파괴여야 한다: Destroy(지연)를 쓰면 같은 프레임의 팝인 애니메이션이
    // 스케일 0을 넣는 순간 아직 살아 있는 콜라이더가 "BoxCollider does not
    // support negative scale or size" 에러를 내고, Error Pause 에디터를 멈춘다.
    private static void StripCollider(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
            DestroyImmediate(collider);
    }

    private static void Paint(GameObject go, Color color)
    {
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sharedMaterial = MakeMaterial(color);
    }

    private static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        return material;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
