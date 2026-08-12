using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 그래프 스냅샷 타임라인(히스토리 스크러버).
///
/// 서버는 방마다 graph_version 별 스냅샷을 쌓아 둔다(GET /api/history/{room_id}).
/// HistoryApiClient 는 조회·로드까지 다 갖고 있었지만 화면이 없어 쓰이지 못했다.
/// 여기서 디자이너 타임라인 스프라이트로 그 화면을 만든다.
///
/// 점 하나 = 스냅샷 하나. 누르면 그 시점의 그래프로 되돌아간다.
///  - 기본:   지나간 시점
///  - 현재:   가장 최신 스냅샷(맨 오른쪽)
///  - 선택됨: 지금 보고 있는 시점
///
/// 스프라이트 출처: Assets/05_Design/JW/UI/Sprites/Timeline (Resources/Timeline 으로 복사본)
/// </summary>
[DefaultExecutionOrder(-400)]
public class MvpHistoryTimeline : MonoBehaviour
{
    [Header("배치")]
    // 보드는 1920x1000 이고 하단 액션바가 y=-410(아래끝 -457)에 있다.
    // 겹치지 않게 보드 바깥(-500) 아래로 내린다.
    [Tooltip("설계 보드 기준 아래쪽 오프셋(로컬 단위)")]
    [SerializeField] private Vector2 _boardOffset = new Vector2(0f, -600f);
    [SerializeField] private Vector2 _size = new Vector2(1500f, 160f);

    [Header("점 크기")]
    [SerializeField] private float _stampSize = 46f;
    [SerializeField] private float _selectedSize = 62f;

    [Header("문구")]
    [SerializeField] private string _emptyText = "아직 되돌아갈 기록이 없어요";

