using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NodeXR.UI
{
    public class CandidateCardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Refs (Assign in Inspector)")]
        public RawImage rawPreview;        
        public RawImage preview;           
        public Image selectOutline;        
        public Button hitButton;           
        public Transform linkAnchor;       

        [Header("Optional")]
        public TMP_Text textLabel;                 
        public GameObject loadingIndicator;        

        [Header("Drag")]
        public bool draggable = true;
        public Canvas rootCanvas;                  
        public RectTransform dragRoot;             

        public int index;
        public event Action<int> Clicked;
        public event Action<int, Vector3> DragEnded; 

        private RectTransform _rt;
        private Vector2 _dragOffset;

        private void Awake()
        {
            if (!rawPreview && preview) rawPreview = preview;
            if (!preview && rawPreview) preview = rawPreview;

            _rt = dragRoot ? dragRoot : GetComponent<RectTransform>();
            if (!rootCanvas) rootCanvas = GetComponentInParent<Canvas>();

            if (hitButton)
            {
                hitButton.onClick.RemoveListener(OnClick);
                hitButton.onClick.AddListener(OnClick);
            }

            SetSelected(false);
            ClearTexture(); // 초기화 시 텍스처 비움
            SetLoading(false);
        }

        private void OnClick()
        {
            Clicked?.Invoke(index);
        }

        public Transform GetLinkAnchor()
        {
            return linkAnchor ? linkAnchor : transform;
        }

        // [수정 포인트]: SetActive를 건드리지 않고 Alpha와 Texture로만 제어
        public void SetTexture(Texture tex)
        {
            if (!rawPreview && preview) rawPreview = preview;
            if (rawPreview == null) return;

            // 1. 오브젝트는 항상 켜둡니다 (렌더링 에러 방지)
            rawPreview.gameObject.SetActive(true);
            
            // 2. 텍스처 할당 및 투명도 조절
            rawPreview.texture = tex;
            rawPreview.color = (tex != null) ? Color.white : new Color(1, 1, 1, 0);

            if (preview && preview != rawPreview)
            {
                preview.gameObject.SetActive(true);
                preview.texture = tex;
                preview.color = (tex != null) ? Color.white : new Color(1, 1, 1, 0);
            }
        }

        // [수정 포인트]: 오브젝트를 꺼버리는 대신 텍스처만 제거
        public void ClearTexture()
        {
            if (!rawPreview && preview) rawPreview = preview;

            if (rawPreview)
            {
                rawPreview.texture = null;
                rawPreview.color = new Color(1, 1, 1, 0); // 투명하게 만듦
            }

            if (preview && preview != rawPreview)
            {
                preview.texture = null;
                preview.color = new Color(1, 1, 1, 0);
            }
        }

        public void SetSelected(bool selected)
        {
            if (selectOutline) selectOutline.gameObject.SetActive(selected);
        }

        public void SetInteractable(bool interactable)
        {
            if (hitButton) hitButton.interactable = interactable;
        }

        public void SetLabel(string label)
        {
            if (textLabel) textLabel.text = label;
        }

        public void SetLoading(bool loading)
        {
            if (loadingIndicator) loadingIndicator.SetActive(loading);
        }

        // -------- Drag (UI) --------
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!draggable || _rt == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt,
                eventData.position,
                eventData.pressEventCamera,
                out var localPoint);

            _dragOffset = localPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!draggable || _rt == null) return;

            var parentRt = _rt.parent as RectTransform;
            if (parentRt == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRt,
                eventData.position,
                eventData.pressEventCamera,
                out var localPoint))
            {
                _rt.localPosition = localPoint - _dragOffset;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!draggable) return;
            DragEnded?.Invoke(index, GetLinkAnchor().position);
        }
    }
}