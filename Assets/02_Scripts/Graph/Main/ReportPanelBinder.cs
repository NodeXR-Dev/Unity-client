using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 회의 리포트 패널에 서버 값을 채운다.
//
// UI 는 에디터에서 만든다. 이 스크립트는 만들어 둔 오브젝트를 Inspector 로 받아 값만 넣는다.
//
// [템플릿 복제] 키워드 칩과 참여자 줄은 **하나만** 만들어 두면 된다. 필요한 개수만큼 복제하고
//   남는 건 끈다. 참여자 수와 키워드 수는 회의마다 다르므로 미리 5개를 만들어 두는 것보다
//   이쪽이 맞다. 템플릿 자신은 런타임에 꺼지고 복제본만 보인다(에디터에서는 켜 둬야 보인다).
//
// [자식 찾기] 복제본의 라벨·점은 이름이 아니라 컴포넌트로 찾는다.
//   - 칩: 자식 중 첫 TMP_Text = 키워드 글자
//   - 참여자 줄: 자식 중 첫 Image = 색 점, 첫 TMP_Text = 이름
//   그래서 칩/줄 안에 다른 Image·TMP_Text 를 추가하면 잘못 잡힌다. 그럴 땐 아래
//   _chipLabelPath / _rowDotPath / _rowNamePath 에 자식 이름을 적어 지정할 수 있다.
[DisallowMultipleComponent]
public class ReportPanelBinder : MonoBehaviour
{
    [Header("서버 (비우면 런타임에 찾는다)")]
    [SerializeField] private ReportApiClient _apiClient;

    [Header("텍스트")]
    [SerializeField] private TMP_Text _topicText;
    [Tooltip("\"2026.08.09 · 09:20 – 10:05 · 45분\"")]
    [SerializeField] private TMP_Text _whenText;
    [Tooltip("불러오는 중 / 실패 안내. 없으면 생략")]
    [SerializeField] private TMP_Text _statusText;

    [Header("키워드 — 칩 하나만 만들어 두면 복제한다")]
    [Tooltip("칩들이 들어갈 부모 (KeyRow)")]
    [SerializeField] private Transform _chipParent;
    [Tooltip("복제할 칩 (Chip1)")]
    [SerializeField] private GameObject _chipTemplate;
    [SerializeField] private int _maxKeywords = 3;
    [Tooltip("칩 글자 수 상한. 서버는 노드 원문을 주므로 길면 자른다")]
    [SerializeField] private int _keywordMaxChars = 14;

    [Header("참여자 — 줄 하나만 만들어 두면 복제한다")]
    [Tooltip("줄들이 들어갈 부모 (Participants)")]
    [SerializeField] private Transform _rowParent;
    [Tooltip("복제할 줄 (Row1)")]
    [SerializeField] private GameObject _rowTemplate;
    [Tooltip("참여자 색. 순서대로 쓰고, 인원이 더 많으면 앞에서부터 다시 쓴다")]
    [SerializeField] private Color[] _dotColors =
    {
        new Color32(0xE8, 0x97, 0x96, 0xFF),
        new Color32(0xF2, 0xCB, 0x79, 0xFF),
        new Color32(0x90, 0xD7, 0x90, 0xFF),
        new Color32(0x8D, 0xB6, 0xE9, 0xFF),
        new Color32(0xD2, 0xA1, 0xED, 0xFF),
    };

    [Header("자식 지정 (비우면 컴포넌트로 자동 탐색)")]
    [SerializeField] private string _chipLabelPath = "";
    [SerializeField] private string _rowDotPath = "";
    [SerializeField] private string _rowNamePath = "";

    private readonly List<GameObject> _chips = new List<GameObject>();
    private readonly List<GameObject> _rows = new List<GameObject>();
    private bool _busy;

    public ReportDto Last { get; private set; }

    // ─────────────────────────────────────────────
    // 진입점 — '회의 마치기' 버튼 onClick 에 연결한다
    // ─────────────────────────────────────────────

    public void ShowReport()
    {
        if (_busy) return;
        _busy = true;
        StartCoroutine(CoShowReport());
    }

    private IEnumerator CoShowReport()
    {
        SetStatus("리포트를 불러오는 중...");

        if (_apiClient == null) _apiClient = FindFirstObjectByType<ReportApiClient>();
        if (_apiClient == null)
        {
            Debug.LogWarning("[ReportPanelBinder] ReportApiClient 를 찾지 못했습니다.");
            SetStatus("리포트를 불러오지 못했어요");
            _busy = false;
            yield break;
        }

        ReportDto report = null;
        bool done = false;
        _apiClient.GetReport(r => { report = r; done = true; });
        while (!done) yield return null;

        if (report == null)
        {
            SetStatus("리포트를 불러오지 못했어요");
            _busy = false;
            yield break;
        }

        SetStatus("");
        Bind(report);
        _busy = false;
    }

    // ─────────────────────────────────────────────
    // 값 채우기
    // ─────────────────────────────────────────────

