using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthBar : MonoBehaviour
{
    public Image fillImage;            // 체력바 이미지
    public TMP_Text healthText;        // 체력 수치 텍스트

    private float maxHealth = 100f;

    // 최대 체력 설정
    public void SetMaxHealth(float health)
    {
        maxHealth = health;
        fillImage.fillAmount = 1f;
        UpdateHealth(maxHealth);
    }

    // 체력 갱신
    public void UpdateHealth(float currentHealth)
    {
        fillImage.fillAmount = Mathf.Clamp01(currentHealth / maxHealth);

        if (healthText != null)
        {
            healthText.text = $"{Mathf.CeilToInt(currentHealth)} / {Mathf.CeilToInt(maxHealth)}";
        }
    }

    // 항상 카메라를 바라보도록
    void LateUpdate()
    {
        if (Camera.main != null)
        {
            transform.LookAt(Camera.main.transform);
        }
    }
}
