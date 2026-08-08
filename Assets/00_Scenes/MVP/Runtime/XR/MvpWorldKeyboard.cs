using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEngine.UI;

// Quest 앱과 PC Link에서 동일하게 사용할 수 있는 MVP 전용 공간 키보드.
// PC Link에서는 TouchScreenKeyboard가 지원되지 않으므로 노드 옆 월드 공간에 직접 표시한다.
[DisallowMultipleComponent]
public class MvpWorldKeyboard : MonoBehaviour
{
    private static readonly string[] Initials =
    {
        "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ",
        "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"
    };

    private static readonly string[] Medials =
    {
        "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ",
        "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ",
        "ㅡ", "ㅢ", "ㅣ"
    };

    private static readonly string[] Finals =
    {
        "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ",
        "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ",
        "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"
    };

    private static MvpWorldKeyboard _instance;
    // 기존 크기의 80%. 손으로 잡기에는 충분하고, 시야를 덜 가린다.
    private const float KeyboardWorldScale = 0.000672f;

    private readonly Dictionary<string, int> _initialIndex =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> _medialIndex =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> _finalIndex =
        new Dictionary<string, int>();

    private Canvas _canvas;
    private RectTransform _panel;
    private RectTransform _keyRoot;
    private TMP_Text _preview;
    private TMP_Text _modeLabel;
    private TMP_InputField _target;
    private string _committed = "";
    private int _initial = -1;
    private int _medial = -1;
    private int _final;
    private bool _korean = true;
    // 숫자·기호 레이아웃. _korean 은 "숫자 모드에서 빠져나올 때 돌아갈 언어"로 유지된다.
    private bool _numeric;
    // Shift: 한글이면 쌍자음/ㅒㅖ, 영문이면 대문자. 모드가 바뀌면 풀린다.
    private bool _shift;

    public static bool IsOpen =>
        _instance != null && _instance.gameObject.activeInHierarchy;

    // 정면 레이어 UI 겹침 중재용 전역 신호.
    // 키보드는 눈앞 40cm 아래 35cm(= 추천 패널·테이블 도크와 같은 공간)에 열리므로,
    // 겹치는 패널들이 이 이벤트를 구독해 스스로 비켜난다(숨김/접기) — "앞 UI가 뒤 UI를
    // 가려서 조작 불가" 상황을 애초에 만들지 않는다.
    public static event Action OnOpenedGlobal;
    public static event Action OnClosedGlobal;

    // 지금 키보드가 편집 중인 입력칸(겹침 중재 시 "그 패널은 숨기면 안 됨" 판정용).
    public static TMP_InputField CurrentTarget =>
        _instance != null ? _instance._target : null;

    public static void Open(TMP_InputField target)
    {
        if (target == null)
            return;

        if (_instance == null)
        {
            GameObject root = new GameObject(
                "MvpWorldKeyboard",
                typeof(RectTransform));
            _instance = root.AddComponent<MvpWorldKeyboard>();
        }

        _instance.OpenInternal(target);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        BuildIndexMaps();
        BuildVisuals();
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void BuildIndexMaps()
    {
        for (int i = 0; i < Initials.Length; i++)
            _initialIndex[Initials[i]] = i;
        for (int i = 0; i < Medials.Length; i++)
            _medialIndex[Medials[i]] = i;
        for (int i = 1; i < Finals.Length; i++)
            _finalIndex[Finals[i]] = i;
    }

    // ── 디자인 시안 색상 ──────────────────────────────────────
    // 어두운 회색 판 + 파란 테두리, 키캡은 판보다 한 톤 밝은 회색, Enter 만 파랑.
    private static readonly Color PanelBg     = new Color(0.169f, 0.169f, 0.169f, 0.995f);
    private static readonly Color KeyCap      = new Color(0.290f, 0.290f, 0.290f, 1f);
    private static readonly Color KeyCapAlt   = new Color(0.235f, 0.235f, 0.235f, 1f);
    private static readonly Color FieldBg     = new Color(0.118f, 0.118f, 0.118f, 1f);
    private static readonly Color AccentBlue  = new Color(0.176f, 0.588f, 0.898f, 1f);
    private static readonly Color KeyInk      = new Color(0.94f, 0.95f, 0.96f, 1f);

    private void BuildVisuals()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 900;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(920f, 640f);   // 발화 상태 바가 위에 얹힌다
        root.localScale = Vector3.one * KeyboardWorldScale;

        BuildVoiceStatusBar(transform);

        Image background = MvpStudentUiFactory.CreatePanel(
            transform,
            "KeyboardPanel",
            new Vector2(0f, -36f),
            new Vector2(920f, 560f),
            PanelBg,
            true);
        _panel = background.rectTransform;

        Outline rim = background.GetComponent<Outline>();
        if (rim == null)
            rim = background.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.176f, 0.588f, 0.898f, 0.95f);
        rim.effectDistance = new Vector2(3f, -3f);

        // ── 윗줄: 마이크 / 입력칸 / 지우기 / 전체삭제 ──────────
        // 아이콘 글리프(🎤 ⌫ ⌨ 등)는 이 프로젝트 한글 폰트에 없어 두부(□)로 나온다.
        // 스프라이트가 준비될 때까지는 폰트에 있는 글자로 대체한다.
        _voiceKey = CreateIconKey(
            "Voice", "음성", new Vector2(-386f, 208f), new Vector2(76f, 76f),
            KeyCap, ToggleVoiceKey, 22f, true);
        _voiceKeyLabel = _voiceKey.GetComponentInChildren<TMP_Text>();
        _voiceIndicator = MvpVoiceIndicator.Attach(
            _voiceKey.transform, new Vector2(26f, 26f), 11f);

