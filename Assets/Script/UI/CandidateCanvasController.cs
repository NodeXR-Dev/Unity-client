using System;
using UnityEngine;
using UnityEngine.UI; // Image 컴포넌트 접근을 위해 반드시 필요
using NodeXR.UI;
using TMPro;

namespace NodeXR
{
    public class CandidateCanvasController : MonoBehaviour
    {
        [Header("Cards (Assign size 3)")]
        public CandidateCardView[] cards; 

        [Header("Lines (Optional)")]
        public bool useLines = true;
        public LinkLineView linePrefab;
        public Transform linesRoot;
        public Transform categoryAnchor; 

        [Header("Category Info")]
        public TextMeshProUGUI categoryLabel; 

        public event Action<int> OnSelectIndex;
        public event Action<int, Vector3> OnCardMoved; 

        private int _selected = -1;
        private LinkLineView[] _lines;

        private void Awake()
        {
            BindCards();
            BuildLinesIfNeeded();
        }

        private void BindCards()
        {
            if (cards == null || cards.Length == 0) return;

            for (int i = 0; i < cards.Length; i++)
            {
                if (!cards[i]) continue;

                int idx = i;
                cards[i].index = idx;

                // 중복 등록 방지 후 이벤트 바인딩
                cards[i].Clicked -= HandleClicked;
                cards[i].Clicked += HandleClicked;

                cards[i].DragEnded -= HandleDragEnded;
                cards[i].DragEnded += HandleDragEnded;
            }
        }

        private void BuildLinesIfNeeded()
        {
            if (!useLines || !linePrefab || !linesRoot || !categoryAnchor || cards == null || cards.Length < 3) return;

            _lines = new LinkLineView[3];

            for (int i = 0; i < 3; i++)
            {
                if (!cards[i]) continue;

                var line = Instantiate(linePrefab, linesRoot);
                line.a = categoryAnchor;
                line.b = cards[i].GetLinkAnchor();
                _lines[i] = line;
            }
        }

        private void HandleClicked(int idx) => Select(idx);
        private void HandleDragEnded(int idx, Vector3 worldPos) => OnCardMoved?.Invoke(idx, worldPos);

        public void Show(bool on)
        {
            gameObject.SetActive(on);
            if (on) ClearSelection();
        }

        public void ClearSelection()
        {
            _selected = -1;
            if (cards == null) return;

            foreach (var c in cards)
                if (c) c.SetSelected(false);
        }

        public void Select(int index)
        {
            _selected = index;
            if (cards != null)
            {
                for (int i = 0; i < cards.Length; i++)
                {
                    if (cards[i]) cards[i].SetSelected(i == index);
                }
            }
            OnSelectIndex?.Invoke(index);
        }

        // --- [핵심] DecisionGraphManager의 에러를 해결하는 메서드 ---
        public Sprite GetImageAt(int index)
        {
            if (IsValid(index))
            {
                // CandidateCardView 내부의 Image 컴포넌트를 직접 찾아 스프라이트를 반환합니다.
                // 이 방식은 CandidateCardView 내부 변수명(displayImage 등)이 달라도 작동합니다.
                Image img = cards[index].GetComponentInChildren<Image>();
                return img != null ? img.sprite : null;
            }
            return null;
        }

        // --- 기존 모든 기능 유지 ---
        public void SetCategoryLabel(string text)
        {
            if (categoryLabel != null) categoryLabel.text = text;
        }

        public void SetCardLabel(int index, string label) 
        { 
            if (IsValid(index)) cards[index].SetLabel(label); 
        }

        public void SetCardLoading(int index, bool loading) 
        { 
            if (IsValid(index)) cards[index].SetLoading(loading); 
        }

        public void SetCardTexture(int index, Texture2D tex) 
        { 
            if (IsValid(index)) cards[index].SetTexture(tex); 
        }

        public void ClearCardTexture(int index) 
        { 
            if (IsValid(index)) cards[index].ClearTexture(); 
        }

        public void SetInteractableAll(bool on)
        {
            if (cards == null) return;
            foreach (var c in cards)
                if (c) c.SetInteractable(on);
        }

        private bool IsValid(int index)
        {
            return cards != null && index >= 0 && index < cards.Length && cards[index] != null;
        }
    }
}