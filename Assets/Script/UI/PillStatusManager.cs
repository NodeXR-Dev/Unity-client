using UnityEngine;
using UnityEngine.UI;
using TMPro; // 텍스트 제어를 위해 추가

public class PillStatusManager : MonoBehaviour
{
    public enum Status { Default, Listening, Processing, Success, Error, CannotCreate, SelectCategory }

    [System.Serializable]
    public struct StatusVisual
    {
        public Status status;
        public Sprite combinedSprite; // 배경 이미지
        public Sprite iconSprite;     // 로딩바 등 아이콘
        public bool isClickable;
        public string defaultText;    // 해당 상태일 때 기본적으로 뜰 텍스트
    }

    [Header("Refs")]
    public Button hitButton;        
    public Image backgroundImage;   
    public Image iconImage;         
    public TextMeshProUGUI statusText; // 주제(카테고리)를 표시할 텍스트 객체

    public StatusVisual[] statusConfigs;
    private bool _isProcessing = false;

    // --- [추가] 외부에서 텍스트만 바꿀 때 사용하는 함수 ---
    public void UpdateStatusText(string newText)
    {
        if (statusText != null)
        {
            statusText.text = newText;
        }
    }

    public void SetStatus(Status targetStatus)
    {
        foreach (var config in statusConfigs)
        {
            if (config.status == targetStatus)
            {
                if (backgroundImage) backgroundImage.sprite = config.combinedSprite;
                if (iconImage) iconImage.sprite = config.iconSprite;
                if (hitButton) hitButton.interactable = config.isClickable;
                
                // 상태 변경 시 기본 텍스트가 있다면 설정 (예: SelectCategory 시 "카테고리 선택")
                if (!string.IsNullOrEmpty(config.defaultText))
                {
                    UpdateStatusText(config.defaultText);
                }

                _isProcessing = (config.status == Status.Processing);
                return;
            }
        }
    }

    void Update()
    {
        if (_isProcessing && iconImage != null)
            iconImage.transform.Rotate(Vector3.forward, -180f * Time.deltaTime);
    }
}