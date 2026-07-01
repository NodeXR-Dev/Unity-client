using System.Collections.Generic;
using UnityEngine;

// 테스트 전용: 서버 seed_all_dummy.sql 의 실제 UUID 노드로 그래프를 로드한다(그래프 로더).
// 실행 시 뜨는 기본노드가 서버 DB에 존재하는 노드가 되어
// 이름변경/텍스트수정/삭제/이동이 서버(NODE_TEXT_UPDATE / NODE_DELETE / NODE_MOVE)에 반영된다.
//
// [전제]
//   - 서버 DB에 seed_all_dummy.sql 이 적용돼 있어야 함.
//   - GraphSyncClient._roomId 를 ROOM_ID 와 일치시킬 것.
//
// [주의] 로컬에서 "추가"한 노드는 서버 DB에 없어(NODE_CREATE 미지원) 수정/삭제 시 서버가 NODE404 를 반환한다.
//        서버 반영 검증은 아래 seed 노드에 대해서만 유효하다.
public class SeedGraphLoader : MonoBehaviour
{
    // ── seed_all_dummy.sql 값 ──────────────────────
    public const string ROOM_ID     = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string SUBGRAPH_ID = "15151515-1515-1515-1515-151515150001";
    public const string PART_CHAIR  = "16161616-1616-1616-1616-161616160001"; // 의자 (PART)
    public const string PROP_BACK   = "16161616-1616-1616-1616-161616160002"; // 곡선형 등받이
    public const string PROP_WOOD   = "16161616-1616-1616-1616-161616160003"; // 나무 재질
    public const string PROP_BROWN  = "16161616-1616-1616-1616-161616160004"; // 따뜻한 브라운 색감

    [SerializeField] private GraphManager _graphManager;
    [SerializeField] private bool _loadOnStart = true;

    private void Start()
    {
        if (_loadOnStart) LoadSeedGraph();
    }

    [ContextMenu("Load Seed Graph")]
    public void LoadSeedGraph()
    {
        if (_graphManager == null) _graphManager = FindObjectOfType<GraphManager>();
        if (_graphManager == null)
        {
            Debug.LogWarning("[SeedGraphLoader] GraphManager를 찾을 수 없습니다.");
            return;
        }

        var graph = new GraphData
        {
            room_id       = ROOM_ID,
            graph_version = 1,
            nodes         = new List<NodeData>(),
            edges         = new List<EdgeData>(),
        };

        // mock 노드가 보이던 원점 줄(y=0)에 배치해 가시성 확보. (서버 위치와 무관 — 로컬 표시용)
        graph.nodes.Add(MakeNode(PART_CHAIR, "PART",     "의자",             0.0f, 0.0f));  // 렌더 안 됨(PART)
        graph.nodes.Add(MakeNode(PROP_BACK,  "PROPERTY", "곡선형 등받이",     -1.5f, 0.0f));
        graph.nodes.Add(MakeNode(PROP_WOOD,  "PROPERTY", "나무 재질",          0.0f, 0.0f));
        graph.nodes.Add(MakeNode(PROP_BROWN, "PROPERTY", "따뜻한 브라운 색감",  1.5f, 0.0f));

        _graphManager.LoadGraph(graph);
        _graphManager.RenderGraph();

        Debug.Log("[SeedGraphLoader] seed 그래프 로드 완료: PART 1 + PROPERTY 3");
    }

    private static NodeData MakeNode(string id, string type, string text, float x, float y)
    {
        return new NodeData
        {
            node_id      = id,
            type         = type,
            label        = text,
            node_text    = text,
            sub_graph_id = SUBGRAPH_ID,
            position     = new float[] { x, y, 0f },
        };
    }

    // ── 서버 왕복 검증용 ContextMenu 트리거 (UI 없이) ──
    // 실행(Play) 후 컴포넌트 우클릭 메뉴에서 호출. 서버 로그/DB + Unity 콘솔(ERROR 미수신)로 확인.

    [ContextMenu("Test/NODE_TEXT_UPDATE (나무 재질)")]
    private void TestTextUpdate() => _graphManager?.RequestUpdateNodeText(PROP_WOOD, "나무 재질(수정됨)");

    [ContextMenu("Test/NODE_DELETE (브라운, leaf)")]
    private void TestDelete() => _graphManager?.RequestDeleteNode(PROP_BROWN);

    [ContextMenu("Test/NODE_MOVE (나무 재질)")]
    private void TestMove() => _graphManager?.RequestMoveNode(PROP_WOOD, new Vector3(0.5f, -1.5f, 0f));

    [ContextMenu("Test/PART rename (의자)")]
    private void TestPartRename() => _graphManager?.RequestRenamePartNode(PART_CHAIR, "의자(수정됨)");

    [ContextMenu("Test/PART delete (의자)")]
    private void TestPartDelete() => _graphManager?.RequestDeleteNode(PART_CHAIR);
}
