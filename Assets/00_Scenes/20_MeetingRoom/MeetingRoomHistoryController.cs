/*
 * 파일명: MeetingRoomHistoryController.cs
 * 목적: 히스토리 기능의 오케스트레이터.
 *   하단 바(MeetingRoomHistoryTimeline)를 드래그하면 → 그 시점의 그래프 스냅샷을 받아 →
 *   히스토리 전용 뷰(MeetingRoomHistoryGraphView, 디자이너 NodeBox 프리팹)로 렌더한다.
 *
 * 동작:
 *  1) Start 에서 provider.FetchList 로 스냅샷 목록을 받아 version 오름차순 정렬.
 *  2) 타임라인 normalized(0~1) → version 인덱스로 매핑 (0=가장 오래된, 1=최신/현재).
 *  3) 해당 version 스냅샷을 fetch(한 번 받은 건 캐시) → historyView.Render(graphData).
 *
 * 중요: 이 뷰는 "본 회의실의 라이브 그래프"와 분리된 '히스토리 전용' 로컬 미리보기다.
 *       (내가 과거로 스크럽할 때 방 안 모두의 그래프가 같이 바뀌면 안 되므로)
 *
 * 세팅: [Tools > MeetingRoom > Create History] 에디터 메뉴가 뷰/바/컨트롤러를 자동 배선한다.
 *  - useMock=true 면 백엔드 없이 바로 확인 가능. 백엔드 조회 API가 나오면 useMock=false 로.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeetingRoomHistoryController : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("히스토리 전용 그래프 뷰 (디자이너 NodeBox로 렌더). 라이브 그래프와 분리된 로컬 미리보기.")]
    [SerializeField] private MeetingRoomHistoryGraphView historyView;
    [Tooltip("하단 바")]
    [SerializeField] private MeetingRoomHistoryTimeline timeline;
    [Tooltip("바 위 스냅샷 틱(눈금). 선택.")]
    [SerializeField] private MeetingRoomHistoryTimelineTicks ticks;

    [Header("대상 방")]
    [Tooltip("스냅샷을 조회할 room_id")]
    [SerializeField] private string roomId = "test_room";

    [Header("데이터 소스")]
    [Tooltip("체크하면 백엔드 없이 가짜 스냅샷으로 동작(테스트용). 조회 API가 생기면 해제.")]
    [SerializeField] private bool useMock = true;
    [SerializeField] private string baseUrl = "http://localhost:8000";
    [Tooltip("Mock 모드에서 만들 스냅샷 버전 수")]
    [SerializeField] private int mockVersionCount = 6;

    private IMeetingRoomHistoryProvider _provider;
    private readonly List<HistorySnapshotMeta> _metas = new List<HistorySnapshotMeta>();
    private readonly Dictionary<int, GraphData> _cache = new Dictionary<int, GraphData>();
    private int _currentVersion = int.MinValue;
    private bool _ready;

    void Start()
    {
        _provider = useMock
            ? (IMeetingRoomHistoryProvider)new MockMeetingRoomHistoryProvider(mockVersionCount)
            : new ApiMeetingRoomHistoryProvider(baseUrl);

        if (historyView == null)
            Debug.LogWarning("[History] historyView가 연결되지 않았습니다. 렌더가 되지 않습니다.");

        StartCoroutine(Init());
    }

    void OnDestroy()
    {
        if (timeline != null) timeline.OnChanged -= OnTimelineChanged;
    }

    private IEnumerator Init()
    {
        yield return _provider.FetchList(
            roomId,
            metas =>
            {
                _metas.Clear();
                if (metas != null) _metas.AddRange(metas);
                _metas.Sort((a, b) => a.version.CompareTo(b.version)); // 과거→현재
            },
            err => Debug.LogWarning(err));

        if (_metas.Count == 0)
        {
            Debug.LogWarning("[History] 스냅샷이 없습니다. (아직 회의 변경 이력이 없거나 조회 실패)");
            yield break;
        }

        _ready = true;

        // 변화 지점(스냅샷)마다 바에 틱 표시 (한 프레임 뒤 = Canvas 레이아웃 확정 후)
        if (ticks != null)
        {
            yield return null;
            ticks.SetCount(_metas.Count);
        }

        if (timeline != null) timeline.OnChanged += OnTimelineChanged;

        // 시작 시점을 현재(오른쪽 끝) 기준으로 한 번 반영
        float startNorm = timeline != null ? timeline.normalized : 1f;
        OnTimelineChanged(startNorm);

        Debug.Log($"[History] 준비 완료 — 스냅샷 {_metas.Count}개 (v{_metas[0].version}~v{_metas[_metas.Count - 1].version}). 바를 드래그해 과거↔현재를 보세요.");
    }

    private void OnTimelineChanged(float normalized)
    {
        if (!_ready || _metas.Count == 0) return;

        // 0~1 → 스냅샷 인덱스 (0 = 가장 오래된, 마지막 = 최신/현재)
        int index = Mathf.RoundToInt(Mathf.Clamp01(normalized) * (_metas.Count - 1));
        int version = _metas[index].version;
        if (version == _currentVersion) return;
        _currentVersion = version;

        if (_cache.TryGetValue(version, out var cached))
        {
            Render(cached);
            return;
        }

        StartCoroutine(FetchAndRender(version));
    }

    private IEnumerator FetchAndRender(int version)
    {
        yield return _provider.FetchSnapshot(
            roomId,
            version,
            data =>
            {
                if (data == null) return;
                _cache[version] = data;
                // 스크럽이 그새 다른 버전으로 이동했으면 그리지 않음
                if (version == _currentVersion) Render(data);
            },
            err => Debug.LogWarning(err));
    }

    private void Render(GraphData data)
    {
        if (historyView == null || data == null) return;
        historyView.Render(data);
    }
}
