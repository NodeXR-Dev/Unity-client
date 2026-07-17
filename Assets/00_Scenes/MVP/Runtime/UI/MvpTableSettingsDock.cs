using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 자주 쓰지 않는 개인 설정을 공용 설계판에서 분리해 테이블 가장자리에 놓는다.
// 위치와 크기는 각 사용자에게만 적용되며 그래프나 네트워크 상태를 변경하지 않는다.
[DefaultExecutionOrder(650)]
public class MvpTableSettingsDock : MonoBehaviour
{
    [SerializeField] private float _collapsedScale = 0.00095f;
    [SerializeField] private float _tableReach = 0.68f;
    [SerializeField] private float _sideOffset = 0.28f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Camera _camera;
    private Renderer _table;
    private MvpWorkspaceLayout _workspaceLayout;
    private GraphSyncClient _graphSyncClient;
    private TMP_Text _connectionText;
    private TMP_Text _helpText;
    private Button _sizeButton;
    private bool _largeWorkspace;
    private bool _showHelp;
    private float _nextStatusRefresh;

    private void Start()
    {
        ResolveReferences();
        BuildCanvas();
        BuildCollapsed();
        PlaceOnTable();
        _canvas.gameObject.SetActive(_shown);
    }

    private bool _shown;
    private bool _expandedNow;

    private void OnEnable()
    {
        // 월드 키보드는 도크와 같은 공간(사용자 앞 아래쪽)에 열리므로,
        // 열리면 확장 패널을 접어 겹침·오조작을 막는다.
        MvpWorldKeyboard.OnOpenedGlobal += CollapseForKeyboard;
    }

    private void OnDisable()
    {
        MvpWorldKeyboard.OnOpenedGlobal -= CollapseForKeyboard;
    }

    private void CollapseForKeyboard()
    {
        if (_expandedNow && _canvas != null &&
            _canvas.gameObject.activeInHierarchy)
            BuildCollapsed();
    }

    // 설계 단계 진입 시 MvpClassroomFlow 가 호출 — 접힌 '내 자리 설정' 버튼을 테이블 위에 보인다.
    public void ShowCollapsed()
    {
        _shown = true;
        if (_canvas == null)
            return;   // Start 이전이면 Start 가 _shown 을 반영한다.
        _canvas.gameObject.SetActive(true);
        BuildCollapsed();
    }

    public void HideDock()
    {
        _shown = false;
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        Camera active = ResolveActiveCamera();
        if (active != null && active != _camera)
        {
            _camera = active;
            if (_canvas != null)
                _canvas.worldCamera = _camera;
            _table = null;
            PlaceOnTable();
        }

        if (Time.unscaledTime >= _nextStatusRefresh)
        {
            _nextStatusRefresh = Time.unscaledTime + 0.5f;
            RefreshConnectionStatus();
        }
    }

    private void ResolveReferences()
    {
        _camera = ResolveActiveCamera();
        if (_workspaceLayout == null)
            _workspaceLayout =
                FindFirstObjectByType<MvpWorkspaceLayout>();
        if (_graphSyncClient == null)
            _graphSyncClient =
                FindFirstObjectByType<GraphSyncClient>();
        if (_table == null)
            _table = FindBestTable(_camera);
    }