    private static readonly Color LineTint = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color StampTint = new Color(1f, 1f, 1f, 0.75f);
    private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.85f);
    private static readonly Color LabelDim = new Color(1f, 1f, 1f, 0.45f);

    private HistoryApiClient _history;
    private RectTransform _root;
    private RectTransform _track;
    private TextMeshProUGUI _label;
    private readonly List<Image> _stamps = new List<Image>();

    private int _selected = -1;
    private int _count;
    private bool _built;
    private float _nextResolve;

    // ------------------------------------------------------------------

    private void Update()
    {
        if (Time.unscaledTime < _nextResolve)
            return;
        _nextResolve = Time.unscaledTime + 0.5f;

        if (_history == null)
            _history = FindFirstObjectByType<HistoryApiClient>();

        if (!_built)
            TryBuild();
        else
            FollowBoard();
    }

    /// <summary>서버에서 히스토리를 새로 받아 타임라인을 그린다.</summary>
    public void Refresh()
    {
        if (_history == null)
            _history = FindFirstObjectByType<HistoryApiClient>();
        if (_history == null)
        {
            Debug.LogWarning("[MVP 타임라인] HistoryApiClient 가 씬에 없습니다.");
            return;
        }

        _history.GetHistory(list =>
        {
            _count = list != null ? list.Count : 0;
            _selected = _count - 1;   // 기본은 '현재'
            Rebuild();
        });
    }

    // ------------------------------------------------------------------
    // 화면 구성
    // ------------------------------------------------------------------

    private RectTransform ResolveBoard()
    {
        MainSketchView view = FindFirstObjectByType<MainSketchView>();
        if (view == null)
            return null;
        Canvas canvas = view.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    private void TryBuild()
    {
        RectTransform board = ResolveBoard();
        if (board == null)
            return;

        var go = new GameObject("MvpHistoryTimeline",
            typeof(RectTransform), typeof(Image));
        _root = go.GetComponent<RectTransform>();
        _root.SetParent(board, false);
        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = _size;
        _root.anchoredPosition = _boardOffset;

        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.11f, 0.55f);
        bg.raycastTarget = false;

        // 기준선
        var lineGo = new GameObject("Line", typeof(RectTransform), typeof(Image));
        var lineRt = lineGo.GetComponent<RectTransform>();
        lineRt.SetParent(_root, false);
        lineRt.anchorMin = new Vector2(0.04f, 0.5f);
        lineRt.anchorMax = new Vector2(0.96f, 0.5f);
        lineRt.pivot = new Vector2(0.5f, 0.5f);
        lineRt.offsetMin = new Vector2(0f, -3f);
        lineRt.offsetMax = new Vector2(0f, 3f);
        var lineImg = lineGo.GetComponent<Image>();
        lineImg.sprite = Load("line");
        lineImg.type = Image.Type.Sliced;
        lineImg.color = LineTint;
        lineImg.raycastTarget = false;

        // 점이 놓일 영역(기준선과 같은 폭)
        var trackGo = new GameObject("Track", typeof(RectTransform));
        _track = trackGo.GetComponent<RectTransform>();
        _track.SetParent(_root, false);
        _track.anchorMin = new Vector2(0.04f, 0.5f);
        _track.anchorMax = new Vector2(0.96f, 0.5f);
        _track.pivot = new Vector2(0.5f, 0.5f);
        _track.offsetMin = new Vector2(0f, -_selectedSize * 0.5f);
        _track.offsetMax = new Vector2(0f, _selectedSize * 0.5f);

        _label = CreateLabel(_root, "Label",
            new Vector2(0f, -_size.y * 0.34f), _emptyText, 30f, LabelDim);

        _built = true;
        Refresh();
    }

    // 보드가 움직이면(책상 모드 전환 등) 따라간다.
    private void FollowBoard()
    {
        if (_root == null)
            return;
        RectTransform board = ResolveBoard();
        if (board != null && _root.parent != board)
            _root.SetParent(board, false);
    }

    private void Rebuild()
    {
        if (_track == null)
            return;

        foreach (Image stamp in _stamps)
        {
            if (stamp != null)
                Destroy(stamp.gameObject);
        }
        _stamps.Clear();

        if (_count <= 0)
        {
            if (_label != null)
            {
                _label.text = _emptyText;
                _label.color = LabelDim;
            }
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            int index = i;
            bool isCurrent = i == _count - 1;

            var go = new GameObject("Stamp_" + i,
                typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_track, false);
            rt.anchorMin = rt.anchorMax = new Vector2(Fraction(i), 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.one * _stampSize;

            var img = go.GetComponent<Image>();
            img.sprite = Load(isCurrent ? "stamp_current" : "stamp_default");
            img.color = StampTint;
            img.preserveAspect = true;

            go.GetComponent<Button>().onClick.AddListener(() => Select(index));
            _stamps.Add(img);
        }

        ApplySelection();
    }

    // 점이 하나뿐이면 오른쪽 끝(= 현재)에 둔다.
    private float Fraction(int index) =>
        _count <= 1 ? 1f : (float)index / (_count - 1);

    private void Select(int index)
    {
        if (index < 0 || index >= _count || _history == null)
            return;

        if (!_history.LoadSnapshotAt(index))
            return;

        _selected = index;
        ApplySelection();
    }

    private void ApplySelection()
    {
        for (int i = 0; i < _stamps.Count; i++)
        {
            Image img = _stamps[i];
            if (img == null)
                continue;

            bool selected = i == _selected;
            bool isCurrent = i == _count - 1;
            img.sprite = Load(selected
                ? "stamp_selected"
                : isCurrent ? "stamp_current" : "stamp_default");
            img.color = selected ? Color.white : StampTint;
            img.rectTransform.sizeDelta =
                Vector2.one * (selected ? _selectedSize : _stampSize);
        }

        if (_label == null)
            return;

        if (_count <= 0)
        {
            _label.text = _emptyText;
            _label.color = LabelDim;
            return;
        }

        bool atCurrent = _selected == _count - 1;
        _label.color = LabelColor;
        _label.text = atCurrent
            ? "지금 설계 (" + _count + "번째)"
            : (_selected + 1) + "번째 기록 · 되돌아본 중";
    }

    // ------------------------------------------------------------------

    private static readonly Dictionary<string, Sprite> Cache =
        new Dictionary<string, Sprite>();

    private static Sprite Load(string key)
    {
        if (Cache.TryGetValue(key, out Sprite cached))
            return cached;

        Sprite sprite = Resources.Load<Sprite>("Timeline/" + key);
        Cache[key] = sprite;
        if (sprite == null)
            Debug.LogWarning("[MVP 타임라인] 스프라이트 없음: Timeline/" + key);
        return sprite;
    }

    private static TextMeshProUGUI CreateLabel(
        RectTransform parent,
        string name,
        Vector2 anchoredPosition,
        string text,
        float size,
        Color color)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900f, 46f);
        rt.anchoredPosition = anchoredPosition;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;

        // 보드에 이미 쓰인 폰트를 따라가 한글 글리프를 보장한다.
        TextMeshProUGUI sample = parent.GetComponentInParent<Canvas>()
            ?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (sample != null && sample.font != null)
            tmp.font = sample.font;

        return tmp;
    }
}
