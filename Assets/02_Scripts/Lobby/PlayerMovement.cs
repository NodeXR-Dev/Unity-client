using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    private Vector3 _velocity;
    private bool _jumpPressed;

    private CharacterController _controller;

    public float PlayerSpeed = 2f;
    public float JumpForce = 5f;
    public float GravityValue = -9.81f;

    public Camera MYcamera;

    // ä�� ��ũ��Ʈ ����
    private Multiplayerchat chat;

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            MYcamera = Camera.main;
            MYcamera.GetComponent<FirstPersonCamera>().Target = transform;
        }

        // ���� �ִ� ä�� ��ũ��Ʈ ã��
        chat = FindFirstObjectByType<Multiplayerchat>();
    }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    //  ��ǲ�ʵ� ��Ŀ�� ���� üũ ����
    private bool IsTyping()
    {
        return chat != null && chat.input != null && chat.input.isFocused;
    }

    void Update()
    {
        // ��ǲ�ʵ� ��Ŀ�� ���̸� ���� �Է� ����
        if (IsTyping())
            return;

        if (Input.GetButtonDown("Jump"))
        {
            _jumpPressed = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_controller.isGrounded)
        {
            _velocity = new Vector3(0, -1, 0);
        }

        //  ��ǲ�ʵ� ��Ŀ���� �̵� �Է� ���� (WASD/ȭ��ǥ ����)
        Vector3 inputDir = IsTyping()
            ? Vector3.zero
            : new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));

        Quaternion cameraRotationY = Quaternion.Euler(0, MYcamera.transform.rotation.eulerAngles.y, 0);
        Vector3 move = cameraRotationY * inputDir * Runner.DeltaTime * PlayerSpeed;

        _velocity.y += GravityValue * Runner.DeltaTime;

        //  ��Ŀ�� ���̸� ������ ����
        if (_jumpPressed && _controller.isGrounded && !IsTyping())
        {
            _velocity.y += JumpForce;
        }

        _controller.Move(move + _velocity * Runner.DeltaTime);

        float yaw = MYcamera.transform.rotation.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0, yaw, 0);

        _jumpPressed = false;
    }
}
