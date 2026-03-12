using UnityEngine;
using Meta.WitAi.Dictation;
using NodeXR;
using System.Text;

public class VoiceInputHandler : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private AppStateMachine stateMachine;
    [SerializeField] private DictationService dictationService;
    [SerializeField] private PillStatusManager pillManager; 

    public bool isRecording = false; 
    private StringBuilder _accumulatedTranscript = new StringBuilder();

    private void Awake()
    {
        if (!dictationService) dictationService = GetComponent<DictationService>();
        if (!pillManager) pillManager = Object.FindFirstObjectByType<PillStatusManager>();
        
        // 수정 포인트: FindFirstObjectByType 사용 시 제네릭 제약 조건 해결
        if (!stateMachine) stateMachine = Object.FindFirstObjectByType<NodeXR.AppStateMachine>();

        if (dictationService != null)
        {
            dictationService.DictationEvents.OnFullTranscription.AddListener(OnTranscriptionFinished);
            dictationService.DictationEvents.OnError.AddListener(HandleVoiceError);
        }
    }

    public void ToggleMic()
    {
        // 현재 상태 반전
        if (!isRecording) StartRecording();
        else StopRecording();
        
        Debug.Log($"<color=white>[Voice]</color> <b>ToggleMic</b> 호출됨 (현재 상태: {(isRecording ? "녹음 중" : "정지됨")})");
    }

    private void StartRecording()
    {
        isRecording = true;
        _accumulatedTranscript.Clear(); // 새로운 대화 시작 시 이전 기록 초기화

        // UI 상태 업데이트
        if (pillManager != null) pillManager.SetStatus(PillStatusManager.Status.Listening);
        
        if (dictationService != null)
        {
            dictationService.Activate();
            Debug.Log("<color=lime>[Voice]</color> Wit.ai 서비스 활성화");
        }
    }

    private void StopRecording()
    {
        // [추가] 확실하게 처리 중 UI를 띄웁니다.
        if (pillManager != null) 
        {
            pillManager.SetStatus(PillStatusManager.Status.Processing);
            Debug.Log("<color=orange>[UI]</color> 서버 전송 시작 - Processing 상태로 변경");
        }

        if (dictationService != null)
        {
            dictationService.Deactivate();
        }
        
        SendToStateMachine();
    }

    private void SendToStateMachine()
    {
        string fullText = _accumulatedTranscript.ToString().Trim();
        
        // [중요] 녹음 상태를 확실히 종료
        isRecording = false;

        if (!string.IsNullOrWhiteSpace(fullText))
        {
            Debug.Log($"<color=cyan>[Voice]</color> <b>전송 준비 완료:</b> {fullText}");
            if (stateMachine != null) 
            {
                // 서버에 데이터 전송 시작
                stateMachine.SubmitUtterance(fullText);
            }
            _accumulatedTranscript.Clear(); 
        }
        else
        {
            // 텍스트가 비어있을 때 바로 Default로 가지 않고, 
            // 사용자에게 인식이 안 됐음을 알리거나 잠시 대기합니다.
            Debug.Log("<color=yellow>[Voice]</color> 인식된 텍스트가 없습니다.");
            
            if (pillManager != null)
            {
                pillManager.UpdateStatusText("다시 말씀해 주세요");
                // 1초 뒤에 원래 상태(Default)로 복구
                Invoke(nameof(ResetToDefault), 1.5f);
            }
        }
    }

    public void OnTranscriptionFinished(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 한 문장이 끝날 때마다 버퍼에 추가만 함 (서버 전송 X)
        _accumulatedTranscript.Append(text).Append(" ");
        
        Debug.Log($"<color=lightblue>[Voice-Live]</color> 문장 누적 중: {text}");
        Debug.Log($"<color=grey>[Voice-Buffer]</color> 현재 전체 문장: {_accumulatedTranscript}");
    }

    private void HandleVoiceError(string error, string message)
    {
        Debug.LogError($"<color=red>[Voice Error]</color> {error}: {message}");
        isRecording = false;
        if (pillManager) 
        {
            pillManager.SetStatus(PillStatusManager.Status.Error);
            Invoke(nameof(ResetToDefault), 2.0f);
        }
    }

    private void ResetToDefault()
    {
        if (pillManager) pillManager.SetStatus(PillStatusManager.Status.Default);
    }
}