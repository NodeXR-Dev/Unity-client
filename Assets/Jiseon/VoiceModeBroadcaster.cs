using UnityEngine;

public class VoiceModeBroadcaster : MonoBehaviour
{
    [Header("����(3D) ���� ����")]
    public float proxMinDistance = 1f;
    public float proxMaxDistance = 25f;

    [Header("�׽�Ʈ�� ����Ű")]
    public bool enableHotkeys = true;  // I/O/P

    void Update()
    {
        if (!enableHotkeys) return;
        if (Input.GetKeyDown(KeyCode.I)) SetGlobalAll();
        if (Input.GetKeyDown(KeyCode.O)) SetProximityAll();
        if (Input.GetKeyDown(KeyCode.P)) SetMuteAll();
    }

    public void SetGlobalAll()
    {
        foreach (var m in FindObjectsByType<VoiceAudioModeHard>(FindObjectsSortMode.None))
            m.SetMode(VoicePlayMode.Global2D);
        Debug.Log("[Voice] Global (2D) ON");
    }

    public void SetProximityAll()
    {
        foreach (var m in FindObjectsByType<VoiceAudioModeHard>(FindObjectsSortMode.None))
        {
            m.proxMinDistance = proxMinDistance;
            m.proxMaxDistance = proxMaxDistance;
            m.SetMode(VoicePlayMode.Proximity3D);
        }
        Debug.Log("[Voice] Proximity (3D) ON");
    }

    public void SetMuteAll()
    {
        foreach (var m in FindObjectsByType<VoiceAudioModeHard>(FindObjectsSortMode.None))
            m.SetMode(VoicePlayMode.Off);
        Debug.Log("[Voice] MUTE ALL");
    }
}
