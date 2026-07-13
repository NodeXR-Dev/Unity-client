/*
 * File: EdgeData.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Defines graph edge data for NodeXR.
 * Notes:
 * - Edge type is intentionally omitted in MVP.
 * - Relationship is inferred from from/to node types.
 * - PROPERTY → PROPERTY means property refinement.
 * - PROPERTY → PART means applying a property to a part or ALL.
 * - REFERENCE → PROPERTY means the reference describes a property.
 * - REFERENCE → PART means the reference applies to a part or ALL.
 */

[System.Serializable]
public class EdgeData
{
    public string edge_id;
    public string from_node_id;
    public string to_node_id;

    // 서버 그래프 조회/GRAPH_UPDATED 에서 내려오는 값. 이 엣지가 현재 2D 생성 문맥에 쓰였는지 여부(파싱·저장만).
    public bool used_in_generation;
}