    public void Bind(ReportDto r)
    {
        if (r == null) return;
        Last = r;

        if (_topicText != null)
            _topicText.text = string.IsNullOrWhiteSpace(r.topic) ? "회의 리포트" : r.topic;
        if (_whenText != null)
            _whenText.text = r.HeaderLine();

        BindKeywords(r);
        BindParticipants(r);

        // ContentSizeFitter 가 겹겹이 물려 있어 한 프레임 기다리면 칩 폭이 한 박자 늦게 맞는다.
        // 값이 바뀐 직후 강제로 다시 계산해 깜빡임을 없앤다.
        if (transform is RectTransform rt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private void BindKeywords(ReportDto r)
    {
        if (_chipTemplate == null || _chipParent == null) return;

        List<string> keywords = r.ShortKeywords(_maxKeywords, _keywordMaxChars);

        // 프리팹에 예시로 들어 있는 칩(Chip1_1, Chip1_2 …)은 _chips 리스트 밖이라
        // 그냥 두면 실제 키워드와 함께 남아 보인다. 전부 끄고 필요한 개수만 다시 켠다.
        HideAllChildren(_chipParent);

        EnsureCount(_chips, _chipTemplate, _chipParent, keywords.Count);

        for (int i = 0; i < _chips.Count; i++)
        {
            bool used = i < keywords.Count;
            _chips[i].SetActive(used);
            if (!used) continue;

            TMP_Text label = FindChild<TMP_Text>(_chips[i], _chipLabelPath);
            if (label != null) label.text = keywords[i];
        }
    }

    private void BindParticipants(ReportDto r)
    {
        if (_rowTemplate == null || _rowParent == null) return;

        int people = r.participants_ratio.Count;

        // 칩과 같은 이유 — 프리팹의 예시 줄(Row1_1 … Row1_4)을 먼저 전부 끈다.
        HideAllChildren(_rowParent);

        EnsureCount(_rows, _rowTemplate, _rowParent, people);

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < people;
            _rows[i].SetActive(used);
            if (!used) continue;

            TMP_Text nameText = FindChild<TMP_Text>(_rows[i], _rowNamePath);
            if (nameText != null) nameText.text = r.participants_ratio[i].nickname;

            Graphic dot = FindChild<Image>(_rows[i], _rowDotPath);
            if (dot != null) dot.color = DotColor(i);
        }
    }

    // 부모의 자식을 전부 끈다. 실제로 쓸 것만 뒤에서 다시 켠다.
    private static void HideAllChildren(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            if (child != null)
                child.gameObject.SetActive(false);
        }
    }

    public Color DotColor(int index)
    {
        if (_dotColors == null || _dotColors.Length == 0) return Color.white;
        int n = _dotColors.Length;
        return _dotColors[((index % n) + n) % n];
    }

    // ─────────────────────────────────────────────
    // 템플릿 복제
    // ─────────────────────────────────────────────

    // pool 이 need 개가 될 때까지 template 을 복제한다. 템플릿 자신이 0번이다.
    private void EnsureCount(List<GameObject> pool, GameObject template, Transform parent, int need)
    {
        if (pool.Count == 0) pool.Add(template);   // 템플릿을 첫 칸으로 쓴다

        while (pool.Count < need)
        {
            // worldPositionStays=false — 레이아웃 그룹 안이라 부모 기준으로 붙어야 한다.
            GameObject clone = Instantiate(template, parent, false);
            clone.name = $"{template.name}_{pool.Count}";
            pool.Add(clone);
        }
    }

    private T FindChild<T>(GameObject root, string path) where T : Component
    {
        if (!string.IsNullOrEmpty(path))
        {
            Transform t = root.transform.Find(path);
            if (t != null) return t.GetComponent<T>();
            Debug.LogWarning($"[ReportPanelBinder] '{path}' 를 {root.name} 안에서 찾지 못했습니다.");
            return null;
        }
        // 자기 자신은 건너뛴다 — 칩 루트의 Image(배경)를 점으로 잡으면 안 된다.
        foreach (T c in root.GetComponentsInChildren<T>(true))
            if (c.gameObject != root) return c;
        return null;
    }

    private void SetStatus(string message)
    {
        if (_statusText == null) return;
        _statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        if (!string.IsNullOrEmpty(message)) _statusText.text = message;
    }

    // ─────────────────────────────────────────────
    // 서버 없이 확인
    // ─────────────────────────────────────────────

    [ContextMenu("Test: 리포트 표시")]
    private void TestShow() => ShowReport();

    [ContextMenu("Test: 더미 데이터로 표시")]
    private void TestBindDummy()
    {
        var r = new ReportDto
        {
            topic = "물로켓 설계 회의",
            started_at = System.DateTime.Now.AddMinutes(-45).ToString("o"),
            ended_at = System.DateTime.Now.ToString("o"),
            duration_seconds = 45 * 60,
            total_utterance_count = 37,
            keywords = new List<string> { "온보딩 플로우 개선", "노드 그래프 동기화", "리포트 UI 방향" },
        };
        string[] names = { "참여자 1", "참여자 2", "참여자 3", "참여자 4", "참여자 5" };
        for (int i = 0; i < names.Length; i++)
        {
            r.participants.Add(names[i]);
            r.participants_ratio.Add(new ReportParticipantRatioDto
            {
                nickname = names[i], ratio = 20f, utterance_count = 7,
            });
        }
        Bind(r);
    }

    [ContextMenu("Test: 복제본 정리")]
    private void TestClearClones()
    {
        // 에디터에서 더미로 만든 복제본을 지운다. 템플릿(0번)은 남긴다.
        for (int i = _chips.Count - 1; i >= 1; i--) DestroyImmediate(_chips[i]);
        for (int i = _rows.Count - 1; i >= 1; i--) DestroyImmediate(_rows[i]);
        if (_chips.Count > 1) _chips.RemoveRange(1, _chips.Count - 1);
        if (_rows.Count > 1) _rows.RemoveRange(1, _rows.Count - 1);
        if (_chips.Count > 0) _chips[0].SetActive(true);
        if (_rows.Count > 0) _rows[0].SetActive(true);
    }
}
