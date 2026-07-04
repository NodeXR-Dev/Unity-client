using System.Collections.Generic;
using UnityEngine;

// 테스트 전용: 서버 seed_all_dummy.sql 의 실제 UUID/구조로 그래프를 로드한다(그래프 로더).
// 서버에 그래프 조회 REST/스냅샷 push 가 없으므로(2026-07-04 확인), 초기 그래프를 서버와
// 동일하게 재현하는 하드코딩 스탠드인이다. **node_id·타입·라벨·부모가 서버와 정확히 일치**해야
// 발화 응답 병합(MergeServerGraph, 같은 UUID)에서 라벨이 뒤바뀌는 충돌이 없다.
//
// 구조(= seed_all_dummy.sql):
//   0001 PART "청소 로봇"(parent=NULL → ALL)
//    ├ 0002 PART "집게 팔"     → 0003 PROPERTY "부드러운 집게 끝"
//    ├ 0004 PART "분리수거 통"  → 0005 PROPERTY "두 개의 칸"
//    └ 0006 PART "로봇 몸통"    → 0007 PROPERTY "웃는 얼굴" / 0008 PROPERTY "친환경 색감"
//   0009 REFERENCE "포스터 분위기" → 0010 PROPERTY "바닷속 배경",  0009 → 0001(REFERENCES)
//
// [전제] 서버 DB에 seed_all_dummy.sql 이 적용돼 있어야 하고, GraphSyncClient._roomId 를 ROOM_ID 와 일치.
// [주의] 로컬에서 "추가"한 노드는 서버 DB에 없어(NODE_CREATE 미지원) 수정/삭제 시 서버가 NODE404.
public class SeedGraphLoader : MonoBehaviour
{
    // ── seed_all_dummy.sql 값 ──────────────────────
    public const string ROOM_ID  = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string SUB_MAIN = "15151515-1515-1515-1515-151515150001";  // 로봇 서브그래프
    public const string SUB_REF  = "15151515-1515-1515-1515-151515150002";  // 레퍼런스 서브그래프

    // 노드 UUID (서버 seed 와 동일)
    public const string N_ROBOT   = "16161616-1616-1616-1616-161616160001"; // PART, ALL(root)
    public const string N_ARM     = "16161616-1616-1616-1616-161616160002"; // PART
    public const string N_ARM_TIP = "16161616-1616-1616-1616-161616160003"; // PROPERTY
    public const string N_BIN     = "16161616-1616-1616-1616-161616160004"; // PART
    public const string N_BIN_SLOT= "16161616-1616-1616-1616-161616160005"; // PROPERTY
    public const string N_BODY    = "16161616-1616-1616-1616-161616160006"; // PART
    public const string N_FACE    = "16161616-1616-1616-1616-161616160007"; // PROPERTY
    public const string N_COLOR   = "16161616-1616-1616-1616-161616160008"; // PROPERTY
    public const string N_REF     = "16161616-1616-1616-1616-161616160009"; // REFERENCE(root)
    public const string N_BG      = "16161616-1616-1616-1616-161616160010"; // PROPERTY

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

