using UnityEngine;
using Photon.Voice.Unity;
using Photon.Voice.Fusion;
using System.Collections;

public class VoiceLevelSource_V2 : MonoBehaviour
{
    [Header("설정")]
    public float smoothTime = 0.05f;
    [Range(0, 1)] public float level01; 

    private Recorder _recorder;
    private float _vel;

    private IEnumerator Start()
    {
        // Unity 6 권장: FindFirstObjectByType 사용
        while (_recorder == null)
        {
            var fvc = Object.FindFirstObjectByType<FusionVoiceClient>();
            if (fvc != null) _recorder = fvc.PrimaryRecorder;
            
            if (_recorder == null)
                _recorder = Object.FindFirstObjectByType<Recorder>();

            if (_recorder == null) yield return new WaitForSeconds(0.2f);
        }
        Debug.Log("<color=green>Unity 6 Voice Recorder 연결 완료!</color>");
    }

    void Update()
    {
        // [수정된 부분] IsRecording 대신 RecordingState를 확인하거나 
        // 단순히 LevelMeter가 사용 가능한지 체크합니다.
        if (_recorder == null || _recorder.LevelMeter == null) 
        {
            level01 = Mathf.SmoothDamp(level01, 0, ref _vel, smoothTime);
            return;
        }

        // LevelMeter.CurrentAvgAmp는 포톤 내부에서 이미 계산된 값입니다.
        float rawLevel = _recorder.LevelMeter.CurrentAvgAmp;

        // 민감도 조절 (목소리가 작으면 15f를 더 키우세요)
        float target = Mathf.Clamp01(rawLevel * 50f); 
        level01 = Mathf.SmoothDamp(level01, target, ref _vel, smoothTime);
    }
}