using System;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Vuplex.WebView;

// 노드 R 버튼 → "이 노드와 비슷한 느낌의 레퍼런스 디자인" 이미지 검색 패널.
// Vuplex CanvasWebViewPrefab 으로 Bing 이미지 검색(세이프서치 강제)을 보드 옆에 띄운다.
//  - 키워드는 호출부(MvpClassroomFlow)가 결정: 서버 추천(/api/references/keyword) 또는
//    노드 부모 체인 기반 로컬 조합. 상단 입력칸을 눌러 월드 키보드로 직접 고칠 수도 있다.
//  - 웹뷰는 열려 있는 동안만 존재한다(닫으면 Destroy — Quest 메모리/프레임 보호).
[DisallowMultipleComponent]
public class MvpReferencePanel : MonoBehaviour
{
    private const float PanelWidthMeters = 0.92f;
    private const int PanelPixelsX = 1100;
    private const int PanelPixelsY = 820;
    private const float TopBarPixels = 96f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private CanvasWebViewPrefab _webView;
    private TMP_InputField _keywordInput;
    private TMP_Text _statusText;
    private string _currentNodeId;
    private bool _keywordEditedByUser;
    private Transform _board;
    private Action<Canvas> _attachPointable;

    public bool IsOpen =>
        _canvas != null && _canvas.gameObject.activeInHierarchy;

    // R 버튼에서 호출. attachPointable 은 flow 의 XR 입력 부착 유틸(PointableCanvas).
    public void OpenForNode(
        string nodeId, string keyword, Action<Canvas> attachPointable)
    {
        _currentNodeId = nodeId;
        _keywordEditedByUser = false;
        _attachPointable = attachPointable;

        if (_canvas == null)
            BuildPanel();
        _canvas.gameObject.SetActive(true);
        PlaceBesideBoard();

        _keywordInput.SetTextWithoutNotify(keyword ?? "");
        Search(keyword);
    }

    // 서버 추천 키워드가 늦게 도착했을 때 — 사용자가 이미 고쳤으면 무시한다.
    public void SuggestKeyword(string nodeId, string keyword)
    {
        if (!IsOpen || _keywordEditedByUser ||
            nodeId != _currentNodeId || string.IsNullOrWhiteSpace(keyword))
            return;
        _keywordInput.SetTextWithoutNotify(keyword);
        Search(keyword);
    }

    public void Close()
    {
        // 웹뷰는 유지 비용이 커서 패널을 닫을 때 통째로 파괴한다.
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        _canvas = null;
        _webView = null;
        _keywordInput = null;
        _statusText = null;
    }

    private void OnDestroy()
    {
        Close();
    }

    // ─────────────────────────────────────────────
    // 검색
    // ─────────────────────────────────────────────

    private void Search(string keyword)
    {
        keyword = (keyword ?? "").Trim();
        if (keyword.Length == 0)
        {
            SetStatus("검색어를 입력해 주세요.");
            return;
        }

        SetStatus("‘" + keyword + "’ 레퍼런스를 찾는 중…");
        LoadWhenReady(BuildSearchUrl(keyword));
    }

    // 초등 대상: 세이프서치를 항상 강제한다(adlt=strict).
    private static string BuildSearchUrl(string keyword)
    {
        return "https://www.bing.com/images/search?q=" +
               UnityWebRequest.EscapeURL(keyword) +
               "&adlt=strict";
    }

    private async void LoadWhenReady(string url)
    {
        try
        {
            if (_webView == null)
                return;
            await _webView.WaitUntilInitialized();
            if (_webView == null || !IsOpen)
                return;
            _webView.WebView.LoadUrl(url);
            SetStatus("");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[MvpReferencePanel] 웹뷰 로드 실패: " + exception.Message);
            SetStatus("레퍼런스를 불러오지 못했어요. 인터넷 연결을 확인해 주세요.");
        }
    }

    private void SetStatus(string text)
    {
        if (_statusText != null)
        {
            _statusText.text = text ?? "";
            _statusText.gameObject.SetActive(
                !string.IsNullOrEmpty(_statusText.text));
        }
    }

    // ─────────────────────────────────────────────
    // 패널 구성
    // ─────────────────────────────────────────────

