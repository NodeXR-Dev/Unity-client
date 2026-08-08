using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비의 글자 입력을 월드 키보드로 통일한다.
///
/// 두 가지를 한다.
///  1) 로비 입력칸(이름·회의실 제목·비밀번호)을 누르면 MvpWorldKeyboard 를 연다.
///     음성 인식은 그 키보드의 '말하기' 키로만 시작한다 — 패널이 열리자마자
///     마이크가 켜지던 방식은 사용자가 준비되기 전에 녹음이 시작돼 혼란스러웠다.
///  2) 요구사항 패널에는 입력칸이 아예 없어서(그래서 flow 의 featureTextInput 이
///     미연결이었다) 여기서 하나 만들어 붙이고, 입력된 문장을 공개 API
///     LobbyCreateRequirementFlow.SetRequirementFeatureText() 로 전달한다.
///     이 값이 비어 있으면 서버 세션이 시작되지 않는다.
///
/// 남의 파일(02_Scripts/Lobby)은 수정하지 않고 공개 API 만 사용한다.
/// </summary>
[DefaultExecutionOrder(-500)]
public class MvpLobbyTextInput : MonoBehaviour
{
    [Header("요구사항 입력칸 문구")]
    [SerializeField] private string _requirementTitle =
        "어떤 걸 만들고 싶은지 알려 주세요";
    [SerializeField] private string _requirementHint =
        "눌러서 입력하세요 · 키보드의 '음성' 키로 말해도 됩니다";
    [SerializeField] private string _requirementPlaceholder =
        "예) 물로켓이 더 멀리 날아가게 만들고 싶어요";

    [Header("입력칸 재탐색 주기(초)")]
    [SerializeField] private float _rescanInterval = 0.5f;

    private static readonly Color PanelBg =
        new Color(0.09f, 0.11f, 0.16f, 0.96f);
    // 배경이 비치면 글자가 읽히지 않는다. 입력칸은 확실히 어둡게 깐다.
    private static readonly Color FieldBg =
        new Color(0.04f, 0.05f, 0.08f, 0.98f);
    private static readonly Color TextMain = Color.white;
    private static readonly Color TextDim =
        new Color(1f, 1f, 1f, 0.55f);

    private LobbyCreateRequirementFlow _flow;
    private GameObject _requirementsPanel;

    private readonly HashSet<TMP_InputField> _hooked =
        new HashSet<TMP_InputField>();

    private RectTransform _ui;
    private TMP_InputField _requirementField;
    private float _nextScan;

    private void OnDisable()
    {
        foreach (TMP_InputField field in _hooked)
        {
            if (field != null)
                field.onSelect.RemoveListener(HandleFieldSelected);
        }
        _hooked.Clear();
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + Mathf.Max(0.1f, _rescanInterval);
            HookInputFields();
        }

        if (!ResolveFlow())
            return;

