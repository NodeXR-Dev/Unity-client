using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NodeXR.UI
{
    public class MainHUDController : MonoBehaviour
    {
        [SerializeField] private TMP_Text textCategory;
        [SerializeField] private PillStatusManager pillManager; 
        [SerializeField] private NextButtonView nextButton;    
        [SerializeField] private SpriteButtonView btn3D;       

        private string _currentCategory = "스케치";

        // AppStateMachine에서 카테고리 선택 시 호출하여 내부 상태를 동기화합니다.
        public void UpdateCurrentCategory(string categoryName)
        {
            _currentCategory = categoryName;
            SetCategoryText(_currentCategory);
        }

        public void ApplyPhase(UnityPhase phase)
        {
            if (!pillManager) return;

            switch (phase)
            {
                case UnityPhase.BASIC_DISCUSS:
                    _currentCategory = "스케치";
                    SetCategoryText(_currentCategory);
                    pillManager.SetStatus(PillStatusManager.Status.Default);
                    if(nextButton) nextButton.SetDisabled();
                    if(btn3D) btn3D.SetDisabled();
                    break;

                case UnityPhase.CATEGORY_SELECT:
                    pillManager.SetStatus(PillStatusManager.Status.SelectCategory);
                    // [핵심 수정] 여기서 nextButton.SetDisabled()를 호출하지 않습니다.
                    // 버튼 활성화는 사용자가 카테고리를 클릭했을 때 StateMachine이 결정하게 합니다.
                    if(btn3D) btn3D.SetEnabled();
                    break;

                case UnityPhase.CATEGORY_DISCUSS:
                    // 선택된 카테고리 이름을 상단 텍스트에 고정합니다.
                    SetCategoryText(_currentCategory); 
                    pillManager.SetStatus(PillStatusManager.Status.Default);
                    if(nextButton) nextButton.SetDisabled();
                    break;

                case UnityPhase.PREVIEW_3D:
                    pillManager.SetStatus(PillStatusManager.Status.CannotCreate);
                    if(nextButton) nextButton.SetActive(); 
                    break;
            }
        }

        private void SetCategoryText(string text) { if (textCategory) textCategory.text = text; }
    }
}