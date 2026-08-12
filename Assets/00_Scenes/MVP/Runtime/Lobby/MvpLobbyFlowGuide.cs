using TMPro;
using UnityEngine;

/// <summary>
/// 로비를 "한 번에 한 단계"로 보여준다.
///
/// 디자이너 로비는 캔버스 3개가 동시에 떠 있고, 그중 PWPanel(참가용 비밀번호)이
/// 기본으로 켜져 있어 방을 만들 때도 화면에 겹쳐 보였다. 처음 쓰는 사람은
/// 어디에 뭘 입력해야 하는지 알 수 없다.
///
/// 여기서는 원본 흐름을 건드리지 않고, 지금 어떤 패널이 열려 있는지로 단계를 판정해
/// 그 단계의 캔버스만 눈앞에 보여준다.
///
/// 주의: LobbyCreateRequirementFlow 가 LobbyCanvas_Right/Panel 에 붙어 있어서
/// 캔버스를 SetActive(false) 하면 진행 중인 코루틴이 죽는다.
/// 그래서 GameObject 를 끄지 않고 CanvasGroup 으로만 숨긴다.
/// </summary>
[DefaultExecutionOrder(-450)]
public class MvpLobbyFlowGuide : MonoBehaviour
{
    private enum Step
    {
        Name,          // ① 이름 입력
        Choose,        // ② 새 회의 시작 or 방 목록에서 참가
        RoomTitle,     // ③ 회의실 제목
        Requirements,  // ④ 무엇을 만들지
        Creating,      // ⑤ 만드는 중
        Password,      // (참가) 비밀번호
    }

    [Header("배치")]
    [SerializeField] private float _distance = 1.5f;
    [SerializeField] private float _eyeHeight = 1.45f;

    [Tooltip("패널 세로 크기(m). 원본은 2.4m 라 1.5m 거리에서는 화면을 벗어난다.")]
    [SerializeField] private float _panelHeight = 1.15f;

    [Header("단계 안내 표시")]
    [SerializeField] private bool _showStepLabel = true;

