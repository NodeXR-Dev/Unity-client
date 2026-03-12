using UnityEngine;

public class FirstPersonCamera : MonoBehaviour
{
    public Transform Target;
    public float MouseSensitivity = 10f;

    // 인스펙터에서 직접 연결
    public GameObject chatObject;

    private float verticalRotation;
    private float horizontalRotation;

    void LateUpdate()
    {
        // CHAT이 활성화돼 있으면 카메라 회전/이동 로직 중단
        if (chatObject != null && chatObject.activeInHierarchy)
            return;

        if (Target == null)
        {
            return;
        }

        transform.position = Target.position;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        verticalRotation -= mouseY * MouseSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, -70f, 70f);

        horizontalRotation += mouseX * MouseSensitivity;

        transform.rotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0);
    }
}
