using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NodeXR.UI
{
    public class SpriteButtonView : MonoBehaviour
    {
        public enum State { Enabled, Disabled }

        [Header("Refs (Assign in Inspector)")]
        [SerializeField] private Button hitButton;   // Btn3D/Hit (Button)
        [SerializeField] private Image visualImage;  // Btn3D/Visual (Image)

        [Header("Sprites")]
        [SerializeField] private Sprite enabledSprite;
        [SerializeField] private Sprite disabledSprite;

        [Header("Init")]
        [SerializeField] private State initialState = State.Disabled;

        public UnityEvent onClicked = new UnityEvent();

        private State _state;

        private void Awake()
        {
            if (!hitButton) Debug.LogError("[SpriteButtonView] hitButton is not assigned.", this);
            if (!visualImage) Debug.LogError("[SpriteButtonView] visualImage is not assigned.", this);

            if (hitButton)
            {
                hitButton.onClick.RemoveListener(HandleClick);
                hitButton.onClick.AddListener(HandleClick);
            }

            ApplyState(initialState);
        }

        private void HandleClick()
        {
            if (_state == State.Disabled) return;
            onClicked?.Invoke();
        }

        public void ApplyState(State state)
        {
            _state = state;
            if (!hitButton || !visualImage) return;

            switch (state)
            {
                case State.Enabled:
                    visualImage.sprite = enabledSprite;
                    hitButton.interactable = true;
                    break;

                case State.Disabled:
                    visualImage.sprite = disabledSprite;
                    hitButton.interactable = false; // 클릭 자체를 막고 싶으면 이대로
                    break;
            }

            // 안전: 스프라이트가 null이면 안 보이는 이슈가 나서 경고
            if (visualImage.sprite == null)
                Debug.LogWarning("[SpriteButtonView] visualImage.sprite is null. Check sprites assignment.", this);
        }

        // 인스펙터/코드에서 쓰기 편하게 래퍼
        public void SetEnabled()  => ApplyState(State.Enabled);
        public void SetDisabled() => ApplyState(State.Disabled);
    }
}
