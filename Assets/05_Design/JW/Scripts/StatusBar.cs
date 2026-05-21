using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class StatusBar : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("상태별 스프라이트")]
    public Sprite spriteDefault;
    public Sprite spriteHoverClick;
    public Sprite spriteUnavailable;

    [Header("텍스트")]
    public string statusText = "카테고리를 선택해 주세요";

    [Header("설정")]
    public bool isUnavailable = false;

    private Image _image;
    private TextMeshProUGUI _label;

    void Awake()
    {
        _image = GetComponent<Image>();
        _label = GetComponentInChildren<TextMeshProUGUI>();
        Apply();
    }

    void OnValidate()
    {
        _image = GetComponent<Image>();
        _label = GetComponentInChildren<TextMeshProUGUI>();
        Apply();
    }

    public void Apply()
    {
        if (_image == null) return;

        // 비활성 상태면 바로 Unavailable
        if (isUnavailable)
        {
            _image.sprite = spriteUnavailable;
            if (_label)
            {
                _label.text  = statusText;
                _label.color = new Color(1, 1, 1, 0.4f); // 흐리게
            }
            return;
        }

        _image.sprite = spriteDefault;
        if (_label)
        {
            _label.text  = statusText;
            _label.color = Color.white;
        }
    }

    // 호버/클릭은 Unavailable 아닐 때만
    public void OnPointerEnter(PointerEventData e)
    {
        if (isUnavailable) return;
        _image.sprite = spriteHoverClick;
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (isUnavailable) return;
        _image.sprite = spriteDefault;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (isUnavailable) return;
        _image.sprite = spriteHoverClick;
    }

    // 외부에서 호출
    public void SetText(string text)       { statusText = text; Apply(); }
    public void SetUnavailable(bool value) { isUnavailable = value; Apply(); }
}