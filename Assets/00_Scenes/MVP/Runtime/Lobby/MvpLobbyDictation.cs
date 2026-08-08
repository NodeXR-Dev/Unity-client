using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 '요구사항' 패널에 온디바이스 음성 인식을 붙인다.
///
/// LobbyCreateRequirementFlow.StartRequirementSpeechToText() 는 빈 스텁이고
/// featureTextInput 도 씬에 연결돼 있지 않아, 지금까지 요구사항 텍스트가 항상
/// 비어 있었다(= 세션 시작이 차단됨). 여기서 인식한 문장을 공개 API
/// SetRequirementFeatureText() 로 넣어 그 구멍을 메운다.
///
/// 원본 흐름의 '2초 동안 소리가 났는지' 게이트는 마이크를 직접 열기 때문에
/// STT 와 장치를 다툴 수 있다. 그래서 MvpLobby 씬에서는 그 게이트를 끄고
/// '문장이 실제로 인식됐는지'로 대체한다 — 잡음이 아니라 말을 확인하므로 더 정확하다.
///
/// 남의 파일(02_Scripts/Lobby)은 수정하지 않고 공개 API 만 사용한다.
/// </summary>
[DefaultExecutionOrder(-500)]
public class MvpLobbyDictation : MonoBehaviour
{
    [Header("패널이 열리면 자동으로 듣기 시작")]
    [SerializeField] private bool _autoListenOnOpen = true;

    [Header("문구")]
    [SerializeField] private string _idleText =
        "마이크를 눌러 만들고 싶은 것을 말해 주세요";
    [SerializeField] private string _listeningText = "듣고 있어요…";
    [SerializeField] private string _preparingText = "음성 인식 준비 중…";

    private static readonly Color PanelBg =
        new Color(0.09f, 0.11f, 0.16f, 0.90f);
    private static readonly Color AccentBlue =
        new Color(0.29f, 0.62f, 0.94f, 1f);
    private static readonly Color MicActive =
        new Color(0.90f, 0.32f, 0.32f, 1f);
    private static readonly Color TextMain = Color.white;
    private static readonly Color TextDim =
        new Color(1f, 1f, 1f, 0.55f);

    private LobbyCreateRequirementFlow _flow;
    private GameObject _requirementsPanel;
    private MvpOnDeviceDictation _dictation;

    private RectTransform _ui;
    private TextMeshProUGUI _transcript;
    private TextMeshProUGUI _status;
    private Image _micButtonImage;
    private Image _levelFill;
    private TextMeshProUGUI _micLabel;

    private bool _panelWasOpen;
    private bool _preparing;
    private bool _prepareFailed;
    private string _capturedText = "";

    private void OnEnable()
    {
        _panelWasOpen = false;
        _capturedText = "";
    }

    private void OnDisable()
    {
        StopListening(false);
    }

    private void Update()
    {
        if (!ResolveFlow())
            return;

        bool open = _requirementsPanel != null &&
                    _requirementsPanel.activeInHierarchy;

        if (open != _panelWasOpen)
        {
            _panelWasOpen = open;
            if (open)
                HandlePanelOpened();
            else
                StopListening(false);
        }

        if (!open)
            return;

        EnsureUi();
        RefreshUi();
    }

