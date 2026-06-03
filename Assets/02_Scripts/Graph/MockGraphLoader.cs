/*
 * 파일명: MockGraphLoader.cs
 * 작성자: Developer 3
 * 생성일: 2026-05-20
 * 목적: Test_SH 씬에서 Mock GraphData를 생성하고 GraphManager에 로드해 렌더링을 검증한다.
 * 핵심 내용:
 * - 서버 없이 로컬에서 그래프 구조를 확인하기 위한 임시 테스트 스크립트이다.
 * - Start()에서 LoadGraph → RenderGraph 순서로 호출한다.
 * - 실제 서버 연동 단계에서는 이 스크립트를 제거하고 서버 응답으로 교체한다.
 *
 * Mock 그래프 구조:
 *   [PROPERTY] 미래지향 스타일 → [PROPERTY] 유토피아 → [PROPERTY] 식물
 *   [PROPERTY] 미래지향 스타일 → [PART] ALL   ← 서브그래프 최상위 PROPERTY가 PART에 연결
 *   [PROPERTY] 금속 → [PROPERTY] 무광
 */
using System.Collections.Generic;
using UnityEngine;

public class MockGraphLoader : MonoBehaviour
{
    [SerializeField] private GraphManager _graphManager;

    private void Start()
    {
        if (_graphManager == null)
            _graphManager = GetComponent<GraphManager>();

        if (_graphManager == null)
        {
            Debug.LogError("[MockGraphLoader] GraphManager를 찾을 수 없습니다. Inspector에서 연결하거나 같은 GameObject에 추가해주세요.");
            return;
        }

        GraphData mockGraph = BuildMockGraph();
        _graphManager.LoadGraph(mockGraph);
        _graphManager.RenderGraph();
    }

    private GraphData BuildMockGraph()
    {
        var graph = new GraphData
        {
            room_id = "test_room",
            graph_version = 1,
            nodes = new List<NodeData>
            {
                new NodeData { node_id = "prop_future", type = "PROPERTY", label = "미래지향 스타일", position = new float[] { -5f,  3f, 0f }, property_category = "style"    },
                new NodeData { node_id = "prop_utopia", type = "PROPERTY", label = "유토피아",       position = new float[] { -5f,  0f, 0f }, property_category = "concept"  },
                new NodeData { node_id = "prop_plant",  type = "PROPERTY", label = "식물",           position = new float[] { -5f, -3f, 0f }, property_category = "motif"    },
                new NodeData { node_id = "part_all",    type = "PART",     label = "ALL",            position = new float[] { -1f, -3f, 0f }, is_global = true               },
                new NodeData { node_id = "prop_metal",  type = "PROPERTY", label = "금속",           position = new float[] {  5f,  0f, 0f }, property_category = "material" },
                new NodeData { node_id = "prop_matte",  type = "PROPERTY", label = "무광",           position = new float[] {  5f,  3f, 0f }, property_category = "texture"  },
            },
            edges = new List<EdgeData>
            {
                new EdgeData { edge_id = "e1", from_node_id = "prop_future", to_node_id = "prop_utopia" }, // PROPERTY → PROPERTY (속성 세부화)
                new EdgeData { edge_id = "e2", from_node_id = "prop_utopia", to_node_id = "prop_plant"  }, // PROPERTY → PROPERTY (속성 세부화)
                new EdgeData { edge_id = "e3", from_node_id = "prop_future", to_node_id = "part_all"    }, // PROPERTY → PART (최상위 속성을 전체 이미지에 적용)
                new EdgeData { edge_id = "e4", from_node_id = "prop_metal",  to_node_id = "prop_matte"  }, // PROPERTY → PROPERTY (속성 세부화)
            }
        };

        return graph;
    }
}
