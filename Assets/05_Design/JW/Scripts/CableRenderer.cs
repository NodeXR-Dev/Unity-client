using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CableRenderer : MonoBehaviour
{
    [Header("연결 대상")]
    public Transform endpointA;
    public Transform endpointB;

    [Header("커브 설정")]
    public int resolution = 30;
    [Range(0f, 2f)]
    public float tangentStrength = 0.5f; // S자 굴곡 강도

    private LineRenderer lr;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = resolution;
        lr.startWidth = 0.008f;
        lr.endWidth = 0.008f;
    }

    void Update()
    {
        if (endpointA == null || endpointB == null) return;
        UpdateCurve();
    }

    void UpdateCurve()
    {
        Vector3 a = endpointA.position;
        Vector3 b = endpointB.position;

        // A에서 B 방향으로의 거리
        float dist = Vector3.Distance(a, b) * tangentStrength;

        // 제어점: A는 오른쪽(+X), B는 왼쪽(-X)으로 뻗음
        Vector3 ctrl1 = a + new Vector3(dist, 0, 0);
        Vector3 ctrl2 = b - new Vector3(dist, 0, 0);

        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            lr.SetPosition(i, CubicBezier(a, ctrl1, ctrl2, b, t));
        }
    }

    Vector3 CubicBezier(Vector3 a, Vector3 c1, Vector3 c2, Vector3 b, float t)
    {
        float u = 1 - t;
        return u * u * u * a
             + 3 * u * u * t * c1
             + 3 * u * t * t * c2
             + t * t * t * b;
    }

    // 연결 설정 (CableManager에서 호출)
    public void SetEndpoints(Transform a, Transform b)
    {
        endpointA = a;
        endpointB = b;
    }
}