        Image previewPanel = MvpStudentUiFactory.CreatePanel(
            _panel,
            "PreviewPanel",
            new Vector2(-24f, 208f),
            new Vector2(620f, 68f),
            FieldBg,
            true);
        _preview = MvpStudentUiFactory.CreateText(
            previewPanel.transform,
            "Preview",
            "",
            Vector2.zero,
            new Vector2(580f, 56f),
            28f,
            TextAlignmentOptions.MidlineLeft,
            true,
            KeyInk,
            1);

        CreateIconKey(
            "Backspace", "지움", new Vector2(322f, 208f), new Vector2(76f, 76f),
            KeyCap, Backspace, 22f, true);

        CreateIconKey(
            "Clear", "Clear", new Vector2(408f, 208f), new Vector2(84f, 76f),
            KeyCap, ClearAll, 22f, true);

        // 모드 표시는 입력칸 위 작은 라벨로만 남긴다(시안엔 큰 제목이 없다).
        _modeLabel = MvpStudentUiFactory.CreateText(
            _panel,
            "Mode",
            "한글 입력",
            new Vector2(-24f, 256f),
            new Vector2(620f, 30f),
            18f,
            TextAlignmentOptions.MidlineLeft,
            true,
            new Color(0.62f, 0.66f, 0.70f, 1f),
            1);

        _keyRoot = MvpStudentUiFactory.CreateRect(
            _panel,
            "Keys",
            new Vector2(0f, -32f),
            new Vector2(880f, 420f));

