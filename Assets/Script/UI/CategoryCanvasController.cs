using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NodeXR
{
    public class CategoryCanvasController : MonoBehaviour
    {
        [Header("Template")]
        public Button buttonTemplate;

        [Header("Settings")]
        public string selectionBgName = "Selection_BG";

        [Header("Limit / Sort")]
        [Tooltip("0 이하면 제한 없음. (예: 6이면 최대 6개만 표시)")]
        public int maxButtons = 6;

        [Tooltip("true면 이름 오름차순 정렬")]
        public bool sortByName = false;

        public event Action<string> OnSelectCategory;

        private readonly List<Button> _spawned = new List<Button>();

        // ✅ name -> id 매핑 저장 (서버 /api/categories 결과 기반)
        private readonly Dictionary<string, string> _idByName = new Dictionary<string, string>();

        public void Show(bool on) => gameObject.SetActive(on);

        /// <summary>
        /// (기존 유지) WS dto에서 카테고리 목록만 뽑아 빌드 (이 방식은 id를 모름)
        /// ✅ graph_state fallback 제거 (폭증 원인)
        /// </summary>
        public void BuildFromDto(GraphEventDto dto)
        {
            if (dto != null && dto.categories != null && dto.categories.Count > 0)
            {
                _idByName.Clear(); // dto.categories는 id가 없으니 매핑 비움
                Build(ApplyLimitAndSort(dto.categories));
            }
            else
            {
                _idByName.Clear();
                Clear();
            }
        }

        /// <summary>
        /// ✅ /api/categories 결과(items: id+name)로 빌드
        /// - ROOT 제외
        /// - 이름 기준 중복 제거
        /// - name->id 매핑 저장
        /// - maxButtons 제한 적용
        /// </summary>
        public void BuildFromItems(List<CategoryItem> items)
        {
            if (items == null || items.Count == 0)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 빌드할 카테고리 데이터가 없습니다.");
                Clear();
                _idByName.Clear();
                return;
            }

            _idByName.Clear();

            var labels = new List<string>();
            var seen = new HashSet<string>();

            foreach (var it in items)
            {
                if (it == null) continue;

                string name = it.category_name?.Trim();
                string id = it.category_id?.Trim();

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                if (name.Equals("ROOT", StringComparison.OrdinalIgnoreCase)) continue;

                // 이름 기준으로 중복 제거
                if (seen.Add(name))
                {
                    labels.Add(name);
                    _idByName[name] = id; // ✅ name->id 저장
                }
            }

            labels = ApplyLimitAndSort(labels);
            Build(labels);
        }

        /// <summary>
        /// ✅ AppStateMachine에서 category_id 찾기
        /// </summary>
        public bool TryGetCategoryId(string categoryName, out string categoryId)
        {
            categoryId = "";
            if (string.IsNullOrEmpty(categoryName)) return false;

            return _idByName.TryGetValue(categoryName.Trim(), out categoryId) && !string.IsNullOrEmpty(categoryId);
        }

        // =====================================================
        // 기존 Build (List<string>) 유지
        // =====================================================
        public void Build(List<string> categories)
        {
            if (categories == null || categories.Count == 0)
            {
                Debug.LogWarning("<color=yellow>[UI]</color> 빌드할 카테고리 데이터가 없습니다.");
                Clear();
                return;
            }

            if (buttonTemplate == null)
            {
                Debug.LogError("<color=red>[UI]</color> buttonTemplate이 할당되지 않았습니다.");
                return;
            }

            Clear();

            Transform container = buttonTemplate.transform.parent;

            // 템플릿은 항상 비활성
            if (buttonTemplate.gameObject.activeSelf)
                buttonTemplate.gameObject.SetActive(false);

            foreach (var cat in categories)
            {
                if (string.IsNullOrEmpty(cat)) continue;

                string categoryName = cat.Trim();
                if (string.IsNullOrEmpty(categoryName)) continue;

                Button btn = Instantiate(buttonTemplate, container);
                btn.gameObject.SetActive(true);
                btn.name = $"Category_{categoryName}";

                TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) tmp.text = categoryName;

                // 초기 선택 배경은 꺼둠
                SetSelectionActive(btn, false);

                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => HandleCategoryClick(btn, categoryName));

                _spawned.Add(btn);
            }

            if (container.TryGetComponent<RectTransform>(out var rect))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }

            Debug.Log($"<color=orange>[UI]</color> 총 {categories.Count}개의 카테고리 버튼 생성 완료.");
        }

        private void HandleCategoryClick(Button clickedBtn, string categoryName)
        {
            if (clickedBtn == null) return;

            // 선택 배경 업데이트
            foreach (var b in _spawned)
            {
                if (b != null) SetSelectionActive(b, b == clickedBtn);
            }

            OnSelectCategory?.Invoke(categoryName);
            Debug.Log($"<color=cyan>[UI]</color> 유저 선택: {categoryName}");
        }

        private List<string> ApplyLimitAndSort(List<string> labels)
        {
            if (labels == null) return labels;

            // trim + 빈값 제거 + 중복 제거(안전망)
            var seen = new HashSet<string>();
            var cleaned = new List<string>();

            foreach (var s in labels)
            {
                var name = s?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (seen.Add(name)) cleaned.Add(name);
            }

            if (sortByName)
                cleaned.Sort(StringComparer.Ordinal);

            if (maxButtons > 0 && cleaned.Count > maxButtons)
                cleaned = cleaned.GetRange(0, maxButtons);

            return cleaned;
        }

        private void SetSelectionActive(Button btn, bool isActive)
        {
            if (btn == null) return;
            Transform selectionBg = FindChildRecursive(btn.transform, selectionBgName);
            if (selectionBg != null) selectionBg.gameObject.SetActive(isActive);
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void Clear()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null) Destroy(_spawned[i].gameObject);
            }
            _spawned.Clear();
        }
    }
}