        if (_requirementsPanel.activeInHierarchy)
            EnsureRequirementUi();
    }

    // ------------------------------------------------------------------
    // 입력칸 → 월드 키보드
    // ------------------------------------------------------------------

    // 패널이 나중에 켜지면서 입력칸이 새로 나타나므로 주기적으로 다시 훑는다.
    private void HookInputFields()
    {
        TMP_InputField[] fields = FindObjectsByType<TMP_InputField>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (TMP_InputField field in fields)
        {
            if (field == null || _hooked.Contains(field))
                continue;

            field.onSelect.AddListener(HandleFieldSelected);
            _hooked.Add(field);
        }
    }

    private void HandleFieldSelected(string _)
    {
        TMP_InputField field = FindSelectedField();
        if (field == null)
            return;

        // 키보드가 target 을 DeactivateInputField 하면서 재진입할 수 있어 막는다.
        if (MvpWorldKeyboard.CurrentTarget == field)
            return;

        MvpWorldKeyboard.Open(field);
    }

    private TMP_InputField FindSelectedField()
    {
        UnityEngine.EventSystems.EventSystem events =
            UnityEngine.EventSystems.EventSystem.current;
        GameObject selected =
            events != null ? events.currentSelectedGameObject : null;
        return selected != null
            ? selected.GetComponent<TMP_InputField>()
            : null;
    }

    // ------------------------------------------------------------------
    // 요구사항 입력칸 (원본 패널에 없어서 새로 만든다)
    // ------------------------------------------------------------------

    private bool ResolveFlow()
    {
        if (_flow == null)
        {
            _flow = FindFirstObjectByType<LobbyCreateRequirementFlow>();
            if (_flow == null)
                return false;
            _requirementsPanel = null;
        }

        if (_requirementsPanel == null)
        {
            // requirementsPanel 은 private 직렬화 필드다. 읽기만 한다.
            var field = typeof(LobbyCreateRequirementFlow).GetField(
                "requirementsPanel",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            _requirementsPanel = field?.GetValue(_flow) as GameObject;
            if (_requirementsPanel == null)
                return false;
        }

        return true;
    }

    private void EnsureRequirementUi()
    {
        if (_ui != null)
            return;

        RectTransform parent =
            _requirementsPanel.GetComponent<RectTransform>();
        if (parent == null)
            return;

        TMP_FontAsset font = FindPanelFont(parent);

        var root = new GameObject("MvpRequirementInput",
            typeof(RectTransform), typeof(Image));
        _ui = root.GetComponent<RectTransform>();
        _ui.SetParent(parent, false);
        // 패널 아래쪽엔 원본 '시작' 버튼이 있다(패널기준 y -260~-128).
        // 그 위 빈 공간을 전부 쓴다.
        _ui.anchorMin = new Vector2(0.06f, 0.33f);
        _ui.anchorMax = new Vector2(0.94f, 0.96f);
        _ui.offsetMin = Vector2.zero;
        _ui.offsetMax = Vector2.zero;

        Image bg = root.GetComponent<Image>();
        bg.color = PanelBg;
        bg.raycastTarget = false;

        CreateText(_ui, font, "Title", 44f,
            new Vector2(0.03f, 0.80f), new Vector2(0.97f, 0.98f),
            TextAlignmentOptions.Left, TextMain).text = _requirementTitle;

        CreateText(_ui, font, "Hint", 30f,
            new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.16f),
            TextAlignmentOptions.Left, TextDim).text = _requirementHint;

        // 입력칸 본체
        var fieldGo = new GameObject("RequirementField",
            typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(_ui, false);
        fieldRt.anchorMin = new Vector2(0.03f, 0.20f);
        fieldRt.anchorMax = new Vector2(0.97f, 0.76f);
        fieldRt.offsetMin = Vector2.zero;
        fieldRt.offsetMax = Vector2.zero;
        fieldGo.GetComponent<Image>().color = FieldBg;

        var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.SetParent(fieldRt, false);
        viewportRt.anchorMin = new Vector2(0f, 0f);
        viewportRt.anchorMax = new Vector2(1f, 1f);
        viewportRt.offsetMin = new Vector2(18f, 8f);
        viewportRt.offsetMax = new Vector2(-18f, -8f);

        TextMeshProUGUI placeholder = CreateText(viewportRt, font, "Placeholder", 38f,
            Vector2.zero, Vector2.one, TextAlignmentOptions.Left, TextDim);
        placeholder.text = _requirementPlaceholder;

        TextMeshProUGUI text = CreateText(viewportRt, font, "Text", 38f,
            Vector2.zero, Vector2.one, TextAlignmentOptions.Left, TextMain);
        text.text = string.Empty;

        _requirementField = fieldGo.GetComponent<TMP_InputField>();
        _requirementField.textViewport = viewportRt;
        _requirementField.textComponent = text;
        _requirementField.placeholder = placeholder;
        _requirementField.lineType = TMP_InputField.LineType.MultiLineNewline;
        _requirementField.pointSize = 38f;
        _requirementField.onValueChanged.AddListener(HandleRequirementChanged);
        _requirementField.onEndEdit.AddListener(HandleRequirementChanged);

        // flow 의 featureTextInput 슬롯이 비어 있으므로 여기서 채워 준다.
        // (SetRequirementFeatureText 로도 넣지만, 슬롯이 있으면 flow 가 직접 읽는다)
        var slot = typeof(LobbyCreateRequirementFlow).GetField(
            "featureTextInput",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        slot?.SetValue(_flow, _requirementField);
    }

    private void HandleRequirementChanged(string value)
    {
        if (_flow == null)
            return;

        string trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            _flow.ClearRequirementFeatureText();
        else
            _flow.SetRequirementFeatureText(trimmed);
    }

    // ------------------------------------------------------------------

    private static TextMeshProUGUI CreateText(
        RectTransform parent,
        TMP_FontAsset font,
        string name,
        float size,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextAlignmentOptions alignment,
        Color color)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        return text;
    }

    // 패널에 이미 쓰인 폰트를 재사용한다(한글 글리프 보장 + 디자인 일치).
    private static TMP_FontAsset FindPanelFont(RectTransform parent)
    {
        var texts = new List<TextMeshProUGUI>();
        parent.GetComponentsInChildren(true, texts);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text != null && text.font != null)
                return text.font;
        }

        Canvas canvas = parent.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            texts.Clear();
            canvas.GetComponentsInChildren(true, texts);
            foreach (TextMeshProUGUI text in texts)
            {
                if (text != null && text.font != null)
                    return text.font;
            }
        }
        return null;
    }
}
