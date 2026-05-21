using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class IconButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("상태별 스프라이트")]
    public Sprite spriteDefault;
    public Sprite spriteHover;
    public Sprite spritePressed;
    public Sprite spriteDisabled;
    public Sprite spriteLoading;

    [Header("설정")]
    public bool isDisabled = false;
    public bool isLoading  = false;

    private Image _image;

    void Awake()
    {
        _image = GetComponent<Image>();
        Apply();
    }

    void OnValidate() => Apply();

    public void Apply()
    {
        if (_image == null) return;

        if (isLoading)        { _image.sprite = spriteLoading;  return; }
        if (isDisabled)       { _image.sprite = spriteDisabled; return; }
        _image.sprite = spriteDefault;
    }

    // 호버/클릭은 isDisabled/isLoading 아닐 때만
    public void OnPointerEnter(PointerEventData e)
    {
        if (isDisabled || isLoading) return;
        _image.sprite = spriteHover;
    }
    public void OnPointerExit(PointerEventData e)
    {
        if (isDisabled || isLoading) return;
        _image.sprite = spriteDefault;
    }
    public void OnPointerDown(PointerEventData e)
    {
        if (isDisabled || isLoading) return;
        _image.sprite = spritePressed;
    }
    public void OnPointerUp(PointerEventData e)
    {
        if (isDisabled || isLoading) return;
        _image.sprite = spriteHover;
    }

    // 외부에서 상태 바꿀 때
    public void SetDisabled(bool value) { isDisabled = value; Apply(); }
    public void SetLoading(bool value)  { isLoading  = value; Apply(); }
}