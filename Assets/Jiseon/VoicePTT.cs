using Photon.Voice.Unity;
using UnityEngine;

public class VoicePTT : MonoBehaviour
{
    [Header("����")]
    public Recorder recorder;

    [Header("Ű ���ε�")]
    public KeyCode pushToTalkKey = KeyCode.V; // ������ ���ȸ� ���ϱ�
    public KeyCode toggleKey = KeyCode.B; // �׻� ���ϱ� ���

    [Header("����(�б�����)")]
    public bool alwaysOn = false; // B�� ��۵Ǵ� ����

    void Awake()
    {
        if (!recorder) recorder = FindFirstObjectByType<Recorder>();
    }

    void OnEnable()
    {
        ApplyState(); // ���� alwaysOn ���¸� Recorder�� �ݿ�
    }

    void Update()
    {
        if (!recorder) return;

        // �׻� ���ϱ� ���
        if (Input.GetKeyDown(toggleKey))
        {
            alwaysOn = !alwaysOn;
            ApplyState();
#if UNITY_EDITOR
            Debug.Log($"[VoicePTT] AlwaysOn={(alwaysOn ? "ON" : "OFF")}");
#endif
        }

        if (alwaysOn)
        {
            // �׻� ���ϱ� ��忡�� PTT ����, ������ ���� ����
            if (!recorder.TransmitEnabled) recorder.TransmitEnabled = true;
            return;
        }

        // Ǫ������ũ
        if (Input.GetKeyDown(pushToTalkKey)) recorder.TransmitEnabled = true;
        if (Input.GetKeyUp(pushToTalkKey)) recorder.TransmitEnabled = false;
    }

    void ApplyState()
    {
        if (!recorder) return;

        if (alwaysOn)
        {
            // �׻� ���ϱ�: ��� ���� ON (�������� ��� ���̸� �ʿ信 ���� ����)
            recorder.TransmitEnabled = true;
            // �ʿ��: recorder.VoiceDetection = false; // VAD�� ���� ��� ����
        }
        else
        {
            // PTT ���: ���� ���� ���� OFF
            recorder.TransmitEnabled = false;
            // �ʿ��: recorder.VoiceDetection = false; // PTT�� �� �Ÿ� ����
        }
    }
}