    // ------------------------------------------------------------------
    // 참조 해석
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
            {
                Debug.LogWarning(
                    "[MVP 로비 STT] requirementsPanel 을 찾지 못해 " +
                    "음성 입력을 붙이지 못했습니다.");
                return false;
            }
        }

        return true;
    }

    private MvpOnDeviceDictation ResolveDictation()
    {
        if (_dictation != null)
            return _dictation;

        _dictation = GetComponent<MvpOnDeviceDictation>();
        if (_dictation == null)
            _dictation = gameObject.AddComponent<MvpOnDeviceDictation>();

        _dictation.OnPartial -= HandlePartial;
        _dictation.OnFinal -= HandleFinal;
        _dictation.OnPartial += HandlePartial;
        _dictation.OnFinal += HandleFinal;
        return _dictation;
    }

    // ------------------------------------------------------------------
    // 인식 제어
    // ------------------------------------------------------------------

    private void HandlePanelOpened()
    {
        _capturedText = "";
        _flow.ClearRequirementFeatureText();

        if (!MvpOnDeviceDictation.IsSupported())
        {
            _prepareFailed = true;
            Debug.LogWarning(
                "[MVP 로비 STT] 이 플랫폼에서는 음성 인식을 쓸 수 없습니다. " +
                "요구사항을 직접 입력해야 합니다.");
            return;
        }

        if (_autoListenOnOpen)
            BeginListening();
    }

    public void ToggleMic()
    {
        MvpOnDeviceDictation dictation = ResolveDictation();
        if (dictation.IsListening)
            StopListening(true);
        else
            BeginListening();
    }

    private void BeginListening()
    {
        if (_preparing)
            return;

        MvpOnDeviceDictation dictation = ResolveDictation();
        if (dictation.IsListening)
            return;

        if (!dictation.IsPrepared)
        {
            _preparing = true;
            dictation.Prepare(
                null,
                (ok, reason) =>
                {
                    _preparing = false;
                    _prepareFailed = !ok;
                    if (!ok)
                    {
                        Debug.LogWarning(
                            "[MVP 로비 STT] 준비 실패: " + reason);
                        return;
                    }
                    dictation.StartListening();
                });
            return;
        }

        if (!dictation.StartListening())
            _prepareFailed = true;
    }

    private void StopListening(bool commit)
    {
        if (_dictation == null || !_dictation.IsListening)
            return;

        string text = _dictation.StopListening();
        if (commit && !string.IsNullOrWhiteSpace(text))
            Commit(text);
    }

    private void HandlePartial(string partial)
    {
        if (_transcript != null && !string.IsNullOrWhiteSpace(partial))
        {
            _transcript.color = TextDim;
            _transcript.text = partial;
        }
    }

    private void HandleFinal(string final)
    {
        if (string.IsNullOrWhiteSpace(final))
            return;
        Commit(final);
    }

    private void Commit(string text)
    {
        _capturedText = text.Trim();
        _flow.SetRequirementFeatureText(_capturedText);
        if (_transcript != null)
        {
            _transcript.color = TextMain;
            _transcript.text = _capturedText;
        }
        Debug.Log("[MVP 로비 STT] 요구사항 인식: " + _capturedText);
    }

    /// <summary>인식된 요구사항 문장. 비어 있으면 방을 만들 수 없다.</summary>
    public string CapturedText => _capturedText;

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    private void EnsureUi()
    {
        if (_ui != null)
            return;

        RectTransform parent =
            _requirementsPanel.GetComponent<RectTransform>();
        if (parent == null)
            return;

        TMP_FontAsset font = FindPanelFont(parent);

        var root = new GameObject("MvpLobbyDictationUI",
            typeof(RectTransform), typeof(Image));
        _ui = root.GetComponent<RectTransform>();
        _ui.SetParent(parent, false);
        // 패널 아래쪽에 가로로 붙인다. 패널 크기를 몰라도 되도록 앵커를 쓴다.
        _ui.anchorMin = new Vector2(0.06f, 0.04f);
        _ui.anchorMax = new Vector2(0.94f, 0.28f);
        _ui.offsetMin = Vector2.zero;
        _ui.offsetMax = Vector2.zero;

        Image bg = root.GetComponent<Image>();
        bg.color = PanelBg;
        bg.raycastTarget = false;

        // 상태 문구
        _status = CreateText(_ui, font, "Status", 34f,
            new Vector2(0.02f, 0.66f), new Vector2(0.72f, 0.96f),
            TextAlignmentOptions.Left, TextDim);

        // 인식된 문장
        _transcript = CreateText(_ui, font, "Transcript", 44f,
            new Vector2(0.02f, 0.10f), new Vector2(0.72f, 0.64f),
            TextAlignmentOptions.TopLeft, TextMain);
        _transcript.enableWordWrapping = true;

        // 마이크 버튼
        var micGo = new GameObject("MicButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        var micRt = micGo.GetComponent<RectTransform>();
        micRt.SetParent(_ui, false);
        micRt.anchorMin = new Vector2(0.75f, 0.16f);
        micRt.anchorMax = new Vector2(0.98f, 0.84f);
        micRt.offsetMin = Vector2.zero;
        micRt.offsetMax = Vector2.zero;
        _micButtonImage = micGo.GetComponent<Image>();
        _micButtonImage.color = AccentBlue;
        micGo.GetComponent<Button>().onClick.AddListener(ToggleMic);

        _micLabel = CreateText(micRt, font, "Label", 38f,
            Vector2.zero, Vector2.one,
            TextAlignmentOptions.Center, TextMain);
        _micLabel.raycastTarget = false;

        // 입력 레벨 바 (말이 들어오는지 눈으로 확인)
        var levelBg = new GameObject("Level",
            typeof(RectTransform), typeof(Image));
        var levelRt = levelBg.GetComponent<RectTransform>();
        levelRt.SetParent(_ui, false);
        levelRt.anchorMin = new Vector2(0.02f, 0.02f);
        levelRt.anchorMax = new Vector2(0.72f, 0.07f);
        levelRt.offsetMin = Vector2.zero;
        levelRt.offsetMax = Vector2.zero;
        levelBg.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        levelBg.GetComponent<Image>().raycastTarget = false;

        var fillGo = new GameObject("Fill",
            typeof(RectTransform), typeof(Image));
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.SetParent(levelRt, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fillRt.pivot = new Vector2(0f, 0.5f);
        _levelFill = fillGo.GetComponent<Image>();
        _levelFill.color = AccentBlue;
        _levelFill.raycastTarget = false;
    }

    private void RefreshUi()
    {
        if (_status == null)
            return;

        bool listening = _dictation != null && _dictation.IsListening;

        if (_prepareFailed)
        {
            _status.text = "음성 인식을 쓸 수 없어요";
        }
        else if (_preparing)
        {
            _status.text = _preparingText;
        }
        else if (listening)
        {
            _status.text = _listeningText;
        }
        else if (!string.IsNullOrWhiteSpace(_capturedText))
        {
            _status.text = "이 내용으로 시작할게요";
        }
        else
        {
            _status.text = _idleText;
        }

        if (_transcript != null &&
            string.IsNullOrWhiteSpace(_transcript.text) &&
            !listening)
        {
            _transcript.color = TextDim;
            _transcript.text = "예) 물로켓이 더 멀리 날아가게 만들고 싶어요";
        }

        if (_micButtonImage != null)
            _micButtonImage.color = listening ? MicActive : AccentBlue;
        if (_micLabel != null)
            _micLabel.text = listening ? "완료" : "마이크";

        if (_levelFill != null)
        {
            float level = _dictation != null ? _dictation.Level : 0f;
            float width = Mathf.Clamp01(level * 6f);
            _levelFill.rectTransform.anchorMax =
                new Vector2(listening ? width : 0f, 1f);
        }
    }

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
        return text;
    }

    // 패널에 이미 쓰인 폰트를 그대로 재사용한다(한글 글리프 보장 + 디자인 일치).
    private static TMP_FontAsset FindPanelFont(RectTransform parent)
    {
        List<TextMeshProUGUI> texts = new List<TextMeshProUGUI>();
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
