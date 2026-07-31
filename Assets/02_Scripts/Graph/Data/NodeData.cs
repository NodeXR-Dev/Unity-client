/*
 * File: NodeData.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Defines graph node data for NodeXR.
 * Notes:
 * - type is stored as string for server JSON compatibility (JsonUtility serializes enum as int).
 * - property_category is AI-generated free text, not a fixed enum.
 * - label and node_text both exist because server responses may use either field.
 */
using UnityEngine;

[System.Serializable]
public class NodeData
{
    public string node_id;
    public string type;       // "PART" | "PROPERTY" | "REFERENCE"
    public string label;      // 주 표시명
    public string node_text;  // 서버에서 label 대신 node_text로 올 수 있음

    public float[] position;  // [x, y, z] — 서버 JSON 배열 형식과 일치

    // PROPERTY 전용 — AI가 자유롭게 생성하는 문자열, enum으로 제한하지 않음
    public string property_category;

    // PART 전용 — ALL 파트 판별용
    // TODO: 추후 서버 스펙에 따라 data: { is_global: true } 형태로 옮길 수 있음
    public bool is_global;

    // 서브그래프(같은 PROPERTY 트리) 식별자.
    // 서버 DB의 sub_graph_id를 그대로 저장한다. 서버가 안 주면 GraphManager가
    // 레이아웃 루트 node_id로 백필한다. 전체 그래프(서브그래프 강체) 이동에 사용.
    public string sub_graph_id;

    // /api/utterances 응답의 부모 노드 id. 엣지로도 유추 가능하나 응답값을 보존한다.
    public string parent_node_id;

    // 서버 그래프 조회/GRAPH_UPDATED 에서 내려오는 값. 이 노드가 현재 2D 생성 문맥에 쓰였는지 여부(파싱·저장만).
    public bool used_in_generation;

    // MVP: 자식 PROPERTY의 "활성화" 상태. 활성 자식만 부모로부터 초록선으로 이어지고,
    // 비활성은 선이 없고 흐리게 표시된다. 기본 false = 새 자식은 비활성으로 시작.
    // (루트/PART 에는 의미 없음 — MVP 활성화 UI가 자식 PROPERTY 에만 사용.)
    public bool is_active;

    // 타입별 부가 데이터. REFERENCE는 자산/이미지 정보, 그 외는 비어 있음({}).
    // 현재는 파싱·저장만 하고 화면 표시는 하지 않는다(REFERENCE NodeView 미구현).
    public NodeAssetData data;

    // --- 편의 프로퍼티 (직렬화 대상 아님) ---

    // label 우선, 없으면 node_text 반환
    public string DisplayText =>
        !string.IsNullOrEmpty(label) ? label : node_text;

    // type 문자열 → NodeType enum. 파싱 실패 시 UNKNOWN 반환
    public NodeType NodeType
    {
        get
        {
            if (System.Enum.TryParse(type, ignoreCase: true, out NodeType result))
                return result;
            return NodeType.UNKNOWN;
        }
    }

    // position 배열 → Vector3
    public Vector3 Position =>
        position != null && position.Length == 3
            ? new Vector3(position[0], position[1], position[2])
            : Vector3.zero;

    // 노드 이동 시 position 배열 갱신 (GraphManager.RequestMoveNode에서 사용 예정)
    public void SetPosition(Vector3 newPosition)
    {
        if (position == null || position.Length != 3)
            position = new float[3];
        position[0] = newPosition.x;
        position[1] = newPosition.y;
        position[2] = newPosition.z;
    }
}

// 노드 타입별 부가 데이터. REFERENCE 전용 필드(자산/이미지)를 담는다.
// 그 외 타입은 서버가 {} 로 주므로 필드가 기본값으로 남는다.
[System.Serializable]
public class NodeAssetData
{
    public string asset_id;
    public string mime_type;
    public int width;
    public int height;
    public string reference_image_url;
}