    private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.85f);

    private Transform _left;
    private Transform _center;
    private Transform _right;
    private CanvasGroup _leftGroup;
    private CanvasGroup _centerGroup;
    private CanvasGroup _rightGroup;

    private GameObject _namePanel;
    private GameObject _createPanel;
    private GameObject _pwPanel;
    private GameObject _requirementsPanel;
    private GameObject _makeImagePanel;

    private TextMeshProUGUI _stepLabel;
    private Transform _player;
    private Step _current = Step.Name;
    private Step _applied = (Step)(-1);
    private float _nextResolve;

    private void Update()
    {
        if (Time.unscaledTime >= _nextResolve)
        {
            _nextResolve = Time.unscaledTime + 0.3f;
            ResolveReferences();
        }

        if (_right == null)
            return;

        _current = ResolveStep();
        if (_current != _applied)
        {
            _applied = _current;
            ApplyStep(_current);
        }

        PlaceActiveCanvas(_current);
    }

    // ------------------------------------------------------------------

    private void ResolveReferences()
    {
        if (_player == null || _head == null)
        {
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                _player = rig.transform;
                // 머리는 리그 위에 얹혀 돈다. 시작 방향을 맞출 때 이걸 빼줘야
                // 헤드셋을 비스듬히 쓴 채로 들어와도 UI 가 정면에 온다.
                _head = rig.centerEyeAnchor != null
                    ? rig.centerEyeAnchor
                    : (Camera.main != null ? Camera.main.transform : null);
            }
        }

        if (_left == null)
            _left = FindRoot("LobbyCanvas_Left");
        if (_center == null)
            _center = FindRoot("LobbyCanvas");
        if (_right == null)
            _right = FindRoot("LobbyCanvas_Right");

        _leftGroup = EnsureGroup(_left, _leftGroup);
        _centerGroup = EnsureGroup(_center, _centerGroup);
        _rightGroup = EnsureGroup(_right, _rightGroup);

        if (_namePanel == null && _left != null)
            _namePanel = FindChild(_left, "NamePanel");
        if (_right == null)
            return;
        if (_createPanel == null)
            _createPanel = FindChild(_right, "createSessionPanel");
        if (_pwPanel == null)
            _pwPanel = FindChild(_right, "PWPanel");
        if (_requirementsPanel == null)
            _requirementsPanel = FindChild(_right, "RequirementsPanel");
        if (_makeImagePanel == null)
            _makeImagePanel = FindChild(_right, "make_img");
    }

    private static Transform FindRoot(string name)
    {
        GameObject go = GameObject.Find(name);
        // 카메라 리그 자식으로 있는 중복 세트는 제외한다.
        if (go != null && go.transform.parent == null)
            return go.transform;
        return null;
    }

    private static GameObject FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t.gameObject;
        }
        return null;
    }

    private static CanvasGroup EnsureGroup(Transform root, CanvasGroup cached)
    {
        if (cached != null || root == null)
            return cached;

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    // 지금 열려 있는 패널로 단계를 역산한다(원본 흐름이 SetActive 로 제어하므로).
    private Step ResolveStep()
    {
        if (IsOn(_makeImagePanel))
            return Step.Creating;
        if (IsOn(_requirementsPanel))
            return Step.Requirements;
        if (IsOn(_createPanel))
            return Step.RoomTitle;
        if (IsOn(_pwPanel))
            return Step.Password;
        return HasNickname() ? Step.Choose : Step.Name;
    }

    private static bool IsOn(GameObject go) =>
        go != null && go.activeInHierarchy;

    private bool HasNickname()
    {
        if (_namePanel == null)
            return false;

        TMP_InputField field =
            _namePanel.GetComponentInChildren<TMP_InputField>(true);
        return field != null &&
               !string.IsNullOrWhiteSpace(
                   (field.text ?? string.Empty).Replace("​", string.Empty));
    }

    private void ApplyStep(Step step)
    {
        bool showLeft = step == Step.Name;
        bool showCenter = step == Step.Choose;
        bool showRight = step == Step.RoomTitle ||
                         step == Step.Requirements ||
                         step == Step.Creating ||
                         step == Step.Password;

        SetVisible(_leftGroup, showLeft);
        SetVisible(_centerGroup, showCenter);
        SetVisible(_rightGroup, showRight);

        // 오른쪽 캔버스 안에서도 지금 단계의 패널만 남긴다.
        if (showRight)
        {
            SetPanel(_createPanel, step == Step.RoomTitle);
            SetPanel(_requirementsPanel, step == Step.Requirements);
            SetPanel(_makeImagePanel, step == Step.Creating);
            SetPanel(_pwPanel, step == Step.Password);
        }
        else
        {
            // 참가용 비밀번호 패널은 기본으로 켜져 있어 늘 겹쳐 보였다.
            SetPanel(_pwPanel, false);
        }

        UpdateStepLabel(step);
    }

    private static void SetVisible(CanvasGroup group, bool visible)
    {
        if (group == null)
            return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    // 이미 원하는 상태면 건드리지 않는다(원본 흐름의 SetActive 와 싸우지 않도록).
    private static void SetPanel(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    // 배경 FBX 안의 로케이터. 디자이너가 "여기 UI를 붙이라"고 심어 둔 자리다.
    // 있으면 그쪽을 우선하고, 없으면 플레이어 기준으로 계산한다.
    private Transform _uiAnchor;
    private Transform _userSpawn;
    private Transform _head;

    // 배치를 한 번만 넣으면 지워진다(아래 ApplySpawnOnce 주석 참고).
    // 이 시간 동안은 어긋날 때마다 다시 맞추고, 지나면 손대지 않는다.
    [SerializeField] private float _spawnSettleSeconds = 3f;

    private bool _spawnPlaced;
    private bool _spawnDone;
    private float _spawnDeadline;

    private void ResolveLocators()
    {
        if (_uiAnchor == null || _userSpawn == null)
        {
            GameObject world = GameObject.Find("XRMeetingWorld");
            if (world == null)
                return;

            foreach (Transform t in world.transform)
            {
                if (t.name == "ANCHOR_StartUI") _uiAnchor = t;
                else if (t.name == "SPAWN_User") _userSpawn = t;
            }
        }

        ApplySpawnOnce();
    }

    /// <summary>
    /// 사용자를 SPAWN_User 로케이터에 세우고 UI(=노을) 쪽을 보게 한다.
    ///
    /// 한 번만 넣으면 안 된다. OVR 리그가 초기화를 마치면서 회전을 (0,0,0) 으로
    /// 한 번 되돌리는데, 그게 Start 직후가 아니라 몇 프레임 뒤라 먼저 넣은 값이 지워진다.
    /// 실측: 배치 로그는 정상인데 정착 후 리그 euler 가 (0,0,0) 이 되고,
    /// UI 가 정면에서 111도 옆으로 밀려 시작 화면이 아예 안 보였다.
    ///
    /// 그래서 짧은 정착 구간 동안 어긋나면 다시 맞춘다. 구간이 지나면 손대지 않는다.
    /// 계속 붙잡으면 사용자가 몸을 돌릴 수 없다.
    /// </summary>
    private void ApplySpawnOnce()
    {
        if (_spawnDone || _uiAnchor == null || _userSpawn == null || _player == null)
            return;

        Vector3 forward = _uiAnchor.position - _userSpawn.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            return;

        float targetYaw = Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;

        // 헤드셋을 쓰면 머리 회전이 리그 위에 더해진다. 리그를 그만큼 빼서 돌려야
        // 실제로 눈이 보는 방향이 UI 를 향한다. (에디터는 머리가 없어 0)
        float headYaw = _head != null ? _head.localEulerAngles.y : 0f;
        Quaternion want = Quaternion.Euler(0f, targetYaw - headYaw, 0f);

        if (!_spawnPlaced)
        {
            _player.SetPositionAndRotation(_userSpawn.position, want);
            _spawnPlaced = true;
            _spawnDeadline = Time.unscaledTime + Mathf.Max(0.5f, _spawnSettleSeconds);
            return;
        }

        // 위치는 다시 잡지 않는다. 로코모션이 중력으로 바닥에 앉히는 중이라
        // 여기서 끼어들면 그 정착과 싸운다. 지워지는 건 회전뿐이다.
        if (Time.unscaledTime < _spawnDeadline)
        {
            const float YawTolerance = 5f;
            if (Mathf.Abs(Mathf.DeltaAngle(_player.eulerAngles.y, want.eulerAngles.y)) > YawTolerance)
                _player.rotation = want;
            return;
        }

        _spawnDone = true;
        Debug.Log("[MVP 로비] 시작 배치 완료 — " + _player.position.ToString("F2") +
                  " / UI 를 향해 " + targetYaw.ToString("F0") + "도");
    }

    // 지금 단계의 캔버스를 눈앞 정해진 거리에 세운다.
    private void PlaceActiveCanvas(Step step)
    {
        if (_player == null)
            return;

        Transform target =
            step == Step.Name ? _left :
            step == Step.Choose ? _center : _right;
        if (target == null)
            return;

        ResolveLocators();

        Vector3 forward;
        Vector3 pos;

        if (_uiAnchor != null && _userSpawn != null)
        {
            // 로케이터 기준: 사용자는 SPAWN_User 에 서고 UI 는 ANCHOR_StartUI 에 뜬다.
            // 둘 사이 거리가 곧 디자이너가 정한 시야 거리(1.8m)다.
            forward = _uiAnchor.position - _userSpawn.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            pos = _uiAnchor.position;
        }
        else
        {
            forward = Vector3.ProjectOnPlane(_player.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 basePos = new Vector3(_player.position.x, 0f, _player.position.z);
            pos = basePos + forward * _distance;
            pos.y = _eyeHeight;
        }

        RectTransform content = ContentOf(step);

        target.rotation = Quaternion.LookRotation(forward, Vector3.up);
        FitHeight(target, content);

        // 내용물(예: RequirementsPanel)은 캔버스 중심에 있지 않다.
        // 캔버스가 아니라 그 내용물이 눈앞에 오도록 offset 을 준다.
        target.position = pos;
        if (content != null && content != target)
        {
            Vector3 drift = content.position - target.position;
            target.position = pos - drift;
        }
    }

    // 단계마다 실제로 보이는 내용물. 캔버스 전체가 아니라 이걸 기준으로 크기를 맞춘다.
    private RectTransform ContentOf(Step step)
    {
        switch (step)
        {
            case Step.Name: return AsRect(_namePanel);
            case Step.RoomTitle: return AsRect(_createPanel);
            case Step.Requirements: return AsRect(_requirementsPanel);
            case Step.Creating: return AsRect(_makeImagePanel);
            case Step.Password: return AsRect(_pwPanel);
            default: return null;   // Choose 는 캔버스 전체가 내용
        }
    }

    private static RectTransform AsRect(GameObject go) =>
        go != null ? go.transform as RectTransform : null;

    /// <summary>
    /// 눈앞 1.5m 에서 보기 좋은 크기로 맞춘다.
    /// 원본 캔버스는 2.4m 높이라 그대로 두면 시야를 벗어나고,
    /// 반대로 캔버스 기준으로만 줄이면 그 안의 작은 패널(예: RequirementsPanel 은
    /// 캔버스의 21%)이 손톱만 해진다. 그래서 실제로 보이는 내용물 높이를 기준으로 잡는다.
    /// </summary>
    private void FitHeight(Transform target, RectTransform content)
    {
        if (_panelHeight <= 0f)
            return;

        var canvasRect = target as RectTransform;
        if (canvasRect == null)
            return;

        // 내용물이 캔버스 안에서 차지하는 세로 비율
        float contentHeight = content != null && content.rect.height > 1f
            ? content.rect.height
            : canvasRect.rect.height;
        if (contentHeight <= 1f)
            return;

        float scale = _panelHeight / contentHeight;
        if (Mathf.Abs(target.localScale.y - scale) > 0.000001f)
            target.localScale = Vector3.one * scale;
    }

    // ------------------------------------------------------------------

    private void UpdateStepLabel(Step step)
    {
        if (!_showStepLabel)
            return;

        EnsureStepLabel();
        if (_stepLabel == null)
            return;

        _stepLabel.text = StepText(step);
        _stepLabel.transform.parent.gameObject.SetActive(
            !string.IsNullOrEmpty(_stepLabel.text));
    }

    private static string StepText(Step step)
    {
        switch (step)
        {
            case Step.Name: return "1단계 · 이름을 입력해 주세요";
            case Step.Choose: return "2단계 · 새 회의를 시작하거나 목록에서 참가하세요";
            case Step.RoomTitle: return "3단계 · 회의실 제목을 정해 주세요";
            case Step.Requirements: return "4단계 · 무엇을 만들지 알려 주세요";
            case Step.Creating: return "회의실을 만드는 중…";
            case Step.Password: return "비밀번호를 입력해 주세요";
            default: return "";
        }
    }

    private void EnsureStepLabel()
    {
        if (_stepLabel != null)
            return;
        if (_left == null)
            return;

        TMP_FontAsset font = null;
        foreach (TextMeshProUGUI t in _left.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.font != null) { font = t.font; break; }
        }

        var holder = new GameObject("MvpLobbyStepLabel", typeof(Canvas));
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)holder.transform;
        rt.sizeDelta = new Vector2(1600f, 140f);
        rt.localScale = Vector3.one * 0.0009f;

        var textGo = new GameObject("Text",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.SetParent(rt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        _stepLabel = textGo.GetComponent<TextMeshProUGUI>();
        if (font != null)
            _stepLabel.font = font;
        _stepLabel.fontSize = 64f;
        _stepLabel.alignment = TextAlignmentOptions.Center;
        _stepLabel.color = LabelColor;
        _stepLabel.raycastTarget = false;
    }

    private void LateUpdate()
    {
        // 안내 문구는 현재 패널 위에 띄운다.
        if (_stepLabel == null || _player == null)
            return;

        Transform target =
            _current == Step.Name ? _left :
            _current == Step.Choose ? _center : _right;
        if (target == null)
            return;

        ResolveLocators();

        Vector3 forward;
        Vector3 pos;

        if (_uiAnchor != null && _userSpawn != null)
        {
            forward = _uiAnchor.position - _userSpawn.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            // 패널보다 살짝 앞·위에 띄워 겹치지 않게 한다.
            pos = _uiAnchor.position - forward * 0.02f;
            pos.y = _uiAnchor.position.y + 0.72f;
        }
        else
        {
            forward = Vector3.ProjectOnPlane(_player.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 basePos = new Vector3(_player.position.x, 0f, _player.position.z);
            pos = basePos + forward * (_distance - 0.02f);
            pos.y = _eyeHeight + 0.72f;
        }

        _stepLabel.transform.parent.SetPositionAndRotation(
            pos,
            Quaternion.LookRotation(forward, Vector3.up));
    }
}
