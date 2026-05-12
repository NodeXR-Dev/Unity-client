using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerCardScript : MonoBehaviour
{
    [Header("UI 요소 연결")]
    public RawImage faceRawImage;    // 캐릭터 얼굴이 그려질 도화지
    public TextMeshProUGUI nicknameText; // 유저 닉네임 텍스트

    /// <summary>
    /// 카드의 정보를 초기화하는 함수입니다.
    /// </summary>
    /// <param name="name">표시할 닉네임</param>
    /// <param name="faceTexture">해당 유저의 실시간 Render Texture</param>
    public void Setup(string name, Texture faceTexture)
    {
        // 1. 닉네임 설정
        if (nicknameText != null)
        {
            nicknameText.text = name;
        }

        // 2. 실시간 얼굴 텍스처 연결
        if (faceRawImage != null)
        {
            faceRawImage.texture = faceTexture;
            
            // 만약 얼굴이 뒤집혀 보인다면 아래 주석을 해제하세요.
            // faceRawImage.uvRect = new Rect(0, 0, 1, 1); 
        }
    }
}