    private void BuildPanel()
    {
        GameObject root = new GameObject(
            "MvpReferenceCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        _canvasRect = (RectTransform)root.transform;
        _canvasRect.sizeDelta = new Vector2(PanelPixelsX, PanelPixelsY);

        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 340;   // 추천 패널(320)보다 위, 확인 팝업(400)보다 아래
        _canvas.worldCamera = Camera.main;

        float metersPerPixel = PanelWidthMeters / PanelPixelsX;
        root.transform.localScale = Vector3.one * metersPerPixel;

        Image background = MvpStudentUiFactory.CreatePanel(
            root.transform,
            "ReferencePanelBg",
            Vector2.zero,
            new Vector2(PanelPixelsX, PanelPixelsY),
            new Color(0.035f, 0.055f, 0.10f, 0.99f),
            true);
        Outline rim = background.gameObject.AddComponent<Outline>();
        rim.effectColor = MvpStudentUiFactory.HoloCyan;
        rim.effectDistance = new Vector2(2f, -2f);

        // ── 상단 바: 제목 · 키워드 입력(월드 키보드) · 닫기 ──
        float topY = PanelPixelsY * 0.5f - TopBarPixels * 0.5f;

        MvpStudentUiFactory.CreateText(
            background.transform,
            "Title",
            "레퍼런스 찾기",
            new Vector2(-PanelPixelsX * 0.5f + 128f, topY),
            new Vector2(220f, 44f),
            25f,
            TextAlignmentOptions.MidlineLeft,
            true,
            Color.white,
            1);

        _keywordInput = MvpStudentUiFactory.CreateInput(
            background.transform,
            "KeywordInput",
            "검색어",
            new Vector2(56f, topY),
            new Vector2(560f, 60f),
            21f);
        // 입력칸을 누르면 노드 이름과 같은 방식으로 월드 키보드가 열린다.
        var keyboardBridge =
            _keywordInput.gameObject.AddComponent<MvpXrKeyboardInput>();
        keyboardBridge.Configure(_keywordInput);
        _keywordInput.onEndEdit.AddListener(value =>
        {
            _keywordEditedByUser = true;
            Search(value);
        });

        MvpStudentUiFactory.CreateButton(
            background.transform,
            "CloseReference",
            "닫기",
            new Vector2(PanelPixelsX * 0.5f - 78f, topY),
            new Vector2(112f, 56f),
            new Color(0.23f, 0.31f, 0.46f, 1f),
            Close,
            21f);

        _statusText = MvpStudentUiFactory.CreateText(
            background.transform,
            "Status",
            "",
            new Vector2(0f, 0f),
            new Vector2(PanelPixelsX - 120f, 80f),
            23f,
            TextAlignmentOptions.Center,
            false,
            MvpStudentUiFactory.HoloCyan,
            2);

        // ── 웹뷰: 상단 바 아래 전체 ──
        _webView = CanvasWebViewPrefab.Instantiate();
        _webView.transform.SetParent(root.transform, false);
        _webView.NativeOnScreenKeyboardEnabled = false; // 입력은 월드 키보드만 사용
        _webView.Resolution = 1f;

        RectTransform webRect = (RectTransform)_webView.transform;
        webRect.anchorMin = Vector2.zero;
        webRect.anchorMax = Vector2.one;
        webRect.offsetMin = new Vector2(10f, 10f);
        webRect.offsetMax = new Vector2(-10f, -TopBarPixels - 6f);
        webRect.localScale = Vector3.one;

        // XR 레이 입력 + 리치스루(손을 너머로 뻗으면 반투명해지며 뒤 노드 조작 허용)
        _attachPointable?.Invoke(_canvas);
        root.AddComponent<MvpPanelXray>();
    }

    // 보드 오른쪽 옆에 세워 둔다(보드가 움직이면 따라감).
    private void PlaceBesideBoard()
    {
        if (_board == null)
        {
            MvpWorkspaceLayout layout =
                FindFirstObjectByType<MvpWorkspaceLayout>();
            _board = layout != null ? layout.MainSketchPanel : null;
        }

        if (_board != null)
        {
            _canvas.transform.SetPositionAndRotation(
                _board.position +
                _board.right * 0.86f +
                _board.up * 0.02f -
                _board.forward * 0.04f,
                _board.rotation);
            return;
        }

        // 보드가 없으면(비상) 카메라 앞에.
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        forward.Normalize();
        _canvas.transform.SetPositionAndRotation(
            cam.transform.position + forward * 1.0f,
            Quaternion.LookRotation(forward));
    }

    private void LateUpdate()
    {
        if (IsOpen)
            PlaceBesideBoard();
    }
}
