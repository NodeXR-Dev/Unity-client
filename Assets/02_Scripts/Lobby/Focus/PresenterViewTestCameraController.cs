using UnityEngine;

public class PresenterViewTestCameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float sprintMultiplier = 2f;
    [SerializeField] private float lookSensitivity = 2.2f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private bool exitSharedViewOnInput = true;
    [SerializeField] private PresenterViewRenderer rendererController;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        Vector3 eulerAngles = transform.eulerAngles;
        yaw = eulerAngles.y;
        pitch = eulerAngles.x;
        ResolveRendererController();
    }

    private void Update()
    {
        ExitSharedViewIfNeeded();
        UpdateLook();
        UpdateMovement();
    }

    private void ResolveRendererController()
    {
        if (rendererController != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        rendererController = FindFirstObjectByType<PresenterViewRenderer>();
#else
        rendererController = FindObjectOfType<PresenterViewRenderer>();
#endif
    }

    private void ExitSharedViewIfNeeded()
    {
        if (!exitSharedViewOnInput)
        {
            return;
        }

        ResolveRendererController();

        if (rendererController == null ||
            rendererController.IsLocalPersonalMode ||
            !rendererController.IsShowingPresenterView ||
            !HasCameraControlInput())
        {
            return;
        }

        rendererController.ExitToPersonalMode();
    }

    private bool HasCameraControlInput()
    {
        return HasMovementInput() ||
               Input.GetMouseButton(1);
    }

    private void UpdateLook()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!Input.GetMouseButton(1))
        {
            return;
        }

        yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement()
    {
        Vector3 input = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) input += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) input += Vector3.back;
        if (Input.GetKey(KeyCode.D)) input += Vector3.right;
        if (Input.GetKey(KeyCode.A)) input += Vector3.left;
        if (Input.GetKey(KeyCode.E)) input += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) input += Vector3.down;

        if (input.sqrMagnitude <= 0f)
        {
            return;
        }

        float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * sprintMultiplier : moveSpeed;
        Vector3 worldMove = transform.TransformDirection(input.normalized) * (speed * Time.deltaTime);
        transform.position += worldMove;
    }

    private bool HasMovementInput()
    {
        return Input.GetKey(KeyCode.W) ||
               Input.GetKey(KeyCode.S) ||
               Input.GetKey(KeyCode.D) ||
               Input.GetKey(KeyCode.A) ||
               Input.GetKey(KeyCode.E) ||
               Input.GetKey(KeyCode.Q);
    }
}
