// BillboardUI.cs
using UnityEngine;

public class BillboardUI : MonoBehaviour
{
    public Transform target;     // 따라갈 플레이어 머리(Head)
    public Vector3 offset = new Vector3(0, 2.0f, 0); // 머리 위 높이
    Camera cam;
    void Start() { cam = Camera.main; }
    void LateUpdate()
    {
        if (target) transform.position = target.position + offset;
        if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
