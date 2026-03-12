using UnityEngine;
using UnityEngine.UI;

namespace NodeXR.UI
{
    public class CoreImagePanelView : MonoBehaviour
    {
        [Header("Refs - Core Panel")]
        [SerializeField] private GameObject placeholder;
        [SerializeField] private RawImage preview2D;

        [Header("Refs - 3D Button")]
        [SerializeField] private SpriteButtonView btn3D;

        [Header("Refs - Center 3D Panel")]
        [SerializeField] private GameObject model3DPanel;

        private bool _hasPreviewImage = false;

        private void Awake()
        {
            SetHasPreview(false);
            if (model3DPanel) model3DPanel.SetActive(false);
            if (btn3D != null) btn3D.onClicked.AddListener(OpenModel3D);
        }

        // AppStateMachine에서 호출하는 핵심 메서드
        public void SetPreviewTexture(Texture tex)
        {
            if (preview2D == null) return;

            preview2D.texture = tex;
            preview2D.enabled = (tex != null);
            
            // 텍스처가 있으면 투명도 1, 없으면 0 (CandidateCardView와 동일 방식)
            preview2D.color = (tex != null) ? Color.white : new Color(1, 1, 1, 0);

            SetHasPreview(tex != null);
        }

        private void SetHasPreview(bool hasImage)
        {
            _hasPreviewImage = hasImage;
            if (placeholder) placeholder.SetActive(!hasImage);
            if (btn3D)
            {
                if (hasImage) btn3D.SetEnabled();
                else btn3D.SetDisabled();
            }
        }

        public void OpenModel3D() { if (_hasPreviewImage && model3DPanel) model3DPanel.SetActive(true); }
        public void CloseModel3D() { if (model3DPanel) model3DPanel.SetActive(false); }
    }
}