    private void BuildCanvas()
    {
        if (_canvas != null)
            return;

        GameObject root = new GameObject(
            "MvpTableSettingsCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        _canvasRect = root.GetComponent<RectTransform>();
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = _camera;
        _canvas.sortingOrder = 150;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.dynamicPixelsPerUnit = 12f;

        _canvasRect.localScale =
            Vector3.one * _collapsedScale;
    }

    private void BuildCollapsed()
    {
        _expandedNow = false;
        ClearCanvas();
        MvpStudentUiFactory.SetRect(
            _canvasRect,
            Vector2.zero,
            new Vector2(230f, 88f));

        Image glow = MvpStudentUiFactory.CreatePanel(
            _canvas.transform,
            "SettingsGlow",
            Vector2.zero,
            new Vector2(222f, 82f),
            new Color(
                MvpStudentUiFactory.HoloCyan.r,
                MvpStudentUiFactory.HoloCyan.g,
                MvpStudentUiFactory.HoloCyan.b,
                0.24f),
            false);
        glow.raycastTarget = false;

        Button open = MvpStudentUiFactory.CreateButton(
            _canvas.transform,
            "OpenSettings",
            "내 자리 설정",
            Vector2.zero,
            new Vector2(204f, 70f),
            MvpStudentUiFactory.ElectricBlue,
            BuildExpanded,
            22f);

        Outline rim = open.gameObject.AddComponent<Outline>();
        rim.effectColor = MvpStudentUiFactory.HoloCyan;
        rim.effectDistance = new Vector2(3f, -3f);
        PlaceOnTable();
    }

    public void RefreshRoomPlacement()
    {
        _camera = ResolveActiveCamera();
        _table = null;
        PlaceOnTable();
    }


    private void BuildExpanded()
    {
        _expandedNow = true;
        ClearCanvas();
        MvpStudentUiFactory.SetRect(
            _canvasRect,
            Vector2.zero,
            new Vector2(500f, 330f));

        Image panel = MvpStudentUiFactory.CreatePanel(
            _canvas.transform,
            "SettingsPanel",
            Vector2.zero,
            new Vector2(490f, 320f),
            MvpStudentUiFactory.DeepSpace,
            true);

        Outline rim = panel.gameObject.AddComponent<Outline>();
        rim.effectColor = MvpStudentUiFactory.HoloCyan;
        rim.effectDistance = new Vector2(3f, -3f);

        MvpStudentUiFactory.CreateText(
            panel.transform,
            "Title",
            "내 자리 설정",
            new Vector2(-105f, 126f),
            new Vector2(240f, 44f),
            27f,
            TextAlignmentOptions.MidlineLeft,
            true,
            Color.white,
            1);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "Close",
            "닫기",
            new Vector2(189f, 126f),
            new Vector2(92f, 48f),
            MvpStudentUiFactory.GlassBlue,
            BuildCollapsed,
            20f);

        _connectionText = MvpStudentUiFactory.CreateText(
            panel.transform,
            "Connection",
            "",
            new Vector2(0f, 82f),
            new Vector2(420f, 32f),
            17f,
            TextAlignmentOptions.Center,
            true,
            MvpStudentUiFactory.HoloCyan,
            1);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "ReturnToSeat",
            "의자로 돌아가기",
            new Vector2(-106f, 32f),
            new Vector2(198f, 52f),
            MvpStudentUiFactory.ElectricBlue,
            ReturnToSeat,
            18f);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "RecenterWorkspace",
            "작업판 맞추기",
            new Vector2(106f, 32f),
            new Vector2(198f, 52f),
            MvpStudentUiFactory.GlassBlue,
            RecenterWorkspace,
            18f);


        _sizeButton = MvpStudentUiFactory.CreateButton(
            panel.transform,
            "WorkspaceSize",
            "작업판 크게 보기",
            new Vector2(-106f, -26f),
            new Vector2(198f, 52f),
            MvpStudentUiFactory.Mint,
            ToggleWorkspaceSize,
            18f);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "Help",
            "사용법",
            new Vector2(106f, -26f),
            new Vector2(198f, 52f),
            MvpStudentUiFactory.Amber,
            ToggleHelp,
            18f);

        _helpText = MvpStudentUiFactory.CreateText(
            panel.transform,
            "HelpText",
            "이 설정은 나에게만 적용돼요.",
            new Vector2(-106f, -92f),
            new Vector2(230f, 62f),
            15f,
            TextAlignmentOptions.Center,
            false,
            new Color(0.78f, 0.86f, 1f, 1f),
            3);

        MvpStudentUiFactory.CreateButton(
            panel.transform,
            "LeaveRoom",
            "방 나가기",
            new Vector2(106f, -92f),
            new Vector2(198f, 52f),
            new Color(0.55f, 0.22f, 0.28f, 1f),
            RequestLeaveRoom,
            18f);

        RefreshConnectionStatus();
        RefreshSizeLabel();
        PlaceOnTable();
    }

    // '방 나가기' — 확인 팝업과 실제 종료 절차는 MvpClassroomFlow 가 담당한다.
    private void RequestLeaveRoom()
    {
        MvpClassroomFlow flow =
            FindFirstObjectByType<MvpClassroomFlow>();
        if (flow == null)
        {
            if (_helpText != null)
            {
                _helpText.text = "지금은 방을 나갈 수 없어요.";
                _helpText.color = MvpStudentUiFactory.Coral;
            }
            return;
        }

        BuildCollapsed();
        flow.RequestLeaveRoom();
    }

    private void ReturnToSeat()
    {
        MvpMeetingRoomPlayerController player =
            FindFirstObjectByType<MvpMeetingRoomPlayerController>();
        player?.RespawnAtAssignedSeat();
        PlaceOnTable();

        if (_helpText != null)
        {
            _helpText.text =
                "배정된 회의실 의자로 돌아갑니다.";
            _helpText.color = MvpStudentUiFactory.Mint;
        }
    }

    private void RecenterWorkspace()
    {
        ResolveReferences();
        _workspaceLayout?.RecenterWorkspaceToActiveView();
        if (_helpText != null)
        {
            _helpText.text =
                "작업판과 노드를 테이블 중앙의 기본 위치로 다시 정렬했어요.";
            _helpText.color = MvpStudentUiFactory.Mint;
        }
    }

    private void ToggleWorkspaceSize()
    {
        _largeWorkspace = !_largeWorkspace;
        ResolveReferences();
        _workspaceLayout?.SetWorkspaceScaleMultiplier(
            _largeWorkspace ? 1.15f : 1f);
        RefreshSizeLabel();
    }

    private void RefreshSizeLabel()
    {
        if (_sizeButton == null)
            return;

        TMP_Text label =
            _sizeButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = _largeWorkspace
                ? "작업판 기본 크기"
                : "작업판 크게 보기";
    }

    private void ToggleHelp()
    {
        _showHelp = !_showHelp;
        if (_helpText == null)
            return;

        _helpText.text = _showHelp
            ? "주먹 2초 → 손바닥으로 미리보기 → 원하는 곳에서 다시 주먹으로 만들어요."
            : "이 설정은 나에게만 적용돼요.";
        _helpText.color = _showHelp
            ? MvpStudentUiFactory.HoloCyan
            : new Color(0.78f, 0.86f, 1f, 1f);
    }

    private void RefreshConnectionStatus()
    {
        if (_connectionText == null)
            return;

        bool connected =
            _graphSyncClient != null &&
            _graphSyncClient.IsConnected;
        _connectionText.text = connected
            ? "● 서버 연결됨 · 변경사항 공유 중"
            : "● 체험 모드 · 이 기기에서만 저장";
        _connectionText.color = connected
            ? MvpStudentUiFactory.Mint
            : MvpStudentUiFactory.Amber;
    }

    private void PlaceOnTable()
    {
        if (_canvasRect == null)
            return;

        ResolveReferences();
        if (_camera == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(
            _camera.transform.forward,
            Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = _camera.transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        Vector3 position;
        if (_table != null)
        {
            Bounds bounds = _table.bounds;
            position =
                _camera.transform.position +
                forward * _tableReach +
                right * _sideOffset;
            position.x = Mathf.Clamp(
                position.x,
                bounds.min.x + 0.22f,
                bounds.max.x - 0.22f);
            position.z = Mathf.Clamp(
                position.z,
                bounds.min.z + 0.22f,
                bounds.max.z - 0.22f);
            position.y = bounds.max.y + 0.025f;
        }
        else
        {
            position =
                _camera.transform.position +
                forward * 0.65f +
                right * _sideOffset +
                Vector3.down * 0.45f;
        }

        // 캔버스 피벗이 중앙이므로, 패널 절반 높이만큼 올려 하단이 상판 아래로
        // 파묻히지 않게 한다(확장 패널에서 아래 줄 버튼이 잘리는 것 방지).
        position += Vector3.up *
            (_canvasRect.sizeDelta.y * _collapsedScale * 0.5f);

        Vector3 viewDirection =
            position - _camera.transform.position;
        if (viewDirection.sqrMagnitude < 0.001f)
            viewDirection = forward;

        _canvasRect.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(
                viewDirection.normalized,
                Vector3.up));
        _canvasRect.localScale =
            Vector3.one * _collapsedScale;
    }

    private static Camera ResolveActiveCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        Camera[] cameras =
            Resources.FindObjectsOfTypeAll<Camera>();
        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.isActiveAndEnabled &&
                candidate.CompareTag("MainCamera"))
                return candidate;
        }
        foreach (Camera candidate in cameras)
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.isActiveAndEnabled)
                return candidate;
        }
        return null;
    }

    private static Renderer FindBestTable(Camera camera)
    {
        Renderer best = null;
        float bestScore = float.MaxValue;
        Renderer[] renderers =
            Resources.FindObjectsOfTypeAll<Renderer>();

        foreach (Renderer candidate in renderers)
        {
            if (candidate == null ||
                !candidate.gameObject.scene.IsValid())
                continue;

            string name =
                candidate.gameObject.name.ToLowerInvariant();
            if (!name.Contains("table") &&
                !name.Contains("desk"))
                continue;

            float distance = camera != null
                ? Vector3.Distance(
                    camera.transform.position,
                    candidate.bounds.center)
                : 0f;
            if (distance < bestScore)
            {
                bestScore = distance;
                best = candidate;
            }
        }
        return best;
    }

    private void ClearCanvas()
    {
        if (_canvas == null)
            return;

        for (int index = _canvas.transform.childCount - 1;
             index >= 0;
             index--)
        {
            GameObject child =
                _canvas.transform.GetChild(index).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        _connectionText = null;
        _helpText = null;
        _sizeButton = null;
    }
}
