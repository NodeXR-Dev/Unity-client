using UnityEngine;
using UnityEngine.UI; // 이게 있어야 Image 칸이 생깁니다!

public class ViewModeManager : MonoBehaviour
{
    [Header("Settings")]
    public OVRPassthroughLayer passthroughLayer; 
    public Material vrSkybox;                    
    
    [Header("UI Toggle Settings")] // <--- 이 부분이 인스펙터에 나타날 거예요!
    public Image toggleButtonImage;  // 'Btn_ViewMode' 오브젝트를 여기에 넣으세요
    public Sprite vrButtonSprite;   // VR 흰색 원 이미지
    public Sprite arButtonSprite;   // AR 흰색 원 이미지
    
    private bool isVrMode = false;

    void Start()
    {
        SetPassthroughMode();
    }

    public void ToggleViewMode()
    {
        isVrMode = !isVrMode;
        if (isVrMode) SetVrMode();
        else SetPassthroughMode();
    }

    private void SetVrMode()
    {
        passthroughLayer.hidden = true;
        RenderSettings.skybox = vrSkybox;
        Camera.main.clearFlags = CameraClearFlags.Skybox;

        // VR 이미지로 교체
        if (toggleButtonImage != null && vrButtonSprite != null)
            toggleButtonImage.sprite = vrButtonSprite;

        Debug.Log("VR 모드로 전환됨");
    }

    private void SetPassthroughMode()
    {
        passthroughLayer.hidden = false;
        RenderSettings.skybox = null; 
        Camera.main.clearFlags = CameraClearFlags.SolidColor;
        Camera.main.backgroundColor = new Color(0, 0, 0, 0);

        // AR 이미지로 교체
        if (toggleButtonImage != null && arButtonSprite != null)
            toggleButtonImage.sprite = arButtonSprite;

        Debug.Log("패스스루(AR) 모드로 전환됨");
    }
}