        EnsureKeyboardGrabHandle(root);
        RebuildKeys();
    }

    // ── 발화 상태 바 ─────────────────────────────────────────
    // 시안: 키보드 위에 키보드 가로폭만큼의 알약 바. 테두리가 도는 그라디언트(디자이너 셰이더)
    // 안에 상태 문구가 들어간다. 왼쪽 캐릭터 자리는 이미지가 아직 없어 비워 둔다.
    private RectTransform _voiceBar;
    private TMP_Text _voiceBarLabel;
    private Image _voiceBarCharacter;
    private Sprite _bearListening;
    private Sprite _bearConverting;
    private Sprite _bearDone;

    // 상태 바가 보여 줄 3단계.
    internal enum VoicePhase { Listening, Converting, Done }

    private void BuildVoiceStatusBar(Transform parent)
    {
        Image bar = MvpStudentUiFactory.CreatePanel(
            parent,
            "VoiceStatusBar",
            new Vector2(0f, 280f),
            new Vector2(920f, 64f),
            new Color(0.22f, 0.19f, 0.13f, 0.92f),
            true);
        _voiceBar = bar.rectTransform;

        // 디자이너의 회전 그라디언트 테두리. 셰이더가 없으면 컴포넌트를 붙이지 않는다.
        Shader border = Shader.Find("UI/RotatingGradientBorder");
        if (border != null)
        {
            RotatingGradientBorderUI gradient =
                bar.gameObject.AddComponent<RotatingGradientBorderUI>();
            gradient.shader = border;
            gradient.rotationSpeed = 0.619f;
            gradient.borderWidth = 3f;
            gradient.cornerRadius = 32f;   // 알약 모양(높이 64의 절반)
            gradient.bgAngle = 49f;
            gradient.gradientResolution = 250;
            gradient.borderGradient = MakeGradient(
                new Color(0.925f, 0.684f, 0.109f, 1f), Color.white, 1f, 1f);
            gradient.bgGradient = MakeGradient(
                new Color(1f, 0.740f, 0f, 1f), Color.white, 0.329f, 0.102f);
        }

        // 왼쪽 캐릭터. 원본이 정사각(762x762)이라 바 높이보다 크게 잡아 살짝 넘치게 둔다.
        _bearListening  = Resources.Load<Sprite>("VoiceBear/bear_listening");
        _bearConverting = Resources.Load<Sprite>("VoiceBear/bear_converting");
        _bearDone       = Resources.Load<Sprite>("VoiceBear/bear_done");

        _voiceBarCharacter = MvpStudentUiFactory.CreatePanel(
            bar.transform,
            "Character",
            new Vector2(-410f, 10f),
            new Vector2(132f, 132f),   // 바(64)보다 크게 — 시안처럼 위아래로 걸친다
            Color.white,
            false);
        _voiceBarCharacter.raycastTarget = false;
        _voiceBarCharacter.preserveAspect = true;
        _voiceBarCharacter.sprite = _bearListening;
        _voiceBarCharacter.type = Image.Type.Simple;

        _voiceBarLabel = MvpStudentUiFactory.CreateText(
            bar.transform,
            "VoiceStatusLabel",
            "발화를 듣고 있어요",
            new Vector2(30f, 0f),   // 왼쪽 곰과 겹치지 않게 살짝 오른쪽
            new Vector2(700f, 44f),
            24f,
            TextAlignmentOptions.Center,
            true,
            // 배경이 밝은 금빛 그라디언트라 흰 글씨는 안 보인다. 어두운 톤으로.
            new Color(0.20f, 0.16f, 0.08f, 1f),
            1);

        _voiceBar.gameObject.SetActive(false);   // 말할 때만 나타난다
    }

    private static Gradient MakeGradient(Color a, Color b, float alphaA, float alphaB)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
            new[] { new GradientAlphaKey(alphaA, 0f), new GradientAlphaKey(alphaB, 1f) });
        return g;
    }

    // 음성 상태에 따라 바를 보이고 문구를 바꾼다.
    //   듣는 중 → 변환 중 → 완료(잠깐 보였다 사라짐)
    internal void SetVoiceStatus(string message, bool visible)
    {
        SetVoiceStatus(message, visible, VoicePhase.Listening);
    }

    internal void SetVoiceStatus(string message, bool visible, VoicePhase phase)
    {
        if (_voiceBar == null)
            return;

        _voiceBar.gameObject.SetActive(visible);
        if (!visible)
            return;

        if (_voiceBarLabel != null)
            _voiceBarLabel.text = message;

        if (_voiceBarCharacter != null)
        {
            Sprite bear =
                phase == VoicePhase.Done ? _bearDone :
                phase == VoicePhase.Converting ? _bearConverting :
                _bearListening;
            if (bear != null)
                _voiceBarCharacter.sprite = bear;
        }
    }

    // 시안의 원형/사각 보조 키. 라벨을 그대로 쓰되 글꼴 크기와 색을 따로 준다.
    private Button CreateIconKey(
        string name, string label, Vector2 position, Vector2 size,
        Color color, Action action, float fontSize, bool onPanel)
    {
        Button key = MvpStudentUiFactory.CreateButton(
            onPanel ? _panel : _keyRoot,
            name,
            label,
            position,
            size,
            color,
            () =>
            {
                if (MvpAudioCue.Instance != null)
                    MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick, 0.7f);
                action?.Invoke();
            },
            fontSize);

        TMP_Text text = key.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.color = KeyInk;
        Outline outline = key.GetComponent<Outline>();
        if (outline != null)
            outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
        return key;
    }

    // 입력칸을 통째로 비운다(시안의 Clear).
    private void ClearAll()
    {
        _committed = "";
        _initial = -1;
        _medial = -1;
        _final = 0;
        _voicePartial = "";
        RefreshPreview();
    }

    private void OpenInternal(TMP_InputField target)
    {
        _target = target;
        _committed = (target.text ?? "").Replace("\u200B", string.Empty);
        _initial = -1;
        _medial = -1;
        _final = 0;

        gameObject.SetActive(true);
        PlaceNearTarget(target);
        RebuildKeys();
        RefreshPreview();
        OnOpenedGlobal?.Invoke();

        target.DeactivateInputField();

        MvpXrInteractionBridge bridge =
            FindFirstObjectByType<MvpXrInteractionBridge>();
        bridge?.WireScene();

        DimObstructingNodes(true);   // 키보드 앞을 가리는 노드를 흐리게
    }

    private struct MvpDimmedNode
    {
        public CanvasGroup group;
        public float prevAlpha;
    }

    private readonly List<MvpDimmedNode> _dimmedNodes =
        new List<MvpDimmedNode>();

    private void OnDisable()
    {
        DimObstructingNodes(false);   // 키보드 닫히면 노드 복원
    }

    // 카메라와 키보드 사이(시선을 가리는) 노드를 흐리게 하고, 닫힐 때 원래대로 되돌린다.
    private void DimObstructingNodes(bool dim)
    {
        if (!dim)
        {
            foreach (MvpDimmedNode d in _dimmedNodes)
                if (d.group != null)
                    d.group.alpha = d.prevAlpha;
            _dimmedNodes.Clear();
            return;
        }

        _dimmedNodes.Clear();
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 toKb = transform.position - camPos;
        float kbDist = toKb.magnitude;
        if (kbDist < 0.05f) return;
        toKb /= kbDist;

        foreach (NodeView nv in
                 FindObjectsByType<NodeView>(FindObjectsSortMode.None))
        {
            if (nv == null) continue;
            Vector3 np = nv.transform.position;
            float along = Vector3.Dot(np - camPos, toKb);
            if (along <= 0.1f || along >= kbDist - 0.05f)
                continue;   // 카메라 뒤이거나 키보드보다 멀면 가리지 않음
            Vector3 proj = camPos + toKb * along;
            if (Vector3.Distance(np, proj) > 0.45f)
                continue;   // 시선에서 벗어나면 제외

            Transform canvas = nv.transform.Find("Canvas");
            CanvasGroup cg = canvas != null
                ? canvas.GetComponent<CanvasGroup>()
                : null;
            if (cg == null && canvas != null)
                cg = canvas.gameObject.AddComponent<CanvasGroup>();
            if (cg == null) continue;

            _dimmedNodes.Add(new MvpDimmedNode { group = cg, prevAlpha = cg.alpha });
            cg.alpha = 0.2f;
        }
    }

    private void PlaceNearTarget(TMP_InputField target)
    {
        // 시스템 키보드처럼: 대상이 노드든 뭐든, 키보드는 항상 유저(카메라) 정면에 생성한다.
        Camera camera = Camera.main;
        if (camera == null)
        return;

        Vector3 forward = Vector3.ProjectOnPlane(
        camera.transform.forward,
        Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
        forward = camera.transform.forward.normalized;

        // 학생의 팔을 과하게 뻗지 않도록 눈 기준 전방 40cm,
        // 아래 35cm의 편안한 타이핑 영역에 둔다.
        transform.position =
        camera.transform.position + forward * 0.40f -
        Vector3.up * 0.35f;
        // 수평으로 유저를 향하게(+Z=유저 반대=읽기 정상) 한 뒤, 틸트는 반대 방향 +22.5도만 적용.
        // (수직 성분을 넣으면 아래로 향하는 각이 더해져 22.5보다 커진다 — 순수 22.5 유지)
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up) *
        Quaternion.Euler(22.5f, 0f, 0f);
    }

    private void RebuildKeys()
    {
        if (_keyRoot == null)
        return;

        for (int i = _keyRoot.childCount - 1; i >= 0; i--)
        Destroy(_keyRoot.GetChild(i).gameObject);

        if (_numeric)
        BuildNumericKeys();
        else if (_korean)
        BuildKoreanKeys();
        else
        BuildEnglishKeys();

        if (_modeLabel != null)
        {
            _modeLabel.text = _numeric
            ? "숫자·기호"
            : _korean
                ? (_shift ? "한글 입력 (쌍자음)" : "한글 입력")
                : (_shift ? "영문 입력 (대문자)" : "영문 입력");
            _modeLabel.color = _numeric
            ? MvpStudentUiFactory.Mint
            : _korean
                ? MvpStudentUiFactory.HoloCyan
                : MvpStudentUiFactory.Amber;
        }

        PrepareKeyboardButtons();
        MvpXrInteractionBridge bridge =
        FindFirstObjectByType<MvpXrInteractionBridge>();
        bridge?.WireScene();
    }

    private void PrepareKeyboardButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null)
            continue;

            Graphic target = button.targetGraphic;
            if (target == null)
            target = button.GetComponent<Graphic>() ??
                     button.GetComponentInChildren<Graphic>(true);
            if (target != null)
            {
                target.raycastTarget = true;
                button.targetGraphic = target;
            }
        }
    }


    // 시안 기준 행 y 좌표. 숫자줄이 항상 맨 위에 있고 그 아래 문자 3줄, 맨 아래 기능줄.
    private const float RowNumber = 156f;
    private const float RowTop    =  84f;
    private const float RowMid    =  12f;
    private const float RowBottom = -60f;
    private const float RowAction = -140f;

    private void BuildKoreanKeys()
    {
        BuildNumberRow();
        // 두벌식 그대로: Shift 를 누르면 윗줄이 쌍자음(ㅃㅉㄸㄲㅆ)과 ㅒ/ㅖ 로 바뀐다.
        CreateRow(
            _shift
            ? new[] { "ㅃ", "ㅉ", "ㄸ", "ㄲ", "ㅆ", "ㅛ", "ㅕ", "ㅑ", "ㅒ", "ㅖ" }
            : new[] { "ㅂ", "ㅈ", "ㄷ", "ㄱ", "ㅅ", "ㅛ", "ㅕ", "ㅑ", "ㅐ", "ㅔ" },
            RowTop, 76f);
        CreateRow(
            new[] { "ㅁ", "ㄴ", "ㅇ", "ㄹ", "ㅎ", "ㅗ", "ㅓ", "ㅏ", "ㅣ" },
            RowMid, 76f);
        BuildShiftRow(new[] { "ㅋ", "ㅌ", "ㅊ", "ㅍ", "ㅠ", "ㅜ", "ㅡ" });
        BuildActionRow();
    }

    private void BuildEnglishKeys()
    {
        BuildNumberRow();
        // 기본은 소문자, Shift 를 누르면 대문자. 키캡 글자가 곧 입력될 글자다.
        CreateRow(ShiftCase(
            new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" }), RowTop, 76f);
        CreateRow(ShiftCase(
            new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l" }), RowMid, 76f);
        BuildShiftRow(ShiftCase(new[] { "z", "x", "c", "v", "b", "n", "m" }));
        BuildActionRow();
    }

    private void BuildNumericKeys()
    {
        BuildNumberRow();
        CreateRow(
            new[] { "-", "/", ":", ";", "(", ")", "₩", "&", "@", "\"" }, RowTop, 76f);
        CreateRow(
            new[] { ".", ",", "?", "!", "'", "+", "=", "_", "%" }, RowMid, 76f);
        BuildShiftRow(new[] { "<", ">", "[", "]", "{", "}", "~" });
        BuildActionRow();
    }

    // 시안의 맨 윗줄 숫자 — 모드와 무관하게 항상 보인다.
    private void BuildNumberRow()
    {
        CreateRow(
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
            RowNumber, 76f);
    }

    // 시안의 셋째 줄: 왼쪽 ⇧, 가운데 글자들, 오른쪽 키보드 내리기.
    private void BuildShiftRow(string[] middle)
    {
        CreateFunctionKey(
            "⇧", -366f, RowBottom, 96f,
            _numeric ? (Action)null : ToggleShift,
            _shift ? AccentBlue : KeyCapAlt, 30f);

        float width = 76f, gap = 9f;
        float total = middle.Length * width + (middle.Length - 1) * gap;
        float start = -total * 0.5f + width * 0.5f;
        for (int i = 0; i < middle.Length; i++)
        {
            string key = middle[i];
            CreateKey(key, start + i * (width + gap), RowBottom, width,
                () => PressCharacter(key));
        }

        CreateFunctionKey("닫기", 366f, RowBottom, 96f, CloseWithoutApply, KeyCapAlt, 20f);
    }

    // 시안의 맨 아랫줄: 한/영(시안의 이모지 자리) · #+= · Space · Enter.
    private void BuildActionRow()
    {
        CreateFunctionKey(
            _korean ? "한" : "A", -378f, RowAction, 76f,
            _numeric ? (Action)null : ToggleMode, KeyCapAlt, 26f);

        CreateFunctionKey(
            _numeric ? (_korean ? "가나다" : "ABC") : "#+=",
            -282f, RowAction, 108f, ToggleNumeric, KeyCapAlt, 22f);

        CreateFunctionKey("Space", 34f, RowAction, 500f, AddSpace, KeyCapAlt, 22f);
        CreateFunctionKey("Enter", 366f, RowAction, 150f, ApplyAndClose, AccentBlue, 24f);
    }

    // 기능 키(문자 입력이 아닌 것). action 이 null 이면 눌리지 않는다.
    private void CreateFunctionKey(
        string label, float x, float y, float width, Action action, Color color, float fontSize)
    {
        Button key = MvpStudentUiFactory.CreateButton(
            _keyRoot,
            "Key_" + label,
            label,
            new Vector2(x, y),
            new Vector2(width, 62f),
            color,
            () =>
            {
                if (MvpAudioCue.Instance != null)
                    MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick, 0.7f);
                action?.Invoke();
            },
            fontSize);
        key.interactable = action != null;

        TMP_Text text = key.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.color = KeyInk;
        Outline outline = key.GetComponent<Outline>();
        if (outline != null)
            outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
    }

    private string[] ShiftCase(string[] lower)
    {
        if (!_shift)
            return lower;
        string[] upper = new string[lower.Length];
        for (int i = 0; i < lower.Length; i++)
            upper[i] = lower[i].ToUpperInvariant();
        return upper;
    }

    private void CreateRow(string[] labels, float y, float width)
    {
        float gap = 8f;
        float total = labels.Length * width + (labels.Length - 1) * gap;
        float start = -total * 0.5f + width * 0.5f;
        for (int i = 0; i < labels.Length; i++)
        {
            string key = labels[i];
            CreateKey(
                key,
                start + i * (width + gap),
                y,
                width,
                () => PressCharacter(key));
        }
    }

    private void CreateKey(
        string label,
        float x,
        float y,
        float width,
        Action action)
    {
        Button key = MvpStudentUiFactory.CreateButton(
            _keyRoot,
            "Key_" + label,
            label,
            new Vector2(x, y),
            new Vector2(width, 62f),
            KeyCap,
            () =>
            {
                if (MvpAudioCue.Instance != null)
                    MvpAudioCue.Instance.Play(MvpAudioCue.Cue.KeyClick, 0.7f);
                action?.Invoke();
            },
            26f);

        TMP_Text text = key.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.color = KeyInk;

        // 살짝 튀어나온 키캡 테두리. 포킹 목표도 각 키의 실제 사각형과 일치한다.
        Outline outline = key.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
            outline.effectDistance = new Vector2(1f, -2f);
        }
    }

    private void PressCharacter(string value)
    {
        // 숫자·기호와 영문은 조합 없이 그대로 붙인다. 키캡에 보이는 글자가 곧 입력값이다
        // (예전엔 영문을 무조건 소문자로 바꿔 넣어 대문자를 칠 방법이 아예 없었다).
        if (_numeric || !_korean)
        {
            AddLiteral(value);
            return;
        }

        if (_medialIndex.ContainsKey(value))
            AddVowel(value);
        else
            AddConsonant(value);
        RefreshPreview();
    }

    private void AddConsonant(string consonant)
    {
        if (_initial < 0)
        {
            if (_initialIndex.TryGetValue(consonant, out int nextInitial))
                _initial = nextInitial;
            else
                _committed += consonant;
            return;
        }

        if (_medial < 0)
        {
            string combined = CombineInitial(Initials[_initial], consonant);
            if (combined != null)
            {
                _initial = _initialIndex[combined];
                return;
            }

            _committed += Initials[_initial];
            _initial = _initialIndex.TryGetValue(
                consonant,
                out int replacement) ? replacement : -1;
            if (_initial < 0)
                _committed += consonant;
            return;
        }

        if (_final == 0)
        {
            if (_finalIndex.TryGetValue(consonant, out int nextFinal))
                _final = nextFinal;
            else
            {
                CommitComposition();
                AddConsonant(consonant);
            }
            return;
        }

        string compound = CombineFinal(Finals[_final], consonant);
        if (compound != null)
        {
            _final = _finalIndex[compound];
            return;
        }

        CommitComposition();
        AddConsonant(consonant);
    }

    private void AddVowel(string vowel)
    {
        int vowelIndex = _medialIndex[vowel];

        if (_initial < 0)
        {
            _committed += vowel;
            return;
        }

        if (_medial < 0)
        {
            _medial = vowelIndex;
            return;
        }

        if (_final == 0)
        {
            string compound = CombineMedial(Medials[_medial], vowel);
            if (compound != null)
            {
                _medial = _medialIndex[compound];
                return;
            }

            CommitComposition();
            _committed += vowel;
            return;
        }

        string movedInitial;
        string remainingFinal;
        SplitFinal(Finals[_final], out remainingFinal, out movedInitial);
        _final = string.IsNullOrEmpty(remainingFinal)
            ? 0
            : _finalIndex[remainingFinal];
        _committed += CurrentComposition();
        _initial = _initialIndex.TryGetValue(
            movedInitial,
            out int nextInitial) ? nextInitial : -1;
        _medial = vowelIndex;
        _final = 0;
    }

    private void AddLiteral(string value)
    {
        CommitComposition();
        _committed += value;
        RefreshPreview();
    }

    private void AddSpace()
    {
        CommitComposition();
        if (_committed.Length == 0 ||
            !_committed.EndsWith(" ", StringComparison.Ordinal))
            _committed += " ";
        RefreshPreview();
    }

    private void Backspace()
    {
        if (_final != 0)
        {
            string final = Finals[_final];
            string first;
            string second;
            SplitFinal(final, out first, out second);
            _final = first != final && !string.IsNullOrEmpty(first)
                ? _finalIndex[first]
                : 0;
        }
        else if (_medial >= 0)
        {
            string medial = Medials[_medial];
            string first = SplitMedial(medial);
            _medial = first != null ? _medialIndex[first] : -1;
        }
        else if (_initial >= 0)
        {
            string initial = Initials[_initial];
            string first = SplitInitial(initial);
            _initial = first != null ? _initialIndex[first] : -1;
        }
        else if (_committed.Length > 0)
        {
            _committed =
                _committed.Substring(0, _committed.Length - 1);
        }

        RefreshPreview();
    }

    private void ToggleMode()
    {
        CommitComposition();
        _korean = !_korean;
        _numeric = false;   // 한/영 은 항상 문자 레이아웃으로 돌아온다
        _shift = false;

        RebuildKeys();
        RefreshPreview();
    }

    // 쌍자음/대문자 전환. 한 글자만 바꾸는 게 아니라 계속 켜져 있는 토글이다
    // (VR 에서 "누르고 있기"가 어려워 실제 키보드의 CapsLock 처럼 동작시킨다).
    private void ToggleShift()
    {
        _shift = !_shift;
        RebuildKeys();
    }

    // 숫자·기호 레이아웃 ↔ 직전 언어. Shift 는 레이아웃이 달라지므로 함께 푼다.
    private void ToggleNumeric()
    {
        CommitComposition();
        _numeric = !_numeric;
        _shift = false;
        RebuildKeys();
        RefreshPreview();
    }

    private void ApplyAndClose()
    {
        CommitComposition();
        string value = _committed.Trim();

        if (_target != null && !string.IsNullOrEmpty(value))
        {
            _target.SetTextWithoutNotify(value);

            // 노드와 직접 파트 입력은 onEndEdit, + 파트 포트와 이름 변경은
            // onSubmit을 사용한다. 월드 키보드 Enter는 실제 키보드처럼 둘 다 전달한다.
            _target.onEndEdit.Invoke(value);
            _target.onSubmit.Invoke(value);
            _target.DeactivateInputField();
        }

        CloseWithoutApply();
    }

    private void CloseWithoutApply()
    {
        if (_dictation != null && _dictation.IsListening)
            _dictation.StopListening();
        _voicePartial = "";
        SetVoiceKeyState("말하기", VoiceKeyIdleColor);

        _target = null;
        _initial = -1;
        _medial = -1;
        _final = 0;
        gameObject.SetActive(false);
        OnClosedGlobal?.Invoke();
    }

    private void CommitComposition()
    {
        string current = CurrentComposition();
        if (!string.IsNullOrEmpty(current))
            _committed += current;
        _initial = -1;
        _medial = -1;
        _final = 0;
    }

    private void RefreshPreview()
    {
        if (_preview == null)
            return;
        // 음성 인식 중이면 아직 확정 전인 부분 자막을 회색 톤으로 이어 보여 준다.
        string voice = string.IsNullOrEmpty(_voicePartial)
            ? ""
            : "<color=#7C8CA6>" + _voicePartial + "</color>";
        _preview.text = _committed + CurrentComposition() + voice + "▌";
    }

    // ─────────────────────────────────────────────
    // '말하기' 키 — 어떤 입력칸이든 온디바이스 받아쓰기로 채운다
    // ─────────────────────────────────────────────

    private static readonly Color VoiceKeyIdleColor =
        new Color(0.36f, 0.28f, 0.62f, 1f);
    private static readonly Color VoiceKeyActiveColor =
        new Color(0.72f, 0.30f, 0.38f, 1f);

    private Button _voiceKey;
    private TMP_Text _voiceKeyLabel;
    private MvpVoiceIndicator _voiceIndicator;
    private MvpOnDeviceDictation _dictation;
    private string _voicePartial = "";

    private void ToggleVoiceKey()
    {
        // 듣는 중 → 수동 확정
        if (_dictation != null && _dictation.IsListening)
        {
            _dictation.StopListening();   // 들은 게 있으면 OnFinal 로 반영
            SetVoiceKeyState("말하기", VoiceKeyIdleColor);
            _voicePartial = "";
            RefreshPreview();
            return;
        }

        if (!MvpOnDeviceDictation.IsSupported())
        {
            if (_modeLabel != null)
                _modeLabel.text = "음성 미지원 기기";
            return;
        }

        if (_dictation == null)
        {
            _dictation = GetComponent<MvpOnDeviceDictation>();
            if (_dictation == null)
                _dictation = gameObject.AddComponent<MvpOnDeviceDictation>();
            _dictation.OnPartial += HandleVoicePartial;
            _dictation.OnFinal += HandleVoiceFinal;
        }

        if (_dictation.IsPrepared)
        {
            StartVoiceKey();
            return;
        }

        SetVoiceKeyState("준비 중…", VoiceKeyIdleColor);
        _dictation.Prepare(
            null,
            (ok, reason) =>
            {
                if (this == null || !gameObject.activeInHierarchy)
                    return;
                if (ok)
                {
                    StartVoiceKey();
                    return;
                }
                SetVoiceKeyState("말하기", VoiceKeyIdleColor);
                if (_modeLabel != null)
                    _modeLabel.text = reason ?? "음성 사용 불가";
            });
    }

    private void StartVoiceKey()
    {
        if (_dictation.StartListening())
        {
            SetVoiceKeyState("듣는 중", VoiceKeyActiveColor);
        }
        else
        {
            SetVoiceKeyState("말하기", VoiceKeyIdleColor);
            if (_modeLabel != null)
                _modeLabel.text = "마이크를 찾지 못했어요";
        }
    }

    // 마이크는 아이콘 버튼이라 라벨을 글자로 바꾸지 않는다.
    // 상태는 색·인디케이터와 위쪽 발화 상태 바로 보여 준다.
    private void SetVoiceKeyState(string label, Color color)
    {
        if (_voiceKey != null)
            MvpStudentUiFactory.SetButtonColor(_voiceKey, color);

        if (_voiceIndicator != null)
            _voiceIndicator.SetState(
                label == "듣는 중"
                    ? MvpVoiceIndicator.State.Listening
                    : label == "준비 중…"
                        ? MvpVoiceIndicator.State.Preparing
                        : MvpVoiceIndicator.State.Idle,
                _dictation);

        if (label == "듣는 중")
            SetVoiceStatus("발화를 듣고 있어요", true, VoicePhase.Listening);
        else if (label == "준비 중…")
            SetVoiceStatus("음성 인식을 준비하고 있어요", true, VoicePhase.Listening);
        else
            SetVoiceStatus("", false);
    }

    private void HandleVoicePartial(string text)
    {
        _voicePartial = text;
        // 말이 들어오기 시작하면 "변환 중"으로 넘어간다(시안 2단계).
        SetVoiceStatus("발화를 텍스트로 변환하고 있어요", true, VoicePhase.Converting);
        RefreshPreview();
    }

    private void HandleVoiceFinal(string text)
    {
        CommitComposition();
        if (_committed.Length > 0 &&
            !_committed.EndsWith(" ", StringComparison.Ordinal))
            _committed += " ";
        _committed += text;
        _voicePartial = "";

        if (_voiceKey != null)
            MvpStudentUiFactory.SetButtonColor(_voiceKey, VoiceKeyIdleColor);
        if (_voiceIndicator != null)
            _voiceIndicator.SetState(MvpVoiceIndicator.State.Idle, _dictation);

        // 시안 3단계: 완료 문구를 잠깐 보여 준 뒤 바를 접는다.
        SetVoiceStatus("인식이 완료되었어요", true, VoicePhase.Done);
        CancelInvoke(nameof(HideVoiceStatus));
        Invoke(nameof(HideVoiceStatus), 1.4f);

        RefreshPreview();
    }

    private void HideVoiceStatus()
    {
        SetVoiceStatus("", false);
    }

    private string CurrentComposition()
    {
        if (_initial < 0)
            return "";

        if (_medial < 0)
            return Initials[_initial];

        int code =
            0xAC00 +
            (_initial * 21 + _medial) * 28 +
            Mathf.Clamp(_final, 0, 27);
        return char.ConvertFromUtf32(code);
    }

    private static string CombineInitial(string first, string second)
    {
        if (first == "ㄱ" && second == "ㄱ") return "ㄲ";
        if (first == "ㄷ" && second == "ㄷ") return "ㄸ";
        if (first == "ㅂ" && second == "ㅂ") return "ㅃ";
        if (first == "ㅅ" && second == "ㅅ") return "ㅆ";
        if (first == "ㅈ" && second == "ㅈ") return "ㅉ";
        return null;
    }

    private static string SplitInitial(string value)
    {
        if (value == "ㄲ") return "ㄱ";
        if (value == "ㄸ") return "ㄷ";
        if (value == "ㅃ") return "ㅂ";
        if (value == "ㅆ") return "ㅅ";
        if (value == "ㅉ") return "ㅈ";
        return null;
    }

    private static string CombineMedial(string first, string second)
    {
        if (first == "ㅗ" && second == "ㅏ") return "ㅘ";
        if (first == "ㅗ" && second == "ㅐ") return "ㅙ";
        if (first == "ㅗ" && second == "ㅣ") return "ㅚ";
        if (first == "ㅜ" && second == "ㅓ") return "ㅝ";
        if (first == "ㅜ" && second == "ㅔ") return "ㅞ";
        if (first == "ㅜ" && second == "ㅣ") return "ㅟ";
        if (first == "ㅡ" && second == "ㅣ") return "ㅢ";
        return null;
    }

    private static string SplitMedial(string value)
    {
        if (value == "ㅘ" || value == "ㅙ" || value == "ㅚ") return "ㅗ";
        if (value == "ㅝ" || value == "ㅞ" || value == "ㅟ") return "ㅜ";
        if (value == "ㅢ") return "ㅡ";
        return null;
    }

    private static string CombineFinal(string first, string second)
    {
        if (first == "ㄱ" && second == "ㄱ") return "ㄲ";
        if (first == "ㄱ" && second == "ㅅ") return "ㄳ";
        if (first == "ㄴ" && second == "ㅈ") return "ㄵ";
        if (first == "ㄴ" && second == "ㅎ") return "ㄶ";
        if (first == "ㄹ" && second == "ㄱ") return "ㄺ";
        if (first == "ㄹ" && second == "ㅁ") return "ㄻ";
        if (first == "ㄹ" && second == "ㅂ") return "ㄼ";
        if (first == "ㄹ" && second == "ㅅ") return "ㄽ";
        if (first == "ㄹ" && second == "ㅌ") return "ㄾ";
        if (first == "ㄹ" && second == "ㅍ") return "ㄿ";
        if (first == "ㄹ" && second == "ㅎ") return "ㅀ";
        if (first == "ㅂ" && second == "ㅅ") return "ㅄ";
        if (first == "ㅅ" && second == "ㅅ") return "ㅆ";
        return null;
    }

    private static void SplitFinal(
        string value,
        out string first,
        out string second)
    {
        first = "";
        second = value;

        if (value == "ㄲ") { first = "ㄱ"; second = "ㄱ"; }
        else if (value == "ㄳ") { first = "ㄱ"; second = "ㅅ"; }
        else if (value == "ㄵ") { first = "ㄴ"; second = "ㅈ"; }
        else if (value == "ㄶ") { first = "ㄴ"; second = "ㅎ"; }
        else if (value == "ㄺ") { first = "ㄹ"; second = "ㄱ"; }
        else if (value == "ㄻ") { first = "ㄹ"; second = "ㅁ"; }
        else if (value == "ㄼ") { first = "ㄹ"; second = "ㅂ"; }
        else if (value == "ㄽ") { first = "ㄹ"; second = "ㅅ"; }
        else if (value == "ㄾ") { first = "ㄹ"; second = "ㅌ"; }
        else if (value == "ㄿ") { first = "ㄹ"; second = "ㅍ"; }
        else if (value == "ㅀ") { first = "ㄹ"; second = "ㅎ"; }
        else if (value == "ㅄ") { first = "ㅂ"; second = "ㅅ"; }
    }


    private void EnsureKeyboardGrabHandle(RectTransform root)
    {
        if (transform.Find("MvpKeyboardGrabHandle") != null)
        return;

        Image visual = MvpStudentUiFactory.CreatePanel(
        _panel,
        "MoveHandleVisual",
        new Vector2(0f, 246f),
        new Vector2(220f, 32f),
        new Color(0.10f, 0.42f, 0.62f, 0.9f),
        false);
        visual.raycastTarget = false;
        MvpStudentUiFactory.CreateText(
        visual.transform,
        "MoveHandleLabel",
        "잡고 이동",
        Vector2.zero,
        new Vector2(190f, 28f),
        16f,
        TextAlignmentOptions.Center,
        true,
        Color.white,
        1).raycastTarget = false;

        GameObject handle = new GameObject("MvpKeyboardGrabHandle");
        handle.transform.SetParent(transform, false);
        // 키보드 중앙에 두고, 콜라이더로 키보드 '전체'를 덮는다.
        // 이러면 키보드 어디를 핀치해도 잡히고, 뒤 노드로 레이가 새지 않는다(핀치=그랩, 포크=키).
        handle.transform.localPosition = new Vector3(0f, 0f, -18f);

        BoxCollider collider = handle.AddComponent<BoxCollider>();
        collider.size = new Vector3(780f, 600f, 64f);
        collider.isTrigger = true;

        Rigidbody body = handle.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        MvpKeyboardFreeTransformer transformer =
        handle.AddComponent<MvpKeyboardFreeTransformer>();
        transformer.Configure(transform);

        Grabbable grabbable = handle.AddComponent<Grabbable>();
        grabbable.MaxGrabPoints = 1;
        grabbable.InjectOptionalTargetTransform(transform);
        grabbable.InjectOptionalRigidbody(body);
        grabbable.InjectOptionalThrowWhenUnselected(false);
        grabbable.InjectOptionalOneGrabTransformer(transformer);

        GrabInteractable controllerGrab =
        handle.AddComponent<GrabInteractable>();
        controllerGrab.InjectAllGrabInteractable(body);
        controllerGrab.InjectOptionalPointableElement(grabbable);
        controllerGrab.UseClosestPointAsGrabSource = true;
        controllerGrab.ReleaseDistance = 0.16f;

        HandGrabInteractable handGrab =
        handle.AddComponent<HandGrabInteractable>();
        handGrab.InjectAllHandGrabInteractable(
        GrabTypeFlags.Pinch,
        body,
        GrabbingRule.DefaultPinchRule,
        GrabbingRule.DefaultPalmRule);
        handGrab.InjectOptionalPointableElement(grabbable);
    }
}