        // 노드. PART/REFERENCE 는 서브그래프 NodeView 가 없어 위치 무의미(메인그래프 PartPort 로 표시).
        // PROPERTY 는 전부 서브그래프 루트(부모가 PART/REF)라 Reflow 가 위치를 건드리지 않는다 →
        // 여기 좌표가 그대로 최종 위치이므로, 클라 화면에 보이도록 y=0 라인에 x 로 펼친다.
        graph.nodes.Add(MakeNode(N_ROBOT,   "PART",      "바닷속 쓰레기를 줍는 친환경 청소 로봇", null,      SUB_MAIN,  0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_ARM,     "PART",      "종이컵과 빨대로 만든 집게 팔",          N_ROBOT,   SUB_MAIN,  0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_ARM_TIP, "PROPERTY",  "둥글고 부드러운 집게 끝",                N_ARM,     SUB_MAIN, -3.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_BIN,     "PART",      "플라스틱 병과 종이를 나누는 분리수거 통", N_ROBOT,   SUB_MAIN,  0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_BIN_SLOT,"PROPERTY",  "투명 창과 색깔 라벨이 있는 두 개의 칸",   N_BIN,     SUB_MAIN, -1.5f,  0.0f));
        graph.nodes.Add(MakeNode(N_BODY,    "PART",      "웃는 얼굴 화면이 있는 로봇 몸통",         N_ROBOT,   SUB_MAIN,  0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_FACE,    "PROPERTY",  "초등학생이 좋아할 귀여운 웃는 얼굴",       N_BODY,    SUB_MAIN,  0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_COLOR,   "PROPERTY",  "파란색과 노란색의 밝은 친환경 색감",       N_BODY,    SUB_MAIN,  1.5f,  0.0f));
        graph.nodes.Add(MakeNode(N_REF,     "REFERENCE", "바다 보호 발표 포스터 분위기",            null,      SUB_REF,   0.0f,  0.0f));
        graph.nodes.Add(MakeNode(N_BG,      "PROPERTY",  "물고기와 파도, 작은 쓰레기가 보이는 바닷속 배경", N_REF, SUB_REF,   3.0f,  0.0f));

        // 엣지 (서버 seed 와 동일. 서술 관계라 PROPERTY→PROPERTY 가 아니므로 서브그래프 선은 안 그려짐)
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180001", N_ROBOT, N_ARM));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180002", N_ARM,   N_ARM_TIP));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180003", N_ROBOT, N_BIN));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180004", N_BIN,   N_BIN_SLOT));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180005", N_ROBOT, N_BODY));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180006", N_BODY,  N_FACE));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180007", N_BODY,  N_COLOR));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180008", N_REF,   N_BG));
        graph.edges.Add(MakeEdge("18181818-1818-1818-1818-181818180009", N_REF,   N_ROBOT));

        _graphManager.LoadGraph(graph);
        _graphManager.RenderGraph();

        Debug.Log("[SeedGraphLoader] seed 그래프 로드 완료: PART 4(ALL 1 + 파트 3) + PROPERTY 5 + REFERENCE 1");
    }

    private static NodeData MakeNode(string id, string type, string text, string parentId, string subGraphId, float x, float y)
    {
        return new NodeData
        {
            node_id        = id,
            type           = type,
            label          = text,
            node_text      = text,
            parent_node_id = parentId,   // ALL 유도(BackfillIsGlobal)·부모 문맥용
            sub_graph_id   = subGraphId,
            position       = new float[] { x, y, 0f },
        };
    }

    private static EdgeData MakeEdge(string edgeId, string fromId, string toId)
    {
        return new EdgeData
        {
            edge_id      = edgeId,
            from_node_id = fromId,
            to_node_id   = toId,
        };
    }

    // ── 서버 왕복 검증용 ContextMenu 트리거 (UI 없이) ──
    [ContextMenu("Test/NODE_TEXT_UPDATE (집게 끝)")]
    private void TestTextUpdate() => _graphManager?.RequestUpdateNodeText(N_ARM_TIP, "둥글고 부드러운 집게 끝(수정됨)");

    [ContextMenu("Test/NODE_DELETE (색감, leaf)")]
    private void TestDelete() => _graphManager?.RequestDeleteNode(N_COLOR);

    [ContextMenu("Test/NODE_MOVE (집게 끝)")]
    private void TestMove() => _graphManager?.RequestMoveNode(N_ARM_TIP, new Vector3(0.5f, -1.5f, 0f));

    [ContextMenu("Test/PART rename (집게 팔)")]
    private void TestPartRename() => _graphManager?.RequestRenamePartNode(N_ARM, "집게 팔(수정됨)");

    [ContextMenu("Test/PART delete (집게 팔)")]
    private void TestPartDelete() => _graphManager?.RequestDeleteNode(N_ARM);
}
