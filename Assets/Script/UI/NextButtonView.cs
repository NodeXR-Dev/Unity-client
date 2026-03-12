using UnityEngine;
using UnityEngine.UI;

public class NextButtonView : MonoBehaviour
{
    // 기획에 따라 상태를 활성/비활성으로 단순화
    public enum State { Active, Disabled }

    [Header("Refs (Assign in Inspector)")]
    public Button hitButton;     // 클릭을 받는 실질적 버튼 (Btn_Next/Hit)
    public Image visualImage;    // 화살표 그래픽 이미지 (Btn_Next/Visual)

    [Header("Sprites")]
    public Sprite spriteActive;
    public Sprite spriteDisabled;

    [Header("Init")]
    public State initialState = State.Disabled;

    private void Awake()
    {
        if (!hitButton) Debug.LogError("[NextButtonView] hitButton is not assigned.", this);
        if (!visualImage) Debug.LogError("[NextButtonView] visualImage is not assigned.", this);

        ApplyState(initialState);
    }

    public void ApplyState(State state)
    {
        if (!hitButton || !visualImage) return;

        switch (state)
        {
            case State.Active:
                visualImage.sprite = spriteActive;
                hitButton.interactable = true;
                break;

            case State.Disabled:
                visualImage.sprite = spriteDisabled;
                hitButton.interactable = false;
                break;
        }
    }

    // 외부에서 접근하기 쉬운 래퍼 함수
    public void SetActive()   => ApplyState(State.Active);
    public void SetDisabled() => ApplyState(State.Disabled);
}